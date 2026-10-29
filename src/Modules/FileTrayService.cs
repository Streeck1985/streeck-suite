using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Archivo guardado en la bandeja de la isla. Solo se guarda la ruta: el archivo no se copia ni se mueve.
    /// </summary>
    internal class TrayFile
    {
        public string Path;
        public string Name;
        public long Size;
        public Bitmap Thumbnail;   // Miniatura (fotos, PDF...) o icono del tipo de archivo
    }

    /// <summary>
    /// Bandeja de archivos (mockup E1): rutas que el usuario soltó sobre la isla. Se guardan en
    /// %LOCALAPPDATA%\StreeckSuite\bandeja.txt si el ajuste "Conservar al reiniciar" está activado.
    /// Todo se usa desde el hilo de la interfaz.
    /// </summary>
    internal static class FileTrayService
    {
        private const int ThumbnailPx = 96;
        private static readonly string FilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Log.FilePath), "bandeja.txt");
        private static List<TrayFile> _files;

        public static event Action Changed;

        public static List<TrayFile> Files
        {
            get
            {
                Load();
                // Los que se borraron o movieron desde fuera desaparecen solos
                if (_files.RemoveAll(f => !File.Exists(f.Path) && !Directory.Exists(f.Path)) > 0) Save();
                return new List<TrayFile>(_files);
            }
        }

        public static void Add(IEnumerable<string> paths)
        {
            Load();
            foreach (string path in paths)
            {
                if (_files.Exists(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                TrayFile file = Create(path);
                if (file != null) _files.Add(file);
            }
            Save();
        }

        public static void Remove(TrayFile file)
        {
            Load();
            _files.RemoveAll(f => string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public static void Clear()
        {
            Load();
            _files.Clear();
            Save();
        }

        public static void Open(TrayFile file)
        {
            try
            {
                System.Diagnostics.Process.Start(file.Path);
            }
            catch (Exception ex)
            {
                Log.Error("[FileTrayService] No se pudo abrir {0}: {1}", file.Path, ex.Message);
            }
        }

        /// <summary>
        /// "312 KB", "1,4 MB"... (carpetas: "Carpeta").
        /// </summary>
        public static string SizeText(TrayFile file)
        {
            if (file.Size < 0) return "Carpeta";
            double size = file.Size;
            string[] units = { "B", "KB", "MB", "GB" };
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return size.ToString(unit == 0 || size >= 100 ? "0" : "0.#", new System.Globalization.CultureInfo("es-ES")) + " " + units[unit];
        }

        private static TrayFile Create(string path)
        {
            try
            {
                bool isFolder = Directory.Exists(path);
                if (!isFolder && !File.Exists(path)) return null;
                return new TrayFile
                {
                    Path = path,
                    Name = System.IO.Path.GetFileName(path.TrimEnd('\\')),
                    Size = isFolder ? -1 : new FileInfo(path).Length,
                    Thumbnail = ShellImage.Get(path, ThumbnailPx, ShellImage.ThumbnailOrIcon)
                };
            }
            catch (Exception ex)
            {
                Log.Error("[FileTrayService] No se pudo añadir {0}: {1}", path, ex.Message);
                return null;
            }
        }

        private static void Load()
        {
            if (_files != null) return;
            _files = new List<TrayFile>();
            if (!SuiteSettings.FileTrayPersist || !File.Exists(FilePath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    TrayFile file = line.Trim().Length > 0 ? Create(line.Trim()) : null;
                    if (file != null) _files.Add(file);
                }
            }
            catch (IOException ex)
            {
                Log.Error("[FileTrayService] No se pudo leer {0}: {1}", FilePath, ex.Message);
            }
        }

        private static void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                if (SuiteSettings.FileTrayPersist) foreach (TrayFile file in _files) lines.Add(file.Path);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (IOException ex)
            {
                Log.Error("[FileTrayService] No se pudo guardar {0}: {1}", FilePath, ex.Message);
            }
            Action handler = Changed;
            if (handler != null) handler();
        }
    }
}
