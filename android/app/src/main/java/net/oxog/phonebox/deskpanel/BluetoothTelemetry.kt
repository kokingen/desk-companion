package net.oxog.phonebox.deskpanel

import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException

/** v2 positional records; connection-local cache, discarded on every reconnect. */
internal class BluetoothTelemetry {
    private var descriptors:JSONArray?=null
    private var cpu:JSONObject?=null;private var memory:JSONObject?=null
    private var disk:JSONObject?=null;private var wifi:JSONObject?=null;private var ai:JSONObject?=null
    fun decode(a:JSONArray):JSONObject {
        if(a.length()!=15)throw IOException("Telemetry layout")
        if(!a.isNull(7))descriptors=a.getJSONArray(7)
        if(!a.isNull(9))cpu=a.getJSONObject(9)
        if(!a.isNull(10))memory=a.getJSONObject(10)
        if(!a.isNull(11))disk=a.getJSONObject(11)
        if(!a.isNull(12))wifi=a.getJSONObject(12)
        if(!a.isNull(13))ai=a.getJSONObject(13)
        val desc=descriptors?:throw IOException("Telemetry metadata missing")
        val values=a.getJSONArray(8);if(desc.length()!=values.length())throw IOException("Telemetry interface count")
        val sampled=a.getLong(5);val sequence=a.getLong(4);val interfaces=JSONArray()
        for(i in 0 until desc.length()){
            val d=desc.getJSONArray(i);val n=values.getJSONArray(i)
            val row=JSONObject().put("id",d.getString(0)).put("name",d.getString(1)).put("description",d.getString(2)).put("kind",d.getString(3)).put("physical",d.getBoolean(4))
            val keys=listOf("connected","rxBytes","txBytes","rawDownBps","rawUpBps","smoothDownBps","smoothUpBps","intervalMs")
            keys.forEachIndexed{k,key->row.put(key,n.get(k))}
            row.put("sampledAtUnixMs",sampled+n.getLong(8)).put("sequence",sequence).put("valid",n.getBoolean(9)).put("reason",n.get(10));interfaces.put(row)
        }
        val metrics=JSONObject().put("sequence",sequence).put("sampledAtUnixMs",sampled).put("monotonicMs",a.getDouble(6)).put("interfaces",interfaces)
            .put("cpu",cpu?:throw IOException("CPU missing")).put("memory",memory?:throw IOException("RAM missing")).put("disk",disk?:throw IOException("Disk missing")).put("wifiSignal",wifi?:JSONObject.NULL)
        return JSONObject().put("schema",a.getInt(0)).put("agentSessionId",a.getString(1)).put("serverSentAtUnixMs",a.getLong(2)).put("uptimeMs",a.getLong(3)).put("metrics",metrics).put("ai",ai?:throw IOException("Task state missing")).put("eventHistory",a.get(14))
    }
}
