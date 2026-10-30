# Compila streeck_taskbar.dll (x64) con las Build Tools de Visual Studio.
# La DLL queda fijada dentro del Explorador, asi que cada compilacion lleva un numero distinto en el nombre:
# la suite carga siempre la mas reciente y las viejas se borran cuando el Explorador las suelta.
# (Sin acentos a proposito: Windows PowerShell lee este archivo como ANSI.)
$ErrorActionPreference = "Stop"
$installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$vs = & "$installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "No se encontraron las Build Tools de C++" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

$src = Join-Path $PSScriptRoot "StreeckTaskbar"
$out = (Resolve-Path (Join-Path $PSScriptRoot "..\bin")).Path
$obj = Join-Path $env:TEMP "streeck_tap_obj"
New-Item -ItemType Directory -Force $obj | Out-Null
Get-ChildItem $out -Filter "streeck_taskbar_*.dll" | ForEach-Object { try { Remove-Item $_.FullName -ErrorAction Stop } catch { } }
$name = "streeck_taskbar_{0}.dll" -f (Get-Date -Format "yyyyMMddHHmmss")
$base = Join-Path $obj ([IO.Path]::GetFileNameWithoutExtension($name))

$env:PATH = "$installer;$env:PATH"
$cmd = "`"$vcvars`" >nul 2>&1 && cl /nologo /std:c++20 /EHsc /O2 /MT /W3 /utf-8 /DUNICODE /D_UNICODE /Fo`"$obj\\`" `"$src\tap.cpp`" /LD /link /DEF:`"$src\exports.def`" /IMPLIB:`"$base.lib`" /OUT:`"$out\$name`" ole32.lib oleaut32.lib user32.lib runtimeobject.lib 2>&1"
$ErrorActionPreference = "Continue"
cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion de la DLL" }
Write-Host "DLL: $out\$name"
