package net.oxog.phonebox.deskpanel

/** Pure timestamp checks, shared by storage and JVM fixtures; no Android/database dependency. */
internal object CompanionSampleFreshness {
    data class Age(val estimatedMs:Double,val upperBoundMs:Double)
    fun ageAtWrite(sampleAgeAtReceiveMs:Double,uncertaintyMs:Double,receivedMonotonicMs:Long,nowMonotonicMs:Long):Age? {
        if(!sampleAgeAtReceiveMs.isFinite() || sampleAgeAtReceiveMs<0 || !uncertaintyMs.isFinite() || uncertaintyMs<0)return null
        if(receivedMonotonicMs<=0 || nowMonotonicMs<receivedMonotonicMs)return null
        val estimated=sampleAgeAtReceiveMs+(nowMonotonicMs-receivedMonotonicMs).toDouble()
        val upper=estimated+uncertaintyMs
        return if(upper.isFinite())Age(estimated,upper) else null
    }
    fun fieldIsFresh(age:Age?,snapshotAtUnixMs:Long,fieldAtUnixMs:Long,nativeValid:Boolean,budgetMs:Double):Boolean {
        if(age==null || !nativeValid || snapshotAtUnixMs<=0 || fieldAtUnixMs<=0)return false
        // Independent native loops can publish within the same millisecond; large future stamps are invalid.
        if(fieldAtUnixMs>snapshotAtUnixMs && fieldAtUnixMs-snapshotAtUnixMs>50)return false
        return age.upperBoundMs<=1000 && age.upperBoundMs+(snapshotAtUnixMs-fieldAtUnixMs).coerceAtLeast(0)<=budgetMs
    }
}
