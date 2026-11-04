using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Devuelve a Windows la memoria que ya no se usa (imágenes temporales del análisis del fondo, iconos, respuestas de
    /// red...). .NET la guarda "por si acaso" hasta que hay presión de memoria; aquí se compacta y se libera al minuto de
    /// arrancar, después de analizar un fondo nuevo y cada 10 minutos. Cuesta unos milisegundos.
    /// </summary>
    public static class MemoryTrim
    {
        private static Timer _timer;
        private static int _running;

        public static void Start()
        {
            _timer = new Timer(_ => Now(), null, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(10));
        }

        public static void Now()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using (Process self = Process.GetCurrentProcess())
                {
                    EmptyWorkingSet(self.Handle);
                    Log.Info("[MemoryTrim] Memoria: {0:0} MB en uso, {1:0} MB privados", self.WorkingSet64 / 1048576.0, self.PrivateMemorySize64 / 1048576.0);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[MemoryTrim] {0}", ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr process);
    }
}
