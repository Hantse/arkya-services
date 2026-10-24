// Run with playwright-cli run-code --filename tests/wiki.browser.mjs against serve.mjs.
// Every API response is intercepted. No live login, signup or publication is performed.
async (page) => {
  const base = new URL(page.url()).origin;
  const api = 'https://api.arkya.online';
  const checks = [];
  const errors = [];
  const homeRequests = [];
  let scenario = 'normal';
  let releaseSlow;
  let slowStarted;
  let slowDone;
  const posts = ['fr', 'en'].flatMap(language => [1, 2, 3].map(index => ({
    id: `20000000-0000-4000-8000-${language === 'fr' ? '10000000000' : '20000000000'}${index}`,
    kind: 'news', language,
    title: index === 3 ? '<img src=x onerror="window.homeInjected=true">' :
      `${language === 'fr' ? 'Nouvelles d’Arkya' : 'Arkya news'} ${index}`,
    summary: language === 'fr' ? 'L’île, les métiers et les premières aventures à découvrir.' :
      'The island, professions and first adventures to discover.',
    body: 'A public article for this isolated browser check.', gameVersion: null,
    publishedAt: `2026-10-${25 - index}T12:00:00Z`,
  })));
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label); };
  const onError = error => errors.push(error.message);
  const apiRoute = async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const headers = { 'Access-Control-Allow-Origin': base, 'Access-Control-Allow-Credentials': 'true',
      'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
      'Access-Control-Allow-Headers': 'Authorization, Content-Type, X-Arkya-Refresh' };
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers });
    if (path.startsWith('/v1/content/posts')) {
      check(request.method() === 'GET', 'Home news uses read-only publication requests');
      check(!request.headers().authorization && !request.headers().cookie, 'Home news sends no account credentials');
      const respond = (data, status = 200) => route.fulfill({ status, contentType: 'application/json', headers, body: JSON.stringify(data) });
      if (scenario === 'error') return respond({}, 503);
      if (path !== '/v1/content/posts') return respond(posts.find(post => post.id === path.split('/').pop()) || {}, 200);
      homeRequests.push(url);
      const selected = posts.filter(post => post.language === url.searchParams.get('language')).map((post, index) =>
        scenario === 'presentation' && index === 2 ? { ...post, title: post.language === 'fr' ? 'L’artisanat prend forme' : 'Crafting takes shape' } : post);
      const data = scenario === 'empty' ? { items: [], hasMore: false } : { items: selected, hasMore: false };
      if (scenario === 'slow' && url.searchParams.get('language') === 'fr') {
        slowStarted?.();
        await new Promise(resolve => { releaseSlow = resolve; });
        try { await respond(data); } catch { /* The obsolete request can be closed after the language changes. */ }
        slowDone?.();
        return;
      }
      return respond(data);
    }
    return route.fulfill({ status: path.startsWith('/v1/auth/') ? 401 : 404,
      contentType: 'application/json', headers, body: '{}' });
  };
  const realmsRoute = route => route.fulfill({ contentType: 'application/json', body: '[]' });
  const index = () => page.locator('.wiki-entry-card').first().waitFor();
  const article = () => page.locator('#wiki-article-title').waitFor();
  const category = id => page.locator(`#wiki-categories a[href*="category=${id}"]`);
  const language = id => page.getByRole('button', { name: id === 'fr' ? 'Français' : 'English', exact: true });
  const fit = async (label, widths = [320, 390, 768, 1440]) => {
    for (const width of widths) {
      await page.setViewportSize({ width, height: 1000 });
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${label}: no horizontal overflow at ${width}px`);
      check(await page.locator('#wiki-search').isVisible(), `${label}: search remains visible at ${width}px`);
    }
  };
  page.on('pageerror', onError);
  await page.context().route(`${api}/**`, apiRoute);
  await page.context().route(`${base}/v1/public/realms**`, realmsRoute);
  try {
    await page.goto(`${base}/wiki.html?lang=fr`);
    await index();
    check(await page.locator('html').getAttribute('lang') === 'fr', 'French deep link sets the page language');
    check((await page.title()).includes('Wiki'), 'Wiki has a page title');
    check(await page.locator('#wiki-categories a').count() === 8, 'Index exposes all seven categories plus all articles');
    check(await page.locator('#wiki-result-count').getAttribute('role') === 'status', 'Search result counts are announced');
    check(await page.locator('#wiki-content .wiki-featured a').count() === 3, 'Index gives direct access to classes, recipes and professions');
    const total = await page.locator('.wiki-entry-card').count();
    check(total >= 40, 'Index contains a substantive reference catalog');
    await fit('French index');
    await page.screenshot({ path: '../../output/playwright/arkya-wiki-desktop.png', fullPage: true });

    for (const [id, title] of [['classes', 'Classes'], ['abilities', 'Capacités'], ['crafting', 'Métiers'],
      ['recipes', 'Recettes'], ['resources', 'Ressources'], ['world', 'Monde'], ['start', 'Bien débuter']]) {
      await category(id).click();
      check(new URL(page.url()).searchParams.get('category') === id, `${id}: category is shareable`);
      check(await category(id).getAttribute('aria-current') === 'true', `${id}: selected category is exposed accessibly`);
      check(await page.locator('#wiki-content h2').first().textContent() === title, `${id}: category has a dedicated heading`);
      check(await page.locator('.wiki-entry-card').count() > 0, `${id}: category contains articles`);
      const labels = await page.locator('.wiki-entry-meta > span:first-child').allTextContents();
      check(labels.every(value => value === title), `${id}: results stay inside the category`);
    }
    await category('classes').focus();
    await page.keyboard.press('Enter');
    check(new URL(page.url()).searchParams.get('category') === 'classes', 'Category links work with the keyboard');

    await category('recipes').click();
    await page.locator('#wiki-search').fill('BRIQUE');
    check(await page.locator('.wiki-entry-card').count() >= 2, 'French recipe search finds bricks case-insensitively');
    check(new URL(page.url()).searchParams.get('q') === 'BRIQUE', 'Search is retained in the shareable URL');
    await page.locator('#wiki-search').fill('brique gres');
    check(await page.locator('.wiki-entry-card').count() === 1, 'Accent-free multiword search finds the sandstone brick');
    const recipeTitle = await page.locator('.wiki-entry-card h3 a').textContent();
    await page.locator('.wiki-entry-card h3 a').click();
    await article();
    const recipeUrl = page.url();
    const recipeId = new URL(recipeUrl).searchParams.get('entry');
    check(Boolean(recipeId), 'Recipe opens an individual article URL');
    check(await page.locator('#wiki-article-title').textContent() === recipeTitle, 'Article matches the chosen result');
    check(await page.locator('.wiki-facts dt').count() > 0, 'Recipe includes structured ingredient and crafting facts');
    check(await page.locator('.wiki-section').count() > 0, 'Recipe includes its full reference content');
    check(await page.locator('.wiki-article-status').isVisible(), 'Article states its development status');
    check((await page.locator('.wiki-reviewed').textContent()).includes('2026'), 'Article shows its review date');
    for (const href of await page.locator('.wiki-toc a').evaluateAll(nodes => nodes.map(node => node.getAttribute('href')))) {
      check(await page.locator(href).count() === 1, `Article contents link ${href} resolves to a section`);
    }
    check((await page.title()).includes(recipeTitle), 'Article title is reflected in page metadata');
    check(await page.locator('meta[name="description"]').getAttribute('content') === await page.locator('.wiki-article-lead').textContent(), 'Article description is reflected in page metadata');
    await page.reload();
    await article();
    check(new URL(page.url()).searchParams.get('entry') === recipeId, 'Article deep link survives reload');
    await page.goBack();
    await index();
    check(await page.locator('#wiki-search').inputValue() === 'brique gres', 'Browser back restores the previous search');
    check(new URL(page.url()).searchParams.get('category') === 'recipes', 'Browser back restores the category');
    await page.goForward();
    await article();
    check(new URL(page.url()).searchParams.get('entry') === recipeId, 'Browser forward restores the article');

    await language('en').click();
    await article();
    check(new URL(page.url()).searchParams.get('entry') === recipeId, 'Language switch keeps the selected article');
    check(new URL(page.url()).searchParams.get('lang') === 'en', 'Language switch updates the shareable URL');
    check((await page.locator('#wiki-article-title').textContent()).toLowerCase().includes('brick'), 'Recipe is translated into English');
    await fit('English recipe');
    await page.locator('.wiki-back').click();
    check(await page.locator('#wiki-search').inputValue() === 'brique gres', 'Article return preserves the query');
    check(new URL(page.url()).searchParams.get('category') === 'recipes', 'Article return preserves the category');
    await page.locator('#wiki-clear').click();
    await index();
    check(await page.locator('#wiki-search').inputValue() === '', 'Clear search empties its input');
    check(new URL(page.url()).searchParams.get('category') === 'recipes', 'Clear search preserves the chosen category');
    check(await page.locator('#wiki-search').evaluate(node => node === document.activeElement), 'Clear search restores input focus');

    await category('abilities').click();
    await page.locator('#wiki-search').fill('heal');
    const healLink = page.locator('.wiki-entry-card h3 a').filter({ hasText: /^Heal$/ });
    check(await healLink.count() === 1, 'English ability search contains the Heal article');
    await healLink.click();
    await article();
    check(new URL(page.url()).searchParams.get('entry') === 'heal', 'Ability has a stable individual deep link');
    check(await page.locator('.wiki-article > .status').textContent() === 'In design', 'Unimplemented combat effects are labeled as design');
    check(await page.locator('.wiki-related a').count() > 0, 'Ability article connects to related reference pages');
    const relatedId = new URL(await page.locator('.wiki-related a').first().getAttribute('href'), base).searchParams.get('entry');
    await page.locator('.wiki-related a').first().click();
    await article();
    check(new URL(page.url()).searchParams.get('entry') === relatedId, 'Related article navigation resolves its target');
    await page.goBack();
    await article();
    check(new URL(page.url()).searchParams.get('entry') === 'heal', 'Browser back returns from a related article');
    await fit('English ability');
    await page.evaluate(() => { document.activeElement?.blur(); scrollTo(0, 0); });
    await page.screenshot({ path: '../../output/playwright/arkya-wiki-skill-desktop.png', fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: '../../output/playwright/arkya-wiki-skill-mobile.png', fullPage: true });

    for (const lang of ['fr', 'en']) {
      await page.goto(`${base}/wiki.html?lang=${lang}&category=skills&q=heal`);
      await index();
      check(await category('abilities').getAttribute('aria-current') === 'true', `${lang}: old skill links select the abilities category`);
      check(await page.locator('#wiki-search').inputValue() === 'heal', `${lang}: old skill links preserve the Heal search`);
      await page.locator('.wiki-entry-card h3 a').filter({ hasText: lang === 'fr' ? /^Soin \/ Heal$/ : /^Heal$/ }).click();
      await article();
      check(new URL(page.url()).searchParams.get('category') === 'abilities', `${lang}: following an old skill link creates a canonical ability URL`);
      check(await page.locator('.wiki-breadcrumb').textContent().then(text => text.includes(lang === 'fr' ? 'Capacités' : 'Abilities')), `${lang}: Heal is presented as an ability`);

      await page.goto(`${base}/wiki.html?lang=${lang}&entry=skills-and-abilities`);
      await article();
      check(await page.locator('#section-0 h3').textContent() === (lang === 'fr' ? 'Skill : Nature' : 'Skill: Nature'), `${lang}: glossary identifies Nature as a skill`);
      check(await page.locator('#section-1 h3').textContent() === (lang === 'fr' ? 'Ability : Thunder' : 'Ability: Thunder'), `${lang}: glossary identifies Thunder as an ability`);
      check((await page.locator('#section-1 p').textContent()).includes(lang === 'fr' ? 'un sort' : 'a spell'), `${lang}: glossary identifies Thunder as a spell`);
      await fit(`${lang}: skills and abilities glossary`, [320, 1440]);
      await page.screenshot({ path: `../../output/playwright/arkya-wiki-terminology-${lang}.png`, fullPage: true });
    }

    await page.goto(`${base}/wiki.html?lang=en&category=classes&entry=cleric`);
    await article();
    const grimoire = new URL(await page.locator('.wiki-article > a[href*="classes.html"]').getAttribute('href'), base);
    check(grimoire.searchParams.get('class') === 'cleric' && grimoire.searchParams.get('lang') === 'en', 'Class links to its localized interactive grimoire');
    await page.goto(`${base}/wiki.html?lang=fr&category=skills&q=${encodeURIComponent('aucune-fiche-ne-correspond-987654321')}`);
    await page.locator('.wiki-empty').waitFor();
    check(await page.locator('.wiki-entry-card').count() === 0, 'Unmatched search has an explicit empty state');
    await page.locator('.wiki-empty a').click();
    await index();
    check(await page.locator('.wiki-entry-card').count() === total, 'Empty-state recovery restores all articles');
    check(!new URL(page.url()).searchParams.has('q') && !new URL(page.url()).searchParams.has('category'), 'Recovery clears search and category together');

    const hostile = '<img src=x onerror="window.wikiInjected=true"><script>window.wikiInjected=true</script>';
    await page.locator('#wiki-search').fill(hostile);
    await page.locator('.wiki-empty').waitFor();
    check(await page.locator('#wiki-search').inputValue() === hostile, 'Markup-like searches remain literal input');
    check(new URL(page.url()).searchParams.get('q') === hostile, 'Markup-like searches stay encoded inside one URL parameter');
    check(await page.locator('#wiki-content img, #wiki-content script').count() === 0, 'Search cannot create markup in wiki results');
    check(await page.evaluate(() => !window.wikiInjected), 'Search cannot execute injected script');
    await page.goto(`${base}/wiki.html?lang=en&entry=${encodeURIComponent(hostile)}&category=unknown`);
    await page.locator('.wiki-empty').waitFor();
    check(await page.locator('#wiki-article-title').count() === 0, 'Unknown article has a recovery state');
    check(await page.locator('#wiki-content img, #wiki-content script').count() === 0, 'Unknown article identifiers cannot create markup');
    await page.locator('.wiki-empty a').click();
    await index();
    await fit('English index');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.evaluate(() => scrollTo(0, 0));
    await page.screenshot({ path: '../../output/playwright/arkya-wiki-mobile.png', fullPage: true });

    const menuToggle = page.locator('.menu-toggle');
    await menuToggle.focus();
    await page.keyboard.press('Enter');
    check(await menuToggle.getAttribute('aria-expanded') === 'true', 'Mobile navigation opens with the keyboard');
    check(await page.locator('#mobile-menu').isVisible(), 'Mobile navigation becomes visible');
    await page.keyboard.press('Escape');
    check(await menuToggle.getAttribute('aria-expanded') === 'false', 'Escape closes mobile navigation');
    check(await menuToggle.evaluate(node => node === document.activeElement), 'Escape returns focus to the menu button');
    await menuToggle.click();
    await page.locator('#mobile-menu a[href*="wiki.html"]').click();
    await index();
    check(await page.locator('html').getAttribute('lang') === 'en', 'Mobile wiki navigation preserves the chosen language');
    check(await page.locator('#mobile-menu').isHidden(), 'Following a mobile navigation link closes its menu');

    const homeState = value => page.locator(`#home-news[data-state="${value}"]`).waitFor();
    for (const lang of ['fr', 'en']) {
      await page.goto(`${base}/index.html?lang=${lang}`);
      await homeState('ready');
      check(await page.locator('.home-news-card').count() === 3, `${lang}: homepage shows three recent news items`);
      const latest = homeRequests.at(-1).searchParams;
      check(latest.get('kind') === 'news' && latest.get('language') === lang && latest.get('limit') === '3' && latest.get('sort') === 'newest', `${lang}: homepage requests the latest news in the selected language`);
      check(await page.locator('#home-news').getAttribute('aria-live') === 'polite', `${lang}: home news updates are announced`);
      check(await page.locator('.home-actions a[href*="beta.html"]').isVisible(), `${lang}: homepage offers beta registration`);
      check(await page.locator('.home-actions a[href*="wiki.html"]').isVisible(), `${lang}: homepage offers the wiki`);
      check(await page.locator('.home-essentials a[href*="play.html"]').count() === 1, `${lang}: homepage keeps the launcher installation link`);
      check(await page.locator('.home-essentials a[href*="entry=first-steps"]').count() === 1, `${lang}: homepage keeps the first-steps guide`);
      check(await page.locator('.home-essentials a[href*="development.html"]').count() === 1, `${lang}: homepage keeps the development link`);
      check(await page.locator('.home-wiki-grid a').count() === 4, `${lang}: homepage links directly to four useful wiki categories`);
      check(await page.locator('.home-wiki-grid a[href*="category=abilities"] h3').textContent() === (lang === 'fr' ? 'Capacités' : 'Abilities'), `${lang}: homepage labels the combat ability catalog correctly`);
      check(await page.locator('.realms-home [data-realm-list]').isVisible(), `${lang}: homepage keeps the realm list`);
      check(await page.locator('#gallery-grid').count() === 0, `${lang}: homepage gives useful information its own space`);
      check((await page.locator('.home-news-card h3').last().textContent()).includes('<img'), `${lang}: publication titles render as literal text`);
      check(await page.locator('#home-news img, #home-news script').count() === 0 && await page.evaluate(() => !window.homeInjected), `${lang}: publication titles cannot execute markup`);
      const firstPost = new URL(await page.locator('.home-news-card h3 a').first().getAttribute('href'), base);
      check(firstPost.pathname === '/news.html' && firstPost.searchParams.get('lang') === lang && Boolean(firstPost.searchParams.get('post')), `${lang}: homepage news links to localized full articles`);
      for (const href of await page.locator('a[data-language-link]').evaluateAll(nodes => nodes.map(node => node.href))) {
        check(new URL(href).searchParams.get('lang') === lang, `${lang}: public page links preserve language`);
      }
      for (const width of [320, 390, 768, 1440]) {
        await page.setViewportSize({ width, height: 1000 });
        check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${lang}: homepage fits ${width}px`);
      }
      scenario = 'presentation';
      await page.reload();
      await homeState('ready');
      await page.evaluate(() => { document.activeElement?.blur(); scrollTo(0, 0); });
      await page.screenshot({ path: `../../output/playwright/arkya-home-${lang}-desktop.png`, fullPage: true });
      await page.setViewportSize({ width: 390, height: 844 });
      await page.screenshot({ path: `../../output/playwright/arkya-home-${lang}-mobile.png`, fullPage: true });
      scenario = 'normal';
      await page.locator('.home-essentials a[href*="entry=first-steps"]').click();
      await article();
      check(new URL(page.url()).searchParams.get('entry') === 'first-steps', `${lang}: first-steps guide opens its wiki article`);
      check(await page.locator('html').getAttribute('lang') === lang, `${lang}: first-steps article keeps language`);
      await page.goBack();
      await homeState('ready');
      await page.locator('.home-wiki-grid a[href*="category=recipes"]').click();
      await index();
      check(new URL(page.url()).searchParams.get('category') === 'recipes', `${lang}: homepage recipe shortcut opens the recipe category`);
    }
    for (const lang of ['fr', 'en']) {
      scenario = 'error';
      await page.goto(`${base}/index.html?lang=${lang}`);
      await homeState('error');
      check(await page.locator('#home-news-retry').isVisible(), `${lang}: unavailable home news offers retry`);
      check(await page.locator('.home-news-card').count() === 0, `${lang}: unavailable home news removes stale cards`);
      check(await page.locator('.home-actions a[href*="wiki.html"]').isVisible(), `${lang}: useful homepage links remain available during news errors`);
      scenario = 'normal';
      await page.locator('#home-news-retry').click();
      await homeState('ready');
      check(await page.locator('.home-news-card').count() === 3, `${lang}: retry recovers home news`);
      scenario = 'empty';
      await page.reload();
      await homeState('empty');
      check(await page.locator('#home-news-retry').isHidden(), `${lang}: empty published feed is distinct from an unavailable service`);
      check(await page.locator('.home-news-card').count() === 0, `${lang}: empty published feed contains no invented news`);
      check((await page.locator('#home-news').textContent()).includes(lang === 'fr' ? 'Aucune actualité' : 'No news'), `${lang}: empty published feed is translated`);
    }
    scenario = 'slow';
    const started = new Promise(resolve => { slowStarted = resolve; });
    const done = new Promise(resolve => { slowDone = resolve; });
    await page.goto(`${base}/index.html?lang=fr`);
    await started;
    await homeState('loading');
    await language('en').click();
    await homeState('ready');
    releaseSlow();
    await done;
    check(await page.locator('.home-news-card').first().getAttribute('lang') === 'en', 'Older French news cannot replace the newer English selection');
    check((await page.locator('.home-news-card h3').first().textContent()).startsWith('Arkya news'), 'Only news from the current language is displayed after a race');
    check(new URL(page.url()).searchParams.get('lang') === 'en', 'Late news response cannot change the chosen language URL');
    await page.setViewportSize({ width: 320, height: 900 });
    await page.evaluate(() => scrollTo(0, 0));
    await page.locator('.menu-toggle').focus();
    await page.keyboard.press('Enter');
    check(await page.locator('#mobile-menu').isVisible(), 'Homepage mobile menu opens with the keyboard');
    await page.keyboard.press('Escape');
    check(await page.locator('.menu-toggle').evaluate(node => node === document.activeElement), 'Homepage Escape closes menu and restores focus');
    check(errors.length === 0, 'No uncaught browser exceptions');
  } finally {
    releaseSlow?.();
    await page.goto('about:blank');
    await page.context().unroute(`${api}/**`, apiRoute);
    await page.context().unroute(`${base}/v1/public/realms**`, realmsRoute);
    page.off('pageerror', onError);
  }
  return { checks: checks.length, results: checks, errors };
}
