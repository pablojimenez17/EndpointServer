TRADIPRES ENDPOINT SERVER v0.2.1
================================

OBJETIVO
- Crear el proyecto permanente del Endpoint Server.
- Publicarlo en D:\Tradipres\EndpointServer\App.
- Utilizar IIS en el puerto 8088.
- Validar la conexion con TRADI23 / EndpointManager.

PRIMERA INSTALACION
1. Descomprimir esta carpeta en:
   D:\Tradipres\EndpointServer

2. Abrir PowerShell como administrador.

3. Ejecutar:
   Set-ExecutionPolicy -Scope Process Bypass
   cd D:\Tradipres\EndpointServer\scripts
   .\Install.ps1

ACTUALIZACIONES POSTERIORES
   cd D:\Tradipres\EndpointServer\scripts
   .\Publish.ps1

PRUEBA
   .\Test.ps1

URL
   http://TRADI35:8088

NOTA
El endpoint /api/equipos existe pero devuelve una lista vacia. En la siguiente version
se conectara a las tablas reales de EndpointManager cuando confirmemos su esquema.
