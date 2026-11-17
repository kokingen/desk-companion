package net.oxog.phonebox.deskpanel

import android.annotation.SuppressLint
import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.os.Build
import android.Manifest
import android.content.pm.PackageManager
import android.provider.Settings
import android.os.SystemClock
import android.view.WindowManager
import android.webkit.JavascriptInterface
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.EditText
import androidx.webkit.WebViewAssetLoader
import com.google.zxing.integration.android.IntentIntegrator
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

/** Offline-first companion shell; reuses PhoneBox's pinned TLS transport and SSE reconnect loop. */
class CompanionActivity : Activity() {
    private lateinit var web: WebView
    private lateinit var link: AgentLink
    private lateinit var stream: AgentStream
    private lateinit var bluetooth: BluetoothLink
    private lateinit var store: CompanionStore
    private lateinit var performance:CompanionPerformance
    private val io=Executors.newSingleThreadExecutor()
    private val clock=Executors.newSingleThreadScheduledExecutor()
    @Volatile private var pageReady=false
    @Volatile private var destroyed=false
    @Volatile private var resumed=false
    @Volatile private var connected=false
    @Volatile private var transport="none"
    private val persistLock=Any()
    private val historyFence=HistoryGenerationFence()
    @Volatile private var offset=0.0
    @Volatile private var uncertainty=Double.POSITIVE_INFINITY
    @Volatile private var syncAt=0L
    private var lastConnection=""
    private var lastPersistAt=0L
    private var lastTaskId=""
    private val deliveryLock=Any()
    private var pendingState:String?=null
    private var stateInFlight=false
    private var deliveryEpoch=0L

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        link=AgentLink(this, wifiOnly=true)
        store=CompanionStore(this)
        performance=CompanionPerformance(this,{store.diagnostics()},{io.submit<JSONObject>{store.backup(java.io.File(getExternalFilesDir("diagnostics"),"history-before-031-measurements.sqlite"))}.get(5,TimeUnit.SECONDS)})
        CompanionPerformance.foreground=java.lang.ref.WeakReference(performance)
        val assets=WebViewAssetLoader.Builder().addPathHandler("/assets/",WebViewAssetLoader.AssetsPathHandler(this)).build()
        web=WebView(this)
        web.setBackgroundColor(0xff080e18.toInt())
        web.settings.javaScriptEnabled=true
        web.settings.domStorageEnabled=true
        web.settings.allowFileAccess=false
        web.settings.allowContentAccess=false
        web.settings.setSupportMultipleWindows(false)
        WebView.setWebContentsDebuggingEnabled(BuildConfig.DEBUG)
        web.addJavascriptInterface(Bridge(),"Companion")
        web.webViewClient=object:WebViewClient(){
            override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse =
                assets.shouldInterceptRequest(request.url) ?: WebResourceResponse("text/plain","UTF-8",403,"Blocked",emptyMap(),ByteArrayInputStream(ByteArray(0)))
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest)=true
            override fun onPageFinished(view: WebView,url: String) {
                if(url==PAGE) { pageReady=true; connectionEvent() }
            }
        }
        setContentView(web)
        stream=AgentStream(link,refreshRoute=false){event,data ->
            if(destroyed)return@AgentStream
            when(event){
                "state" -> {
                    val state=runCatching { JSONObject(data) }.getOrNull() ?: return@AgentStream
                    if(transport!="bluetooth"){transport="wifi";connected=true;acceptState(state,"wifi")}
                }
                "open","close" -> {
                    if(transport!="bluetooth"){connected=event=="open";transport=if(connected)"wifi" else "none";connectionEvent()}
                }
            }
        }
        bluetooth=BluetoothLink(this,link,{event,state->
            if(!destroyed && resumed){
                if(event=="state"){
                    if(transport!="bluetooth"){transport="bluetooth";connected=true;stream.stop();connectionEvent()}
                    acceptState(state,"bluetooth")
                }else if(event=="close" && transport=="bluetooth"){
                    connected=false;transport="none";connectionEvent();stream.start()
                }
            }
        },{clockOffset,error->offset=clockOffset;uncertainty=error;syncAt=SystemClock.elapsedRealtime()},
        {fingerprint->
            try{io.submit<String>{store.historyCheckpoint(fingerprint)}.get(1,TimeUnit.SECONDS)}
            catch(e:InterruptedException){Thread.currentThread().interrupt();""}
            catch(_:Exception){""}
        })
        io.execute{store.setting("preferredTransport","bluetooth")}
        web.loadUrl(PAGE)
        clock.scheduleWithFixedDelay({synchronizeClock()},1,30,TimeUnit.SECONDS)
        // Pairing is initiated only by the in-app scanner/paste UI. Exported launcher intents cannot inject a PC.
    }
    private fun synchronizeClock() {
        if(destroyed || !resumed)return
        if(transport=="bluetooth" && bluetooth.authenticated){bluetooth.requestTime();return}
        var best=Double.POSITIVE_INFINITY;var bestOffset=0.0
        repeat(3){
            if(destroyed || !resumed)return
            val begin=SystemClock.elapsedRealtime().toDouble()
            runCatching {
                val reply=link.clock() ?: return@runCatching
                reply.body.bufferedReader().use{r ->
                    if(reply.code==200){val server=JSONObject(r.readText()).getDouble("unixMs");val end=SystemClock.elapsedRealtime().toDouble()
                        if(end-begin<best){best=end-begin;bestOffset=server-(begin+end)/2}
                    }
                }
            }
        }
        if(best.isFinite()){offset=bestOffset;uncertainty=best/2;syncAt=SystemClock.elapsedRealtime()}
    }
    private fun acceptState(state:JSONObject,source:String){
        if(destroyed || source=="wifi" && transport=="bluetooth")return
        if(source!="bluetooth"){state.remove("historyPeer");state.remove("historyGeneration")}
        val received=SystemClock.elapsedRealtime()
        state.put("transport",source).put("receivedAtPhoneMonotonicMs",received)
        state.put("clockUncertaintyMs",if(uncertainty.isFinite())uncertainty else JSONObject.NULL)
        val sample=state.optJSONObject("metrics")?.optLong("sampledAtUnixMs",0) ?: 0
        val age=received.toDouble()+offset-sample
        state.put("sampleAgeAtReceiveMs",if(syncAt>0 && received-syncAt<90000 && age>= -uncertainty)age.coerceAtLeast(0.0) else JSONObject.NULL)
        state.put("serverToReceiveMs",if(syncAt>0 && received-syncAt<90000)(received.toDouble()+offset-state.optLong("serverSentAtUnixMs")).coerceAtLeast(0.0) else JSONObject.NULL)
        performance.received(state,received,source)
        deliver("state",state.toString())
        synchronized(persistLock){val taskId=state.optJSONObject("ai")?.optString("eventId").orEmpty()
            if(received-lastPersistAt>=1000 || taskId!=lastTaskId || (state.optJSONArray("eventHistory")?.length() ?: 0)>0){
                lastPersistAt=received;lastTaskId=taskId;io.execute{
                    if(destroyed)return@execute
                    val epoch=state.optLong("historyGeneration",0L)
                    if(historyFence.isRejected(epoch))return@execute
                    try{store.accept(state)}
                    catch(e:Exception){historyFence.reject(epoch);performance.persistenceFailure(e);bluetooth.rejectHistoryGeneration(epoch)}
                }
            }
        }
    }
    private fun connectionEvent(){
        val status=if(connected)"online" else "offline"
        deliver("connection",JSONObject().put("status",status).put("route",transport).toString())
        val key="$transport:$status"
        io.execute{if(!destroyed && key!=lastConnection){lastConnection=key;store.event("connection",key)}}
    }
    private fun deliver(event: String,data: String) {
        if(!pageReady || destroyed)return
        if(event=="state"){
            val epoch=synchronized(deliveryLock){if(pendingState!=null)performance.coalesced();pendingState=data;if(stateInFlight)return;stateInFlight=true;deliveryEpoch}
            runOnUiThread{drainState(epoch)};return
        }
        runOnUiThread { if(!destroyed)web.evaluateJavascript("window.companionEvent&&window.companionEvent(${JSONObject.quote(event)},JSON.parse(${JSONObject.quote(data)}))",null) }
    }
    private fun drainState(epoch:Long){
        val state=synchronized(deliveryLock){if(epoch!=deliveryEpoch)return;if(destroyed||!resumed){pendingState=null;stateInFlight=false;return};val value=pendingState;pendingState=null;if(value==null){stateInFlight=false;return};value}
        web.evaluateJavascript("window.companionEvent&&window.companionEvent('state',JSON.parse(${JSONObject.quote(state)}))"){if(!destroyed)drainState(epoch)}
    }
    inner class Bridge {
        @JavascriptInterface fun pair(){runOnUiThread{pairDialog()}}
        @JavascriptInterface fun bluetooth(){runOnUiThread{bluetoothSetup()}}
        @JavascriptInterface fun history(){io.execute{if(!destroyed)deliver("history",store.history())}}
        @JavascriptInterface fun record(kind: String,value: String){io.execute{if(!destroyed)store.event(kind,value)}}
        @JavascriptInterface fun setting(key: String,value: String){io.execute{if(!destroyed)store.setting(key,value)}}
        @JavascriptInterface fun monotonicMs(): Double=SystemClock.elapsedRealtime().toDouble()
        @JavascriptInterface fun displayed(value:String){if(!destroyed)runCatching{performance.displayed(JSONObject(value))}}
        @JavascriptInterface fun forget(){runOnUiThread{AlertDialog.Builder(this@CompanionActivity).setMessage("忘记已配对电脑？")
            .setPositiveButton("忘记"){_,_->io.execute{bluetooth.stop();stream.stop();transport="none";connected=false;link.pcsJson().let { org.json.JSONArray(it) }.let{a->for(i in 0 until a.length())link.forget(a.getJSONObject(i).getString("id"))};runOnUiThread{connectionEvent();stream.start();bluetooth.start()}}}
            .setNegativeButton("取消",null).show()}}
        @JavascriptInterface fun diagnostics(): String = JSONObject().put("model",android.os.Build.MODEL).put("device",android.os.Build.DEVICE)
            .put("android",android.os.Build.VERSION.RELEASE).put("paired",link.pairedPc() ?: JSONObject.NULL).put("transport",transport).put("bluetooth",bluetooth.status).toString()
    }
    private fun bluetoothSetup(){
        if(Build.VERSION.SDK_INT>=31 && checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)!=PackageManager.PERMISSION_GRANTED){requestPermissions(arrayOf(Manifest.permission.BLUETOOTH_CONNECT),701);return}
        AlertDialog.Builder(this).setTitle("蓝牙主通道").setMessage("请在手机与 Windows 的系统蓝牙设置中配对并核对数字，然后返回本应用。电脑会优先通过蓝牙连接所选 Android 设备；断开后自动使用已配对的 Wi-Fi。")
            .setPositiveButton("打开蓝牙设置"){_,_->startActivity(Intent(Settings.ACTION_BLUETOOTH_SETTINGS))}.setNegativeButton("返回",null).show()
    }
    override fun onRequestPermissionsResult(requestCode:Int,permissions:Array<out String>,grantResults:IntArray){super.onRequestPermissionsResult(requestCode,permissions,grantResults);if(requestCode==701 && grantResults.firstOrNull()==PackageManager.PERMISSION_GRANTED){bluetooth.start();bluetoothSetup()}}
    private fun pairDialog(){
        val input=EditText(this).apply{hint="deskcompanion://pair?...";setSingleLine(false)}
        AlertDialog.Builder(this).setTitle("无线配对").setView(input)
            .setPositiveButton("配对"){_,_->confirmPair(Uri.parse(input.text.toString().trim()))}
            .setNeutralButton("扫码"){_,_->IntentIntegrator(this).setDesiredBarcodeFormats(IntentIntegrator.QR_CODE).setPrompt("扫描电脑上的配对二维码").setBeepEnabled(false).initiateScan()}
            .setNegativeButton("取消",null).show()
    }
    private fun confirmPair(uri: Uri){
        val pair=AgentLink.PairLink.parse(uri)
        if(pair==null){AlertDialog.Builder(this).setMessage("无效配对链接").setPositiveButton("确定",null).show();return}
        AlertDialog.Builder(this).setTitle("核对电脑校验码 ${pair.check}")
            .setMessage("电脑：${pair.pc}\n请确认与电脑上显示的校验码一致。\n证书 SHA-256：\n${pair.fingerprint}")
            .setPositiveButton("一致，连接"){_,_->io.execute{
                val error=link.claim(pair)
                if(error==null){store.event("pairing",JSONObject().put("pc",pair.pc).put("fingerprint",pair.fingerprint).toString());syncAt=0;bluetooth.stop();stream.stop();transport="none";connected=false;stream.start();bluetooth.start()}
                runOnUiThread{if(!destroyed)AlertDialog.Builder(this).setMessage(error ?: "配对成功").setPositiveButton("确定",null).show()}
            }}.setNegativeButton("取消",null).show()
    }
    override fun onNewIntent(intent: Intent){super.onNewIntent(intent)}
    @Deprecated("Android activity result API used by upstream ZXing integration")
    override fun onActivityResult(requestCode: Int,resultCode: Int,data: Intent?){
        val result=IntentIntegrator.parseActivityResult(requestCode,resultCode,data)
        if(result!=null){result.contents?.let{confirmPair(Uri.parse(it))}}else super.onActivityResult(requestCode,resultCode,data)
    }
    override fun onResume(){super.onResume();resumed=true;if(::web.isInitialized){web.onResume();web.resumeTimers()};if(::stream.isInitialized)stream.start();if(::bluetooth.isInitialized)bluetooth.start()}
    override fun onPause(){resumed=false;synchronized(deliveryLock){deliveryEpoch++;pendingState=null;stateInFlight=false};connected=false;transport="none";if(::stream.isInitialized)stream.stop();if(::bluetooth.isInitialized)bluetooth.stop();if(pageReady)connectionEvent();web.onPause();web.pauseTimers();super.onPause()}
    override fun onDestroy(){destroyed=true;stream.stop();bluetooth.close();performance.close();clock.shutdownNow();link.close();io.execute{store.close()};io.shutdown();web.removeJavascriptInterface("Companion");web.destroy();super.onDestroy()}
    companion object{private const val PAGE="https://appassets.androidplatform.net/assets/companion/index.html"}
}
