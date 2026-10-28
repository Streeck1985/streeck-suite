using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Lectura de uso de CPU, RAM y GPU del equipo y del propio proceso de la suite.
    /// Pensado para llamarse desde un hilo en segundo plano: la lectura de contadores de GPU puede tardar.
    /// </summary>
    public class TelemetrySampler : IDisposable
    {
        public class Snapshot
        {
            public float CpuPercent;
            public float RamPercent;
            public double RamUsedGb;
            public double RamTotalGb;
            public float GpuPercent;      // -1 si los contadores de GPU no están disponibles
            public double SuiteRamMb;
            public float SuiteCpuPercent;
        }

        private readonly PerformanceCounterCategory _gpuCategory = new PerformanceCounterCategory("GPU Engine");
        private readonly Process _ownProcess = Process.GetCurrentProcess();
        private Dictionary<string, CounterSample> _lastGpuSamples = new Dictionary<string, CounterSample>();
        private bool _gpuAvailable = true;

        private long _lastIdle, _lastKernel, _lastUser;
        private TimeSpan _lastSuiteCpu;
        private DateTime _lastSampleTime;

        public TelemetrySampler()
        {
            NativeMethods.GetSystemTimes(out _lastIdle, out _lastKernel, out _lastUser);
            _lastSuiteCpu = _ownProcess.TotalProcessorTime;
            _lastSampleTime = DateTime.UtcNow;
        }

        public Snapshot Sample()
        {
            Snapshot snapshot = new Snapshot();
            snapshot.CpuPercent = SampleCpu();
            SampleRam(snapshot);
            snapshot.GpuPercent = SampleGpu();
            SampleSuite(snapshot);
            return snapshot;
        }

        private float SampleCpu()
        {
            long idle, kernel, user;
            if (!NativeMethods.GetSystemTimes(out idle, out kernel, out user)) return 0;

            // El tiempo de kernel incluye el tiempo inactivo
            long idleDelta = idle - _lastIdle;
            long totalDelta = (kernel - _lastKernel) + (user - _lastUser);
            _lastIdle = idle;
            _lastKernel = kernel;
            _lastUser = user;

            if (totalDelta <= 0) return 0;
            return Clamp(100f * (totalDelta - idleDelta) / totalDelta);
        }

        private static void SampleRam(Snapshot snapshot)
        {
            NativeMethods.MEMORYSTATUSEX status = new NativeMethods.MEMORYSTATUSEX();
            status.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MEMORYSTATUSEX));
            if (!NativeMethods.GlobalMemoryStatusEx(ref status)) return;

            const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
            snapshot.RamPercent = status.dwMemoryLoad;
            snapshot.RamTotalGb = status.ullTotalPhys / bytesPerGb;
            snapshot.RamUsedGb = (status.ullTotalPhys - status.ullAvailPhys) / bytesPerGb;
        }

        /// <summary>
        /// Suma la utilización de los motores 3D de todos los procesos (lo mismo que muestra el Administrador de tareas).
        /// Lee la categoría completa de una vez: con un PerformanceCounter por motor (hay cientos) cada lectura
        /// recorría la categoría entera y la suite gastaba ~12% de un núcleo en reposo.
        /// </summary>
        private float SampleGpu()
        {
            if (!_gpuAvailable) return -1;

            try
            {
                InstanceDataCollection engines = _gpuCategory.ReadCategory()["utilization percentage"];
                Dictionary<string, CounterSample> current = new Dictionary<string, CounterSample>();
                float total = 0;

                foreach (InstanceData engine in engines.Values)
                {
                    if (!engine.InstanceName.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;

                    current[engine.InstanceName] = engine.Sample;
                    CounterSample previous;
                    if (_lastGpuSamples.TryGetValue(engine.InstanceName, out previous))
                    {
                        total += CounterSample.Calculate(previous, engine.Sample);
                    }
                }

                // Al reemplazar el diccionario se descartan solos los motores de procesos que ya terminaron
                _lastGpuSamples = current;
                return Clamp(total);
            }
            catch (Exception ex)
            {
                Log.Error("[TelemetrySampler] Contadores de GPU no disponibles: {0}", ex.Message);
                _gpuAvailable = false;
                return -1;
            }
        }

        private void SampleSuite(Snapshot snapshot)
        {
            _ownProcess.Refresh();
            DateTime now = DateTime.UtcNow;
            TimeSpan cpu = _ownProcess.TotalProcessorTime;

            double elapsedMs = (now - _lastSampleTime).TotalMilliseconds;
            if (elapsedMs > 0)
            {
                snapshot.SuiteCpuPercent = Clamp((float)(100.0 * (cpu - _lastSuiteCpu).TotalMilliseconds / (elapsedMs * Environment.ProcessorCount)));
            }
            // Memoria privada: la que la suite ocupa en exclusiva (la columna "Memoria" del Administrador de tareas)
            snapshot.SuiteRamMb = _ownProcess.PrivateMemorySize64 / (1024.0 * 1024.0);

            _lastSuiteCpu = cpu;
            _lastSampleTime = now;
        }

        /// <summary>
        /// Nombre corto del procesador, p. ej. "Ryzen 7 5800X" o "Core i7-12700H".
        /// </summary>
        public static string GetCpuName()
        {
            string name = ReadRegistryString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
            if (name == null) return "CPU";

            name = name.Replace("(R)", "").Replace("(TM)", "").Replace("AMD ", "").Replace("Intel ", "");
            int cut = name.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
            if (cut < 0) cut = name.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
            {
                // "Ryzen 7 5800X 8-Core Processor": quitar también el número de núcleos que precede a "-Core"
                int lastSpace = name.LastIndexOf(' ', cut);
                name = name.Substring(0, lastSpace > 0 && name[cut] == '-' ? lastSpace : cut);
            }
            cut = name.IndexOf(" CPU", StringComparison.OrdinalIgnoreCase);
            if (cut > 0) name = name.Substring(0, cut);
            return name.Trim();
        }

        /// <summary>
        /// Nombre corto de la tarjeta gráfica principal, p. ej. "RTX 3060".
        /// </summary>
        public static string GetGpuName()
        {
            const string displayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            for (int i = 0; i < 8; i++)
            {
                string name = ReadRegistryString(displayClass + "\\" + i.ToString("0000"), "DriverDesc");
                if (name == null || name.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                return name.Replace("NVIDIA ", "").Replace("GeForce ", "").Replace("AMD ", "").Replace("(TM)", "")
                           .Replace("(R)", "").Replace("Graphics", "").Trim();
            }
            return "GPU";
        }

        private static string ReadRegistryString(string keyPath, string valueName)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    return key == null ? null : key.GetValue(valueName) as string;
                }
            }
            catch (Exception)
            {
                return null; // Algunas subclaves de drivers no se pueden leer sin permisos de administrador
            }
        }

        private static float Clamp(float value)
        {
            return Math.Max(0f, Math.Min(100f, value));
        }

        public void Dispose()
        {
            _lastGpuSamples.Clear();
            _ownProcess.Dispose();
        }
    }
}
