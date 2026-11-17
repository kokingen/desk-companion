param(
    [Parameter(Mandatory)][string]$InstallRoot,
    [Parameter(Mandatory)][string]$AdbPath,
    [ValidateRange(0.001,168)][double]$Hours=24,
    [ValidateRange(10,60)][int]$IntervalSeconds=30,
    [switch]$KeepSystemAwake
)
$ErrorActionPreference='Stop'
$InstallRoot=(Resolve-Path -LiteralPath $InstallRoot).Path
$AdbPath=(Resolve-Path -LiteralPath $AdbPath).Path
$target=Join-Path $InstallRoot 'windows\DeskCompanionAgent.exe'
$logRoot=Join-Path $InstallRoot ('logs\release-stability-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $logRoot|Out-Null
$credential=(Get-Content -LiteralPath (Join-Path $InstallRoot 'private-data\admin-token.txt') -Raw).Trim()
$headers=@{Authorization='Bearer '+$credential}
$receiver='net.oxog.phonebox.deskpanel.max3/net.oxog.phonebox.deskpanel.CompanionDiagnosticsReceiver'
$package='net.oxog.phonebox.deskpanel.max3'
$requiredPhoneObservationHours=24.0
$requiredRequestedHours=24.0
function Adb([string[]]$Arguments){
    $stdout=Join-Path $logRoot 'adb-current.stdout';$stderr=Join-Path $logRoot 'adb-current.stderr'
    $p=Start-Process -FilePath $AdbPath -ArgumentList $Arguments -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    if(-not $p.WaitForExit(8000)){$p.Kill();throw 'ADB observation timed out'}
    if($p.ExitCode){throw 'ADB observation failed'}
    return Get-Content -LiteralPath $stdout -Raw
}
function Phone([string]$Command){
    $reply=Adb @('shell','am','broadcast','-a','net.oxog.phonebox.deskpanel.DIAGNOSTICS','-n',$receiver,'--es','command',$Command)
    if($reply -notmatch 'result=0, data="([^\r\n]*)"'){throw 'Phone foreground diagnostic is unavailable'}
    return $Matches[1]|ConvertFrom-Json
}
function FileSha256([string]$Path){
    $stream=[System.IO.File]::OpenRead($Path)
    try {
        $sha=[System.Security.Cryptography.SHA256]::Create()
        try { return (($sha.ComputeHash($stream)|ForEach-Object{$_.ToString('x2')}) -join '') }
        finally { $sha.Dispose() }
    } finally { $stream.Dispose() }
}
$watch=[Diagnostics.Stopwatch]::StartNew()
$started=[DateTimeOffset]::Now
$count=0;$errors=0;$nativeStale=0;$phoneStale=0;$disconnects=0;$gaps=0
$agentSessionRestarts=0;$agentPidRestarts=0;$phoneRestarts=0;$connectionChanges=0;$lifetimeCounterResets=0
$phoneObservationFailures=0;$integrityFailures=0
$lastAt=0.0;$lastHistory=-1800.0;$lastHistogram=-3600.0;$next=0.0
$prevNative=$null;$prevBt=$null;$prevPhone=$null;$prevCpu=$null
$initialCpu=$null;$finalCpu=$null;$finalCpuError=$null;$agentCpuMs=0.0;$agentCpuWallMs=0.0
$initialPhone=$null;$finalPhone=$null;$finalPhoneError=$null;$finalPhoneFresh=$false;$lastPhoneAt=0.0
$initialHistory=$null;$finalHistory=$null;$finalNative=$null;$finalBluetooth=$null
$samples=Join-Path $logRoot 'observations.jsonl'
$startRecord=[ordered]@{startedAt=$started.ToString('o');requestedHours=$Hours;requiredRequestedHours=$requiredRequestedHours;requiredPhoneObservationHours=$requiredPhoneObservationHours;intervalSeconds=$IntervalSeconds;exeSha256=(FileSha256 $target);scopedSystemAwake=[bool]$KeepSystemAwake;scope='Real Windows API/ACK + DUMP-protected release APK receive/DOM counters, histogram, SQLite integrity, battery temperature; no simulated samples; no automatic app restoration'}
$startRecord|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $logRoot 'start.json')
$power=[IntPtr]::Zero
if($KeepSystemAwake){
    if(-not ('CompanionTestPower' -as [type])){
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CompanionTestPower {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct Reason {public uint Version;public uint Flags;[MarshalAs(UnmanagedType.LPWStr)] public string Text;}
 [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr PowerCreateRequest(ref Reason reason);
 [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] public static extern bool PowerSetRequest(IntPtr handle,int type);
 [DllImport("kernel32.dll")] public static extern bool PowerClearRequest(IntPtr handle,int type);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
'@
    }
    $reason=[CompanionTestPower+Reason]::new();$reason.Version=0;$reason.Flags=1;$reason.Text='Desk Companion requested continuous stability measurement'
    $power=[CompanionTestPower]::PowerCreateRequest([ref]$reason)
    if($power -eq [IntPtr]::Zero -or $power -eq [IntPtr]::new(-1) -or -not [CompanionTestPower]::PowerSetRequest($power,1)){
        if($power -ne [IntPtr]::Zero -and $power -ne [IntPtr]::new(-1)){[CompanionTestPower]::CloseHandle($power)|Out-Null}
        throw 'Cannot create scoped system-awake request'
    }
}
try {
    while($watch.Elapsed.TotalHours -lt $Hours){
        $at=$watch.Elapsed.TotalSeconds
        $row=[ordered]@{at=[DateTimeOffset]::Now.ToString('o');elapsedSeconds=$at}
        if($count -gt 0 -and $at-$lastAt -gt [Math]::Max(90,$IntervalSeconds*3)){$gaps++;$row.observationGap=$true}
        $s=$null;$b=$null
        try {
            $s=Invoke-RestMethod 'http://127.0.0.1:18765/api/state' -Headers $headers -TimeoutSec 3
            $row.session=$s.agentSessionId;$row.nativeSequence=$s.metrics.sequence;$row.nativeSampleAt=$s.metrics.sampledAtUnixMs
            $row.nativeAgeMs=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()-$s.metrics.sampledAtUnixMs
            $row.cpuSample=$s.metrics.cpu;$row.memorySample=$s.metrics.memory
            if($row.nativeAgeMs -gt 1000 -or $row.nativeAgeMs -lt -1000){$nativeStale++;$row.nativeStale=$true}
        }catch{$errors++;$row.nativeObservationError=$_.Exception.Message}
        try {
            $b=Invoke-RestMethod 'http://127.0.0.1:18765/api/bluetooth/status' -Headers $headers -TimeoutSec 3
            $row.bluetooth=[ordered]@{connected=$b.connected;mode=$b.flowMode;sent=$b.sentFrames;bytes=$b.sentBytes;totalSentFrames=$b.totalSentFrames;totalSentBytes=$b.totalSentBytes;connections=$b.connections;ack=$b.acknowledgedSequence;rttMs=$b.ackRoundTripMs;inFlight=$b.inFlight;peak=$b.peakInFlight;phoneMessageAgeMs=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()-$b.lastPhoneMessageAtUnixMs}
            if(-not $b.connected -or $row.bluetooth.phoneMessageAgeMs -gt 3000){$disconnects++;$row.bluetoothAbsent=$true}
        }catch{$errors++;$row.bluetoothObservationError=$_.Exception.Message}
        $phone=$null
        try {
            $phone=Phone 'status';$finalPhone=$phone;$lastPhoneAt=$at;$finalPhoneError=$null
            if($null -eq $initialPhone){$initialPhone=$phone}
            $row.phone=$phone
        }catch{$phoneObservationFailures++;$finalPhoneError=$_.Exception.Message;$row.phoneObservationError=$_.Exception.Message}
        if($null -ne $s){
            if($null -ne $prevNative){
                $dtNative=$at-$prevNative.elapsed
                $sessionChanged=($prevNative.session -ne $s.agentSessionId)
                if($sessionChanged){$agentSessionRestarts++;$row.agentSessionRestart=$true}
                elseif($dtNative -gt 0 -and $s.metrics.sequence -eq $prevNative.sequence){$nativeStale++;$row.nativeStopped=$true}
            }
            $prevNative=@{elapsed=$at;session=$s.agentSessionId;sequence=$s.metrics.sequence}
        }
        if($null -ne $b){
            if($null -ne $prevBt){
                $dtBt=$at-$prevBt.elapsed
                $btSessionChanged=($null -ne $prevBt.session -and $null -ne $s -and $prevBt.session -ne $s.agentSessionId)
                if($btSessionChanged){$row.bluetoothSessionChanged=$true}
                elseif($dtBt -gt 0){
                    $lifetimeReset=($b.totalSentFrames -lt $prevBt.totalSentFrames -or $b.totalSentBytes -lt $prevBt.totalSentBytes -or $b.connections -lt $prevBt.connections)
                    if($lifetimeReset){
                        $lifetimeCounterResets++;$row.lifetimeCounterReset=$true
                    }else{
                        $row.sendHz=[Math]::Round(($b.totalSentFrames-$prevBt.totalSentFrames)/$dtBt,3)
                        $row.sendBytesPerSecond=[Math]::Round(($b.totalSentBytes-$prevBt.totalSentBytes)/$dtBt,1)
                        $reasons=@()
                        if($b.connections -gt $prevBt.connections){$reasons+='connections'}
                        if($b.sentFrames -lt $prevBt.sentFrames){$reasons+='sentFramesReset'}
                        if($reasons.Count -gt 0){
                            $connectionChanges++;$row.bluetoothConnectionChange=$true;$row.bluetoothConnectionChangeReasons=($reasons -join ',')
                        }
                    }
                }
            }
            $btSession=if($null -ne $s){$s.agentSessionId}elseif($null -ne $prevBt){$prevBt.session}else{$null}
            $prevBt=@{elapsed=$at;session=$btSession;sentFrames=$b.sentFrames;connections=$b.connections;totalSentFrames=$b.totalSentFrames;totalSentBytes=$b.totalSentBytes}
        }
        if($null -ne $phone){
            if($null -ne $prevPhone){
                $dtPhone=$at-$prevPhone.elapsed
                if($phone.elapsedMs -lt $prevPhone.status.elapsedMs -or $phone.receivedStates -lt $prevPhone.status.receivedStates){$phoneRestarts++;$row.phoneActivityRestart=$true}
                elseif($dtPhone -gt 0){
                    $row.phoneReceiveHz=($phone.receivedStates-$prevPhone.status.receivedStates)/$dtPhone
                    $row.phoneDomHz=($phone.domUpdates-$prevPhone.status.domUpdates)/$dtPhone
                    $row.coalescedDelta=$phone.coalescedStates-$prevPhone.status.coalescedStates
                    if($phone.receivedStates -eq $prevPhone.status.receivedStates -or $phone.domUpdates -eq $prevPhone.status.domUpdates){$phoneStale++;$row.phoneStopped=$true}
                }
            }
            $prevPhone=@{elapsed=$at;status=$phone}
        }
        try {
            $item=Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq $target}|Select-Object -First 1
            if(-not $item){throw 'Installed Agent process unavailable'}
            $p=Get-Process -Id $item.ProcessId
            $cpu=[pscustomobject]@{pid=$p.Id;ms=$p.TotalProcessorTime.TotalMilliseconds;at=$watch.Elapsed.TotalMilliseconds;workingSetBytes=$p.WorkingSet64;privateBytes=$p.PrivateMemorySize64}
            if($null -eq $initialCpu){$initialCpu=$cpu}
            if($null -ne $prevCpu){
                if($prevCpu.pid -eq $cpu.pid){$agentCpuMs+=$cpu.ms-$prevCpu.ms;$agentCpuWallMs+=$cpu.at-$prevCpu.at}
                else {$agentPidRestarts++;$row.agentPidRestart=$true}
            }
            $prevCpu=$cpu
            $row.agentProcess=[ordered]@{pid=$cpu.pid;cpuMs=$cpu.ms;workingSetBytes=$cpu.workingSetBytes;privateBytes=$cpu.privateBytes}
        }catch{$errors++;$row.agentProcessError=$_.Exception.Message}
        try {
            $battery=Adb @('shell','dumpsys','battery')
            if($battery -match 'temperature:\s*(-?\d+)'){$row.batteryCelsius=[double]$Matches[1]/10}
            if($battery -match 'level:\s*(\d+)'){$row.batteryLevel=[int]$Matches[1]}
        }catch{$errors++;$row.batteryObservationError=$_.Exception.Message}
        if($at-$lastHistory -ge 1800){
            try {
                $history=Phone 'history';$finalHistory=$history
                if($null -eq $initialHistory){$initialHistory=$history}
                $row.history=$history
                if($history.integrity -ne 'ok'){$integrityFailures++;$row.integrityFailure=$true}
                $lastHistory=$at
            }catch{$phoneObservationFailures++;$row.historyObservationError=$_.Exception.Message}
            try {
                $meminfo=Adb @('shell','dumpsys','meminfo',$package)
                $meminfo|Set-Content -LiteralPath (Join-Path $logRoot ('phone-meminfo-'+$count+'.txt'))
            }catch{$errors++;$row.meminfoObservationError=$_.Exception.Message}
        }
        if($at-$lastHistogram -ge 3600){
            try {
                Phone 'histogram'|ConvertTo-Json -Depth 5 -Compress|Set-Content -LiteralPath (Join-Path $logRoot ('phone-histogram-'+$count+'.json'))
                $lastHistogram=$at
            }catch{$phoneObservationFailures++;$row.histogramObservationError=$_.Exception.Message}
        }
        $row|ConvertTo-Json -Depth 10 -Compress|Add-Content -LiteralPath $samples
        $count++;$lastAt=$at
        $phoneObservedHours=if($null -ne $initialPhone -and $null -ne $finalPhone){($finalPhone.elapsedMs-$initialPhone.elapsedMs)/3600000}else{$null}
        $persistenceDelta=if($null -ne $initialPhone -and $null -ne $finalPhone){$finalPhone.persistenceFailures-$initialPhone.persistenceFailures}else{$null}
        [ordered]@{startedAt=$started.ToString('o');elapsedHours=$watch.Elapsed.TotalHours;requestedHours=$Hours;requiredRequestedHours=$requiredRequestedHours;requiredPhoneObservationHours=$requiredPhoneObservationHours;checks=$count;errors=$errors;phoneObservationFailures=$phoneObservationFailures;nativeStale=$nativeStale;phoneStale=$phoneStale;bluetoothAbsentChecks=$disconnects;observationGaps=$gaps;agentSessionRestarts=$agentSessionRestarts;agentPidRestarts=$agentPidRestarts;phoneActivityRestarts=$phoneRestarts;bluetoothConnectionChanges=$connectionChanges;lifetimeCounterResets=$lifetimeCounterResets;integrityFailures=$integrityFailures;persistenceFailuresDelta=$persistenceDelta;phoneObservationHoursSoFar=$phoneObservedHours;observationCompleted=($watch.Elapsed.TotalHours -ge $Hours);validContinuousRun=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $logRoot 'progress.json')
        $next+=$IntervalSeconds
        $sleep=[Math]::Min($next-$watch.Elapsed.TotalSeconds,$Hours*3600-$watch.Elapsed.TotalSeconds)
        if($sleep -gt 0){Start-Sleep -Milliseconds ([int]($sleep*1000))}
    }
    $observationCompleted=$watch.Elapsed.TotalHours -ge $Hours
    $finalizationErrors=@()
    try {
        $finalPhone=Phone 'status';$finalPhoneFresh=$true;$finalPhoneError=$null;$lastPhoneAt=$watch.Elapsed.TotalSeconds
    }catch{$finalPhoneFresh=$false;$finalPhoneError=$_.Exception.Message;$phoneObservationFailures++;$finalizationErrors+='final phone status: '+$_.Exception.Message}
    try {
        $finalHistory=Phone 'history'
        if($finalHistory.integrity -ne 'ok'){$integrityFailures++;$finalizationErrors+='final history integrity: '+$finalHistory.integrity}
    }catch{$phoneObservationFailures++;$finalizationErrors+='final phone history: '+$_.Exception.Message}
    try {
        Phone 'histogram'|ConvertTo-Json -Depth 5 -Compress|Set-Content -LiteralPath (Join-Path $logRoot 'phone-histogram-end.json')
    }catch{$phoneObservationFailures++;$finalizationErrors+='final phone histogram: '+$_.Exception.Message}
    try {$finalNative=Invoke-RestMethod 'http://127.0.0.1:18765/api/state' -Headers $headers -TimeoutSec 3}catch{$finalizationErrors+='final native state: '+$_.Exception.Message}
    try {$finalBluetooth=Invoke-RestMethod 'http://127.0.0.1:18765/api/bluetooth/status' -Headers $headers -TimeoutSec 3}catch{$finalizationErrors+='final native bluetooth: '+$_.Exception.Message}
    try {
        $item=Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq $target}|Select-Object -First 1
        if(-not $item){throw 'Installed Agent process unavailable'}
        $p=Get-Process -Id $item.ProcessId
        $finalCpu=[pscustomobject]@{pid=$p.Id;ms=$p.TotalProcessorTime.TotalMilliseconds;at=$watch.Elapsed.TotalMilliseconds;workingSetBytes=$p.WorkingSet64;privateBytes=$p.PrivateMemorySize64;sampledAt=[DateTimeOffset]::Now.ToString('o')}
        if($null -ne $prevCpu -and $prevCpu.pid -eq $finalCpu.pid){$agentCpuMs+=$finalCpu.ms-$prevCpu.ms;$agentCpuWallMs+=$finalCpu.at-$prevCpu.at}
        $prevCpu=$finalCpu
    }catch{$finalCpuError=$_.Exception.Message;$finalizationErrors+='final CPU: '+$_.Exception.Message}
    $phoneObservationHours=if($null -ne $initialPhone -and $null -ne $finalPhone){($finalPhone.elapsedMs-$initialPhone.elapsedMs)/3600000}else{$null}
    $persistenceDelta=if($null -ne $initialPhone -and $null -ne $finalPhone){$finalPhone.persistenceFailures-$initialPhone.persistenceFailures}else{$null}
    $thresholds=[ordered]@{requestedHours=$Hours;requiredRequestedHours=$requiredRequestedHours;requiredPhoneObservationHours=$requiredPhoneObservationHours;maxObservationGapSeconds=[Math]::Max(90,$IntervalSeconds*3);nativeSampleMaxAgeMs=1000;bluetoothPhoneMessageMaxAgeMs=3000}
    $criteria=[ordered]@{
        requestedAtLeast24Hours=($Hours -ge $requiredRequestedHours)
        observationCompleted=$observationCompleted
        noObservationErrors=($errors -eq 0)
        noPhoneObservationFailures=($phoneObservationFailures -eq 0)
        noNativeStale=($nativeStale -eq 0)
        noPhoneStale=($phoneStale -eq 0)
        noBluetoothOutages=($disconnects -eq 0)
        noObservationGaps=($gaps -eq 0)
        noAgentSessionRestarts=($agentSessionRestarts -eq 0)
        noAgentPidRestarts=($agentPidRestarts -eq 0)
        noPhoneActivityRestarts=($phoneRestarts -eq 0)
        persistenceFailuresUnchanged=($null -ne $persistenceDelta -and $persistenceDelta -eq 0)
        noBluetoothConnectionChanges=($connectionChanges -eq 0)
        noLifetimeCounterResets=($lifetimeCounterResets -eq 0)
        phoneObservationAtLeast24Hours=($null -ne $phoneObservationHours -and $phoneObservationHours -ge $requiredPhoneObservationHours)
        historyIntegrityOk=($null -ne $finalHistory -and $finalHistory.integrity -eq 'ok')
        finalPhoneRecorded=($null -ne $finalPhone -and $finalPhoneFresh)
        finalCpuRecorded=($null -ne $finalCpu)
        finalizationSucceeded=($finalizationErrors.Count -eq 0)
    }
    $validContinuousRun=-not ($criteria.Values -contains $false)
    $summary=[ordered]@{startedAt=$started.ToString('o');finishedAt=[DateTimeOffset]::Now.ToString('o');actualHours=$watch.Elapsed.TotalHours;requestedHours=$Hours;observationCompleted=$observationCompleted;validContinuousRun=$validContinuousRun;requiredPhoneObservationHours=$requiredPhoneObservationHours;phoneObservationHours=$phoneObservationHours;thresholds=$thresholds;checks=$count;errors=$errors;phoneObservationFailures=$phoneObservationFailures;nativeStale=$nativeStale;phoneStale=$phoneStale;bluetoothAbsentChecks=$disconnects;observationGaps=$gaps;agentSessionRestarts=$agentSessionRestarts;agentPidRestarts=$agentPidRestarts;phoneActivityRestarts=$phoneRestarts;bluetoothConnectionChanges=$connectionChanges;lifetimeCounterResets=$lifetimeCounterResets;integrityFailures=$integrityFailures;persistenceFailuresInitial=if($null -ne $initialPhone){$initialPhone.persistenceFailures}else{$null};persistenceFailuresFinal=if($null -ne $finalPhone){$finalPhone.persistenceFailures}else{$null};persistenceFailuresDelta=$persistenceDelta;agentAverageCpuPercent=if($agentCpuWallMs -gt 0){100*$agentCpuMs/$agentCpuWallMs/[Environment]::ProcessorCount}else{$null};initialCpu=$initialCpu;finalCpu=$finalCpu;finalCpuError=$finalCpuError;initialPhone=$initialPhone;finalPhone=$finalPhone;finalPhoneFresh=$finalPhoneFresh;finalPhoneObservedAtElapsedSeconds=$lastPhoneAt;finalPhoneError=$finalPhoneError;finalNative=$finalNative;finalBluetooth=$finalBluetooth;initialHistory=$initialHistory;finalHistory=$finalHistory;finalizationErrors=$finalizationErrors;criteria=$criteria;scope='Foreground, screen-on real Android receiver/DOM and native/ACK stability. Physical FPS/latency require separate FrameTimeline measurement; not inferred from these counters.'}
    $summary|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $logRoot 'summary.json')
    [ordered]@{startedAt=$started.ToString('o');elapsedHours=$watch.Elapsed.TotalHours;requestedHours=$Hours;requiredRequestedHours=$requiredRequestedHours;requiredPhoneObservationHours=$requiredPhoneObservationHours;checks=$count;errors=$errors;phoneObservationFailures=$phoneObservationFailures;nativeStale=$nativeStale;phoneStale=$phoneStale;bluetoothAbsentChecks=$disconnects;observationGaps=$gaps;agentSessionRestarts=$agentSessionRestarts;agentPidRestarts=$agentPidRestarts;phoneActivityRestarts=$phoneRestarts;bluetoothConnectionChanges=$connectionChanges;lifetimeCounterResets=$lifetimeCounterResets;integrityFailures=$integrityFailures;persistenceFailuresDelta=$persistenceDelta;phoneObservationHoursSoFar=$phoneObservationHours;observationCompleted=$observationCompleted;validContinuousRun=$validContinuousRun}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $logRoot 'progress.json')
    $summary|ConvertTo-Json -Depth 10
}finally {
    if($power -ne [IntPtr]::Zero -and $power -ne [IntPtr]::new(-1)){[CompanionTestPower]::PowerClearRequest($power,1)|Out-Null;[CompanionTestPower]::CloseHandle($power)|Out-Null}
}
