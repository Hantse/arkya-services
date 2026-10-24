// Run with playwright-cli run-code --filename tests/beta.browser.mjs against serve.mjs.
// API and Discord responses are isolated fixtures. No production account is registered.
async (page) => {
  const base = new URL(page.url()).origin;
  const api = 'https://api.arkya.online';
  const checks = [];
  const errors = [];
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label); };
  const onError = error => errors.push(error.message);
  page.on('pageerror', onError);
  let scenario = 'normal';
  let registered = false;
  let granted = false;
  let writes = 0;
  let reads = 0;
  let callback;
  const token = 'B'.repeat(43);
  const user = { accountId: '197d1fa3-83c2-45cb-bb26-f4b574e88ee8', displayName: 'Beta Explorer',
    expiresAt: new Date(Date.now() + 3600000).toISOString() };
  const response = (route, data, status = 200) => route.fulfill({ status, contentType: 'application/json', headers: {
    'Access-Control-Allow-Origin': base, 'Access-Control-Allow-Credentials': 'true',
    'Access-Control-Allow-Headers': 'Authorization, Content-Type, X-Arkya-Refresh',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  }, body: JSON.stringify(data) });
  const apiRoute = async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (scenario === 'offline') { await route.abort(); return; }
    if (request.method() === 'OPTIONS') { await response(route, {}); return; }
    if (path.endsWith('/challenges')) {
      callback = request.postDataJSON().returnUrl;
      await response(route, { challengeId: 'a'.repeat(32), authorizationUrl: `${api}/v1/auth/discord/authorize/${'a'.repeat(32)}`,
        expiresAt: new Date(Date.now() + 300000).toISOString(), pollIntervalSeconds: 5, returnUrl: callback });
    } else if (path.includes('/authorize/')) {
      await route.fulfill({ status: 302, headers: { Location: callback }, body: '' });
    } else if (path.endsWith('/exchange')) await response(route, { ...user, sessionToken: token });
    else if (path.endsWith('/me')) await response(route, user);
    else if (path.endsWith('/refresh')) await response(route, {}, 401);
    else if (path === '/v1/beta/registration') {
      if (scenario === 'revoked') { await response(route, {}, 401); return; }
      check(request.headers().authorization === `Bearer ${token}`, 'Registration uses the account bearer');
      if (request.method() === 'POST') {
        writes += 1;
        check(JSON.stringify(request.postDataJSON()) === '{}', 'Registration sends no extra personal data');
        registered = true;
      } else reads += 1;
      await response(route, { registered, registeredAt: registered ? '2026-10-24T09:00:00Z' : null, hasBetaAccess: granted });
    } else await response(route, {}, 404);
  };
  await page.context().route(`${api}/**`, apiRoute);
  try {
    await page.goto(`${base}/beta.html?lang=en`);
    await page.locator('#beta-sign-in').waitFor();
    check(await page.locator('#beta-register').isHidden(), 'Anonymous visitors cannot submit a registration');
    check(writes === 0 && reads === 0, 'Anonymous visit does not call the registration endpoint');
    check((await page.locator('#beta-sign-in').getAttribute('href')).includes('returnTo=beta.html'), 'Discord login retains the beta destination');
    check(await page.locator('input').count() === 0, 'No email or extra personal fields are requested');
    for (const lang of ['en', 'fr']) {
      await page.getByRole('button', { name: lang === 'en' ? 'English' : 'Français', exact: true }).click();
      check(await page.title() === (lang === 'en' ? 'Arkya — Beta registration' : 'Arkya — Inscription à la bêta'), `${lang}: localized page title`);
      check((await page.locator('#beta-status').innerText()).includes(lang === 'en' ? 'confirm your registration' : 'confirmez votre inscription'), `${lang}: sign-in and registration are distinct`);
      check((await page.locator('.beta-waves>div>p:last-child').innerText()).includes(lang === 'en' ? 'manual or random' : 'manuelles ou aléatoires'), `${lang}: access waves are explained`);
      for (const width of [1440, 1100, 820, 760, 390, 320]) {
        await page.setViewportSize({ width, height: 1100 });
        check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${lang}: beta page fits ${width}px`);
        check(await page.locator('#beta-sign-in').isVisible(), `${lang}: sign-in remains available at ${width}px`);
      }
    }
    await page.screenshot({ path: 'D:/Repositories/arkya/output/playwright/arkya-beta-mobile.png', fullPage: true });
    await page.setViewportSize({ width: 1440, height: 1100 });
    await page.screenshot({ path: 'D:/Repositories/arkya/output/playwright/arkya-beta-desktop.png', fullPage: true });
    await page.getByRole('button', { name: 'English', exact: true }).click();
    await page.locator('#beta-sign-in').click();
    await page.locator('#discord-login:not([disabled])').click();
    await page.waitForURL(`${base}/beta.html?lang=en`);
    await page.locator('#beta-register:not([disabled])').waitFor();
    check(callback === `${base}/account.html?lang=en&returnTo=beta.html`, 'Discord callback preserves only the safe beta destination and language');
    check(writes === 0, 'Returning from Discord never automatically registers the account');
    check(await page.locator('#beta-account').innerText() === user.displayName, 'Registration identifies the verified account');
    await page.evaluate(() => { const button = document.querySelector('#beta-register'); button.click(); button.click(); });
    await page.locator('#beta-panel[data-state="pending"]').waitFor();
    check(writes === 1, 'Repeated clicks produce one registration write');
    check(await page.locator('#beta-date').isVisible(), 'Pending registration shows the server registration date');
    check(await page.locator('#beta-register').isHidden(), 'Registered accounts cannot submit another registration');
    await page.reload();
    await page.locator('#beta-panel[data-state="pending"]').waitFor();
    check(writes === 1, 'Reload reads registration without another write');
    check(reads >= 2, 'Registration status is restored from the server');
    granted = true;
    await page.locator('#beta-refresh').click();
    await page.locator('#beta-panel[data-state="granted"]').waitFor();
    check(await page.locator('#beta-play').getAttribute('href') === '/play.html?lang=en', 'Granted access opens the launcher page with the chosen language');
    scenario = 'offline';
    await page.locator('#beta-refresh').click();
    await page.locator('#beta-panel[data-state="error"]').waitFor();
    check(await page.locator('#beta-play').isHidden(), 'An unavailable API does not claim confirmed access');
    check(await page.locator('#beta-refresh').isVisible(), 'Service failure offers a retry');
    scenario = 'normal';
    await page.locator('#beta-refresh').click();
    await page.locator('#beta-panel[data-state="granted"]').waitFor();
    scenario = 'revoked';
    await page.locator('#beta-refresh').click();
    await page.locator('#beta-sign-in').waitFor();
    check(await page.evaluate(() => sessionStorage.getItem('arkya-session-v1')) === null, 'A rejected session clears the local bearer');
    check(await page.locator('#beta-account').isHidden(), 'Revoked session removes the account name');
    check(writes === 1, 'Retries and session revocation do not register again');
    scenario = 'normal';
    for (const path of ['index.html', 'account.html', 'play.html']) {
      await page.goto(`${base}/${path}?lang=en`);
      const link = path === 'account.html' ? page.locator('#auth-profile a[data-i18n="nav.betaRegister"]') : page.locator('a[href*="beta.html"]').first();
      check(await link.count() > 0, `${path}: beta registration is linked`);
      if (path === 'index.html') {
        check(await page.locator('.hero-atlas-link').getAttribute('href') === '/beta.html?lang=en', 'Homepage invitation links to beta registration');
        for (const width of [1440, 1200, 1100, 390, 320]) {
          await page.setViewportSize({ width, height: 1000 });
          check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Homepage beta navigation fits ${width}px`);
        }
      }
    }
    check(errors.length === 0, 'No uncaught browser exceptions');
  } finally {
    await page.evaluate(() => sessionStorage.clear());
    await page.goto('about:blank');
    await page.context().unroute(`${api}/**`, apiRoute);
    page.off('pageerror', onError);
  }
  return { checks: checks.length, results: checks, errors };
}
