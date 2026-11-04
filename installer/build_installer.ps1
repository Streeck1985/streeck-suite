# Genera dist\StreeckSuite-Setup.exe: el instalador propio con la suite, la DLL de la barra y el logo dentro.
# Antes compila la suite (build.ps1). (Sin acentos a proposito: Windows PowerShell lee este archivo como ANSI.)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$compiler = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$suite = Join-Path $root "bin\StreeckSuite.exe"
$dll = Get-ChildItem (Join-Path $root "bin") -Filter "streeck_taskbar_*.dll" | Sort-Object Name | Select-Object -Last 1
if (-not (Test-Path $suite)) { throw "Falta bin\StreeckSuite.exe: compila primero con build.ps1" }
if (-not $dll) { throw "Falta la DLL de la barra: compila con native\build_tap.ps1" }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$out = Join-Path $dist "StreeckSuite-Setup.exe"

$cmdArgs = @(
    "/nologo", "/target:winexe", "/platform:x64", "/optimize+",
    "/out:$out",
    "/win32icon:$(Join-Path $root 'assets\streeck.ico')",
    "/reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll",
    "/resource:$(Join-Path $root 'StreeckSuite_512.png'),logo.png",
    "/resource:$suite,payload/StreeckSuite.exe",
    "/resource:$($dll.FullName),payload/$($dll.Name)",
    (Join-Path $PSScriptRoot "Setup.cs")
)
& $compiler @cmdArgs
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion del instalador" }
Write-Host ("Instalador: {0} ({1:0.0} MB)" -f $out, ((Get-Item $out).Length / 1MB))
