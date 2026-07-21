[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Common.ps1')

Write-Step 'Configurando credenciales SQL'
$credential = Get-Credential -Message 'Credenciales SQL de endpoint_app' -UserName 'endpoint_app'
$password = $credential.GetNetworkCredential().Password

$config = @{
    ConnectionStrings = @{
        EndpointManager = "Server=TRADI23;Database=EndpointManager;User ID=$($credential.UserName);Password=$password;Encrypt=True;TrustServerCertificate=True;Connect Timeout=15;"
    }
} | ConvertTo-Json -Depth 4

New-Item -ItemType Directory -Force -Path $script:DeployPath | Out-Null
$configPath = Join-Path $script:DeployPath 'appsettings.Production.json'
[System.IO.File]::WriteAllText($configPath, $config, [System.Text.UTF8Encoding]::new($false))

Write-Host "Configuracion guardada en $configPath" -ForegroundColor Green
