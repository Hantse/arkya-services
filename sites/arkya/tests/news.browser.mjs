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
          title: beta ? language === 'fr' ? 'La bêta fermée d’Arkya ouvre ses inscriptions' : 'Registration for Arkya’s closed beta is open' :
            kind === 'news' ? `${language === 'fr' ? 'Nouvelles de l’île' : 'Island news'} ${index}` :
              `${language === 'fr' ? 'Le monde évolue' : 'The world evolves'} ${index}`,
          summary: language === 'fr' ? 'Une île à explorer, des personnages et les premières aventures à partager.' :
            'An island to explore, characters and the first adventures to share.',
          body: beta ? (language === 'fr' ?
            'L’Île de l’Aube vous attend. Les inscriptions à la bêta fermée sont ouvertes.\n\nCOMMENT S’INSCRIRE\nRendez-vous sur https://arkya.online/beta.html?lang=fr, connectez-vous avec Discord et confirmez votre inscription.\n\nLes accès seront attribués par vagues parmi les comptes inscrits.\n\nLES PREMIÈRES SESSIONS\n- Créez votre personnage.\n- Explorez l’île et partagez vos retours.' :
            'The Isle of Dawn awaits. Registration for Arkya’s closed beta is now open.\n\nHOW TO REGISTER\nVisit https://arkya.online/beta.html?lang=en, sign in with Discord, and confirm your registration.\n\nAccess will be granted in waves from the registered accounts.\n\nTHE FIRST SESSIONS\n- Create your character.\n- Explore the island and share your feedback.') +
            '\n\n<img src=x onerror="window.newsInjected=true">\n<script>window.newsInjected=true</script>' :
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
    check(!['q', 'gameVersion', 'from', 'to'].some(key => query.has(key)), 'Public feeds omit removed filters');
    check(query.get('sort') === 'newest', 'Public feeds always request newest first');
    check(query.get('limit') === '12', 'Public feeds always request twelve publications');
    items.sort((a, b) => b.publishedAt.localeCompare(a.publishedAt));
    const offset = Number(query.get('offset'));
    const limit = Number(query.get('limit'));
    const data = scenario === 'empty' ? { items: [], hasMore: false } : { items: items.slice(offset, offset + limit), hasMore: items.length > offset + limit };
    if (scenario === 'slow' && query.get('kind') === 'news' && query.get('language') === 'en') {
      slowStarted?.();
      await new Promise(resolve => { releaseSlow = resolve; });
      try { await response(route, data); } catch { /* Cancelled requests may already be closed by the browser. */ }
      nextSlowDone?.();
      return;
    }
    await response(route, data);
  };
  const state = value => page.locator(`#publication-panel[data-state="${value}"]`).waitFor();
  const legacyKeys = ['q', 'gameVersion', 'from', 'to', 'sort', 'limit'];
  const noLegacyUrl = () => check(!legacyKeys.some(key => new URL(page.url()).searchParams.has(key)), 'Removed filters are absent from the public URL');
  const noFilterControls = async () => {
    check(await page.locator('#news-filters, #news-query, #news-version, #news-from, #news-to, #news-sort, #news-limit, #news-reset, #news-filter-error, #news-empty-reset').count() === 0,
      'Public search, dates, version, sorting, page size, apply and reset controls are removed');
    check(await page.getByRole('search').count() === 0, 'The public journal has no search form');
  };
  try {
    await page.context().route(`${api}/**`, apiRoute);
    await page.goto(`${base}/news.html?lang=en`);
    await state('ready');
    check(await page.title() === 'Arkya — News & release notes', 'English page title');
    check(await page.locator('.news-card').count() === 12, 'First page shows the requested 12 publications');
    check(await page.getByRole('tab', { name: 'News', exact: true }).getAttribute('aria-selected') === 'true', 'News is the default tab');
    await noFilterControls();
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
    await page.evaluate(() => scrollTo(0, 0));
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
    await noFilterControls();
    check(requests.at(-1).searchParams.get('kind') === 'release-note', 'Release notes use the release feed');
    check((await page.locator('.news-card time').first().getAttribute('datetime')).startsWith('2026-10-24'), 'The newest release note is shown first');
    await page.goto(`${base}/news.html?lang=en&tab=release-note&page=2&q=does-not-exist&gameVersion=0.1.0&from=2026-02-31&to=bad&sort=oldest&limit=48`);
    await state('ready');
    check(await page.locator('.news-card').count() === 12, 'Obsolete filters cannot hide public releases');
    check(requests.at(-1).searchParams.get('offset') === '12', 'Legacy URL retains the selected page');
    check(new URL(page.url()).searchParams.get('tab') === 'release-note', 'Legacy URL retains the selected tab');
    noLegacyUrl();
    const listUrl = page.url();
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
    check(page.url() === listUrl, 'Article back link preserves its tab, language and result page');
    await page.getByRole('button', { name: 'Français', exact: true }).click();
    await state('ready');
    check(new URL(page.url()).searchParams.get('tab') === 'release-note', 'Language switch preserves the selected tab');
    check(!new URL(page.url()).searchParams.has('page'), 'Language switch returns to the first page');
    check(requests.at(-1).searchParams.get('language') === 'fr', 'Language switch fetches distinct French publications');
    await page.getByRole('button', { name: 'English', exact: true }).click();
    await state('ready');
    await page.locator('#tab-release-note').focus();
    await page.keyboard.press('Home');
    await state('ready');
    check(await page.locator('#tab-news').getAttribute('aria-selected') === 'true', 'Home key returns to the first tab');
    await page.locator('.news-card h3 a').first().click();
    await state('ready');
    check(await page.locator('#news-article-beta').getAttribute('href') === 'beta.html?lang=en', 'Opened beta news offers registration');
    check((await page.locator('#news-article-body').textContent()).includes('<script>'), 'Article markup is displayed literally');
    check(await page.locator('#news-article-body img, #news-article-body script').count() === 0, 'Publication body cannot create HTML elements');
    check(await page.evaluate(() => !window.newsInjected), 'Publication markup cannot execute');
    for (const lang of ['fr', 'en']) {
      const article = posts.find(post => post.kind === 'news' && post.language === lang);
      await page.goto(`${base}/news.html?lang=${lang}&post=${article.id}`);
      await state('ready');
      check(await page.locator('.news-intro').isHidden(), `${lang}: article has a dedicated reading view`);
      check(await page.locator('h1:visible').count() === 1, `${lang}: article title is the main heading`);
      check(await page.locator('#news-article-body h2').count() === 2, `${lang}: plain text section labels become headings`);
      check(await page.locator('#news-article-body ul li').count() === 2, `${lang}: plain text bullets become a readable list`);
      check(await page.locator('#news-article-body a').getAttribute('href') === `https://arkya.online/beta.html?lang=${lang}`, `${lang}: article URL is clickable without trailing punctuation`);
      check((await page.locator('#news-article-body').textContent()).includes('<script>'), `${lang}: HTML remains literal after formatting`);
      for (const width of [1440, 1100, 820, 760, 390, 320]) {
        await page.setViewportSize({ width, height: 1100 });
        check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${lang}: article fits ${width}px`);
        const back = await page.locator('#news-back').boundingBox();
        const title = await page.locator('#news-article-title').boundingBox();
        const body = await page.locator('#news-article-body').boundingBox();
        check(Math.abs(back.x - title.x) < 1 && Math.abs(body.x - title.x) < 1, `${lang}: article and return link align at ${width}px`);
        check(Number.parseFloat(await page.locator('#news-article-title').evaluate(node => getComputedStyle(node).fontSize)) <= 48, `${lang}: title stays within the reading scale at ${width}px`);
        if (width === 1440 || width === 390) {
          await page.evaluate(() => scrollTo(0, 0));
          await page.screenshot({ path: `output/playwright/arkya-news-article-${lang}-${width === 1440 ? 'desktop' : 'mobile'}.png`, fullPage: true });
        }
      }
    }
    await page.evaluate(() => scrollTo(0, 0));
    await page.screenshot({ path: 'output/playwright/arkya-news-article-mobile.png', fullPage: true });
    await page.getByRole('button', { name: 'Français', exact: true }).click();
    await state('ready');
    check(await page.locator('#news-article-view').isHidden(), 'Language change returns to that language’s results');
    check(!new URL(page.url()).searchParams.has('post'), 'Language change never relabels a different-language article');

    await page.goto(`${base}/news.html?lang=en&tab=news&post=${beta.id}&q=unknown&gameVersion=broken!&from=invalid&to=invalid&sort=oldest&limit=48`);
    await state('ready');
    check(await page.locator('#news-article-beta').isVisible(), 'Legacy article links retain the requested publication');
    check(new URL(page.url()).searchParams.get('post') === beta.id, 'Canonical article URL retains its publication ID');
    noLegacyUrl();
    scenario = 'unavailable';
    await page.goto(`${base}/news.html?lang=en`);
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
    check(await page.locator('#news-first-page').isHidden(), 'Empty first page has no unnecessary recovery control');
    await noFilterControls();
    await page.goto(`${base}/news.html?lang=en&page=3`);
    await state('empty');
    check(await page.locator('#news-first-page').isVisible(), 'Empty later page offers a route to the first page');
    scenario = 'normal';
    await page.locator('#news-first-page').click();
    await state('ready');
    check(await page.locator('.news-card').count() === 12, 'First page recovery restores the public feed');
    scenario = 'missing';
    await page.goto(`${base}/news.html?lang=en&post=${beta.id}`);
    await state('missing');
    check(await page.locator('#news-back').isVisible(), 'Removed publication keeps a route back to the journal');
    check(await page.locator('#news-article').isHidden(), 'Removed article content is never retained');
    scenario = 'normal';
    await page.goto(`${base}/news.html?lang=en&post=not-a-guid`);
    await state('missing');
    await page.goto(`${base}/news.html?lang=en&page=0`);
    await state('invalid');
    check(await page.locator('#news-first-page').isVisible(), 'Invalid shared page offers first page recovery');
    await page.locator('#news-first-page').click();
    await state('ready');

    scenario = 'slow';
    const started = slowStartedPromise();
    slowDone = slowDonePromise();
    await page.goto(`${base}/news.html?lang=en&tab=news`);
    await started;
    check(await page.locator('#publication-panel').getAttribute('aria-busy') === 'true', 'Loading is announced while a request is pending');
    await page.locator('#tab-release-note').click();
    await state('ready');
    releaseSlow();
    await slowDone;
    check(await page.locator('.news-card').count() === 12, 'An older response cannot replace the selected feed');
    check((await page.locator('.news-card h3').first().textContent()).includes('The world evolves'), 'Only the newly selected release note feed is rendered');
    check(new URL(page.url()).searchParams.get('tab') === 'release-note', 'An older response cannot change the selected tab');
    scenario = 'normal';
    await page.setViewportSize({ width: 320, height: 1100 });
    await page.evaluate(() => scrollTo(0, 0));
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
