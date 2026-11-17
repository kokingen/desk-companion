package net.oxog.phonebox.deskpanel

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import android.os.SystemClock
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

internal object HistoryResume {
    const val FEATURE="event-id-v1"
    const val CONTEXT="history-id-v1"
    fun isValid(value:String)=value.length<=128 && value.all{it in 'a'..'z'||it in 'A'..'Z'||it in '0'..'9'||it==':'||it=='-'||it=='_'}
    fun key(fingerprint:String)="historyAfterId:"+fingerprint
}

/** One writer on the Activity's IO executor. 1s samples: 6h; minute averages: 30d; events: 30d/max 10k. */
class CompanionStore(context: Context) : SQLiteOpenHelper(context, "companion.db", null, 2) {
    override fun onCreate(db: SQLiteDatabase) {
        db.execSQL("CREATE TABLE samples(t INTEGER PRIMARY KEY,cpu REAL,mem REAL,down REAL,up REAL)")
        db.execSQL("CREATE TABLE minutes(t INTEGER PRIMARY KEY,cpu REAL,mem REAL,down REAL,up REAL,n INTEGER)")
        db.execSQL("CREATE TABLE events(id INTEGER PRIMARY KEY AUTOINCREMENT,t INTEGER,kind TEXT,body TEXT,external_id TEXT)")
        db.execSQL("CREATE UNIQUE INDEX event_external ON events(external_id) WHERE external_id IS NOT NULL")
        db.execSQL("CREATE INDEX event_time ON events(t)")
        db.execSQL("CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT)")
    }
    override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        if(oldVersion<2){db.execSQL("ALTER TABLE events ADD COLUMN external_id TEXT");db.execSQL("CREATE UNIQUE INDEX event_external ON events(external_id) WHERE external_id IS NOT NULL")}
    }
    private var lastSample = 0L
    private var lastClean = 0L
    private var lastEventId = ""
    fun event(kind: String, body: String) {
        writableDatabase.insert("events", null, ContentValues().apply {
            put("t", System.currentTimeMillis()); put("kind", kind.take(40)); put("body", body.take(2048))
        })
    }
    fun setting(key: String, value: String) {
        writableDatabase.insertWithOnConflict("settings", null, ContentValues().apply { put("key",key);put("value",value.take(2048)) },SQLiteDatabase.CONFLICT_REPLACE)
    }
    fun historyCheckpoint(fingerprint: String): String {
        if(fingerprint.isEmpty())return ""
        readableDatabase.rawQuery("SELECT value FROM settings WHERE key=?",arrayOf(HistoryResume.key(fingerprint))).use{c->
            if(c.moveToFirst()&&!c.isNull(0)){val value=c.getString(0);if(HistoryResume.isValid(value))return value}
        }
        return ""
    }
    fun accept(state: JSONObject) {
        val now = System.currentTimeMillis()
        val mono = SystemClock.elapsedRealtime()
        val backlog=state.optJSONArray("eventHistory")
        if(backlog!=null && backlog.length()>0){
            // The cursor is the last event in this authenticated batch, never the live ai field or an ACK.
            val lastId=backlog.getJSONObject(backlog.length()-1).optJSONObject("status")?.optString("eventId").orEmpty()
            val peer=state.optString("historyPeer")
            val checkpoint=lastId.takeIf{it.isNotEmpty()&&peer.isNotEmpty()&&peer.length<=128&&HistoryResume.isValid(it)}
            val db=writableDatabase;db.beginTransaction()
            try{for(i in 0 until backlog.length()){
                val status=backlog.getJSONObject(i).getJSONObject("status")
                val external=status.optString("eventId")
                if(external.isNotEmpty())db.insertWithOnConflict("events",null,ContentValues().apply {
                    put("t",status.optLong("eventAtUnixMs",now));put("kind","task");put("body",status.toString());put("external_id",external)
                },SQLiteDatabase.CONFLICT_IGNORE)
            }
                // Same transaction: a crash can expose the stored events and their cursor together, never one without the other.
                if(checkpoint!=null)db.insertWithOnConflict("settings",null,ContentValues().apply {
                    put("key",HistoryResume.key(peer));put("value",checkpoint)
                },SQLiteDatabase.CONFLICT_REPLACE)
                db.setTransactionSuccessful()}finally{db.endTransaction()}
        }
        val ai = state.optJSONObject("ai")
        val id = ai?.optString("eventId").orEmpty()
        if (id.isNotEmpty() && id != "null" && id != lastEventId) {
            lastEventId=id
            writableDatabase.insertWithOnConflict("events",null,ContentValues().apply {
                put("t",ai?.optLong("eventAtUnixMs",now) ?: now);put("kind","task");put("body",ai?.toString() ?: "{}");put("external_id",id)
            },SQLiteDatabase.CONFLICT_IGNORE)
        }
        if (mono-lastSample < 1000) return
        lastSample=mono
        val m=state.optJSONObject("metrics") ?: return
        val sampledAt=m.optLong("sampledAtUnixMs",0)
        val age=CompanionSampleFreshness.ageAtWrite(state.optDouble("sampleAgeAtReceiveMs",Double.NaN),state.optDouble("clockUncertaintyMs",Double.NaN),state.optLong("receivedAtPhoneMonotonicMs",0),mono)
        fun fresh(field:JSONObject,budget:Double)=CompanionSampleFreshness.fieldIsFresh(age,sampledAt,field.optLong("sampledAtUnixMs",0),field.optBoolean("valid"),budget)
        val cpu=m.optJSONObject("cpu")?.let { if(fresh(it,1000.0))it.optDouble("percent",Double.NaN) else Double.NaN } ?: Double.NaN
        val mem=m.optJSONObject("memory")?.let { if(fresh(it,1500.0))it.optDouble("percent",Double.NaN) else Double.NaN } ?: Double.NaN
        // Retain individual interfaces in live UI; physical totals here are a historical chart only, exclude VPN/virtual.
        var down=0.0;var up=0.0;var count=0
        val interfaces=m.optJSONArray("interfaces") ?: JSONArray()
        for(i in 0 until interfaces.length()) { val n=interfaces.getJSONObject(i)
            if(n.optBoolean("physical") && n.optBoolean("connected") && fresh(n,1000.0) && n.optString("kind") in listOf("wifi","ethernet")) {
                down+=n.optDouble("rawDownBps",0.0);up+=n.optDouble("rawUpBps",0.0);count++
            }
        }
        val db=writableDatabase
        db.beginTransaction()
        try {
            // Use the estimated measurement instant on the phone clock. Unknown/expired frames create a gap now.
            val historyAt=if(age!=null && age.upperBoundMs<=1000 && sampledAt>0)now-age.estimatedMs.toLong() else now
            db.insertWithOnConflict("samples",null,ContentValues().apply {
                put("t",historyAt);if(cpu.isFinite())put("cpu",cpu);if(mem.isFinite())put("mem",mem)
                if(count>0){put("down",down);put("up",up)}
            },SQLiteDatabase.CONFLICT_IGNORE)
            val minute=historyAt/60000*60000
            // SQL aggregates ignore unknown fields. Window rolls up once each minute; no raw 100ms writes.
            db.execSQL("INSERT OR REPLACE INTO minutes SELECT ?,AVG(cpu),AVG(mem),AVG(down),AVG(up),MIN(COUNT(cpu),COUNT(mem)) FROM samples WHERE t>=? AND t<?",arrayOf(minute,minute,minute+60000))
            if(mono-lastClean>60000) {
                lastClean=mono
                db.delete("samples","t<?",arrayOf((now-6*3600000).toString()))
                db.delete("minutes","t<?",arrayOf((now-30L*86400000).toString()))
                db.delete("events","t<? OR id NOT IN (SELECT id FROM events ORDER BY id DESC LIMIT 10000)",arrayOf((now-30L*86400000).toString()))
            }
            db.setTransactionSuccessful()
        } finally { db.endTransaction() }
    }
    fun history(): String {
        val result=JSONObject()
        fun rows(sql: String, names: List<String>): JSONArray {
            val out=JSONArray()
            readableDatabase.rawQuery(sql,null).use { c -> while(c.moveToNext()) {
                val row=JSONObject();names.forEachIndexed { i,n -> row.put(n,if(c.isNull(i)) JSONObject.NULL else if(n in listOf("kind","body"))c.getString(i) else c.getDouble(i)) };out.put(row)
            } }
            return out
        }
        result.put("samples",rows("SELECT t,cpu,mem,down,up FROM samples ORDER BY t DESC LIMIT 600",listOf("t","cpu","mem","down","up")))
        result.put("minutes",rows("SELECT t,cpu,mem,down,up,n FROM minutes ORDER BY t DESC LIMIT 1440",listOf("t","cpu","mem","down","up","n")))
        result.put("events",rows("SELECT t,kind,body FROM events ORDER BY id DESC LIMIT 100",listOf("t","kind","body")))
        return result.toString()
    }
    fun diagnostics():JSONObject {
        val out=JSONObject();val db=readableDatabase
        db.rawQuery("PRAGMA quick_check(1)",null).use{c->if(c.moveToFirst())out.put("integrity",c.getString(0))}
        for(table in listOf("samples","minutes","events"))db.rawQuery("SELECT COUNT(*),MIN(t),MAX(t) FROM $table",null).use{c->if(c.moveToFirst())out.put(table,JSONObject().put("rows",c.getLong(0)).put("firstAt",if(c.isNull(1))JSONObject.NULL else c.getLong(1)).put("lastAt",if(c.isNull(2))JSONObject.NULL else c.getLong(2)))}
        return out
    }
    // Called only on the existing single-writer executor by the DUMP-protected diagnostic control.
    fun backup(output:File):JSONObject {
        val db=writableDatabase
        db.rawQuery("PRAGMA wal_checkpoint(FULL)",null).use{c->if(c.moveToFirst()&&c.getInt(0)!=0)throw IllegalStateException("History checkpoint busy")}
        check(!output.exists());File(db.path).copyTo(output)
        return diagnostics().put("backupFile",output.name).put("backupBytes",output.length())
    }
}
