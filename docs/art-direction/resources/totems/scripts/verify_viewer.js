// Pass this function to Playwright CLI run-code with the local library server running.
async page => {
  const report = {schemaVersion: 2, checkedAt: new Date().toISOString(), passed: false,
    viewer: 'http://127.0.0.1:51805/totems/viewer/index.html', models: [], animations: [], downloads: [], errors: []};
  page.on('pageerror', error => report.errors.push(String(error)));
  await page.setViewportSize({width: 1440, height: 1000});
  await page.goto(report.viewer);
  await page.waitForFunction(() => !document.getElementById('play').disabled);
  const manifest = await page.evaluate(async () => (await fetch('../manifest.json', {cache: 'no-store'})).json());
  report.revision = manifest.revision;
  const loaded = async (species, coat) => {
    await page.waitForFunction(([species, coat]) => {
      const model = document.getElementById('totem');
      return model.src.includes(`/${species}-${coat}.glb?`) && !document.getElementById('play').disabled;
    }, [species, coat]);
  };
  for (const species of manifest.species) {
    await page.getByRole('button', {name: species.name, exact: true}).click();
    await loaded(species.id, species.defaultColorway);
    for (const coat of manifest.colorways) {
      await page.getByRole('button', {name: `Pelage ${coat.name}`, exact: true}).click();
      await loaded(species.id, coat.id);
      const model = await page.evaluate(() => {
        const m = document.getElementById('totem');
        return {src: m.src, clips: m.availableAnimations, materials: m.model.materials.length,
          palette: document.querySelectorAll('#palette>span').length};
      });
      if (model.clips.length !== 3 || model.materials !== 3 || model.palette !== 12) throw new Error(JSON.stringify(model));
      report.models.push({id: `${species.id}-${coat.id}`, ...model});
    }
    await page.getByRole('button', {name: `Pelage ${manifest.colorways.find(c => c.id === species.defaultColorway).name}`, exact: true}).click();
    await loaded(species.id, species.defaultColorway);
    for (const [clip, label] of [['Idle','Repos'], ['Walk','Marche'], ['Run','Course']]) {
      await page.getByRole('button', {name: label, exact: true}).click();
      if (await page.locator('#play').getAttribute('aria-pressed') !== 'true') await page.locator('#play').click();
      await page.locator('#playback-rate').selectOption('0.5');
      const start = await page.evaluate(() => document.getElementById('totem').currentTime);
      await page.waitForFunction(start => Math.abs(document.getElementById('totem').currentTime-start) > .025, start);
      await page.locator('#play').click();
      const paused = await page.evaluate(async () => {
        const m = document.getElementById('totem'), time = m.currentTime;
        for (let i=0; i<12; i++) await new Promise(requestAnimationFrame);
        return {duration: m.duration, timeScale: m.timeScale, paused: m.paused, delta: Math.abs(m.currentTime-time)};
      });
      const expected = species.locomotion.clips.find(c => c.name === clip).duration_seconds;
      if (!paused.paused || paused.delta > .00001 || paused.timeScale !== .5 || Math.abs(paused.duration-expected) > .002) throw new Error(JSON.stringify(paused));
      report.animations.push({species: species.id, clip, ...paused});
    }
    await page.locator('#magic').click();
    const emission = await page.evaluate(() => document.getElementById('totem').model.materials.filter(m => /^Totem(Magic|Spark)/.test(m.name)).map(m => [...m.emissiveFactor]));
    if (emission.length !== 2 || emission.flat().some(v => v !== 0)) throw new Error('Emission switch failed');
    await page.locator('#magic').click();
    const links = await page.locator('#downloads a').evaluateAll(links => links.map(a => a.href));
    for (const url of links) {
      const response = await page.request.get(url);
      if (!response.ok()) throw new Error(`Download failed ${url}`);
      report.downloads.push({url, status: response.status()});
      await response.dispose();
    }
    await page.getByRole('button', {name: 'Repos', exact: true}).click();
    await page.evaluate(() => {document.getElementById('totem').currentTime = 0;});
    await page.locator('#playback-rate').selectOption('1');
    await page.evaluate(() => window.scrollTo(0,0));
    await page.screenshot({path: `output/playwright/totem-v2-${species.id}.png`, fullPage: true});
  }
  await page.setViewportSize({width:390,height:844});
  await page.evaluate(() => window.scrollTo(0,0));
  report.mobileOverflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth);
  if (report.mobileOverflow) throw new Error('Mobile horizontal overflow');
  await page.screenshot({path:'output/playwright/totem-v2-mobile.png', fullPage:true});
  await page.setViewportSize({width:1440,height:1000});
  if (report.errors.length) throw new Error(JSON.stringify(report.errors));
  report.passed = true;
  return report;
}
