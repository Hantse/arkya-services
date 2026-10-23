// Run with playwright-cli run-code --filename tests/realms.browser.mjs from a local serve.mjs page.
// All API data and realm-host responses are isolated fixtures; no live service is contacted.
async (page) => {
  const base = new URL(page.url()).origin;
  const realmId = 'beta-aubefont-eu';
  const realmOrigin = `https://${realmId}.arkya.online`;
  const checks = [], errors = [], apiRequests = [];
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label); };
  const onError = error => errors.push(error.message);
  page.on('pageerror', onError);
  let scenario = 'ready';
  const realm = { realmId, displayName: 'Aubefont · Bêta', status: 'Ready', readyServerCount: 2, serverCount: 3,
    updatedAt: new Date().toISOString(), lastObservedAt: new Date().toISOString(), playerCount: null };
  const respond = async route => {
    const request = route.request();
    apiRequests.push({ path: new URL(request.url()).pathname, headers: request.headers() });
    const detail = new URL(request.url()).pathname !== '/v1/public/realms';
    if (scenario === 'outage') return route.fulfill({ status: 503, body: '{}' });
    if (scenario === 'missing' && detail) return route.fulfill({ status: 404, body: '{}' });
    const value = scenario === 'empty' ? [] : scenario === 'old' ? { ...realm, lastObservedAt: '2020-01-01T00:00:00Z' } : realm;
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(detail ? value : scenario === 'empty' ? [] : [value]) });
  };
  const localApi = `${base}/v1/public/realms**`;
  const realmRoutes = `${realmOrigin}/**`;
  const realmRoute = async route => {
    const url = new URL(route.request().url());
    if (url.pathname.startsWith('/v1/public/realms')) return respond(route);
    const path = url.pathname === '/' ? '/realm.html' : url.pathname;
    const response = await route.fetch({ url: `${base}${path}${url.search}` });
    return route.fulfill({ response });
  };
  const authRoute = route => route.fulfill({ status: 401, contentType: 'application/json', body: '{}' });
  await page.context().route(localApi, respond);
  await page.context().route(realmRoutes, realmRoute);
  await page.context().route('https://api.arkya.online/**', authRoute);
  await page.context().addCookies([{ name: 'realm_fixture_cookie', value: 'must-not-reach-public-api', domain: `${realmId}.arkya.online`, path: '/', secure: true, httpOnly: true }]);
  try {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`${base}/realms.html?lang=en`);
    await page.locator('.realm-row').waitFor();
    check((await page.title()) === 'Arkya — Realms', 'Catalogue title is translated');
    check(await page.locator('.realm-row').getAttribute('href') === `${realmOrigin}/?lang=en`, 'Catalogue opens the dedicated public realm hostname');
    check((await page.locator('.realm-status').innerText()) === 'Servers ready', 'Readiness is explicitly labelled as servers');
    await page.screenshot({ path: 'output/playwright/realm-list-desktop.png', fullPage: true });
    await page.locator('.realm-row').click();
    await page.locator('[data-realm-overview]').waitFor({ state: 'visible' });
    check(page.url() === `${realmOrigin}/?lang=en`, 'Realm detail is selected from its hostname');
    check((await page.locator('[data-realm-players]').innerText()) === '—', 'Unavailable population is not displayed as zero');
    check((await page.locator('[data-realm-population-note]').innerText()).includes('not available'), 'Unavailable population has an explanation');
    check((await page.locator('[data-realm-servers]').innerText()) === '2 / 3', 'Realm shows observed readiness counts');
    check(await page.locator('[data-realm-global-link]').first().getAttribute('href') === 'https://arkya.online/?lang=en', 'Global navigation retains an absolute main-site URL');
    check(await page.locator('script[src*="auth"], script[src*="common"], canvas').count() === 0, 'Dedicated public page has no authentication or game bootstrap');
    check(apiRequests.every(request => !request.headers.cookie && !request.headers.authorization), 'Public API requests omit cookies and authorization');
    await page.screenshot({ path: 'output/playwright/realm-desktop.png', fullPage: true });
    await page.locator('[data-language="fr"]').click();
    check(await page.locator('[data-realm-global-link]').first().getAttribute('href') === 'https://arkya.online/?lang=fr', 'Language changes preserve the global URL origin');
    await page.setViewportSize({ width: 390, height: 844 });
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Dedicated page fits a mobile viewport');
    await page.screenshot({ path: 'output/playwright/realm-mobile.png', fullPage: true });
    scenario = 'outage';
    await page.locator('[data-realm-refresh]').click();
    await page.locator('[data-feed-state="stale"]').waitFor();
    check((await page.locator('#realm-title').innerText()) === realm.displayName, 'A transient outage preserves the last realm identity');
    check((await page.locator('[data-realm-status]').innerText()) === 'État non vérifié', 'An outage removes the ready indicator');
    check((await page.locator('[data-realm-servers]').innerText()) === '—', 'A stale snapshot does not claim current ready counts');
    scenario = 'ready';
    await page.locator('[data-realm-refresh]').click();
    await page.locator('[data-feed-state="ready"]').waitFor();
    check((await page.locator('[data-realm-status]').innerText()) === 'Serveurs prêts', 'Recovery restores the current status');
    scenario = 'old';
    await page.locator('[data-realm-refresh]').click();
    await page.locator('[data-realm-status][data-status="Unknown"]').waitFor();
    check((await page.locator('[data-realm-servers]').innerText()) === '—', 'An old server observation is not presented as live');
    scenario = 'missing';
    await page.reload();
    await page.locator('[data-realm-message]:not([hidden])').waitFor();
    check((await page.locator('[data-realm-message]').innerText()).includes('n’existe pas'), 'An unpublished realm has a distinct message');
    check(await page.locator('[data-realm-overview]').isHidden(), 'An unpublished realm displays no fabricated realm data');
    scenario = 'empty';
    await page.goto(`${base}/realms.html?lang=en`);
    await page.locator('[data-realm-message]:not([hidden])').waitFor();
    check((await page.locator('[data-realm-message]').innerText()).includes('No realms'), 'An empty catalogue is distinct from an outage');
    scenario = 'outage';
    await page.reload();
    await page.locator('[data-feed-state="error"]').waitFor();
    check((await page.locator('[data-realm-message]').innerText()).includes('unavailable'), 'An initial outage is clearly visible');
    scenario = 'ready';
    await page.goto(`${base}/?lang=fr`);
    await page.locator('#realms .realm-row').waitFor();
    check(await page.locator('header a[data-i18n="nav.realms"]').count() === 2, 'Desktop and mobile homepage navigation expose realms');
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'The homepage realm section fits a mobile viewport');
    const previousRequests = apiRequests.length;
    await page.goto(`${base}/realm.html?lang=en`);
    await page.locator('[data-realm-message]:not([hidden])').waitFor();
    check((await page.locator('[data-realm-message]').innerText()).includes('Open a realm'), 'A detail page without a realm hostname directs users to the catalogue');
    check(apiRequests.length === previousRequests, 'An invalid realm context makes no catalogue or detail API request');
    check(errors.length === 0, 'No uncaught browser exceptions');
    return { checks: checks.length, results: checks, errors };
  } finally {
    await page.goto('about:blank');
    await page.context().unroute(localApi, respond);
    await page.context().unroute(realmRoutes, realmRoute);
    await page.context().unroute('https://api.arkya.online/**', authRoute);
    await page.context().clearCookies({ name: 'realm_fixture_cookie' });
    page.off('pageerror', onError);
  }
}
