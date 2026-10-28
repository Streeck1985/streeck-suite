# Arranca la suite (recompilando si hace falta) y muestra el registro de los primeros segundos.
# No se queda esperando: la suite corre en segundo plano.
& "$PSScriptRoot\run_suite.ps1"
Start-Sleep -Seconds 8
Get-Content "$env:LOCALAPPDATA\StreeckSuite\streecksuite.log" -Tail 25 -Encoding UTF8
