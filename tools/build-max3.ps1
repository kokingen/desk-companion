# Compatibility wrapper for previous build instructions.
param([string]$Output=(Join-Path $PSScriptRoot '..\dist\companion'))
& (Join-Path $PSScriptRoot 'build-companion.ps1') -Output $Output
