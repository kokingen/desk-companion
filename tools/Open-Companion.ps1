$ErrorActionPreference='Stop'
$file=Join-Path $PSScriptRoot 'private-data\admin-token.txt'
if(-not (Test-Path -LiteralPath $file)){throw 'Start this package first to create the local management credential.'}
$credential=(Get-Content -LiteralPath $file -Raw).Trim()
if($credential -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Invalid local management credential.'}
$null=Invoke-RestMethod 'http://127.0.0.1:18765/api/state' -Headers @{Authorization='Bearer '+$credential} -TimeoutSec 3
# Fragment is consumed and cleared by the local page; it is never sent in an HTTP request or printed.
Start-Process ('http://127.0.0.1:18765/#admin='+$credential)
