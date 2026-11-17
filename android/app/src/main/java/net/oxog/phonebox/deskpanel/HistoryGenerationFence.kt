package net.oxog.phonebox.deskpanel

/** Constant-space monotonic fence for local Bluetooth history generations. */
internal class HistoryGenerationFence {
    @Volatile private var rejectedThrough=0L
    fun isRejected(generation:Long)=generation>0L && generation<=rejectedThrough
    fun reject(generation:Long){if(generation>rejectedThrough)rejectedThrough=generation}
    fun rejectedThrough():Long=rejectedThrough
}
