# Desarrollo: reinicia el Explorador y le mete la DLL mas reciente de bin\ (la vieja queda fijada hasta reiniciarlo).
param([string]$Ajustes = "activo=1`nformato=0`nseparacion=14`nextremos=0`ncristal=0")
$data = Join-Path $env:LOCALAPPDATA "StreeckSuite"
Set-Content (Join-Path $data "barra_skin.ini") $Ajustes -Encoding Ascii
Remove-Item (Join-Path $data "barra_skin.log") -ErrorAction SilentlyContinue
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Threading;
public static class SkinDev {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryW(string p);
  [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string n);
  [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id, IntPtr proc, IntPtr mod, uint tid);
  [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
  public static string Inject(string dll) {
    IntPtr mod = LoadLibraryW(dll); if (mod == IntPtr.Zero) return "LoadLibrary fallo " + Marshal.GetLastWin32Error();
    IntPtr proc = GetProcAddress(mod, "StreeckHookProc");
    IntPtr tray = FindWindow("Shell_TrayWnd", null); uint pid; uint tid = GetWindowThreadProcessId(tray, out pid);
    IntPtr hook = SetWindowsHookEx(4, proc, mod, tid); if (hook == IntPtr.Zero) return "SetWindowsHookEx fallo " + Marshal.GetLastWin32Error();
    IntPtr r; SendMessageTimeout(tray, 0, IntPtr.Zero, IntPtr.Zero, 2, 3000, out r);
    Thread.Sleep(500); UnhookWindowsHookEx(hook); return "ok";
  }
}
'@
Stop-Process -Name explorer -Force
Start-Sleep 2
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
for ($i = 0; $i -lt 30 -and [SkinDev]::FindWindow("Shell_TrayWnd", $null) -eq [IntPtr]::Zero; $i++) { Start-Sleep 1 }
Start-Sleep 4
$dll = (Get-ChildItem (Join-Path $PSScriptRoot "..\bin\streeck_taskbar_*.dll") | Sort-Object Name | Select-Object -Last 1).FullName
"Inyeccion: " + [SkinDev]::Inject($dll)
Start-Sleep 4
(New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::AutoReset, "Local\StreeckSuite.TaskbarSkin")).Set() | Out-Null
Start-Sleep 2
Get-Content (Join-Path $data "barra_skin.log") -Tail 12
