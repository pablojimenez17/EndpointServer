[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Common.ps1')
Invoke-RestMethod -Uri "http://localhost:$script:Port/health" -TimeoutSec 20
