param(
    [string]$BuildDir=(Join-Path $PSScriptRoot '..\dist\companion'),
    [string]$Output=(Join-Path $PSScriptRoot '..\artifacts'),
    [string]$Version='0.3.1',
    [string[]]$ReviewFiles=@()
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release version'}
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot=[IO.Path]::GetFullPath($Output)
$bundle=Join-Path $outputRoot ('DeskCompanion-'+$Version+'-preview')
$source=Join-Path $outputRoot ('DeskCompanion-'+$Version+'-source')
$runtimeZip=$bundle+'.zip'
$sourceZip=$source+'.zip'
$sumsFile=Join-Path $outputRoot 'SHA256SUMS.txt'
$targets=@($bundle,$source,$runtimeZip,$sourceZip,$sumsFile)
foreach($target in $targets){
    if(Test-Path -LiteralPath $target){throw ('Refusing to overwrite an existing release output: '+$target+'. Use a fresh -Output directory for this version.')}
}
$exePath=Join-Path $BuildDir 'windows\DeskCompanionAgent.exe'
$apkPath=Join-Path $BuildDir 'android\DeskCompanion.apk'
foreach($required in @($exePath,$apkPath)){
    if(-not(Test-Path -LiteralPath $required -PathType Leaf)){throw ('Missing build input: '+$required)}
}
$exePath=[IO.Path]::GetFullPath($exePath)
$apkPath=[IO.Path]::GetFullPath($apkPath)
New-Item -ItemType Directory -Force $outputRoot,$bundle,$source | Out-Null
function Copy-PublicFile([string]$From,[string]$To){
    New-Item -ItemType Directory -Force (Split-Path $To -Parent)|Out-Null
    Copy-Item -LiteralPath $From -Destination $To
}
function Copy-PublicFileIfPresent([string]$From,[string]$To){
    if(Test-Path -LiteralPath $From -PathType Leaf){Copy-PublicFile $From $To}
}
# Runtime bundle: prebuilt Windows EXE, release APK, user scripts, licenses and public docs.
Copy-PublicFile $exePath (Join-Path $bundle 'windows\DeskCompanionAgent.exe')
Copy-PublicFile $apkPath (Join-Path $bundle 'android\DeskCompanion.apk')
$runtimeDocs=@(
    'README.md',
    'LICENSE',
    'PRIVACY.md',
    'SECURITY.md',
    'THIRD_PARTY_NOTICES.md',
    'docs/VALIDATION.md',
    'docs/REVIEW.md',
    'docs/031-MEASUREMENT.md',
    'docs/031-DELIVERY.md'
    'docs/SOURCES.md'
)
foreach($name in $runtimeDocs){
    Copy-PublicFileIfPresent (Join-Path $repo $name) (Join-Path $bundle $name)
}
$runtimeScripts=@(
    'Start-Companion.ps1',
    'Open-Companion.ps1',
    'Stop-Companion.ps1',
    'Enable-Startup.ps1',
    'Test-Stability.ps1',
    'Measure-CompanionStability.ps1'
)
foreach($name in $runtimeScripts){
    Copy-PublicFileIfPresent (Join-Path $repo ('tools/'+$name)) (Join-Path $bundle $name)
}
Copy-Item -LiteralPath (Join-Path $repo 'licenses') -Destination (Join-Path $bundle 'licenses') -Recurse
# Benchmark helpers are optional public tooling; they contain no captured data or credentials.
$performanceDir=Join-Path $repo 'tools\performance'
if(Test-Path -LiteralPath $performanceDir -PathType Container){
    New-Item -ItemType Directory -Force (Join-Path $bundle 'tools')|Out-Null
    Copy-Item -LiteralPath $performanceDir -Destination (Join-Path $bundle 'tools\performance') -Recurse
}
# Public review records are copied only when named by an explicit whitelist. The parent must ensure
# every listed record is anonymized and approved for publication; this script never scans the folder.
$reviewsRoot=Join-Path $repo 'docs\reviews'
$reviewEntries=New-Object System.Collections.Generic.List[string]
foreach($entry in $ReviewFiles){if($entry){[void]$reviewEntries.Add($entry)}}
$reviewWhitelist=Join-Path $reviewsRoot 'WHITELIST.txt'
if(Test-Path -LiteralPath $reviewWhitelist -PathType Leaf){
    foreach($line in Get-Content -LiteralPath $reviewWhitelist){
        $entry=$line.Trim()
        if(-not $entry -or $entry.StartsWith('#')){continue}
        [void]$reviewEntries.Add($entry)
    }
}
$copiedReviews=@{}
foreach($entry in $reviewEntries){
    $relative=$entry.Replace('\','/').Trim()
    if(-not $relative){continue}
    if($relative -match '^docs/reviews/'){$relative=$relative.Substring('docs/reviews/'.Length)}
    if($relative.StartsWith('/') -or $relative -match '^[A-Za-z]:' -or $relative -match '(^|/)\.\.(/|$)' -or $relative -match '(^|/)\.(?=/|$)'){
        throw ('Rejected review whitelist entry with an unsafe path: '+$entry)
    }
    if($copiedReviews.ContainsKey($relative)){continue}
    $copiedReviews[$relative]=$true
    $from=[IO.Path]::GetFullPath((Join-Path $reviewsRoot $relative))
    $reviewPrefix=[IO.Path]::GetFullPath($reviewsRoot)+[IO.Path]::DirectorySeparatorChar
    if(-not $from.StartsWith($reviewPrefix,[StringComparison]::OrdinalIgnoreCase)){
        throw ('Rejected review whitelist entry outside docs/reviews: '+$entry)
    }
    if(-not(Test-Path -LiteralPath $from -PathType Leaf)){
        throw ('Whitelisted public review file is missing: docs/reviews/'+$relative)
    }
    Copy-PublicFile $from (Join-Path $bundle ('docs/reviews/'+$relative))
    Copy-PublicFile $from (Join-Path $source ('docs/reviews/'+$relative))
}
# Export a source snapshot, never Git history, machine state or a broad workspace archive.
$files=& git -c core.quotepath=false -C $repo ls-files -co --exclude-standard
if($LASTEXITCODE){throw 'Git source inventory failed'}
$top=@(
    'README.md',
    'global.json',
    'LICENSE',
    '.gitignore',
    'PRIVACY.md',
    'SECURITY.md',
    'THIRD_PARTY_NOTICES.md',
    'docs/UPSTREAM_PHONEBOX.md',
    'docs/VALIDATION.md',
    'docs/REVIEW.md',
    'docs/031-MEASUREMENT.md',
    'docs/031-DELIVERY.md'
    'docs/SOURCES.md'
)
$legacy=@('agent/PhoneBox.Agent/Lan.cs','agent/PhoneBox.Agent/Pairing.cs','agent/PhoneBox.Agent/AppPaths.cs','agent/PhoneBox.Agent/SessionMonitor.cs')
$scripts=@(
    'build-companion.ps1',
    'build-max3.ps1',
    'test-freshness.ps1',
    'package-public.ps1',
    'Start-Companion.ps1',
    'Open-Companion.ps1',
    'Stop-Companion.ps1',
    'Enable-Startup.ps1',
    'Test-Stability.ps1',
    'Measure-CompanionStability.ps1'
)|ForEach-Object {'tools/'+$_}
$excludePattern='(^|/)(bin|obj|build|node_modules|test-results|testresults|coverage|private-data|logs|\.gradle|\.kotlin|\.idea|\.vs|\.cache|cache|debug|outputs|staging|artifacts|dist|raw|traces?|\.opencode-031-input)(/|$)' +
    '|(^|/)(local\.properties|adbkey.*|id_rsa.*|id_ed25519.*|.*token\.txt|bluetooth-phone\.txt|credentials\.(txt|json)|secrets?\.(txt|json))$' +
    '|\.(pfx|pem|p12|jks|keystore|key|pdb|sqlite|sqlite3|db|log|jsonl|stdout|stderr|trace|pftrace|perfetto|pb|zip|7z|tar|gz|apk|aab|exe)$' +
    '|(^|/)(analysis|summary|start|progress|profile-metadata)\.json$' +
    '|(^|/)(pipeline|frames|thermal|markers|trace-health|presented-frames)\.csv$'
foreach($name in ($files|Sort-Object -Unique)){
    # Copy licenses as a whole directory below; Git stdout may decode non-ASCII filenames differently by shell codepage.
    if($name -match '^licenses/'){continue}
    $selected=$name -in $top -or $name -in $legacy -or $name -in $scripts -or $name -match '^(android/|agent/Max3\.(Agent|Tests)/|tools/companion-ui/|tools/performance/)'
    if(-not $selected){continue}
    if($name -match $excludePattern){throw "Excluded source file unexpectedly selected: $name"}
    Copy-PublicFile (Join-Path $repo $name) (Join-Path $source $name)
}
# Copy the complete license folder as a directory so non-ASCII filenames survive shell codepage differences.
Copy-Item -LiteralPath (Join-Path $repo 'licenses') -Destination (Join-Path $source 'licenses') -Recurse
# Per-file manifests cover every payload file. SHA256SUMS.txt covers the two installers and both archives.
foreach($folder in @($bundle,$source)){
    $prefix=$folder.TrimEnd('\')+'\'
    $manifest=Get-ChildItem -LiteralPath $folder -File -Recurse|Sort-Object FullName|ForEach-Object {
        [ordered]@{
            path=$_.FullName.Substring($prefix.Length).Replace('\','/')
            bytes=$_.Length
            sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $folder 'MANIFEST.json') -Encoding UTF8
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($bundle,$runtimeZip)
[IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip)
$bundleName=[IO.Path]::GetFileName($bundle)
$sums=@(
    [pscustomobject]@{path=($bundleName+'/windows/DeskCompanionAgent.exe');file=(Join-Path $bundle 'windows\DeskCompanionAgent.exe')},
    [pscustomobject]@{path=($bundleName+'/android/DeskCompanion.apk');file=(Join-Path $bundle 'android\DeskCompanion.apk')},
    [pscustomobject]@{path=[IO.Path]::GetFileName($sourceZip);file=$sourceZip},
    [pscustomobject]@{path=[IO.Path]::GetFileName($runtimeZip);file=$runtimeZip}
)
$sums|ForEach-Object{(Get-FileHash -LiteralPath $_.file -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.path}|Set-Content -LiteralPath $sumsFile -Encoding ASCII
Write-Output ('Public preview and source snapshot prepared for '+$Version+'. No test acceptance is claimed. Run the parent secret/identity scan before publishing.')
