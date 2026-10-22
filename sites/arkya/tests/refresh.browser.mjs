// Run through playwright-cli run-code from the local site; all auth traffic is a fixture.
async (page) => {
  const base = new URL(page.url()).origin;
  const api = 'http://127.0.0.1:51817';
  const context = page.context();
  const pages = [page, await context.newPage()];
  const checks = [];
  const check = (condition, message) => { if (!condition) throw Error(message); checks.push(message); };
  let rotation = 0;
  let active = 0;
  let maximumConcurrent = 0;
  let revoked = false;
  const errors = [];
  const user = { accountId: '197d1fa3-83c2-45cb-bb26-f4b574e88ee8', displayName: 'Refresh Explorer',
    expiresAt: new Date(Date.now() + 3600000).toISOString() };
  const headers = { 'Access-Control-Allow-Origin': base, 'Access-Control-Allow-Credentials': 'true',
    'Access-Control-Allow-Headers': 'Authorization, Content-Type, X-Arkya-Refresh',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS', 'Cache-Control': 'no-store' };
  const configRoute = route => route.fulfill({ contentType: 'text/javascript',
    body: `window.ArkyaAuthConfig = Object.freeze({identityBaseUrl:'${api}',charactersBaseUrl:'${api}'});` });
  const authRoute = async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') return route.fulfill({ status: 204, headers });
    if (path.endsWith('/refresh')) {
      active++;
      maximumConcurrent = Math.max(maximumConcurrent, active);
      const received = await request.allHeaders();
      try {
        if (revoked) return await route.fulfill({ status: 401, headers });
        check(received['x-arkya-refresh'] === '1', 'Browser sends the refresh CSRF header');
        check(received.cookie === `arkya.refresh=fixture-${rotation}`, 'Browser uses the latest rotated HttpOnly cookie');
        await new Promise(resolve => setTimeout(resolve, 75));
        rotation++;
        await route.fulfill({ status: 200, contentType: 'application/json', headers: { ...headers,
          'Set-Cookie': `arkya.refresh=fixture-${rotation}; Path=/v1/auth; HttpOnly; SameSite=Lax; Max-Age=3600` },
          body: JSON.stringify({ ...user, sessionToken: 'R'.repeat(43) }) });
      } catch (error) { errors.push(error.message); await route.abort(); }
      finally { active--; }
    } else if (path.endsWith('/me')) {
      await route.fulfill({ status: revoked ? 401 : 200, contentType: 'application/json', headers, body: JSON.stringify(user) });
    } else if (path.endsWith('/logout')) {
      revoked = true;
      await route.fulfill({ status: 204, headers: { ...headers, 'Set-Cookie': 'arkya.refresh=; Path=/v1/auth; HttpOnly; SameSite=Lax; Max-Age=0' } });
    } else await route.fulfill({ status: 404, headers });
  };
  await context.route(`${base}/auth-config.js`, configRoute);
  await context.route(`${api}/**`, authRoute);
  await context.addCookies([{ name: 'arkya.refresh', value: 'fixture-0', domain: '127.0.0.1', path: '/v1/auth', httpOnly: true, sameSite: 'Lax' }]);
  try {
    await Promise.all(pages.map(tab => tab.goto(`${base}/account.html?lang=fr`)));
    await Promise.all(pages.map(tab => tab.locator('#auth-profile').waitFor({ state: 'visible' })));
    check(rotation === 2 && maximumConcurrent === 1, 'Two fresh tabs serialize refresh rotations with Web Locks');
    for (const tab of pages) {
      check(await tab.locator('#account-name').innerText() === user.displayName, 'New tab restores the account without Discord');
      check(await tab.evaluate(() => !document.cookie.includes('arkya.refresh') &&
        !JSON.stringify(sessionStorage).includes('fixture-') && !JSON.stringify(localStorage).includes('fixture-')),
      'Refresh secret is absent from JavaScript cookies and browser storage');
    }
    await page.evaluate(() => sessionStorage.clear());
    await page.reload();
    await page.locator('#auth-profile').waitFor({ state: 'visible' });
    check(rotation === 3, 'Clearing tab access storage still allows cookie restoration');
    await page.locator('#auth-logout').click();
    await page.locator('#discord-login:not([disabled])').waitFor({ state: 'visible' });
    check(!(await context.cookies(api)).some(cookie => cookie.name === 'arkya.refresh'), 'Logout removes the HttpOnly cookie');
    await pages[1].reload();
    await pages[1].locator('#discord-login:not([disabled])').waitFor({ state: 'visible' });
    check(await pages[1].evaluate(() => sessionStorage.getItem('arkya-session-v1') === null), 'Another tab clears its revoked access');
    check(errors.length === 0, 'No fixture protocol failures');
  } finally {
    for (const tab of pages) { await tab.evaluate(() => sessionStorage.clear()); await tab.goto('about:blank'); }
    await pages[1].close();
    await context.clearCookies({ name: 'arkya.refresh', domain: '127.0.0.1' });
    await context.unroute(`${base}/auth-config.js`, configRoute);
    await context.unroute(`${api}/**`, authRoute);
  }
  return { checks: checks.length, results: checks, errors };
}
