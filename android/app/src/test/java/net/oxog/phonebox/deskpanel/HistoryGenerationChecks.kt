package net.oxog.phonebox.deskpanel

/** Standalone JVM checks: compileDebugUnitTestKotlin, then run this main with compiled classes + kotlin-stdlib. */
object HistoryGenerationChecks {
    @JvmStatic fun main(args:Array<String>){
        var checks=0
        fun expect(value:Boolean,name:String){check(value){name};println("PASS: $name");checks++}
        val fence=HistoryGenerationFence()
        expect(!fence.isRejected(0L) && !fence.isRejected(-1L),"non-Bluetooth or invalid generations are never fenced")
        expect(!fence.isRejected(1L),"generation 1 starts admitted")
        fence.reject(1L)
        expect(fence.isRejected(1L),"a failed generation 1 rejects its queued persistence work")
        expect(!fence.isRejected(2L),"a newer generation 2 remains admitted")
        fence.reject(2L)
        expect(fence.isRejected(1L) && fence.isRejected(2L) && fence.rejectedThrough()==2L,"rejecting generation 2 keeps all older generations rejected")
        fence.reject(1L)
        expect(fence.rejectedThrough()==2L && !fence.isRejected(3L),"a stale generation 1 failure neither lowers the fence nor blocks generation 3")
        fence.reject(3L)
        expect(fence.isRejected(1L) && fence.isRejected(2L) && fence.isRejected(3L) && !fence.isRejected(4L),"rejecting generation 3 rejects all older work and admits generation 4")
        println("$checks history-generation checks passed")
    }
}
