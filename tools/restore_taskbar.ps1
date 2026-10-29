# Devuelve la barra de tareas de Windows si la suite se cerró a la fuerza (por ejemplo, desde el Administrador de
# tareas) mientras la barra de Streeck estaba activa. Reinicia el Explorador, que vuelve a mostrar su barra, y
# recupera el estado de "ocultar automáticamente" que había antes.
$estado = Join-Path $env:LOCALAPPDATA "StreeckSuite\barra_estado.txt"
$autoOcultar = $false
if (Test-Path $estado) {
    $valor = 0
    [int]::TryParse((Get-Content $estado -Raw).Trim(), [ref]$valor) | Out-Null
    $autoOcultar = ($valor -band 1) -ne 0
    Remove-Item $estado -Force
}

# StuckRects3: el byte 8 guarda el estado de la barra (3 = ocultar automáticamente, 2 = siempre visible)
$clave = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3"
$ajustes = (Get-ItemProperty -Path $clave).Settings
$ajustes[8] = if ($autoOcultar) { 3 } else { 2 }
Set-ItemProperty -Path $clave -Name Settings -Value $ajustes

Stop-Process -Name explorer -Force
Write-Host "Barra de Windows restaurada."
