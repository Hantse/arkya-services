// Run with playwright-cli run-code --filename tests/account.browser.mjs from a local serve.mjs page.
// All Identity responses are isolated fixtures; no request reaches the real API.
async (page) => {
  const base = new URL(page.url()).origin;
  const api = 'https://api.arkya.online';
  const pendingKey = 'arkya-discord-pending-v1';
  const checks = [];
  const errors = [];
  const navigations = [];
  const check = (value, label) => { if (!value) throw Error(label); checks.push(label); };
  const onError = error => errors.push(error.message);
  const onRequest = request => { if (request.isNavigationRequest()) navigations.push(request.url()); };
  page.on('pageerror', onError);
  page.on('request', onRequest);
  let scenario = 'success';
  let exchangeCount = 0;
  let authorizationCount = 0;
  let returnUrl;
  const expiry = new Date(Date.now() + 3600000).toISOString();
  const token = 'Q'.repeat(43);
  const user = { accountId: '197d1fa3-83c2-45cb-bb26-f4b574e88ee8', displayName: 'QA Explorer', expiresAt: expiry };
  const response = (route, data, status = 200, headers = {}) => route.fulfill({
    status, contentType: 'application/json', headers: {
      'Access-Control-Allow-Origin': base, 'Access-Control-Allow-Headers': 'Authorization, Content-Type',
      'Access-Control-Allow-Methods': 'GET, POST, OPTIONS', ...headers,
    }, body: JSON.stringify(data),
  });
  const routeApi = async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') { await response(route, {}); return; }
    if (path.endsWith('/challenges')) {
      returnUrl = request.postDataJSON().returnUrl;
      await response(route, { challengeId: 'a'.repeat(32), authorizationUrl: `${api}/v1/auth/discord/authorize/${'a'.repeat(32)}`,
        expiresAt: new Date(Date.now() + 300000).toISOString(), pollIntervalSeconds: 5,
        ...(scenario !== 'old-server' ? { returnUrl } : {}) });
    } else if (path.includes('/authorize/')) {
      authorizationCount++;
      await route.fulfill({ status: 302, headers: { Location: returnUrl }, body: '' });
    } else if (path.endsWith('/exchange')) {
      exchangeCount++;
      if (scenario === 'denied') await response(route, {}, 410);
      else if (scenario === 'pending') await response(route, { status: 'pending' }, 202, { 'Retry-After': '5' });
      else await response(route, { ...user, sessionToken: token });
    } else if (path.endsWith('/me')) await response(route, user);
    else if (path.endsWith('/logout')) await route.fulfill({ status: 204, headers: { 'Access-Control-Allow-Origin': base } });
    else await response(route, {}, 404);
  };
  const reset = async () => {
    await page.evaluate(() => sessionStorage.clear());
    await page.goto(`${base}/account.html?lang=en`);
    await page.locator('#discord-login:not([disabled])').waitFor();
  };
  await page.context().route(`${api}/**`, routeApi);
  try {
    await reset();
    await page.evaluate(() => {
      const setItem = Storage.prototype.setItem;
      Storage.prototype.setItem = function (key, value) {
        if (key === 'arkya-discord-pending-v1') throw new DOMException('Blocked', 'SecurityError');
        return setItem.call(this, key, value);
      };
    });
    await page.locator('#discord-login').click();
    await page.locator('#auth-status.is-error').waitFor();
    check((await page.locator('#auth-status').innerText()).includes('Allow site storage'), 'Blocked storage produces a visible explanation');
    check(authorizationCount === 0, 'Blocked storage never navigates to authorization');

    scenario = 'old-server';
    await reset();
    await page.locator('#discord-login').click();
    await page.locator('#auth-status.is-error').waitFor();
    check((await page.locator('#auth-status').innerText()).includes('Automatic return'), 'An old server fails visibly before navigation');
    check(authorizationCount === 0, 'No unsupported login leaves the account page');

    scenario = 'denied';
    await reset();
    await page.locator('#discord-login').click();
    await page.locator('#auth-status.is-error').waitFor();
    check(page.url() === `${base}/account.html?lang=en`, 'Discord denial returns to the website in the original language');
    check(await page.evaluate(key => sessionStorage.getItem(key) === null, pendingKey), 'Denial removes the saved proof');

    scenario = 'pending';
    await reset();
    await page.locator('#discord-login').click();
    await page.waitForFunction(key => JSON.parse(sessionStorage.getItem(key) || 'null')?.nextPollAt > Date.now(), pendingKey);
    const exchangesBeforeReload = exchangeCount;
    await page.reload();
    await page.locator('#auth-pending').waitFor({ state: 'visible' });
    check(await page.evaluate(key => sessionStorage.getItem(key) !== null, pendingKey), 'Pending proof survives pagehide and reload');
    check(exchangeCount === exchangesBeforeReload, 'Reload preserves the server polling delay');
    await page.locator('#auth-cancel').click();
    check(await page.evaluate(key => sessionStorage.getItem(key) === null, pendingKey), 'Cancel removes the persisted proof');
    check((await page.locator('#auth-status').innerText()).includes('cancelled'), 'Cancel is visible on the original page');

    await page.evaluate(key => sessionStorage.setItem(key, '{invalid'), pendingKey);
    await page.reload();
    await page.locator('#auth-status.is-error').waitFor();
    check(await page.evaluate(key => sessionStorage.getItem(key) === null, pendingKey), 'Corrupt proof is erased after reload');

    scenario = 'success';
    await reset();
    await page.locator('#discord-login').click();
    await page.locator('#auth-profile').waitFor({ state: 'visible' });
    check(await page.locator('#account-name').innerText() === 'QA Explorer', 'Automatic return displays the verified account');
    await page.goto(`${base}/privacy.html?lang=en`);
    await page.goBack();
    await page.locator('#auth-profile').waitFor({ state: 'visible' });
    check(await page.locator('#account-name').innerText() === 'QA Explorer', 'Browser Back restores the verified account');
    await page.evaluate(() => {
      window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
      window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
    });
    await page.locator('#auth-profile').waitFor({ state: 'visible' });
    check(await page.locator('#account-name').innerText() === 'QA Explorer', 'Persisted page lifecycle revalidates and displays the account');
    check(page.context().pages().length === 1, 'Every scenario uses one browser tab');
    check(navigations.every(url => !/[?&](?:code|state|sessionToken|codeVerifier)=/.test(url) && !url.includes(token)),
      'Site and authorization navigation URLs contain no proof or session secret');
    check(errors.length === 0, 'No uncaught browser exceptions');
  } finally {
    await page.evaluate(() => sessionStorage.clear());
    await page.goto('about:blank');
    await page.context().unroute(`${api}/**`, routeApi);
    page.off('pageerror', onError);
    page.off('request', onRequest);
  }
  return { checks: checks.length, results: checks, errors };
}
