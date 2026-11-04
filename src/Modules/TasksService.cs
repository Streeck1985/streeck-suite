using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Tareas con hora (fase J4, idea de Nusa): se agregan hablándole al asistente ("recuérdame enviar la factura a las
    /// 6") y avisan en la isla a su hora. Se guardan en %LOCALAPPDATA%\StreeckSuite\tareas.txt, una por línea:
    /// fecha|hecha|avisada|texto (fecha vacía = sin hora).
    /// </summary>
    internal static class TasksService
    {
        internal class TaskItem
        {
            public DateTime? Due;
            public bool Done;
            public bool Alerted;
            public string Text;
        }

        private static readonly string FilePath = Path.Combine(Path.GetDirectoryName(Log.FilePath), "tareas.txt");
        private static readonly object Sync = new object();
        private static List<TaskItem> _tasks;

        public static event Action Changed;

        /// <summary>
        /// Pendientes primero (por hora), luego las hechas de hoy.
        /// </summary>
        public static List<TaskItem> Tasks
        {
            get
            {
                lock (Sync)
                {
                    Load();
                    List<TaskItem> list = _tasks.FindAll(t => !t.Done || (t.Due ?? DateTime.Today).Date >= DateTime.Today.AddDays(-1));
                    list.Sort((a, b) => a.Done != b.Done ? (a.Done ? 1 : -1) : (a.Due ?? DateTime.MaxValue).CompareTo(b.Due ?? DateTime.MaxValue));
                    return list;
                }
            }
        }

        public static void Add(string text, DateTime? due)
        {
            lock (Sync)
            {
                Load();
                _tasks.Add(new TaskItem { Text = text.Trim(), Due = due });
                Save();
            }
            Raise();
        }

        public static void Toggle(TaskItem task)
        {
            lock (Sync)
            {
                task.Done = !task.Done;
                Save();
            }
            if (task.Done) IslandEyes.React(IslandEyes.Mood.Wink, 2); // Tarea terminada: guiño
            Raise();
        }

        /// <summary>
        /// Tareas a las que les llegó la hora y aún no se avisaron (se marcan como avisadas).
        /// </summary>
        public static List<TaskItem> TakeDue()
        {
            List<TaskItem> due = new List<TaskItem>();
            lock (Sync)
            {
                Load();
                foreach (TaskItem task in _tasks)
                {
                    if (task.Done || task.Alerted || task.Due == null || task.Due.Value > DateTime.Now) continue;
                    if ((DateTime.Now - task.Due.Value).TotalHours > 12) { task.Alerted = true; continue; } // Muy viejas: no avisar
                    task.Alerted = true;
                    due.Add(task);
                }
                if (due.Count > 0) Save();
            }
            return due;
        }

        public static string TimeText(TaskItem task)
        {
            if (task.Due == null) return "";
            DateTime due = task.Due.Value;
            string time = due.ToString("HH:mm");
            if (due.Date == DateTime.Today) return time;
            if (due.Date == DateTime.Today.AddDays(1)) return "mañana " + time;
            return due.ToString("d MMM", new CultureInfo("es-MX")) + " " + time;
        }

        private static void Load()
        {
            if (_tasks != null) return;
            _tasks = new List<TaskItem>();
            if (!File.Exists(FilePath)) return;
            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                string[] parts = line.Split(new[] { '|' }, 4);
                if (parts.Length < 4) continue;
                DateTime due;
                _tasks.Add(new TaskItem
                {
                    Due = DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out due) ? due : (DateTime?)null,
                    Done = parts[1] == "1",
                    Alerted = parts[2] == "1",
                    Text = parts[3]
                });
            }
        }

        private static void Save()
        {
            // Se descartan las hechas de hace más de una semana
            _tasks.RemoveAll(t => t.Done && (t.Due ?? DateTime.Today).Date < DateTime.Today.AddDays(-7));
            List<string> lines = new List<string>();
            foreach (TaskItem task in _tasks)
            {
                lines.Add((task.Due == null ? "" : task.Due.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) + "|" +
                          (task.Done ? "1" : "0") + "|" + (task.Alerted ? "1" : "0") + "|" + task.Text.Replace("\r", " ").Replace("\n", " "));
            }
            try
            {
                File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
            }
            catch (IOException ex)
            {
                Log.Error("[Tasks] No se pudieron guardar las tareas: {0}", ex.Message);
            }
        }

        private static void Raise()
        {
            Action changed = Changed;
            if (changed != null) changed();
        }
    }
}
