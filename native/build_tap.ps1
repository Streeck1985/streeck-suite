# Compila streeck_taskbar.dll con las Build Tools de Visual Studio: x64 (streeck_taskbar_*.dll) y
# ARM64 para PCs con Snapdragon (streeck_arm64_*.dll; la suite la mete con el rundll32 de Windows).
# La DLL queda fijada dentro del Explorador, asi que cada compilacion lleva un numero distinto en el nombre:
# la suite carga siempre la mas reciente y las viejas se borran cuando el Explorador las suelta.
# (Sin acentos a proposito: Windows PowerShell lee este archivo como ANSI.)
$ErrorActionPreference = "Stop"
$installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
$vs = & "$installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "No se encontraron las Build Tools de C++" }

$src = Join-Path $PSScriptRoot "StreeckTaskbar"
$out = (Resolve-Path (Join-Path $PSScriptRoot "..\bin")).Path
$stamp = Get-Date -Format "yyyyMMddHHmmss"
$env:PATH = "$installer;$env:PATH"

function Build-Tap($vcvarsName, $prefix, $objName) {
    $vcvars = Join-Path $vs "VC\Auxiliary\Build\$vcvarsName"
    if (-not (Test-Path $vcvars)) { throw "Falta $vcvarsName (componente de C++ para esa arquitectura)" }
    $obj = Join-Path $env:TEMP $objName
    New-Item -ItemType Directory -Force $obj | Out-Null
    Get-ChildItem $out -Filter "$prefix*.dll" | ForEach-Object { try { Remove-Item $_.FullName -ErrorAction Stop } catch { } }
    $name = "$prefix$stamp.dll"
    $base = Join-Path $obj ([IO.Path]::GetFileNameWithoutExtension($name))
    $cmd = "`"$vcvars`" >nul 2>&1 && cl /nologo /std:c++20 /EHsc /O2 /MT /W3 /utf-8 /DUNICODE /D_UNICODE /Fo`"$obj\\`" `"$src\tap.cpp`" /LD /link /DEF:`"$src\exports.def`" /IMPLIB:`"$base.lib`" /OUT:`"$out\$name`" ole32.lib oleaut32.lib user32.lib runtimeobject.lib 2>&1"
    $ErrorActionPreference = "Continue"
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion de $name" }
    $ErrorActionPreference = "Stop"
    Write-Host "DLL: $out\$name"
}

Build-Tap "vcvars64.bat" "streeck_taskbar_" "streeck_tap_obj"
Build-Tap "vcvarsamd64_arm64.bat" "streeck_arm64_" "streeck_tap_obj_arm64"
