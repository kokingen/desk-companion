// Synthetic fixtures are used only to assert UI state transitions in this test, never in the shipped application.
import {chromium} from '@playwright/test';
import http from 'node:http';import fs from 'node:fs/promises';import path from 'node:path';import assert from 'node:assert/strict';
const root=new URL('../../android/app/src/main/assets/companion/',import.meta.url);
const server=http.createServer(async(req,res)=>{try{const name=req.url==='/'?'index.html':path.basename(req.url);const data=await fs.readFile(new URL(name,root));res.setHeader('Content-Type',name.endsWith('.js')?'application/javascript':name.endsWith('.css')?'text/css':'text/html');res.end(data)}catch{res.writeHead(404);res.end()}});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const browser=await chromium.launch();const page=await browser.newPage({viewport:{width:360,height:720}});const errors=[];page.on('pageerror',e=>errors.push(String(e)));
try{
  await page.goto('http://127.0.0.1:'+server.address().port);
  await page.waitForSelector('#avatar svg');
  assert.match(await page.locator('#connection').innerText(),/离线/);
  // Saved pairing may open TLS before the page is ready; a real state must recover a dropped open signal.
  await page.evaluate(()=>{window.__svg=document.querySelector('#avatar svg');});
  const fixture={sampleAgeAtReceiveMs:30,serverSentAtUnixMs:10000,metrics:{sequence:1,cpu:{percent:37,valid:true,sampledAtUnixMs:9900},memory:{percent:62,valid:true,sampledAtUnixMs:9700},disk:{valid:false},interfaces:[{kind:'wifi',physical:true,connected:true,valid:true,smoothDownBps:1000000,smoothUpBps:250000,intervalMs:100}]},ai:{state:'coding',source:'log_inferred'}};
  await page.evaluate(s=>{for(let i=0;i<100;i++)window.companionEvent('state',{...s,metrics:{...s.metrics,sequence:i+1}})},fixture);
  assert.equal(await page.locator('#cpu').innerText(),'37.0');assert.match(await page.locator('#wifi').innerText(),/1.00 MB\/s/);
  assert.equal(await page.evaluate(()=>window.__svg===document.querySelector('#avatar svg')),true);
  await page.locator('#avatar').click();await page.waitForTimeout(1200);
  assert.equal(await page.locator('#livechart').evaluate(c=>{const p=c.getContext('2d').getImageData(0,0,c.width,c.height).data;for(let i=0;i<p.length;i+=4)if(p[i]<160&&p[i+1]>150&&p[i+2]>150&&p[i+3]>0)return true;return false;}),true,'trend canvas paints metric observations');
  assert.equal(await page.locator('#cpu').innerText(),'—');assert.match(await page.locator('#connection').innerText(),/过期/);
  for(const viewport of [{width:360,height:720},{width:320,height:568},{width:400,height:800}]){
    await page.setViewportSize(viewport);
    const layout=await page.evaluate(()=>{const c=document.querySelector('#livechart').getBoundingClientRect(),n=document.querySelector('.numbers').getBoundingClientRect(),w=document.querySelector('.network').getBoundingClientRect(),f=document.querySelector('footer').getBoundingClientRect();return {width:document.documentElement.scrollWidth,chartAfterMetrics:c.top>=n.bottom,chartBeforeNetwork:c.bottom<=w.top,footerBottom:f.bottom,canvasHeight:c.height};});
    assert.ok(layout.width<=viewport.width&&layout.chartAfterMetrics&&layout.chartBeforeNetwork&&layout.footerBottom<=viewport.height+1&&layout.canvasHeight>=24,'main metrics/trend/network remain visible at '+JSON.stringify(viewport));
  }
  await page.locator('#details').click();assert.equal(await page.locator('#history').evaluate(e=>e.open),true);
  assert.deepEqual(errors,[]);console.log('PASS: offline, fresh metrics fixture, live trend painting and three viewport layouts, no avatar DOM replacement after 100 updates, touch, stale timeout, details, no browser errors');
}finally{await browser.close();server.close();}
