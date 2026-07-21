[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Common.ps1')
Invoke-RestMethod -Uri "http://localhost:$script:Port/api/status" -TimeoutSec 20 | ConvertTo-Json -Depth 5
