param(
    [Parameter(Mandatory)][string]$AgentExe,
    [Parameter(Mandatory)][string]$OutDir,
    [Parameter(Mandatory)][string]$TokenFile,
    [string]$ApkPath,
    [string]$Tag='observe',
    [ValidateRange(1,86400)][double]$Seconds=1805,
    [string]$BaseUrl='http://127.0.0.1:18765',
    [ValidateRange(1,60000)][int]$IntervalMs=100,
    [string]$ObserverScript
)
$ErrorActionPreference='Stop'
if($Tag -notmatch '^[A-Za-z0-9._-]+$'){throw 'Tag must contain only letters, digits, dot, underscore or hyphen'}
$target=(Resolve-Path -LiteralPath $AgentExe).Path
$token=(Resolve-Path -LiteralPath $TokenFile).Path
if(-not $ObserverScript){$ObserverScript=Join-Path $PSScriptRoot 'observe-windows.mjs'}
$ObserverScript=(Resolve-Path -LiteralPath $ObserverScript).Path
$out=[IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $out|Out-Null
$windowsFile=Join-Path $out ($Tag+'-windows.jsonl')
foreach($name in @(($Tag+'-cpu-start.json'),($Tag+'-windows.jsonl'),($Tag+'-cpu-end.json'))){
    if(Test-Path -LiteralPath (Join-Path $out $name)){throw ('Refusing to overwrite existing observation: '+$name)}
}
$node=(Get-Command node -ErrorAction Stop).Source
$apkHash=$null
if($ApkPath){
    $apk=(Resolve-Path -LiteralPath $ApkPath).Path
    $apkHash=(Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()
}
$item=Get-CimInstance Win32_Process|Where-Object {$_.ExecutablePath -eq $target}|Select-Object -First 1
if(-not $item){throw 'Agent process for the supplied executable is not running'}
$p=Get-Process -Id $item.ProcessId
$before=$p.TotalProcessorTime.TotalMilliseconds
$startRecord=[ordered]@{
    at=[DateTimeOffset]::Now.ToString('o')
    pid=$p.Id
    cpuMs=$before
    logicalProcessors=[Environment]::ProcessorCount
    workingSetBytes=$p.WorkingSet64
    exeSha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    apkSha256=$apkHash
    scope='Process CPU and memory observation plus local API/BLE counters; observation only, no pass/fail thresholds'
}
$startRecord|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out ($Tag+'-cpu-start.json')) -Encoding UTF8
$watch=[Diagnostics.Stopwatch]::StartNew()
& $node $ObserverScript --token-file $token --out $windowsFile --seconds $Seconds --base-url $BaseUrl --interval-ms $IntervalMs
if($LASTEXITCODE){throw 'Observer failed'}
$p.Refresh()
$finish=[ordered]@{
    at=[DateTimeOffset]::Now.ToString('o')
    elapsedMs=$watch.Elapsed.TotalMilliseconds
    pid=$p.Id
    cpuMs=$p.TotalProcessorTime.TotalMilliseconds
    averageCpuPercent=100*($p.TotalProcessorTime.TotalMilliseconds-$before)/$watch.Elapsed.TotalMilliseconds/[Environment]::ProcessorCount
    workingSetBytes=$p.WorkingSet64
    privateBytes=$p.PrivateMemorySize64
    method='Process.TotalProcessorTime delta / wall time / logical processors; includes loopback observer request work'
    windowsObservation=[IO.Path]::GetFileName($windowsFile)
}
$finish|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out ($Tag+'-cpu-end.json')) -Encoding UTF8
$finish|ConvertTo-Json
