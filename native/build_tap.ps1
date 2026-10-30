# Compila streeck_taskbar.dll (x64) con las Build Tools de Visual Studio.
# La DLL queda fijada dentro del Explorador, así que cada compilación lleva un número distinto en el nombre:
# la suite carga siempre la más reciente y las viejas se borran cuando el Explorador las suelta.
$ErrorActionPreference = "Stop"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "No se encontraron las Build Tools de C++" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

$src = Join-Path $PSScriptRoot "StreeckTaskbar"
$out = Join-Path $PSScriptRoot "..\bin"
$obj = Join-Path $env:TEMP "streeck_tap_obj"
New-Item -ItemType Directory -Force $out, $obj | Out-Null
Get-ChildItem $out -Filter "streeck_taskbar_*.dll" | ForEach-Object { try { Remove-Item $_.FullName -ErrorAction Stop } catch { } }
$name = "streeck_taskbar_{0}.dll" -f (Get-Date -Format "yyyyMMddHHmmss")

$cmd = "`"$vcvars`" >nul && cl /nologo /std:c++17 /EHsc /O2 /MT /W3 /utf-8 /DUNICODE /D_UNICODE /Fo`"$obj\\`" `"$src\tap.cpp`" /LD /link /DEF:`"$src\exports.def`" /OUT:`"$out\$name`" ole32.lib oleaut32.lib user32.lib runtimeobject.lib"
cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "Falló la compilación de la DLL" }
Remove-Item (Join-Path $out ($name -replace '\.dll$', '.lib')), (Join-Path $out ($name -replace '\.dll$', '.exp')) -ErrorAction SilentlyContinue
Write-Host "DLL: $out\$name"
