package net.oxog.phonebox.deskpanel

import android.app.Activity
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.BatteryManager
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.SystemClock
import android.os.Trace
import android.view.FrameMetrics
import android.view.Window
import org.json.JSONObject
import java.io.BufferedWriter
import java.io.File
import java.lang.ref.WeakReference
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong

/** Release-build measurements. DUMP-protected control; never exports pairing keys or task text. */
class CompanionPerformance(private val activity:Activity,private val history:()->JSONObject,private val historyBackup:()->JSONObject) {
    companion object { @Volatile var foreground=WeakReference<CompanionPerformance>(null) }
    private val started=SystemClock.elapsedRealtimeNanos()
    private val received=AtomicLong();private val dom=AtomicLong();private val visual=AtomicLong();private val coalesced=AtomicLong()
    private val persistenceFailures=AtomicLong()
    private val queue=ArrayBlockingQueue<Pair<Int,String>>(8192)
    private val dropped=AtomicLong()
    private val executor=Executors.newSingleThreadScheduledExecutor()
    private var pipeline:BufferedWriter?=null;private var frames:BufferedWriter?=null;private var thermal:BufferedWriter?=null
    private var frameThread:HandlerThread?=null
    @Volatile private var recording=false
    private var captureDirectory:File?=null
    private var captureStarted=0L
    private var captureDuration=0L
    private var nextThermal=0L
    private var lastDomKey=""
    private val ages=LongArray(5002);private val upperAges=LongArray(5002)
    private var latencySamples=0L
    private val histogramLock=Any()
    private val frameListener=Window.OnFrameMetricsAvailableListener { _,m,drop->
        if(recording)emit(1,listOf(SystemClock.elapsedRealtimeNanos(),System.nanoTime(),m.getMetric(FrameMetrics.INTENDED_VSYNC_TIMESTAMP),m.getMetric(FrameMetrics.VSYNC_TIMESTAMP),m.getMetric(FrameMetrics.TOTAL_DURATION),if(Build.VERSION.SDK_INT>=31)m.getMetric(FrameMetrics.DEADLINE)else 16666667,drop,m.getMetric(FrameMetrics.FIRST_DRAW_FRAME),m.getMetric(FrameMetrics.DRAW_DURATION),m.getMetric(FrameMetrics.SYNC_DURATION),if(Build.VERSION.SDK_INT>=31)m.getMetric(FrameMetrics.GPU_DURATION)else -1).joinToString(","))
    }
    init { executor.scheduleWithFixedDelay({drain()},100,100,TimeUnit.MILLISECONDS) }
    fun historyStatus()=history()
    fun backupHistory()=historyBackup()
    fun received(state:JSONObject,at:Long,route:String){
        received.incrementAndGet()
        if(recording){
            val m=state.optJSONObject("metrics");val cpu=m?.optJSONObject("cpu");val mem=m?.optJSONObject("memory")
            val n=m?.optJSONArray("interfaces");var networkInterval=Double.NaN
            if(n!=null)for(i in 0 until n.length()){val nic=n.getJSONObject(i);if(nic.optBoolean("physical")&&nic.optBoolean("connected")){networkInterval=nic.optDouble("intervalMs");break}}
            emit(0,listOf("receive",SystemClock.elapsedRealtimeNanos(),System.nanoTime(),state.optString("agentSessionId"),m?.optLong("sequence"),route,m?.optLong("sampledAtUnixMs"),state.optLong("serverSentAtUnixMs"),at,state.optDouble("sampleAgeAtReceiveMs"),state.optDouble("clockUncertaintyMs"),cpu?.optLong("sequence"),cpu?.optDouble("intervalMs"),mem?.optLong("sequence"),mem?.optDouble("intervalMs"),mem?.optLong("usedBytes"),mem?.optDouble("percent"),networkInterval,cpu?.optLong("sampledAtUnixMs"),mem?.optLong("sampledAtUnixMs"),state.optJSONArray("eventHistory")?.length()?:0,state.optJSONArray("eventHistory")?.let{if(it.length()>0)it.getJSONObject(it.length()-1).optLong("sequence")else 0}?:0,cpu?.optBoolean("valid"),mem?.optBoolean("valid"),cpu?.optDouble("percent")).joinToString(","))
            Trace.beginSection("DC_RX_${m?.optLong("sequence")}");Trace.endSection()
        }
    }
    fun coalesced(){coalesced.incrementAndGet()}
    fun persistenceFailure(error:Throwable?=null){persistenceFailures.incrementAndGet();if(error!=null)android.util.Log.w("CompanionStore",error.javaClass.simpleName)}
    fun displayed(data:JSONObject){
        val key=data.optString("session")+":"+data.optLong("sequence")
        synchronized(this){if(key==lastDomKey)return;lastDomKey=key}
        dom.incrementAndGet()
        val age=data.optDouble("ageMs");val uncertainty=data.optDouble("uncertaintyMs")
        if(age.isFinite()&&age>=0&&uncertainty.isFinite()&&uncertainty>=0)synchronized(histogramLock){latencySamples++;ages[kotlin.math.ceil(age).toInt().coerceIn(0,5001)]++;upperAges[kotlin.math.ceil(age+uncertainty).toInt().coerceIn(0,5001)]++}
        val seq=data.optLong("sequence")
        if(recording){emit(0,listOf("dom",SystemClock.elapsedRealtimeNanos(),System.nanoTime(),data.optString("session"),seq,"",data.optLong("sampledAt"),data.optLong("serverSentAt"),data.optLong("receivedAt"),age,uncertainty).joinToString(","));Trace.beginSection("DC_DOM_$seq");Trace.endSection()}
        // Visual fences are intrusive measurement work, not part of normal UI delivery.
        if(!recording)return
        activity.runOnUiThread {
            val web=activity.findViewById<android.view.View>(android.R.id.content) as? android.view.ViewGroup
            val view=web?.getChildAt(0) as? android.webkit.WebView
            view?.postVisualStateCallback(seq,object:android.webkit.WebView.VisualStateCallback(){override fun onComplete(requestId:Long){
                visual.incrementAndGet()
                if(recording){emit(0,listOf("visual",SystemClock.elapsedRealtimeNanos(),System.nanoTime(),data.optString("session"),seq,"",data.optLong("sampledAt"),data.optLong("serverSentAt"),data.optLong("receivedAt"),age,uncertainty).joinToString(","));Trace.beginSection("DC_VISUAL_$seq");Trace.endSection()}
            }})
        }
    }
    private fun percentile(hist:LongArray,q:Double):Long?{if(latencySamples==0L)return null;val target=kotlin.math.ceil(latencySamples*q).toLong();var n=0L;for(i in hist.indices){n+=hist[i];if(n>=target)return i.toLong()};return null}
    fun status(includeHistogram:Boolean=false):JSONObject {
        val out=JSONObject().put("elapsedMs",(SystemClock.elapsedRealtimeNanos()-started)/1e6).put("receivedStates",received.get()).put("domUpdates",dom.get()).put("visualReady",visual.get()).put("coalescedStates",coalesced.get()).put("recording",recording).put("droppedMeasurementRows",dropped.get()).put("persistenceFailures",persistenceFailures.get())
        synchronized(histogramLock){out.put("latencySamples",latencySamples).put("domAgeP95Ms",percentile(ages,.95)).put("domAgeP99Ms",percentile(ages,.99)).put("domAgeUpperP95Ms",percentile(upperAges,.95)).put("domAgeUpperP99Ms",percentile(upperAges,.99));if(includeHistogram){out.put("ageHistogramMs",org.json.JSONArray(ages.toList())).put("upperAgeHistogramMs",org.json.JSONArray(upperAges.toList()))}}
        return out
    }
    fun start(id:String,seconds:Int):JSONObject {
        require(id.matches(Regex("[A-Za-z0-9_-]{1,64}")) && seconds in 1..3600)
        check(!recording)
        val dir=File(activity.getExternalFilesDir("diagnostics"),id);check(!dir.exists());check(dir.mkdirs())
        captureDirectory=dir;captureStarted=SystemClock.elapsedRealtime();captureDuration=seconds*1000L;nextThermal=0;dropped.set(0)
        pipeline=File(dir,"pipeline.csv").bufferedWriter();frames=File(dir,"frames.csv").bufferedWriter();thermal=File(dir,"thermal.csv").bufferedWriter()
        pipeline!!.write("stage,bootNs,monoNs,session,sequence,route,sampledAtUnixMs,serverSentAtUnixMs,receivedAtBootMs,ageMs,clockErrorMs,cpuSequence,cpuIntervalMs,memorySequence,memoryIntervalMs,usedBytes,memoryPercent,networkIntervalMs,cpuSampledAtUnixMs,memorySampledAtUnixMs,eventHistoryCount,historyLastSequence,cpuValid,memoryValid,cpuPercent\n")
        frames!!.write("callbackBootNs,callbackMonoNs,intendedVsyncNs,vsyncNs,totalNs,deadlineNs,callbackDrops,firstDraw,drawNs,syncNs,gpuNs\n")
        thermal!!.write("bootMs,batteryCelsius,batteryLevel,plugged\n")
        val refresh=if(Build.VERSION.SDK_INT>=30)activity.display?.refreshRate?:60f else activity.windowManager.defaultDisplay.refreshRate
        File(dir,"start.json").writeText(status().put("version",BuildConfig.VERSION_NAME).put("measurementId",id).put("requestedSeconds",seconds).put("refreshRateHz",refresh).put("clockBootNs",SystemClock.elapsedRealtimeNanos()).put("clockMonoNs",System.nanoTime()).toString(2))
        val thread=HandlerThread("companion-frame-metrics");thread.start();frameThread=thread;recording=true
        activity.window.addOnFrameMetricsAvailableListener(frameListener,Handler(thread.looper))
        return status().put("directory",dir.name)
    }
    private fun emit(kind:Int,line:String){if(!queue.offer(kind to line))dropped.incrementAndGet()}
    @Synchronized private fun drain(){
        try {
            while(true){val item=queue.poll()?:break;val writer=if(item.first==0)pipeline else frames;writer?.write(item.second+"\n")}
            if(recording && SystemClock.elapsedRealtime()>=nextThermal){nextThermal=SystemClock.elapsedRealtime()+5000;val b=activity.registerReceiver(null,IntentFilter(Intent.ACTION_BATTERY_CHANGED));val raw=b?.getIntExtra(BatteryManager.EXTRA_TEMPERATURE,Int.MIN_VALUE);val celsius=if(raw!=null&&raw!=Int.MIN_VALUE)raw/10.0 else null;thermal?.write(listOf(SystemClock.elapsedRealtime(),celsius,b?.getIntExtra(BatteryManager.EXTRA_LEVEL,-1),b?.getIntExtra(BatteryManager.EXTRA_PLUGGED,-1)).joinToString(",")+"\n");pipeline?.flush();frames?.flush();thermal?.flush()}
            if(recording && SystemClock.elapsedRealtime()-captureStarted>=captureDuration)activity.runOnUiThread{stop()}
        }catch(e:Exception){dropped.incrementAndGet();android.util.Log.e("CompanionMeasure",e.javaClass.simpleName)}
    }
    @Synchronized fun stop():JSONObject {
        recording=false
        frameThread?.let{activity.window.removeOnFrameMetricsAvailableListener(frameListener);it.quitSafely();it.join(1000)};frameThread=null
        drain();pipeline?.close();frames?.close();thermal?.close();pipeline=null;frames=null;thermal=null
        val result=status().put("captureDurationMs",SystemClock.elapsedRealtime()-captureStarted);captureDirectory?.let{File(it,"summary.json").writeText(result.toString(2))};return result
    }
    fun close(){stop();executor.shutdown();if(foreground.get()===this)foreground=WeakReference(null)}
}

class CompanionDiagnosticsReceiver:BroadcastReceiver(){
    override fun onReceive(context:Context,intent:Intent){
        val perf=CompanionPerformance.foreground.get()
        if(perf==null){resultCode=1;resultData="Companion foreground activity unavailable";return}
        try{val result=when(intent.getStringExtra("command")){"start"->perf.start(intent.getStringExtra("id")?:"capture",intent.getIntExtra("seconds",60));"stop"->perf.stop();"histogram"->perf.status(true);"history"->perf.historyStatus();"backupHistory"->perf.backupHistory();else->perf.status()};resultCode=0;resultData=result.toString()}
        catch(e:Exception){resultCode=1;resultData=e.javaClass.simpleName}
    }
}
