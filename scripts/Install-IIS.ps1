[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Common.ps1')
Import-Module WebAdministration

Write-Step 'Configurando IIS'
New-Item -ItemType Directory -Force -Path $script:DeployPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $script:DeployPath 'logs') | Out-Null

if (-not (Test-Path "IIS:\AppPools\$script:AppPoolName")) {
    New-WebAppPool -Name $script:AppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$script:AppPoolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$script:AppPoolName" -Name processModel.identityType -Value ApplicationPoolIdentity

if (-not (Test-Path "IIS:\Sites\$script:SiteName")) {
    New-Website -Name $script:SiteName -Port $script:Port -PhysicalPath $script:DeployPath -ApplicationPool $script:AppPoolName | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$script:SiteName" -Name physicalPath -Value $script:DeployPath
    Set-ItemProperty "IIS:\Sites\$script:SiteName" -Name applicationPool -Value $script:AppPoolName
}

$identity = "IIS AppPool\$script:AppPoolName"
# M (no solo RX): el servidor escribe aquí el estado en JSON
# (orden_acciones.json, diarios.json). Con solo RX la escritura lanza
# UnauthorizedAccessException y los endpoints devuelven 500 tras ya
# haber insertado en la BD (p. ej. al programar un mantenimiento).
icacls $script:DeployPath /grant "${identity}:(OI)(CI)M" /T /C | Out-Null
icacls (Join-Path $script:DeployPath 'logs') /grant "${identity}:(OI)(CI)M" /T /C | Out-Null

Write-Host 'IIS configurado correctamente.' -ForegroundColor Green
