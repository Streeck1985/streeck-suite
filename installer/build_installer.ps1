# Genera dist\StreeckSuite-Setup.exe: el instalador propio con la suite, la DLL de la barra y el logo dentro.
# Antes compila la suite (build.ps1). (Sin acentos a proposito: Windows PowerShell lee este archivo como ANSI.)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$compiler = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$suite = Join-Path $root "bin\StreeckSuite.exe"
$dll = Get-ChildItem (Join-Path $root "bin") -Filter "streeck_taskbar_*.dll" | Sort-Object Name | Select-Object -Last 1
if (-not (Test-Path $suite)) { throw "Falta bin\StreeckSuite.exe: compila primero con build.ps1" }
if (-not $dll) { throw "Falta la DLL de la barra: compila con native\build_tap.ps1" }
$dllArm = Get-ChildItem (Join-Path $root "bin") -Filter "streeck_arm64_*.dll" | Sort-Object Name | Select-Object -Last 1
if (-not $dllArm) { throw "Falta la DLL ARM64 de la barra: compila con native\build_tap.ps1" }

# Version: la misma de la suite (src\Core\AppInfo.cs)
$appInfo = [IO.File]::ReadAllText((Join-Path $root "src\Core\AppInfo.cs"))
$version = [regex]::Match($appInfo, 'Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw "No se encontro la version en src\Core\AppInfo.cs" }
$buildInfo = Join-Path $PSScriptRoot "BuildInfo.g.cs"
[IO.File]::WriteAllText($buildInfo, "namespace StreeckSetup { internal static class BuildInfo { public const string Version = `"$version`"; } }")

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
    "/resource:$($dllArm.FullName),payload/$($dllArm.Name)",
    # WebView2 (Claude en la isla): libreria de Microsoft
    "/resource:$(Join-Path $root 'lib\webview2\Microsoft.Web.WebView2.Core.dll'),payload/Microsoft.Web.WebView2.Core.dll",
    "/resource:$(Join-Path $root 'lib\webview2\Microsoft.Web.WebView2.WinForms.dll'),payload/Microsoft.Web.WebView2.WinForms.dll",
    "/resource:$(Join-Path $root 'lib\webview2\WebView2Loader.dll'),payload/WebView2Loader.dll",
    (Join-Path $PSScriptRoot "Setup.cs"),
    $buildInfo
)
& $compiler @cmdArgs
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion del instalador" }
Write-Host ("Instalador {0}: {1} ({2:0.0} MB)" -f $version, $out, ((Get-Item $out).Length / 1MB))
