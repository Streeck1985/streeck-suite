# =========================================================================
# Streeck Suite - Lanzador: recompila si hay cambios en src\ y arranca la suite en segundo plano
# (la suite sigue funcionando al cerrar esta ventana; para salir: icono de la bandeja > Salir)
# =========================================================================

$Exe = "$PSScriptRoot\bin\StreeckSuite.exe"
$Sources = Get-ChildItem -Path "$PSScriptRoot\src" -Filter "*.cs" -Recurse
$NewestSource = ($Sources | Measure-Object LastWriteTime -Maximum).Maximum

if (!(Test-Path $Exe) -or (Get-Item $Exe).LastWriteTime -lt $NewestSource) {
    # Una suite abierta bloquea el .exe y la compilación fallaría
    Get-Process StreeckSuite -ErrorAction SilentlyContinue | Stop-Process
    Start-Sleep -Milliseconds 500
    & "$PSScriptRoot\build.ps1"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (Get-Process StreeckSuite -ErrorAction SilentlyContinue) {
    Write-Host "Streeck Suite ya está en ejecución." -ForegroundColor Yellow
    exit 0
}

Start-Process $Exe -WorkingDirectory "$PSScriptRoot\bin"
Write-Host ""
Write-Host " Streeck Suite iniciada:" -ForegroundColor Green
Write-Host "    1. Dynamic Island / Notch en la parte superior central" -ForegroundColor White
Write-Host "    2. Icono en la bandeja del sistema (junto al reloj)" -ForegroundColor White
Write-Host "    3. Modo Privacidad Aero (Ctrl + Alt + P)" -ForegroundColor White
Write-Host "    4. Widgets de escritorio y reloj 3D sobre tu fondo" -ForegroundColor White
Write-Host "    5. Auto-suspension en juegos a pantalla completa" -ForegroundColor White
Write-Host ""
Write-Host " Registro: $env:LOCALAPPDATA\StreeckSuite\streecksuite.log" -ForegroundColor DarkGray
