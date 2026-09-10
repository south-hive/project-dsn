// Optional browser check: npm install --prefix artifacts/browser-tools playwright
import assert from 'node:assert/strict';
import { chromium } from '../artifacts/browser-tools/node_modules/playwright/index.mjs';
const browser = await chromium.launch(process.env.DSN_BROWSER_EXECUTABLE ? {executablePath: process.env.DSN_BROWSER_EXECUTABLE} : {});
try {
  const page = await browser.newPage({viewport: {width:1280,height:900}});
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(process.env.DSN_VIEW_ADDRESS);
  await page.locator('#connect').click();
  await page.waitForFunction(() => document.getElementById('health').textContent === '연결됨');
  await page.locator('#workspace').selectOption('bench');
  assert.match(await page.locator('#pipeline-path').innerText(), /Source.*bench.*sqlite/);
  await page.locator('#read').click();
  await page.waitForFunction(() => document.querySelectorAll('#records tr').length > 1);
  const before = await page.locator('#records tr').count();
  assert.equal(await page.locator('#records img').count(), 0);
  await page.locator('#metric').selectOption('value_read_iops');
  await page.locator('#source').fill('telemetry-app');
  assert((await page.locator('#records tr').count()) < before);
  await page.locator('#source').fill('');
  const downloadPromise = page.waitForEvent('download');
  await page.locator('#download').click();
  assert.equal((await downloadPromise).suggestedFilename(), 'dsn-records.json');
  await page.locator('#raw-first').click();
  await page.waitForSelector('#raw details');
  await page.locator('#raw summary').first().click();
  await page.locator('#raw button').first().click();
  await page.waitForFunction(() => document.getElementById('status').textContent.includes('replay_id'));
  await page.locator('#read').click();
  await page.waitForFunction(n => document.querySelectorAll('#records tr').length === n + 1, before);
  await page.screenshot({path:'artifacts/presenter-desktop.png',fullPage:true});
  await page.setViewportSize({width:393,height:852});
  await page.screenshot({path:'artifacts/presenter-mobile.png',fullPage:true});
  assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), JSON.stringify(await page.evaluate(() => [...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth).slice(0,8).map(e=>({tag:e.tagName,id:e.id,width:e.getBoundingClientRect().width})))));
  assert.deepEqual(errors, []);
  console.log('PASS Presenter selection, source filter, chart, download, replay and mobile layout');
} finally { await browser.close(); }
