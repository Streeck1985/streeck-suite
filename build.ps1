$Compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $Compiler)) {
    $Compiler = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$OutputDir = "$PSScriptRoot\bin"
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# El simulador web se abre como file:/// y ahi fetch() no puede leer settings.json,
# asi que se genera una copia como script a partir de la misma fuente
$ConfigJson = Get-Content "$PSScriptRoot\config\settings.json" -Raw -Encoding UTF8
$ConfigJs = "// Generado por build.ps1 a partir de settings.json. No editar a mano.`nwindow.SUITE_CONFIG = $ConfigJson;`n"
[System.IO.File]::WriteAllText("$PSScriptRoot\config\settings.js", $ConfigJs, (New-Object System.Text.UTF8Encoding $false))

$OutputFile = "$OutputDir\StreeckSuite.exe"
$SourceFiles = Get-ChildItem -Path "$PSScriptRoot\src" -Filter "*.cs" -Recurse | ForEach-Object { $_.FullName }

Write-Host "Compilando Windows 11 Custom Suite (C#)..." -ForegroundColor Cyan
Write-Host "Fuentes: $($SourceFiles.Count) archivos"

$RefArg = "/reference:System.dll,System.Core.dll,System.Data.dll,System.Drawing.dll,System.Windows.Forms.dll"

# Windows ML (reloj 3D): metadatos WinRT que trae Windows y las fachadas de .NET para usarlos desde C#
$FrameworkDir = Split-Path $Compiler
$WinMetadata = "$env:SystemRoot\System32\WinMetadata"
$WinRtRefs = @(
    "$FrameworkDir\System.Runtime.dll",
    "$FrameworkDir\System.Runtime.WindowsRuntime.dll",
    "$FrameworkDir\System.Runtime.InteropServices.WindowsRuntime.dll",
    "$WinMetadata\Windows.AI.winmd",
    "$WinMetadata\Windows.ApplicationModel.winmd",
    "$WinMetadata\Windows.Foundation.winmd",
    "$WinMetadata\Windows.Graphics.winmd",
    "$WinMetadata\Windows.Media.winmd",
    "$WinMetadata\Windows.Storage.winmd",
    "$WinMetadata\Windows.UI.winmd"
) | ForEach-Object { "/reference:$_" }

$CmdArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:x64",
    "/utf8output",
    "/win32icon:$PSScriptRoot\assets\streeck.ico",
    "/resource:$PSScriptRoot\assets\streeck.ico,StreeckSuite.streeck.ico",
    $RefArg,
    "/out:$OutputFile"
) + $WinRtRefs + $SourceFiles

& $Compiler $CmdArgs

if ($LASTEXITCODE -eq 0) {
    $Bytes = (Get-Item $OutputFile).Length
    Write-Host "Compilacion exitosa!" -ForegroundColor Green
    Write-Host "Binario: $OutputFile ($([math]::Round($Bytes / 1KB, 1)) KB)" -ForegroundColor Green
} else {
    Write-Host "Fallo la compilacion (codigo $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}
