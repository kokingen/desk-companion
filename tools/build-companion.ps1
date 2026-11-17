param([string]$Output=(Join-Path $PSScriptRoot '..\dist\companion'))
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$root=Split-Path $PSScriptRoot -Parent
Push-Location (Join-Path $PSScriptRoot 'companion-ui')
try {
    npm ci --no-audit --no-fund; if($LASTEXITCODE){throw 'npm ci failed'}
    npm run build; if($LASTEXITCODE){throw 'UI build failed'}
    npm test; if($LASTEXITCODE){throw 'UI checks failed'}
    node browser-test.mjs; if($LASTEXITCODE){throw 'Browser checks failed; install the locked Playwright Chromium first'}
} finally {Pop-Location}
Push-Location $root
try {
    dotnet restore (Join-Path $root 'agent\Max3.Agent') -r win-x64 --locked-mode
    if($LASTEXITCODE){throw 'Windows restore failed; check the committed NuGet lock file'}
    dotnet restore (Join-Path $root 'agent\Max3.Tests') -r win-x64 --locked-mode
    if($LASTEXITCODE){throw 'Test restore failed; check the committed NuGet lock file'}
    dotnet run --project (Join-Path $root 'agent\Max3.Tests') -c Release -r win-x64 --no-restore
    if($LASTEXITCODE){throw 'Native counter and pairing checks failed'}
    dotnet publish (Join-Path $root 'agent\Max3.Agent') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true --no-restore -o (Join-Path $Output 'windows')
    if($LASTEXITCODE){throw 'Windows publish failed'}
} finally { Pop-Location }
Push-Location (Join-Path $root 'android')
try { .\gradlew.bat --no-daemon --max-workers=2 lintDebug assembleDebug assembleRelease compileDebugUnitTestKotlin; if($LASTEXITCODE){throw 'Android build failed'} } finally {Pop-Location}
& (Join-Path $PSScriptRoot 'test-freshness.ps1') -SkipBuild
New-Item -ItemType Directory -Force (Join-Path $Output 'android') | Out-Null
Copy-Item (Join-Path $root 'android\app\build\outputs\apk\release\app-release.apk') (Join-Path $Output 'android\DeskCompanion.apk')
