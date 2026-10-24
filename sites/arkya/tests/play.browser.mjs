// Run with playwright-cli run-code --filename tests/play.browser.mjs against serve.mjs.
// Identity responses are isolated fixtures. Download targets are inspected, never fetched.
async (page) => {
  const base = new URL(page.url()).origin;
  const api = 'https://api.arkya.online';
  const installer = 'https://github.com/Hantse/arkya-services/releases/download/launcher-v0.4.2/Arkya-Launcher-Setup.exe';
  const checks = [];
  const errors = [];
  const requests = [];
  const check = (value, label) => { if (!value) throw new Error(label); checks.push(label); };
  const onError = error => errors.push(error.message);
  const onRequest = request => requests.push(request.url());
  page.on('pageerror', onError);
  page.on('request', onRequest);
  let scenario = 'anonymous';
  let returnUrl;
  const token = 'Q'.repeat(43);
  const user = { accountId: '197d1fa3-83c2-45cb-bb26-f4b574e88ee8', displayName: 'QA Explorer',
    expiresAt: new Date(Date.now() + 3600000).toISOString() };
  const response = (route, data, status = 200) => route.fulfill({ status, contentType: 'application/json', headers: {
    'Access-Control-Allow-Origin': base, 'Access-Control-Allow-Credentials': 'true',
    'Access-Control-Allow-Headers': 'Authorization, Content-Type, X-Arkya-Refresh',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  }, body: JSON.stringify(data) });
  const apiRoute = async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (scenario === 'unavailable') { await route.abort(); return; }
    if (request.method() === 'OPTIONS') { await response(route, {}); return; }
    if (path.endsWith('/challenges')) {
      returnUrl = request.postDataJSON().returnUrl;
      await response(route, { challengeId: 'a'.repeat(32), authorizationUrl: `${api}/v1/auth/discord/authorize/${'a'.repeat(32)}`,
        expiresAt: new Date(Date.now() + 300000).toISOString(), pollIntervalSeconds: 5, returnUrl });
    } else if (path.includes('/authorize/')) {
      await route.fulfill({ status: 302, headers: { Location: returnUrl }, body: '' });
    } else if (path.endsWith('/exchange')) await response(route, { ...user, sessionToken: token });
    else if (path.endsWith('/me')) await response(route, user);
    else if (path.endsWith('/refresh')) await response(route, {}, 401);
    else if (path.endsWith('/logout')) await route.fulfill({ status: 204, headers: {
      'Access-Control-Allow-Origin': base, 'Access-Control-Allow-Credentials': 'true',
    } });
    else await response(route, {}, 404);
  };
  await page.context().route(`${api}/**`, apiRoute);
  try {
    await page.goto(`${base}/play.html?lang=fr`);
    check(await page.locator('#launcher-download').isVisible(), 'Download is immediately available without a session');
    check(await page.locator('#launcher-download').getAttribute('href') === installer, 'Download points directly to the versioned Windows launcher EXE installer');
    const stable = await page.request.get(`${base}/downloads/windows/latest-stable-win-x64.json`);
    check(stable.ok() && JSON.stringify(await stable.json()) === JSON.stringify({ schemaVersion: 1, channel: 'stable', available: false }), 'Stable distribution explicitly has no published release');
    const beta = await page.request.get(`${base}/downloads/windows/latest-win-x64.json`);
    const betaManifest = await beta.json();
    check(beta.ok() && betaManifest.version === '0.1.49' && betaManifest.packageUrl.includes('/windows-beta-0.1.49/') && betaManifest.launcherSessionProtocol === 1, 'The current Beta game publication includes the launcher session protocol');
    check(await page.locator('canvas, #game-cover, #game-fullscreen, #game-sign-in').count() === 0, 'The web player and its session gate are removed');
    for (const lang of ['en', 'fr']) {
      await page.getByRole('button', { name: lang === 'en' ? 'English' : 'Français', exact: true }).click();
      check(await page.locator('#launcher-download').innerText() === (lang === 'en' ? 'Install the launcher' : 'Installer le launcher'), `${lang}: localized download action`);
      check((await page.locator('meta[name="description"]').getAttribute('content')).includes(lang === 'en' ? 'Windows beta' : 'bêta Windows'), `${lang}: localized Windows metadata`);
      check((await page.locator('#launcher-format').innerText()).includes(lang === 'en' ? '.exe installer' : 'installateur .exe'), `${lang}: executable installer format is clear`);
      check((await page.locator('.installation-steps li').nth(0).innerText()).includes('Arkya-Launcher-Setup.exe'), `${lang}: launcher setup wizard instructions`);
      check((await page.locator('.installation-steps li').nth(1).innerText()).includes(lang === 'en' ? 'sign in with Discord in your browser' : 'connectez-vous avec Discord dans votre navigateur'), `${lang}: launcher Discord login comes before the game library`);
      check((await page.locator('.installation-steps li').nth(2).innerText()).includes(lang === 'en' ? 'Choose your folder' : 'Choisissez votre dossier'), `${lang}: player chooses the game installation folder`);
      check((await page.locator('.installation-steps li').nth(2).innerText()).includes(lang === 'en' ? 'Beta channel' : 'canal Bêta'), `${lang}: installation selects the published Beta channel`);
      check((await page.locator('.launcher-note p').innerText()).includes(lang === 'en' ? 'no Stable version has been published yet' : 'aucune version Stable n’est encore publiée'), `${lang}: unavailable Stable is distinguished from Beta`);
      check(!/ZIP|extray|extract/i.test(await page.locator('.installation-steps').innerText()), `${lang}: no obsolete archive extraction instructions`);
      check((await page.locator('.launcher-note a').getAttribute('href')).includes(`lang=${lang}`), `${lang}: local navigation retains language`);
      for (const width of [1440, 1100, 820, 390, 320]) {
        await page.setViewportSize({ width, height: 1000 });
        check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${lang}: download page fits ${width}px`);
        check(await page.locator('#launcher-download').isVisible(), `${lang}: download available at ${width}px`);
        if (lang === 'fr' && width === 320) await page.screenshot({ path: 'D:/Repositories/arkya/output/playwright/arkya-launcher-download-mobile.png', fullPage: true });
      }
    }
    await page.setViewportSize({ width: 1440, height: 1100 });
    await page.screenshot({ path: 'D:/Repositories/arkya/output/playwright/arkya-launcher-download-desktop.png', fullPage: true });
    for (const path of ['index.html', 'classes.html', 'development.html', 'terms.html', 'privacy.html']) {
      for (const lang of ['en', 'fr']) {
        await page.goto(`${base}/${path}?lang=${lang}`);
        check(await page.locator('a[data-i18n="nav.play"]').count() > 0, `${path}: ${lang} launcher navigation`);
        check(await page.locator('a[data-i18n="nav.play"]').first().innerText() === (lang === 'en' ? 'Install the launcher' : 'Installer le launcher'), `${path}: ${lang} clear installation label`);
        for (const width of [1440, 1100, 390, 320]) {
          await page.setViewportSize({ width, height: 1000 });
          check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${path}: ${lang} fits ${width}px`);
          if (path === 'index.html' && width <= 390) {
            const actions = await page.locator('.home-actions').boundingBox();
            const caption = await page.locator('.home-hero-caption').boundingBox();
            check(actions.y + actions.height < caption.y, `Homepage: ${lang} installation action does not overlap artwork caption at ${width}px`);
          }
        }
      }
    }
    await page.goto(`${base}/index.html?lang=en`);
    check(await page.locator('.home-essentials a[href="/play.html?lang=en"]').count() === 1, 'Homepage useful links expose launcher installation');
    scenario = 'unavailable';
    await page.goto(`${base}/play.html?lang=en`);
    check(await page.locator('#launcher-download').isVisible(), 'Identity outage does not hide or disable the launcher download');
    check(await page.locator('#launcher-download').getAttribute('href') === installer, 'Identity outage leaves the real download available');
    scenario = 'success';
    await page.goto(`${base}/account.html?lang=en&returnTo=play.html`);
    await page.locator('#discord-login:not([disabled])').click();
    await page.waitForURL(`${base}/play.html?lang=en`);
    check(await page.locator('#launcher-download').isVisible(), 'An old sign-in return opens the new download page');
    if (await page.locator('.menu-toggle').isVisible()) await page.locator('.menu-toggle').click();
    await page.getByRole('link', { name: 'My account · QA Explorer' }).waitFor();
    check((await page.locator('[data-auth-link]').allTextContents()).every(name => name === 'QA Explorer'), 'Site account restoration is preserved in desktop and mobile navigation');
    check(!page.url().includes(token), 'Site session is absent from the URL');
    check(!requests.some(url => /\/(?:game\/|unity-loader\.mjs|unity-payloads\.mjs|game-client\.mjs|play\.js)/.test(url)), 'No Unity loader, game payload or old player bootstrap is requested');
    check(!requests.some(url => url.includes('/v1/realms') || url.includes('/admissions')), 'The website does not choose a realm or request game admission');
    const noScriptContext = await page.context().browser().newContext({ javaScriptEnabled: false });
    try {
      const noScriptPage = await noScriptContext.newPage();
      await noScriptPage.goto(`${base}/play.html?lang=fr`);
      check(await noScriptPage.locator('#launcher-download').isVisible(), 'Download remains usable with JavaScript disabled');
      check(await noScriptPage.locator('#launcher-download').getAttribute('href') === installer, 'No-script download retains the versioned EXE URL');
      check((await noScriptPage.locator('.installation-steps').innerText()).includes('Arkya-Launcher-Setup.exe'), 'Setup instructions remain readable without JavaScript');
      check((await noScriptPage.locator('.installation-steps').innerText()).includes('Choisissez votre dossier'), 'Game folder instructions remain readable without JavaScript');
      check((await noScriptPage.locator('.installation-steps').innerText()).includes('connectez-vous avec Discord dans votre navigateur'), 'Launcher login instructions remain readable without JavaScript');
    } finally { await noScriptContext.close(); }
    check(errors.length === 0, 'No uncaught browser exceptions');
  } finally {
    await page.evaluate(() => sessionStorage.clear());
    await page.goto('about:blank');
    await page.context().unroute(`${api}/**`, apiRoute);
    page.off('pageerror', onError);
    page.off('request', onRequest);
  }
  return { checks: checks.length, results: checks, errors };
}
