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

# Ficheros de la raíz que hay que conservar entre publicaciones:
#  · appsettings.Production.json → configuración específica del servidor.
#  · diarios.json / orden_acciones.json → estado creado desde la web
#    (recurrencias de los Diarios y orden de acciones por mantenimiento).
#    Si se sobrescriben con los del repo se pierden las programaciones.
$preservar = @(
    'appsettings.Production.json',
    'diarios.json',
    'orden_acciones.json',
    'tareas_avanzadas.json'
)
$backups = @()
foreach ($nombre in $preservar) {
    $origen = Join-Path $script:DeployPath $nombre
    if (Test-Path $origen) {
        $backup = Join-Path $env:TEMP "Tradipres.EndpointServer.$nombre"
        Copy-Item $origen $backup -Force
        $backups += [pscustomobject]@{ Nombre = $nombre; Ruta = $backup }
    }
}

Get-ChildItem $script:DeployPath -Force | Where-Object { $_.Name -ne 'logs' } | Remove-Item -Recurse -Force
Copy-Item (Join-Path $script:PublishPath '*') $script:DeployPath -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $script:DeployPath 'logs') | Out-Null

foreach ($backup in $backups) {
    Copy-Item $backup.Ruta (Join-Path $script:DeployPath $backup.Nombre) -Force
    Write-Host "Preservado: $($backup.Nombre)" -ForegroundColor DarkGray
}

Write-Step 'Arrancando sitio'
Start-EndpointSite
Start-Sleep -Seconds 3

Write-Step 'Validando API'
$response = Invoke-RestMethod -Uri "http://localhost:$script:Port/health" -TimeoutSec 20
Write-Host "Health: $response"
Write-Host "`nPublicacion completada: http://TRADI35:$script:Port" -ForegroundColor Green
