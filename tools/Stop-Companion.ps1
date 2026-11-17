$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'windows\DeskCompanionAgent.exe'
Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $exe } | ForEach-Object {
    $process=Get-Process -Id $_.ProcessId
    Stop-Process -Id $_.ProcessId
    if(-not $process.WaitForExit(5000)){throw 'Agent did not stop within five seconds'}
}
Write-Output 'Stopped this package Desk Companion. Pairing and local history are preserved.'
