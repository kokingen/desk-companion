package net.oxog.phonebox.deskpanel

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothServerSocket
import android.bluetooth.BluetoothSocket
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.SystemClock
import android.util.Base64
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.nio.ByteBuffer
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.zip.GZIPInputStream
import java.util.zip.GZIPOutputStream
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/** Secure RFCOMM server. Windows connects only to an OS-paired phone, then both prove the existing PhoneBox key. */
class BluetoothLink(context: Context,private val link: AgentLink,private val deliver:(String,JSONObject)->Unit,private val clock:(Double,Double)->Unit,private val historyCheckpoint:(String)->String?) {
    private val app=context.applicationContext
    private val adapter=app.getSystemService(BluetoothManager::class.java)?.adapter
    @Volatile private var running=false
    @Volatile private var desired=false
    @Volatile private var generation=0
    private var historyGeneration=0L
    @Volatile private var activeHistoryGeneration=0L
    private val lifecycle=Any()
    @Volatile private var server: BluetoothServerSocket?=null
    @Volatile private var socket: BluetoothSocket?=null
    @Volatile var authenticated=false
        private set
    @Volatile var status="Not started"
        private set
    @Volatile private var lastReceive=0L
    private var thread: Thread?=null
    private val watchdog=Executors.newSingleThreadScheduledExecutor()
    private val pings=Executors.newSingleThreadScheduledExecutor()
    private val sendLock=Any()
    private var output: DataOutputStream?=null
    private var outputSocket:BluetoothSocket?=null
    private var outgoing=ByteArray(0)
    private var tx=0L
    private var pingId=0L
    @Volatile private var pendingId=0L
    @Volatile private var pingAt=0L
    private var clockWindowAt=0L
    private var bestError=Double.POSITIVE_INFINITY
    private var clockSamples=0
    private var windowed=false
    private var pendingAck=0L
    private var sentAck=0L
    init {
        watchdog.scheduleWithFixedDelay({if(desired && !running)start();val owned=socket;if(running && owned!=null && SystemClock.elapsedRealtime()-lastReceive>(if(authenticated)3000 else 5000))runCatching{owned.close()}},1,1,TimeUnit.SECONDS)
        pings.scheduleWithFixedDelay({requestTime()},5,5,TimeUnit.SECONDS)
        pings.scheduleWithFixedDelay({flushAck()},40,40,TimeUnit.MILLISECONDS)
    }
    fun hasPermission()=Build.VERSION.SDK_INT<31 || app.checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT)==PackageManager.PERMISSION_GRANTED
    @SuppressLint("MissingPermission")
    fun start()=synchronized(lifecycle){
        desired=true
        if(running)return
        if(!hasPermission()){status="Nearby devices permission required";return}
        if(adapter?.isEnabled!=true){status="Bluetooth is disabled";return}
        running=true;val gen=++generation
        thread=Thread({loop(gen)},"companion-bluetooth").apply{isDaemon=true;start()}
    }
    fun stop(){val owned=synchronized(lifecycle){desired=false;running=false;generation++;authenticated=false;activeHistoryGeneration=0L;val pair=server to socket;server=null;socket=null;thread?.interrupt();thread=null;pair};runCatching{owned.first?.close()};runCatching{owned.second?.close()};synchronized(sendLock){if(outputSocket===owned.second){output=null;outputSocket=null;outgoing.fill(0)}}}
    fun close(){stop();watchdog.shutdownNow();pings.shutdownNow()}
    fun rejectHistoryGeneration(epoch:Long){
        if(epoch<=0L)return
        val owned=synchronized(lifecycle){if(activeHistoryGeneration==epoch)socket else null}
        if(owned!=null)runCatching{owned.close()}
    }
    @SuppressLint("MissingPermission")
    private fun loop(gen: Int){
        val alive={running && generation==gen}
        while(alive()){
            var current:BluetoothSocket?=null
            var listening:BluetoothServerSocket?=null
            var receiveKey=ByteArray(0)
            var historyEpoch=0L
            try{
                if(!hasPermission() || adapter?.isEnabled!=true){status="Bluetooth disabled or permission missing";Thread.sleep(2000);continue}
                val owned=adapter.listenUsingRfcommWithServiceRecord("Desk Companion",SERVICE);listening=owned
                synchronized(lifecycle){if(!alive())throw IOException("Session cancelled");server=owned;status="Listening"}
                val accepted=owned.accept();current=accepted;owned.close()
                synchronized(lifecycle){if(server===owned)server=null;if(!alive())throw IOException("Session cancelled");socket=accepted;activeHistoryGeneration=0L;lastReceive=SystemClock.elapsedRealtime();status="Authenticating"}
                if(accepted.remoteDevice.bondState!=BluetoothDevice.BOND_BONDED)throw IOException("Bluetooth device is not OS-paired")
                val input=DataInputStream(accepted.inputStream);val out=DataOutputStream(accepted.outputStream)
                val hello=readPlain(input)
                if(hello.optInt("v")!=1)throw IOException("Unsupported Bluetooth protocol")
                val fp=hello.getString("pc");val identity=link.bluetoothIdentity(fp) ?: throw IOException("Pair this PC over Wi-Fi first")
                val cn=hello.getString("cn");if(Base64.decode(cn,Base64.NO_WRAP).size!=32)throw IOException("Invalid nonce")
                val sn=Base64.encodeToString(ByteArray(32).also{SecureRandom().nextBytes(it)},Base64.NO_WRAP)
                val root=MessageDigest.getInstance("SHA-256").digest(identity.token.toByteArray(Charsets.UTF_8))
                val flow=hello.optString("features")=="ack-route-v1"
                val latest=flow && hello.optString("flow")=="latest-window-v2" && hello.optInt("maxInFlight")==3
                val resume=hello.optString("historyResume")==HistoryResume.FEATURE
                val historyId=if(resume)(historyCheckpoint(identity.fingerprint)?.takeIf{HistoryResume.isValid(it)} ?: "") else ""
                val context="v1|$fp|$cn|$sn"+(if(flow)"|ack-route-v1" else "")+(if(latest)"|latest-window-v2|3" else "")+(if(resume)"|${HistoryResume.CONTEXT}|$historyId" else "")
                val challenge=JSONObject().put("sn",sn).put("features",if(flow)"ack-route-v1" else "").put("flow",if(latest)"latest-window-v2" else "").put("maxInFlight",if(latest)3 else 1).put("proof",Base64.encodeToString(mac(root,"phone|$context"),Base64.NO_WRAP))
                if(resume)challenge.put("historyResume",HistoryResume.FEATURE).put("historyAfterId",historyId)
                writePlain(out,challenge)
                val proof=readPlain(input).getString("proof")
                if(!MessageDigest.isEqual(mac(root,"pc|$context"),Base64.decode(proof,Base64.NO_WRAP)))throw IOException("PC identity rejected")
                receiveKey=mac(root,"pc-to-phone|$context")
                synchronized(lifecycle){if(!alive()||socket!==accepted)throw IOException("Session cancelled");synchronized(sendLock){outgoing=mac(root,"phone-to-pc|$context");root.fill(0);output=out;outputSocket=accepted;tx=0;windowed=latest;pendingAck=0;sentAck=0;pingAt=0;bestError=Double.POSITIVE_INFINITY;clockSamples=0;clockWindowAt=SystemClock.elapsedRealtime();authenticated=true;lastReceive=SystemClock.elapsedRealtime()};historyEpoch=++historyGeneration;activeHistoryGeneration=historyEpoch}
                requestTime()
                status="Authenticated"
                var receiveCounter=0L;val telemetry=BluetoothTelemetry()
                while(alive()){
                    val message=readSecure(input,receiveKey,receiveCounter+1);receiveCounter++
                    if(!alive()||socket!==accepted)throw IOException("Session cancelled")
                    lastReceive=SystemClock.elapsedRealtime()
                    when(message.getString("type")){
                        "state","telemetry"->{if(message.getString("type")=="telemetry" && !latest)throw IOException("Unnegotiated telemetry");val data=if(message.getString("type")=="telemetry")telemetry.decode(message.getJSONArray("data"))else message.getJSONObject("data");if(flow)synchronized(sendLock){if(!alive()||outputSocket!==accepted)throw IOException("Session cancelled");val seq=data.getJSONObject("metrics").getLong("sequence");if(latest)pendingAck=seq else writeSecure(JSONObject().put("type","ack").put("sequence",seq))};data.put("historyPeer",identity.fingerprint).put("historyGeneration",historyEpoch);deliver("state",data)}
                        "route"->{if(!flow)throw IOException("Unexpected route update");val hosts=message.getJSONArray("hosts");link.updateBluetoothHosts(fp,(0 until hosts.length()).map{hosts.getString(it)},message.getInt("port"))}
                        "time"->{val now=SystemClock.elapsedRealtime();if(message.getLong("id")==pendingId && pingAt>0){
                            val error=(now-pingAt)/2.0
                            if(now-clockWindowAt>60000){clockWindowAt=now;bestError=Double.POSITIVE_INFINITY}
                            // Prefer the least queued round trip rather than moving the clock with a delayed reply.
                            if(error<bestError){bestError=error;clock(message.getDouble("unixMs")-(pingAt+now)/2.0,error)}
                            pingAt=0;clockSamples++;if(clockSamples<3)requestTime()
                        }}
                        else->throw IOException("Unsupported Bluetooth message")
                    }
                }
            }catch(e:Exception){if(alive()){status=e.javaClass.simpleName;android.util.Log.w("CompanionBluetooth",e.javaClass.simpleName)}
            }finally{
                runCatching{listening?.close()};synchronized(lifecycle){if(server===listening)server=null}
                runCatching{current?.close()}
                synchronized(lifecycle){if(socket===current){authenticated=false;socket=null;if(activeHistoryGeneration==historyEpoch)activeHistoryGeneration=0L}}
                synchronized(sendLock){if(outputSocket===current){output=null;outputSocket=null;outgoing.fill(0)}}
                receiveKey.fill(0)
                if(alive())deliver("close",JSONObject())
            }
            if(alive())try{Thread.sleep(500)}catch(_:InterruptedException){}
        }
    }
    fun requestTime(){
        var owned:BluetoothSocket?=null
        try{synchronized(sendLock){
            if(!authenticated)return
            val now=SystemClock.elapsedRealtime();if(pingAt>0 && now-pingAt<3000)return
            owned=outputSocket;pendingId=++pingId;pingAt=now;writeSecure(JSONObject().put("type","ping").put("id",pendingId))
        }}catch(_:Exception){runCatching{owned?.close()}}
    }
    private fun flushAck(){
        var owned:BluetoothSocket?=null
        try{synchronized(sendLock){if(!authenticated || !windowed || pendingAck<=sentAck)return;owned=outputSocket;writeSecure(JSONObject().put("type","ack").put("sequence",pendingAck));sentAck=pendingAck}}
        catch(_:Exception){runCatching{owned?.close()}}
    }
    private fun readPlain(input:DataInputStream):JSONObject{val n=input.readInt();if(n !in 2..4096)throw IOException("Handshake size");val bytes=ByteArray(n);input.readFully(bytes);return JSONObject(String(bytes,Charsets.UTF_8))}
    private fun writePlain(out:DataOutputStream,value:JSONObject){val bytes=value.toString().toByteArray(Charsets.UTF_8);if(bytes.size>4096)throw IOException("Handshake size");out.write(ByteBuffer.allocate(4+bytes.size).putInt(bytes.size).put(bytes).array());out.flush()}
    private fun mac(key:ByteArray,text:String):ByteArray=Mac.getInstance("HmacSHA256").run{init(SecretKeySpec(key,"HmacSHA256"));doFinal(text.toByteArray(Charsets.UTF_8))}
    private fun nonce(counter:Long)=ByteBuffer.allocate(12).putInt(0x4D415833).putLong(counter).array()
    private fun writeSecure(value:JSONObject){
        val out=output ?: throw IOException("Bluetooth disconnected")
        val zipped=ByteArrayOutputStream().also{bytes->GZIPOutputStream(bytes).use{it.write(value.toString().toByteArray(Charsets.UTF_8))}}.toByteArray()
        val counter=++tx;if(counter<=0)throw IOException("Sequence exhausted")
        val aad=ByteBuffer.allocate(8).putLong(counter).array()
        val cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,SecretKeySpec(outgoing,"AES"),GCMParameterSpec(128,nonce(counter)));cipher.updateAAD(aad)
        val encrypted=cipher.doFinal(zipped);out.write(ByteBuffer.allocate(4+8+encrypted.size).putInt(8+encrypted.size).put(aad).put(encrypted).array());out.flush()
    }
    private fun readSecure(input:DataInputStream,key:ByteArray,expectedCounter:Long):JSONObject{
        val n=input.readInt();if(n !in 24..262144)throw IOException("Frame size")
        val packet=ByteArray(n);input.readFully(packet);val counter=ByteBuffer.wrap(packet,0,8).long
        if(counter!=expectedCounter)throw IOException("Replay/sequence mismatch")
        val cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,SecretKeySpec(key,"AES"),GCMParameterSpec(128,nonce(counter)));cipher.updateAAD(packet,0,8)
        val plain=cipher.doFinal(packet,8,n-8)
        val raw=ByteArrayOutputStream();GZIPInputStream(ByteArrayInputStream(plain)).use{gz->val buffer=ByteArray(4096);while(true){val read=gz.read(buffer);if(read<0)break;if(raw.size()+read>262144)throw IOException("Inflated frame size");raw.write(buffer,0,read)}}
        return JSONObject(raw.toString("UTF-8"))
    }
    companion object{val SERVICE:UUID=UUID.fromString("96fe3f30-25c4-4c20-b788-20dff0197b31")}
}
