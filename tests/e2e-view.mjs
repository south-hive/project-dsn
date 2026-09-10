import assert from 'node:assert/strict';
import {chromium} from '../artifacts/browser-tools/node_modules/playwright/index.mjs';
const browser=await chromium.launch(process.env.DSN_BROWSER_EXECUTABLE?{executablePath:process.env.DSN_BROWSER_EXECUTABLE}:{});
try {
  const page=await browser.newPage({viewport:{width:1280,height:900}});
  const errors=[];let queries=0;page.on('pageerror',e=>errors.push(e.message));page.on('response',r=>{if(r.url().includes('/view?'))queries++;});
  await page.goto(process.env.DSN_VIEW_ADDRESS+'/demo');
  await page.waitForFunction(n=>document.getElementById('state').textContent.includes('읽은 결과 '+n+'건'),process.env.DSN_DEMO_COUNT);
  const initialQueries=queries;await page.waitForTimeout(1300);assert(queries>initialQueries);
  assert.equal(await page.locator('.card').count(),Number(process.env.DSN_DEMO_COUNT)/48);
  assert.match(await page.locator('#path').innerText(),/telemetry-demo.*scale.*sqlite/);
  assert.equal(await page.locator('#records tr').count(),40);
  await page.locator('#metric').selectOption('io_errors');
  assert((await page.locator('#legend span').count())>=2);
  await page.locator('#pause').click();
  assert.match(await page.locator('#state').innerText(),/일시정지/);
  await page.locator('#pause').click();
  await page.waitForFunction(()=>document.getElementById('state').textContent.includes('연결됨'));
  await page.locator('#metric').selectOption('latency_ms');
  await page.screenshot({path:'artifacts/e2e-desktop.png',fullPage:true});
  await page.setViewportSize({width:393,height:852});
  await page.screenshot({path:'artifacts/e2e-mobile.png',fullPage:true});
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
  assert.deepEqual(errors,[]);
  console.log('PASS demo View: automatic polling, DUT cards, charts, pause/resume, mobile layout');
} finally {await browser.close();}
