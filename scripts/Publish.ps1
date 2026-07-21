[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Common.ps1')

Write-Step 'Publicando proyecto'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK no encontrado.' }

Remove-Item $script:PublishPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $script:PublishPath | Out-Null

dotnet publish $script:ProjectPath -c Release -o $script:PublishPath
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish ha fallado.' }

Write-Step 'Deteniendo sitio'
Stop-EndpointSite

New-Item -ItemType Directory -Force -Path $script:DeployPath | Out-Null
$productionConfig = Join-Path $script:DeployPath 'appsettings.Production.json'
$configBackup = $null
if (Test-Path $productionConfig) {
    $configBackup = Join-Path $env:TEMP 'Tradipres.EndpointServer.appsettings.Production.json'
    Copy-Item $productionConfig $configBackup -Force
}

Get-ChildItem $script:DeployPath -Force | Where-Object { $_.Name -ne 'logs' } | Remove-Item -Recurse -Force
Copy-Item (Join-Path $script:PublishPath '*') $script:DeployPath -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $script:DeployPath 'logs') | Out-Null

if ($configBackup -and (Test-Path $configBackup)) {
    Copy-Item $configBackup $productionConfig -Force
}

Write-Step 'Arrancando sitio'
Start-EndpointSite
Start-Sleep -Seconds 3

Write-Step 'Validando API'
$response = Invoke-RestMethod -Uri "http://localhost:$script:Port/api/status" -TimeoutSec 20
$response | ConvertTo-Json -Depth 5
Write-Host "`nPublicacion completada: http://TRADI35:$script:Port" -ForegroundColor Green
