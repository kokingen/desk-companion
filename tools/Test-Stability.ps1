param([ValidateRange(0.001,168)][double]$Hours=24,[ValidateRange(1,300)][int]$IntervalSeconds=30)
$ErrorActionPreference='Stop'
$logs=Join-Path $PSScriptRoot 'logs';New-Item -ItemType Directory -Force $logs|Out-Null
$file=Join-Path $logs ('stability-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.jsonl')
$tokenFile=Join-Path $PSScriptRoot 'private-data\admin-token.txt'
$credential=(Get-Content -LiteralPath $tokenFile -Raw).Trim()
if($credential -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Invalid local administration credential'}
$headers=@{Authorization='Bearer '+$credential}
$watch=[Diagnostics.Stopwatch]::StartNew();$failures=0;$stale=0;$disconnected=0;$gaps=0;$previous=0;$restarts=0;$count=0;$lastCheck=0;$session=''
while($watch.Elapsed.TotalHours -lt $Hours){
    $elapsed=$watch.Elapsed.TotalSeconds
    $row=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');elapsedSeconds=$elapsed}
    if($count -gt 0 -and $elapsed-$lastCheck -gt [Math]::Max(90,$IntervalSeconds*3)){$gaps++;$row.observationGap=$true};$lastCheck=$elapsed
    try{
        $s=Invoke-RestMethod 'http://127.0.0.1:18765/api/state' -Headers $headers -TimeoutSec 3
        $bt=Invoke-RestMethod 'http://127.0.0.1:18765/api/bluetooth/status' -Headers $headers -TimeoutSec 3
        $now=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds();$seq=$s.metrics.sequence
        if($session -ne '' -and $s.agentSessionId -ne $session){$restarts++};$session=$s.agentSessionId
        $row.sequence=$seq;$row.cpu=$s.metrics.cpu.percent;$row.memory=$s.metrics.memory.percent
        $row.nativeSampleAgeMs=$now-$s.metrics.sampledAtUnixMs
        $row.bluetoothConnected=$bt.connected;$row.bluetoothFlowControl=$bt.flowControl
        $row.bluetoothInboundAgeMs=if($bt.lastPhoneMessageAtUnixMs -gt 0){$now-$bt.lastPhoneMessageAtUnixMs}else{$null}
        $row.bluetoothAcknowledgedSequence=$bt.acknowledgedSequence;$row.bluetoothAckRoundTripMs=$bt.ackRoundTripMs
        $row.lanTlsConnections=@(Get-NetTCPConnection -LocalPort 18766 -State Established -ErrorAction SilentlyContinue|Where-Object{$_.RemoteAddress -ne '127.0.0.1'}).Count
        $inboundLimit=if($bt.flowControl){3000}else{75000}
        $phoneLink=($bt.connected -and $null -ne $row.bluetoothInboundAgeMs -and $row.bluetoothInboundAgeMs -lt $inboundLimit) -or $row.lanTlsConnections -gt 0
        if(-not $phoneLink){$disconnected++};if($row.nativeSampleAgeMs -gt 1000 -or $row.nativeSampleAgeMs -lt -1000 -or $seq -eq $previous){$stale++};$previous=$seq
    }catch{$failures++;$row.error=$_.Exception.Message}
    $row|ConvertTo-Json -Compress|Add-Content -LiteralPath $file;$count++
    $remaining=$Hours*3600-$watch.Elapsed.TotalSeconds;if($remaining -gt 0){Start-Sleep -Milliseconds ([int]([Math]::Min($IntervalSeconds,$remaining)*1000))}
}
$summary=[ordered]@{requestedHours=$Hours;actualHours=$watch.Elapsed.TotalHours;checks=$count;requestFailures=$failures;staleNativeSamples=$stale;phoneLinkAbsentChecks=$disconnected;observationGaps=$gaps;observedAgentRestarts=$restarts;log=$file;validUninterruptedObservation=($failures -eq 0 -and $stale -eq 0 -and $disconnected -eq 0 -and $gaps -eq 0 -and $restarts -eq 0);scope='Windows native freshness, authenticated Bluetooth ACK or LAN TLS connection presence; not a certification of phone render FPS or phone display age'}
$summary|ConvertTo-Json|Set-Content -LiteralPath ($file+'.summary.json')
$summary|ConvertTo-Json
