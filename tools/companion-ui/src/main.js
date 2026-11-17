import {createAvatar} from '@bible-strong/avatar-web';
import {percent,ram,speed,fresh,nicText,aiState} from './model.js';
import {createTrend} from './trend.js';
const $=id=>document.getElementById(id);
function text(id,value){const el=$(id);if(el.textContent!==value)el.textContent=value;}
const native=window.Companion;
const character={name:'Desk Companion',body:{primary:{type:'cube',width:230,height:190,depth:160,roundness:0.75},nodes:[]},colors:{body:'#304f6b',eyes:'#8af5d3'},face:{eyes:{width:1.2,height:1.05,gap:1,shiftY:0},perspective:0.7}};
const avatar=createAvatar('#avatar',{character,state:'idle',size:'100%',followPointer:true,settleAfterMs:0,onStateChange:state=>{
  native?.record('robot',JSON.stringify({state,at:Date.now()}));
}});
let online=false,last=null,received=0,lastRobot='idle',lastHistory={samples:[],minutes:[],events:[]},session=null,lastMono=-1;
let count=0,start=performance.now(),lastFrame=start,missed=0,frameIntervals=[],fps=0;
let ages=[],receiveAges=[],serverDelays=[],sequenceGaps=0,previousSeq=0,frames=0,measuredRoute=null;
let detailAt=-Infinity,deviceAt=-Infinity;
const trend=createTrend();let trendDrawAt=-Infinity,wasFresh=false;
function update(statusOnly=false){
  const elapsed=performance.now()-received;
  const isFresh=last&&fresh(last.sampleAgeAtDisplayMs,elapsed,online);
  // Every real state updates the values immediately. The expiry clock only
  // refreshes age while the same state is still fresh; do not run the complete
  // metric/robot update a second time for each 100 ms tick.
  if(statusOnly&&isFresh&&wasFresh){drawTrend();text('freshness',Math.round(last.sampleAgeAtDisplayMs+elapsed)+' ms · MB/s');return;}
  if(wasFresh&&!isFresh)trend.break();wasFresh=!!isFresh;
  drawTrend();
  const calibrated=last&&Number.isFinite(last.sampleAgeAtDisplayMs);
  text('connection',!online?'离线 · 本地待机':!calibrated?'已连接 · 校时中':isFresh?(last.transport==='bluetooth'?'蓝牙 · 实时':'Wi-Fi · 备用'):'数据已过期');
  $('dot').className=isFresh?'live':'';
  const m=last?.metrics;
  const cpuAge=m&&last.serverSentAtUnixMs-m.cpu.sampledAtUnixMs;
  const memAge=m&&last.serverSentAtUnixMs-m.memory.sampledAtUnixMs;
  text('cpu',isFresh&&cpuAge<1000?percent(m?.cpu):'—');text('memory',isFresh&&memAge<1500?ram(m?.memory):'RAM —');
  text('ethernet',nicText(m?.interfaces||[],'ethernet',!!isFresh));
  text('wifi',nicText(m?.interfaces||[],'wifi',!!isFresh));
  text('freshness',!online?'电脑数据离线':!calibrated?'正在测量数据年龄':isFresh?Math.round(last.sampleAgeAtDisplayMs+elapsed)+' ms · MB/s':'电脑数据过期');
  const ai=aiState(last?.ai,!!isFresh);text('ai',ai.label);text('source',ai.source);
  if(ai.state!==lastRobot){lastRobot=ai.state;avatar.setState(ai.state);}
  if($('history').open)detailLive();
}
window.companionEvent=(event,data)=>{
  if(event==='state'){
    if(data.agentSessionId!==session){session=data.agentSessionId;previousSeq=0;lastMono=-1;}
    const route=session+':'+data.transport;
    if(route!==measuredRoute){measuredRoute=route;ages=[];receiveAges=[];serverDelays=[];sequenceGaps=0;previousSeq=0;}
    if(Number.isFinite(data.metrics?.monotonicMs)){if(data.metrics.monotonicMs<=lastMono)return;lastMono=data.metrics.monotonicMs;}
    online=true; // authenticated state is also the open signal when TLS connects before the WebView finishes loading
    const nativeNow=native?.monotonicMs?.();
    const queueMs=Number.isFinite(nativeNow)&&Number.isFinite(data.receivedAtPhoneMonotonicMs)?Math.max(0,nativeNow-data.receivedAtPhoneMonotonicMs):0;
    data.sampleAgeAtDisplayMs=Number.isFinite(data.sampleAgeAtReceiveMs)?data.sampleAgeAtReceiveMs+queueMs:null;
    last=data;received=performance.now();
    if(fresh(data.sampleAgeAtDisplayMs,0,true))trend.push(data,received);
    const seq=data.metrics?.sequence||0;
    if(previousSeq&&seq>previousSeq+1)sequenceGaps+=seq-previousSeq-1;
    previousSeq=seq;
    if(Number.isFinite(data.sampleAgeAtReceiveMs)){receiveAges.push(data.sampleAgeAtReceiveMs);if(receiveAges.length>3600)receiveAges.shift();}
    if(Number.isFinite(data.sampleAgeAtDisplayMs)){ages.push(data.sampleAgeAtDisplayMs);if(ages.length>3600)ages.shift();}
    if(Number.isFinite(data.serverToReceiveMs)){serverDelays.push(data.serverToReceiveMs);if(serverDelays.length>3600)serverDelays.shift();}
    update();
    native?.displayed?.(JSON.stringify({session:data.agentSessionId,sequence:seq,receivedAt:data.receivedAtPhoneMonotonicMs,sampledAt:data.metrics?.sampledAtUnixMs,serverSentAt:data.serverSentAtUnixMs,ageMs:data.sampleAgeAtDisplayMs,uncertaintyMs:data.clockUncertaintyMs}));
  } else if(event==='connection'){
    online=data.status==='online';if(!online){previousSeq=0;avatar.setState('idle');lastRobot='idle';}update();
  } else if(event==='history'){
    lastHistory=data;drawHistory();
  }
};
const touch=()=>{avatar.poke('bounce');native?.record('touch',JSON.stringify({gesture:'bounce',at:Date.now()}));};
$('avatar').addEventListener('pointerup',touch);
$('pair').onclick=()=>native?.pair();
$('bluetooth').onclick=()=>native?.bluetooth();
$('details').onclick=()=>{$('history').showModal();native?.history();detailLive();};
$('close').onclick=()=>$('history').close();
$('refresh').onclick=()=>native?.history();
$('forget').onclick=()=>native?.forget();
function detailLive(){
  const now=performance.now();if(now-detailAt<200)return;detailAt=now;
  if(now-deviceAt>10000){deviceAt=now;try{const d=JSON.parse(native?.diagnostics()||'{}');text('diagnostics',[d.model,d.android&&'Android '+d.android,d.paired].filter(Boolean).join(' · '));}catch{}}
  const d=last?.metrics?.disk;
  text('disk','磁盘读取 '+speed(d?.valid?d.readBps:null)+' / 写入 '+speed(d?.valid?d.writeBps:null)+(online?'':' · 离线历史值'));
  const signals=last?.metrics?.wifiSignal?.interfaces||[];
  text('signal','Wi-Fi 信号：'+(signals.length?signals.map(x=>Number.isFinite(x.rssiDbm)?x.rssiDbm+' dBm':'未知（系统返回 '+x.nativeError+'）').join(' / '):'不可用'));
  const t=diagnostics();text('timing',`帧回调 ${fps.toFixed(1)} FPS · 间隔 P95 ${t.frameIntervalP95Ms?.toFixed(1)||'—'} ms · 数据年龄 P95 ${t.ageP95Ms?.toFixed(0)||'—'} ms · 校时误差 ±${last?.clockUncertaintyMs?.toFixed(1)||'—'} ms · 丢失序号 ${sequenceGaps}`);
  const box=$('interfaces');const list=last?.metrics?.interfaces||[];
  while(box.children.length>list.length)box.lastElementChild.remove();
  list.forEach((n,i)=>{let el=box.children[i];if(!el){el=document.createElement('div');box.append(el);}
    const value=`${n.name} · ${n.kind} · ${n.physical?'物理':'虚拟/其他'} · ${n.connected?'已连接':'未连接'}\n原始 ↓ ${speed(n.rawDownBps)} ↑ ${speed(n.rawUpBps)} · 平滑 ↓ ${speed(n.smoothDownBps)} ↑ ${speed(n.smoothUpBps)} · ${n.intervalMs.toFixed(1)} ms`;
    if(el.textContent!==value)el.textContent=value;
  });
}
function chart(id,rows){const canvas=$(id),ctx=canvas.getContext('2d'),w=canvas.width,h=canvas.height;ctx.clearRect(0,0,w,h);
  for(let p=0;p<=100;p+=25){ctx.strokeStyle='#213346';ctx.beginPath();ctx.moveTo(0,h-p/100*h);ctx.lineTo(w,h-p/100*h);ctx.stroke();}
  const sorted=[...rows].reverse(),end=Date.now(),span=id==='longchart'?86400000:600000,start=end-span;
  for(const [key,color]of [['cpu','#80e6c6'],['mem','#7dacf1']]){ctx.strokeStyle=color;ctx.lineWidth=2;ctx.beginPath();let active=false,previous=0;sorted.forEach(r=>{const v=r[key];if(!Number.isFinite(v)||r.t<start||r.t>end||Number.isFinite(r.n)&&r.n<50){active=false;return;}if(r.t-previous>(id==='longchart'?90000:2500))active=false;const x=(r.t-start)/span*w,y=h-Math.max(0,Math.min(100,v))/100*h;active?ctx.lineTo(x,y):ctx.moveTo(x,y);active=true;previous=r.t;});ctx.stroke();}
}
function drawHistory(){chart('chart',lastHistory.samples||[]);chart('longchart',lastHistory.minutes||[]);const box=$('events');box.replaceChildren();
  for(const e of lastHistory.events||[]){const el=document.createElement('div');let label=e.body;try{const b=JSON.parse(e.body);label=b.state||b.pc||b.gesture||e.body}catch{}el.textContent=new Date(e.t).toLocaleString()+' · '+e.kind+' · '+label;box.append(el);}
}
function p95(a){if(!a.length)return null;const s=[...a].sort((a,b)=>a-b);return s[Math.ceil(s.length*.95)-1];}
function drawTrend(force=false){
  const now=performance.now();if(document.hidden||!force&&now-trendDrawAt<500)return;trendDrawAt=now;
  const canvas=$('livechart'),rect=canvas.getBoundingClientRect(),scale=Math.min(window.devicePixelRatio||1,2);
  const w=Math.max(1,Math.round(rect.width*scale)),h=Math.max(1,Math.round(rect.height*scale));
  if(canvas.width!==w||canvas.height!==h){canvas.width=w;canvas.height=h;}
  const ctx=canvas.getContext('2d');ctx.clearRect(0,0,w,h);ctx.lineWidth=scale;
  for(const p of [0,50,100]){const y=1+(h-2)*(1-p/100);ctx.strokeStyle='#263a4e';ctx.beginPath();ctx.moveTo(0,y);ctx.lineTo(w,y);ctx.stroke();}
  for(const [key,color] of [['cpu','#80e6c6'],['memory','#7dacf1']]){
    ctx.strokeStyle=ctx.fillStyle=color;ctx.lineWidth=1.5*scale;
    for(const path of trend.segments(key,now)){
      ctx.beginPath();path.forEach((p,i)=>{const x=(p.t-now+60000)/60000*w,y=1+(h-2)*(1-p.value/100);i?ctx.lineTo(x,y):ctx.moveTo(x,y);});ctx.stroke();
      if(path.length===1){const p=path[0];ctx.beginPath();ctx.arc((p.t-now+60000)/60000*w,1+(h-2)*(1-p.value/100),scale,0,Math.PI*2);ctx.fill();}
    }
  }
}
new ResizeObserver(()=>drawTrend(true)).observe($('livechart'));
function diagnostics(){return {transport:last?.transport||null,fps,frames,missedVsyncEstimate:missed,frameIntervalP95Ms:p95(frameIntervals),ageSamples:ages.length,ageP95Ms:p95(ages),receiveAgeP95Ms:p95(receiveAges),serverToReceiveP95Ms:p95(serverDelays),sequenceGaps,online,lastSequence:last?.metrics?.sequence||null,clockUncertaintyMs:last?.clockUncertaintyMs??null};}
window.companionDiagnostics=diagnostics;
window.companionTest={avatar,update};
function tick(now){const dt=now-lastFrame;lastFrame=now;frames++;if(dt<1000){frameIntervals.push(dt);if(frameIntervals.length>3600)frameIntervals.shift();missed+=Math.max(0,Math.round(dt/(1000/60))-1);}count++;
  if(now-start>=1000){fps=count*1000/(now-start);text('fps',fps.toFixed(0)+' FPS');start=now;count=0;}requestAnimationFrame(tick);
}
requestAnimationFrame(tick);setInterval(()=>update(true),100);
document.addEventListener('visibilitychange',()=>{if(document.hidden){avatar.pause();online=false;}else{lastFrame=start=performance.now();frames=count=missed=0;frameIntervals=[];avatar.resume();}update();});
window.addEventListener('pagehide',()=>avatar.destroy(),{once:true});
update();
