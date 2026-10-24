// Run with playwright-cli run-code --filename tests/news.browser.mjs against serve.mjs.
// Every API response is isolated; no publication or beta registration is written.
async (page) => {
  const base = new URL(page.url()).origin;
  const api = 'https://api.arkya.online';
  const checks = [];
  const errors = [];
  const requests = [];
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label); };
  const onError = error => errors.push(error.message);
  page.on('pageerror', onError);
  let scenario = 'normal';
  let releaseSlow;
  let slowStarted;
  let slowDone;
  let nextSlowDone;
  const slowStartedPromise = () => new Promise(resolve => { slowStarted = resolve; });
  const slowDonePromise = () => new Promise(resolve => { nextSlowDone = resolve; });
  const posts = [];
  let sequence = 0;
  for (const language of ['fr', 'en']) {
    for (const kind of ['news', 'release-note']) {
      for (let index = 0; index < (kind === 'news' ? 15 : 28); index += 1) {
        sequence += 1;
        const beta = kind === 'news' && index === 0;
        posts.push({ id: `10000000-0000-4000-8000-${String(sequence).padStart(12, '0')}`, kind, language,
          title: beta ? language === 'fr' ? 'La bêta ouvre ses inscriptions' : 'Beta registration is open' :
            kind === 'news' ? `${language === 'fr' ? 'Nouvelles de l’île' : 'Island news'} ${index}` :
              `${language === 'fr' ? 'Le monde évolue' : 'The world evolves'} ${index}`,
          summary: language === 'fr' ? 'Une île à explorer, des personnages et les premières aventures à partager.' :
            'An island to explore, characters and the first adventures to share.',
          body: beta ? `Closed beta / Bêta fermée\n\nhttps://arkya.online/beta.html?lang=${language}\n\n<img src=x onerror="window.newsInjected=true">\n<script>window.newsInjected=true</script>` :
            `Development journal ${index}\n\nMovement, characters and the starter island.\n\nPlain text stays plain text.`,
          gameVersion: kind === 'news' ? null : index % 2 ? '0.1.0' : '0.2.0',
          publishedAt: new Date(Date.UTC(2026, 9, 24 - index, 12)).toISOString(),
        });
      }
    }
  }
  const beta = posts.find(post => post.kind === 'news' && post.language === 'en');
  const response = (route, data, status = 200) => route.fulfill({ status, contentType: 'application/json',
    headers: { 'Access-Control-Allow-Origin': base, 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
      'Access-Control-Allow-Headers': 'Authorization, Content-Type, X-Arkya-Refresh',
      'Access-Control-Allow-Credentials': 'true' }, body: JSON.stringify(data) });
  const apiRoute = async route => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === 'OPTIONS') { await response(route, {}); return; }
    if (url.pathname.startsWith('/v1/auth/')) { await response(route, {}, 401); return; }
    if (!url.pathname.startsWith('/v1/content/posts')) { await response(route, {}, 404); return; }
    check(request.method() === 'GET', 'Publications use read-only requests');
    check(!request.headers().authorization && !request.headers().cookie, 'Public feed sends no account credentials');
    requests.push(url);
    if (scenario === 'unavailable') { await response(route, {}, 503); return; }
    if (scenario === 'malformed') { await response(route, { items: [{ title: 'Unverified data' }], hasMore: false }); return; }
    if (url.pathname !== '/v1/content/posts') {
      const post = posts.find(item => item.id === url.pathname.split('/').pop());
      if (scenario === 'missing' || !post) await response(route, {}, 404);
      else await response(route, post);
      return;
    }
    const query = url.searchParams;
    let items = posts.filter(post => post.kind === query.get('kind') && post.language === query.get('language'));
    if (query.get('q')) items = items.filter(post => `${post.title} ${post.summary} ${post.body}`.toLowerCase().includes(query.get('q').toLowerCase()));
    if (query.get('gameVersion')) items = items.filter(post => post.gameVersion === query.get('gameVersion'));
    if (query.get('from')) items = items.filter(post => post.publishedAt.slice(0, 10) >= query.get('from'));
    if (query.get('to')) items = items.filter(post => post.publishedAt.slice(0, 10) <= query.get('to'));
    items.sort((a, b) => query.get('sort') === 'oldest' ? a.publishedAt.localeCompare(b.publishedAt) : b.publishedAt.localeCompare(a.publishedAt));
    const offset = Number(query.get('offset'));
    const limit = Number(query.get('limit'));
    const data = scenario === 'empty' ? { items: [], hasMore: false } : { items: items.slice(offset, offset + limit), hasMore: items.length > offset + limit };
    if (scenario === 'slow' && query.get('q') === 'Island') {
      slowStarted?.();
      await new Promise(resolve => { releaseSlow = resolve; });
      try { await response(route, data); } catch { /* Cancelled requests may already be closed by the browser. */ }
      nextSlowDone?.();
      return;
    }
    await response(route, data);
  };
  const state = value => page.locator(`#publication-panel[data-state="${value}"]`).waitFor();
  const apply = () => page.locator('#news-filters button[type="submit"]').click();
  try {
    await page.context().route(`${api}/**`, apiRoute);
    await page.goto(`${base}/news.html?lang=en`);
    await state('ready');
    check(await page.title() === 'Arkya — News & release notes', 'English page title');
    check(await page.locator('.news-card').count() === 12, 'First page shows the requested 12 publications');
    check(await page.getByRole('tab', { name: 'News', exact: true }).getAttribute('aria-selected') === 'true', 'News is the default tab');
    check(await page.locator('#news-version-field').isHidden(), 'Game version filter is reserved for release notes');
    check(await page.locator('.news-card .news-beta-link').getAttribute('href') === 'beta.html?lang=en', 'Beta news card links to local registration');
    for (const lang of ['fr', 'en']) {
      await page.getByRole('button', { name: lang === 'fr' ? 'Français' : 'English', exact: true }).click();
      await state('ready');
      check(requests.at(-1).searchParams.get('language') === lang, `${lang}: feed is fetched in the selected language`);
      for (const width of [1440, 1100, 820, 760, 390, 320]) {
        await page.setViewportSize({ width, height: 1100 });
        check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${lang}: news list fits ${width}px`);
        check(await page.locator('.news-card .news-beta-link').isVisible(), `${lang}: beta invitation is available at ${width}px`);
      }
    }
    await page.setViewportSize({ width: 1440, height: 1100 });
    await page.screenshot({ path: 'output/playwright/arkya-news-desktop.png', fullPage: true });
    await page.locator('#news-next').click();
    await state('ready');
    check(await page.locator('.news-card').count() === 3, 'Next page loads the remaining results');
    check(requests.at(-1).searchParams.get('offset') === '12', 'Next page is fetched with a server offset');
    check(new URL(page.url()).searchParams.get('page') === '2', 'Pagination is shareable in the URL');
    await page.goBack();
    await state('ready');
    check(await page.locator('.news-card').count() === 12, 'Browser back restores the first result page');

    await page.locator('#tab-news').focus();
    await page.keyboard.press('ArrowRight');
    await state('ready');
    check(await page.locator('#tab-release-note').getAttribute('aria-selected') === 'true', 'Arrow key activates the release notes tab');
    check(await page.locator('#tab-release-note').evaluate(node => node === document.activeElement), 'Keyboard tab switching preserves focus');
    check(await page.locator('#news-version-field').isVisible(), 'Release notes expose the version filter');
    check(requests.at(-1).searchParams.get('kind') === 'release-note', 'Release notes use the release feed');
    await page.locator('#news-version').fill('0.1.0');
    await page.locator('#news-query').fill('world');
    await page.locator('#news-from').fill('2026-10-01');
    await page.locator('#news-to').fill('2026-10-23');
    await page.locator('#news-sort').selectOption('oldest');
    await page.locator('#news-limit').selectOption('24');
    await apply();
    await state('ready');
    const filtered = requests.at(-1).searchParams;
    for (const [key, value] of Object.entries({ kind: 'release-note', language: 'en', q: 'world', gameVersion: '0.1.0', from: '2026-10-01', to: '2026-10-23', sort: 'oldest', limit: '24', offset: '0' }))
      check(filtered.get(key) === value, `Filter ${key} is applied on the server`);
    check(await page.locator('.news-card').count() === 12, 'Combined filters return the complete matching page');
    check((await page.locator('.news-card time').first().getAttribute('datetime')).startsWith('2026-10-01'), 'Oldest-first order is rendered from the server');
    const filterUrl = page.url();
    await page.locator('.news-card h3 a').first().click();
    await state('ready');
    check(await page.locator('#news-article').isVisible(), 'An article opens in full');
    check((await page.locator('#news-article-body').textContent()).includes('Plain text stays plain text.'), 'Full article body is visible');
    check(await page.locator('#news-permalink').getAttribute('href') === new URL(page.url()).pathname + new URL(page.url()).search, 'Article has a shareable permanent link');
    await page.reload();
    await state('ready');
    check(await page.locator('#news-article').isVisible(), 'Article deep link survives reload');
    await page.locator('#news-back').click();
    await state('ready');
    check(page.url() === filterUrl, 'Article back link preserves all result filters');
    await page.getByRole('button', { name: 'Français', exact: true }).click();
    await state('empty');
    check(new URL(page.url()).searchParams.get('gameVersion') === '0.1.0', 'Language switch preserves the version filter');
    check(requests.at(-1).searchParams.get('language') === 'fr', 'Language switch fetches distinct French publications');
    await page.locator('#news-empty-reset').click();
    await state('ready');
    await page.getByRole('button', { name: 'English', exact: true }).click();
    await state('ready');
    await page.locator('#tab-release-note').focus();
    await page.keyboard.press('Home');
    await state('ready');
    check(await page.locator('#tab-news').getAttribute('aria-selected') === 'true', 'Home key returns to the first tab');
    await page.locator('#news-query').fill('Beta');
    await apply();
    await state('ready');
    check(await page.locator('.news-card').count() === 1, 'Text search is applied by the API');
    await page.locator('.news-card h3 a').click();
    await state('ready');
    check(await page.locator('#news-article-beta').getAttribute('href') === 'beta.html?lang=en', 'Opened beta news offers registration');
    check((await page.locator('#news-article-body').textContent()).includes('<script>'), 'Article markup is displayed literally');
    check(await page.locator('#news-article-body img, #news-article-body script').count() === 0, 'Publication body cannot create HTML elements');
    check(await page.evaluate(() => !window.newsInjected), 'Publication markup cannot execute');
    for (const width of [1440, 760, 390, 320]) {
      await page.setViewportSize({ width, height: 1100 });
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Article fits ${width}px`);
    }
    await page.screenshot({ path: 'output/playwright/arkya-news-article-mobile.png', fullPage: true });
    await page.getByRole('button', { name: 'Français', exact: true }).click();
    await state('ready');
    check(await page.locator('#news-article-view').isHidden(), 'Language change returns to that language’s results');
    check(!new URL(page.url()).searchParams.has('post'), 'Language change never relabels a different-language article');

    await page.goto(`${base}/news.html?lang=en&tab=news&from=2026-10-24&to=2026-10-24`);
    await state('ready');
    check(await page.locator('.news-card').count() === 1, 'Inclusive date range includes the last publication day');
    const beforeInvalidDates = requests.length;
    await page.locator('#news-from').fill('2026-10-25');
    await apply();
    check(await page.locator('#news-filter-error').isVisible(), 'Reversed dates have an accessible validation message');
    check(requests.length === beforeInvalidDates, 'Invalid date ranges are not requested');
    await page.locator('#news-reset').click();
    await state('ready');
    await page.locator('#news-query').fill('does-not-exist');
    await apply();
    await state('empty');
    check(await page.locator('#news-empty-reset').isVisible(), 'Empty search offers a filter reset');
    scenario = 'unavailable';
    await page.locator('#news-reset').click();
    await state('error');
    check(await page.locator('#news-retry').isVisible(), 'Unavailable service offers retry');
    check(await page.locator('.news-card').count() === 0, 'Failed request never retains stale result cards');
    scenario = 'normal';
    await page.locator('#news-retry').click();
    await state('ready');
    scenario = 'malformed';
    await page.reload();
    await state('error');
    check(!(await page.locator('#news-items').textContent()).includes('Unverified data'), 'Malformed feed data is not rendered');
    scenario = 'empty';
    await page.locator('#news-retry').click();
    await state('empty');
    check(await page.locator('#news-empty-reset').isHidden(), 'Empty unfiltered feed is distinct from an empty search');
    scenario = 'missing';
    await page.goto(`${base}/news.html?lang=en&post=${beta.id}`);
    await state('missing');
    check(await page.locator('#news-back').isVisible(), 'Removed publication keeps a route back to the journal');
    check(await page.locator('#news-article').isHidden(), 'Removed article content is never retained');
    scenario = 'normal';
    await page.goto(`${base}/news.html?lang=en&post=not-a-guid`);
    await state('missing');
    await page.goto(`${base}/news.html?lang=en&from=2026-02-31`);
    await state('invalid');
    check(await page.locator('#news-empty-reset').isVisible(), 'Invalid shared filters offer a reset');
    await page.locator('#news-empty-reset').click();
    await state('ready');

    scenario = 'slow';
    const started = slowStartedPromise();
    slowDone = slowDonePromise();
    await page.locator('#news-query').fill('Island');
    await apply();
    await started;
    check(await page.locator('#publication-panel').getAttribute('aria-busy') === 'true', 'Loading is announced while a request is pending');
    await page.locator('#news-query').fill('Beta');
    await apply();
    await state('ready');
    releaseSlow();
    await slowDone;
    check(await page.locator('.news-card').count() === 1, 'An older response cannot replace newer search results');
    check(new URL(page.url()).searchParams.get('q') === 'Beta', 'An older response cannot change the current URL');
    scenario = 'normal';
    await page.setViewportSize({ width: 320, height: 1100 });
    await page.screenshot({ path: 'output/playwright/arkya-news-mobile.png', fullPage: true });
    check(errors.length === 0, 'No uncaught browser exceptions');
  } finally {
    releaseSlow?.();
    await page.goto('about:blank');
    await page.context().unroute(`${api}/**`, apiRoute);
    page.off('pageerror', onError);
  }
  return { checks: checks.length, results: checks, errors };
}
