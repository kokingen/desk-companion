// In-memory display history only. Sampling stays on the PC; no extra network or database work.
export function createTrend(windowMs=60000){
  const series={cpu:[],memory:[]},fieldBreak={cpu:0,memory:0};let session=null,generation=0;
  const prune=now=>{for(const rows of Object.values(series)){while(rows.length&&rows[0].t<now-windowMs)rows.shift();}};
  return {
    series,
    push(state,now){
      if(state.agentSessionId!==session){session=state.agentSessionId;generation++;}
      prune(now);
      for(const key of ['cpu','memory']){
        const sample=state.metrics?.[key],rows=series[key];
        const age=state.serverSentAtUnixMs-sample?.sampledAtUnixMs;
        if(!sample?.valid||!Number.isFinite(sample.percent)||sample.percent<0||sample.percent>100||!Number.isFinite(age)||age<0||age>(key==='cpu'?1000:1500)){fieldBreak[key]++;continue;}
        const segment=generation+':'+fieldBreak[key];
        const identity=segment+':'+sample.sequence+':'+sample.sampledAtUnixMs;
        if(rows[rows.length-1]?.identity===identity)continue;
        rows.push({t:now,value:sample.percent,identity,generation:segment});
        if(rows.length>600)rows.shift();
      }
    },
    break(){generation++;},
    prune,
    segments(key,now){
      prune(now);const paths=[];let previous=null;
      for(const row of series[key]){
        if(!previous||row.generation!==previous.generation||row.t-previous.t>1500)paths.push([]);
        paths[paths.length-1].push(row);previous=row;
      }
      return paths;
    }
  };
}
