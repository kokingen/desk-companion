export const STALE_MS=1000;
export const percent=(s)=>s?.valid&&Number.isFinite(s.percent)?s.percent.toFixed(1):'—';
export const ram=(s)=>s?.valid&&Number.isFinite(s.usedBytes)&&s.usedBytes>=0&&Number.isFinite(s.percent)&&s.percent>=0&&s.percent<=100?`RAM ${(s.usedBytes/1073741824).toFixed(1)} GiB · ${Math.round(s.percent)}%`:'RAM —';
export const speed=(n)=>Number.isFinite(n)?(n/1e6).toFixed(n>=1e6?2:3)+' MB/s':'—';
export function fresh(age,elapsed,connected){return connected&&Number.isFinite(age)&&age>=0&&age+elapsed<=STALE_MS;}
export function nicText(list,kind,live){
  const physical=list.filter(n=>n.kind===kind&&n.physical);
  if(!live)return '离线';
  if(!physical.length)return '无此网卡';
  if(physical.every(n=>n.reason?.startsWith('native_error_')))return '读取失败';
  const connected=physical.filter(n=>n.connected);
  if(!connected.length)return '未连接';
  // One line per physical NIC; never add physical and virtual interfaces together.
  return connected.map(n=>n.valid?'↓ '+speed(n.smoothDownBps)+'  ↑ '+speed(n.smoothUpBps):'采样中').join(' / ');
}
export function aiState(ai,live){
  if(!live||!ai||ai.state==='unknown')return {state:'idle',label:'状态未知',source:live?'暂无可靠任务事件':'离线 · 本地待机'};
  const labels={idle:'空闲',thinking:'思考中',searching:'搜索中',coding:'编写代码',working:'工作中',finished:'任务完成',happy:'任务完成',failed:'执行失败',attention:'需要操作'};
  return {state:ai.state==='attention'?'blocked':ai.state,label:labels[ai.state]||'状态未知',source:ai.source==='log_inferred'?'Codex · 日志推断':ai.source==='confirmed'?'Codex · 本地任务':'来源未知'};
}
