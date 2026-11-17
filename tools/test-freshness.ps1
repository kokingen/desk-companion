param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$android=Join-Path $PSScriptRoot '..\android'
if(-not $SkipBuild){Push-Location $android;try{.\gradlew.bat --no-daemon --max-workers=2 compileDebugUnitTestKotlin;if($LASTEXITCODE){throw 'JVM fixture compilation failed'}}finally{Pop-Location}}
$cache=if($env:GRADLE_USER_HOME){$env:GRADLE_USER_HOME}else{Join-Path ([Environment]::GetFolderPath('UserProfile')) '.gradle'}
$stdlib=Get-ChildItem (Join-Path $cache 'caches\modules-2\files-2.1\org.jetbrains.kotlin\kotlin-stdlib\*\*\*.jar') | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if(-not $stdlib){throw 'Kotlin runtime missing; run Gradle compilation first'}
$java=if($env:JAVA_HOME){Join-Path $env:JAVA_HOME 'bin\java.exe'}else{'java'}
$classes=(Join-Path $android 'app\build\tmp\kotlin-classes\debug')+';'+(Join-Path $android 'app\build\tmp\kotlin-classes\debugUnitTest')+';'+$stdlib.FullName
foreach($checkName in @('CompanionFreshnessChecks','HistoryGenerationChecks')){
    & $java -cp $classes ('net.oxog.phonebox.deskpanel.'+$checkName)
    if($LASTEXITCODE){throw ('JVM checks failed: '+$checkName)}
}
