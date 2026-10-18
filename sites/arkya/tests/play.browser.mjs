// Run with playwright-cli run-code --filename tests/play.browser.mjs against serve.mjs.
// API/build fixtures exist only inside this browser test and are never published.
async (page) => {
  const base = 'http://127.0.0.1:51806';
  const api = 'https://api.arkya.online';
  const checks = [];
  const errors = [];
  const requests = [];
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label); };
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => requests.push(request.url()));
  await page.goto(`${base}/play.html?lang=fr`);
  await page.locator('#game-sign-in').waitFor({ state: 'visible' });
  check(!requests.some(url => url.startsWith(api) || url.includes('/game/')), 'Signed-out production page loads neither API credentials nor Unity files');
  for (const lang of ['en', 'fr']) {
    await page.getByRole('button', { name: lang === 'en' ? 'English' : 'Français', exact: true }).click();
    check((await page.locator('#game-sign-in').getAttribute('href')).endsWith(`lang=${lang}`), `${lang}: sign-in return retains language`);
    for (const width of [1440, 1100, 820, 390, 320]) {
      await page.setViewportSize({ width, height: 1000 });
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${lang}: game page fits ${width}px`);
      if (lang === 'fr' && width === 320) await page.screenshot({ path: 'D:/Repositories/arkya/output/playwright/arkya-web-mobile.png', fullPage: true });
    }
  }
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page.screenshot({ path: 'D:/Repositories/arkya/output/playwright/arkya-web-sign-in.png', fullPage: true });
  for (const path of ['index.html', 'classes.html', 'development.html', 'terms.html', 'privacy.html']) {
    await page.goto(`${base}/${path}?lang=en`);
    check(await page.locator('a[data-i18n="nav.play"]').count() > 0, `${path}: Play navigation`);
    for (const width of [1440, 1100, 390, 320]) {
      await page.setViewportSize({ width, height: 1000 });
      check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${path}: fits ${width}px`);
    }
  }

  let scenario = 'unavailable';
  let expiry = Date.now() + 3600000;
  let authChecks = 0;
  let buildReads = 0;
  let revoked = false;
  const user = () => ({ accountId: '197d1fa3-83c2-45cb-bb26-f4b574e88ee8', displayName: 'QA Explorer', expiresAt: new Date(expiry).toISOString() });
  const token = 'Q'.repeat(43);
  const response = (route, data, status = 200) => route.fulfill({ status, contentType: 'application/json', headers: {
    'Access-Control-Allow-Origin': base, 'Access-Control-Allow-Headers': 'Authorization, Content-Type',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  }, body: JSON.stringify(data) });
  const apiRoute = async route => {
    if (scenario === 'unavailable') { await route.abort(); return; }
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (request.method() === 'OPTIONS') { await response(route, {}); return; }
    if (path.endsWith('/challenges')) {
      expiry = Date.now() + 3600000;
      await response(route, { challengeId: 'a'.repeat(32), authorizationUrl: `${api}/v1/auth/discord/authorize/${'a'.repeat(32)}`,
        expiresAt: new Date(Date.now() + 300000).toISOString(), pollIntervalSeconds: 5 });
    } else if (path.includes('/authorize/')) await route.fulfill({ contentType: 'text/html', body: '<p>Isolated QA authorization fixture.</p>' });
    else if (path.endsWith('/exchange')) await response(route, { ...user(), sessionToken: token });
    else if (path.endsWith('/me')) {
      authChecks++;
      check(request.headers().authorization === `Bearer ${token}`, 'Fixture /me receives the stored Arkya session in Authorization');
      await response(route, user(), revoked ? 401 : 200);
    } else if (path.endsWith('/logout')) { revoked = true; await route.fulfill({ status: 204, headers: { 'Access-Control-Allow-Origin': base } }); }
    else await response(route, {}, 404);
  };
  const gameRoute = async route => {
    buildReads++;
    if (scenario === 'missing-build') { await response(route, {}, 404); return; }
    if (route.request().url().endsWith('build-config.json')) {
      await response(route, { schemaVersion: 1, buildId: 'arkya-web-beta-v1', loaderUrl: 'Build/fixture.loader.js',
        dataUrl: 'Build/fixture.data.unityweb', frameworkUrl: 'Build/fixture.framework.js.unityweb', codeUrl: 'Build/fixture.wasm.unityweb',
        streamingAssetsUrl: 'StreamingAssets', companyName: 'Arkya', productName: 'Arkya', productVersion: 'QA-only' });
    } else {
      await route.fulfill({ contentType: 'text/javascript', body: `window.__qaBridge = []; window.createUnityInstance = async (_canvas, _config, progress) => {
        progress(1); return { SendMessage(object, method, payload) {
          if (method === 'AcceptSiteSession') {
            const value = JSON.parse(payload);
            if (object !== 'ArkyaWebSession' || value.sessionToken !== '${token}' || value.identityBaseUrl !== '${api}' || value.charactersBaseUrl !== '${api}') throw Error('Invalid fixture handoff');
            window.__qaBridge.push(method);
          } else { if (payload !== '') throw Error('Clear must not contain a token'); window.__qaBridge.push(method); }
        }, async Quit() { window.__qaBridge.push('Quit'); }, SetFullscreen() {} };
      };` });
    }
  };
  await page.context().route(`${api}/**`, apiRoute);
  await page.route(`${base}/game/**`, gameRoute);
  try {
    await page.goto(`${base}/play.html?lang=en`);
    await page.locator('#game-sign-in').click();
    check(new URL(page.url()).searchParams.get('returnTo') === 'play.html', 'Sign-in targets the allowlisted game page');
    await page.locator('#discord-login:not([disabled])').click();
    await page.locator('#auth-status.is-error').waitFor();
    check((await page.locator('#auth-status').innerText()).includes('unavailable'), 'Unavailable real-service route produces an honest error');
    check(buildReads === 0, 'Connection failure never reaches a build');

    scenario = 'success';
    await page.locator('#discord-login:not([disabled])').click();
    await page.locator('.game-frame.is-playing').waitFor({ timeout: 20000 });
    check(page.url() === `${base}/play.html?lang=en`, 'Successful account flow returns to the game in the same tab');
    check(authChecks >= 3, 'Page restore, load gate and handoff each verify /me');
    check(await page.evaluate(() => window.__qaBridge.join() === 'AcceptSiteSession'), 'Fixture engine receives exactly one session import');
    check(!page.url().includes(token), 'Session is absent from the URL');
    check(await page.evaluate(() => !Object.values(window).includes('Q'.repeat(43))), 'No persistent global bearer');
    await page.getByRole('button', { name: 'Français', exact: true }).click();
    check(await page.locator('.game-frame.is-playing').count() === 1, 'Language change preserves the running game');
    await page.evaluate(() => window.dispatchEvent(new CustomEvent('arkya:logout')));
    await page.locator('#game-sign-in').waitFor({ state: 'visible' });
    check(await page.evaluate(() => window.__qaBridge.slice(-2).join() === 'ClearSiteSession,Quit'), 'Unity logout clears the bridge before quitting');
    check(await page.evaluate(() => sessionStorage.getItem('arkya-session-v1') === null), 'Unity logout removes the tab session');

    revoked = false;
    await page.goto(`${base}/account.html?lang=fr&returnTo=https://evil.example`);
    await page.locator('#discord-login:not([disabled])').click();
    await page.locator('#auth-profile').waitFor({ state: 'visible', timeout: 15000 });
    check(new URL(page.url()).pathname === '/account.html', 'External return destination is ignored');
    scenario = 'missing-build';
    await page.goto(`${base}/play.html?lang=fr`);
    await page.locator('#game-heading').filter({ hasText: 'Le jeu est indisponible' }).waitFor();
    check(await page.locator('#game-retry').isVisible(), 'Missing actual build is an explicit retryable error');

    scenario = 'success';
    await page.reload();
    await page.locator('.game-frame.is-playing').waitFor();
    await page.evaluate(() => window.dispatchEvent(new CustomEvent('arkya:session-rejected')));
    await page.locator('#game-heading').filter({ hasText: 'Session refusée' }).waitFor();
    check(await page.evaluate(() => window.__qaBridge.slice(-2).join() === 'ClearSiteSession,Quit'), 'Unity session rejection tears down the engine');
    check(errors.length === 0, 'No uncaught browser exceptions');
  } finally {
    // Never let a fixture credential reach the real service on a subsequent navigation.
    await page.evaluate(() => sessionStorage.clear());
    await page.goto('about:blank');
    for (const child of page.context().pages()) if (child !== page) await child.close();
    await page.context().unroute(`${api}/**`, apiRoute);
    await page.unroute(`${base}/game/**`, gameRoute);
  }
  return { checks: checks.length, results: checks, errors };
}
