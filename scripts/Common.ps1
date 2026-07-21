Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:SiteName = 'Tradipres Endpoint Server'
$script:AppPoolName = 'Tradipres.EndpointServer'
$script:Port = 8088
$script:DeployPath = 'D:\Tradipres\EndpointServer\App'
$script:ProjectPath = Join-Path $PSScriptRoot '..\src\Tradipres.EndpointServer.csproj'
$script:PublishPath = Join-Path $env:TEMP 'Tradipres.EndpointServer.Publish'

function Write-Step([string]$Text) {
    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
}

function Stop-EndpointSite {
    Import-Module WebAdministration
    if (Test-Path "IIS:\Sites\$script:SiteName") {
        $site = Get-Website -Name $script:SiteName
        if ($site.State -ne 'Stopped') { Stop-Website -Name $script:SiteName }
    }
    if (Test-Path "IIS:\AppPools\$script:AppPoolName") {
        $pool = Get-WebAppPoolState -Name $script:AppPoolName
        if ($pool.Value -ne 'Stopped') { Stop-WebAppPool -Name $script:AppPoolName }
    }
}

function Start-EndpointSite {
    Import-Module WebAdministration
    if ((Get-WebAppPoolState -Name $script:AppPoolName).Value -ne 'Started') {
        Start-WebAppPool -Name $script:AppPoolName
    }
    if ((Get-WebsiteState -Name $script:SiteName).Value -ne 'Started') {
        Start-Website -Name $script:SiteName
    }
}
