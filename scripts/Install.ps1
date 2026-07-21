[CmdletBinding()]
param()

& (Join-Path $PSScriptRoot 'Install-IIS.ps1')
& (Join-Path $PSScriptRoot 'Configure.ps1')
& (Join-Path $PSScriptRoot 'Publish.ps1')
