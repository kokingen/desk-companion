param([switch]$OpenPairing)
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'windows\DeskCompanionAgent.exe'
$data=Join-Path $PSScriptRoot 'private-data'
$logs=Join-Path $PSScriptRoot 'logs'
if(-not (Test-Path -LiteralPath $exe)){throw 'Agent executable missing from this package.'}
New-Item -ItemType Directory -Force $data,$logs | Out-Null
$existing=Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $exe }
$started=$null
if(-not $existing){
    if(Get-NetTCPConnection -LocalPort 18765 -State Listen -ErrorAction SilentlyContinue){throw 'Port 18765 is already used. Stop the previous Companion before starting this package.'}
    $started=Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot -ArgumentList @('--data-dir',('"'+$data+'"')) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logs 'agent.log') -RedirectStandardError (Join-Path $logs 'agent-error.log') -PassThru
}
$ready=$false
for($attempt=0;$attempt -lt 60;$attempt++){
    if($started){$started.Refresh();if($started.HasExited){throw 'Agent exited during startup. See this package logs.'}}
    $tokenFile=Join-Path $data 'admin-token.txt'
    if(Test-Path -LiteralPath $tokenFile){
        $raw=Get-Content -LiteralPath $tokenFile -Raw -ErrorAction SilentlyContinue
        $credential=if($null -ne $raw){$raw.Trim()}else{''}
        if($credential -match '^[A-Fa-f0-9]{64}$'){
            try{$null=Invoke-RestMethod 'http://127.0.0.1:18765/api/state' -Headers @{Authorization='Bearer '+$credential} -TimeoutSec 1;$ready=$true;break}catch{}
        }
    }
    Start-Sleep -Milliseconds 100
}
if(-not $ready){throw 'Agent did not become ready. Check logs and Windows application control; no security settings were changed.'}
Write-Output 'Desk Companion is running.'
if($OpenPairing){& (Join-Path $PSScriptRoot 'Open-Companion.ps1')}
