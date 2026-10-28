using System;
using System.IO;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Registro en %LOCALAPPDATA%\StreeckSuite\streecksuite.log.
    /// Sustituye a Debug.WriteLine, que se elimina al compilar sin el símbolo DEBUG.
    /// </summary>
    public static class Log
    {
        private const long MaxLogBytes = 1024 * 1024;
        private static readonly object _lock = new object();
        private static readonly string _logPath;

        static Log()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreeckSuite");
            _logPath = Path.Combine(dir, "streecksuite.log");
            try
            {
                Directory.CreateDirectory(dir);
                FileInfo info = new FileInfo(_logPath);
                if (info.Exists && info.Length > MaxLogBytes)
                {
                    info.Delete();
                }
            }
            catch (Exception)
            {
                // Sin acceso al disco no hay log, pero la suite debe seguir funcionando
            }
        }

        public static string FilePath
        {
            get { return _logPath; }
        }

        public static void Info(string format, params object[] args)
        {
            Write("INFO ", format, args);
        }

        public static void Error(string format, params object[] args)
        {
            Write("ERROR", format, args);
        }

        private static void Write(string level, string format, object[] args)
        {
            string message = args.Length > 0 ? string.Format(format, args) : format;
            string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} {1} {2}", DateTime.Now, level, message);

            System.Diagnostics.Debug.WriteLine(line);
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
                catch (Exception)
                {
                    // Ignorado: un fallo de escritura del log no debe tumbar la suite
                }
            }
        }
    }
}
