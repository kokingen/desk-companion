package net.oxog.phonebox.deskpanel

/** Standalone JVM checks: compileDebugUnitTestKotlin, then run this main with compiled classes + kotlin-stdlib. */
object CompanionFreshnessChecks {
    @JvmStatic fun main(args:Array<String>){
        var checks=0
        fun expect(value:Boolean,name:String){check(value){name};println("PASS: $name");checks++}
        fun age(sample:Double=40.0,error:Double=10.0,received:Long=1000,now:Long=1020)=CompanionSampleFreshness.ageAtWrite(sample,error,received,now)
        val recent=age()
        expect(recent?.estimatedMs==60.0 && recent.upperBoundMs==70.0,"write age includes measured transport age, queue wait and clock uncertainty")
        expect(CompanionSampleFreshness.fieldIsFresh(recent,10000,9900,true,1000.0),"recent valid CPU sample is retained")
        expect(age(sample=Double.NaN)==null,"missing clock calibration cannot produce a valid stored sample")
        expect(age(error=Double.NaN)==null && age(error=-1.0)==null,"unknown or invalid clock uncertainty is rejected")
        expect(age(received=0)==null && age(received=2000,now=1020)==null,"missing or future native receive timestamp is rejected")
        expect(!CompanionSampleFreshness.fieldIsFresh(age(now=2500),10000,9900,true,1000.0),"old queued frame becomes a history gap")
        expect(!CompanionSampleFreshness.fieldIsFresh(age(sample=1200.0),10000,10000,true,1500.0),"expired frame cannot become a valid memory row")
        expect(!CompanionSampleFreshness.fieldIsFresh(age(error=1100.0),10000,10000,true,1500.0),"large calibration uncertainty cannot disguise an expired frame")
        expect(!CompanionSampleFreshness.fieldIsFresh(recent,10000,8900,true,1000.0),"CPU field age is checked independently of frame age")
        expect(CompanionSampleFreshness.fieldIsFresh(recent,10000,8900,true,1500.0),"memory uses its explicit freshness budget")
        expect(!CompanionSampleFreshness.fieldIsFresh(recent,10000,10500,true,1500.0),"future field timestamp is rejected")
        expect(!CompanionSampleFreshness.fieldIsFresh(recent,10000,9990,false,1000.0),"native invalid samples remain invalid")
        println("$checks freshness checks passed")
    }
}
