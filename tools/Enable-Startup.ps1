$ErrorActionPreference='Stop'
$link=Join-Path ([Environment]::GetFolderPath('Startup')) 'Desk Companion.lnk'
if(Test-Path -LiteralPath $link){throw 'Startup shortcut already exists; it was not overwritten.'}
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($link)
$shortcut.TargetPath=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shortcut.Arguments='-NoProfile -WindowStyle Hidden -File "'+(Join-Path $PSScriptRoot 'Start-Companion.ps1')+'"'
$shortcut.WorkingDirectory=$PSScriptRoot
$shortcut.WindowStyle=7
$shortcut.Save()
Write-Output 'Startup enabled for the current user. Remove Desk Companion.lnk from Startup to roll back.'
