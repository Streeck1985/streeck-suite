using System;
using System.IO;
using System.Media;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Temporizador y Pomodoro de la pestaña Tiempo (mockup D1). Solo guarda la hora de fin: no hay hilos ni
    /// temporizadores propios; la isla llama a Check en cada fotograma para saber si terminó.
    /// Pomodoro: enfoque y descanso corto alternos; cada 4 enfoques, descanso largo.
    /// </summary>
    internal static class TimerService
    {
        public enum TimerMode { Timer, Pomodoro }
        public enum Phase { Focus, ShortBreak, LongBreak }

        public const int CyclesPerLongBreak = 4;

        private static TimerMode _mode = TimerMode.Timer;
        private static Phase _phase = Phase.Focus;
        private static int _cycle = 1;                       // Enfoque actual (1..4)
        private static TimeSpan _duration = TimeSpan.FromMinutes(5);
        private static TimeSpan _remainingWhenPaused = TimeSpan.FromMinutes(5);
        private static DateTime? _endsAt;                    // null = en pausa o parado
        private static SoundPlayer _player;

        public static TimerMode Mode { get { return _mode; } }
        public static Phase CurrentPhase { get { return _phase; } }
        public static int Cycle { get { return _cycle; } }
        public static TimeSpan Duration { get { return _duration; } }
        public static bool IsRunning { get { return _endsAt != null; } }

        /// <summary>
        /// En marcha o en pausa a medias (la isla cerrada lo muestra).
        /// </summary>
        public static bool IsActive
        {
            get { return IsRunning || Remaining < _duration; }
        }

        public static TimeSpan Remaining
        {
            get
            {
                if (_endsAt == null) return _remainingWhenPaused;
                TimeSpan left = _endsAt.Value - DateTime.Now;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        public static float Progress
        {
            get { return _duration.TotalSeconds <= 0 ? 0 : (float)(Remaining.TotalSeconds / _duration.TotalSeconds); }
        }

        public static void SetMode(TimerMode mode)
        {
            _mode = mode;
            _phase = Phase.Focus;
            _cycle = 1;
            Reset(mode == TimerMode.Pomodoro ? TimeSpan.FromMinutes(SuiteSettings.PomodoroFocusMin) : _duration);
        }

        /// <summary>
        /// Atajos 5/10/15/25 min: pasan a temporizador simple con ese tiempo, parado.
        /// </summary>
        public static void SetMinutes(int minutes)
        {
            _mode = TimerMode.Timer;
            Reset(TimeSpan.FromMinutes(minutes));
        }

        public static void Toggle()
        {
            if (IsRunning)
            {
                _remainingWhenPaused = Remaining;
                _endsAt = null;
            }
            else if (Remaining > TimeSpan.Zero)
            {
                _endsAt = DateTime.Now + _remainingWhenPaused;
            }
        }

        public static void Reset()
        {
            Reset(_duration);
        }

        /// <summary>
        /// Rueda sobre el anillo: ±1 minuto (entre 1 y 180); si está en marcha, se suma al tiempo que queda.
        /// </summary>
        public static void AddMinutes(int minutes)
        {
            TimeSpan delta = TimeSpan.FromMinutes(minutes);
            TimeSpan remaining = Remaining + delta;
            if (remaining < TimeSpan.FromMinutes(1) || remaining > TimeSpan.FromMinutes(180)) return;
            if (!IsActive)
            {
                Reset(remaining); // Sin empezar: cambia la duración
                return;
            }
            if (remaining > _duration) _duration = remaining; // El anillo nunca pasa de lleno
            if (_endsAt != null) _endsAt = _endsAt.Value + delta;
            else _remainingWhenPaused = remaining;
        }

        /// <summary>
        /// Si acaba de terminar: lo deja parado, suena (si está activado) y devuelve true una sola vez.
        /// </summary>
        public static bool Check()
        {
            if (_endsAt == null || DateTime.Now < _endsAt.Value) return false;
            _endsAt = null;
            _remainingWhenPaused = TimeSpan.Zero;
            if (SuiteSettings.TimerSound) PlaySound();
            return true;
        }

        /// <summary>
        /// Pomodoro: tras terminar una fase, prepara y arranca la siguiente (clic en el aviso).
        /// </summary>
        public static void StartNextPhase()
        {
            if (_mode != TimerMode.Pomodoro) { Reset(); return; }
            if (_phase == Phase.Focus)
            {
                _phase = _cycle >= CyclesPerLongBreak ? Phase.LongBreak : Phase.ShortBreak;
            }
            else
            {
                if (_phase == Phase.LongBreak) _cycle = 1; else _cycle++;
                _phase = Phase.Focus;
            }
            Reset(PhaseDuration(_phase));
            Toggle();
        }

        /// <summary>
        /// La fase que viene después de la actual (para el texto del aviso de fin).
        /// </summary>
        public static Phase NextPhase
        {
            get
            {
                if (_phase != Phase.Focus) return Phase.Focus;
                return _cycle >= CyclesPerLongBreak ? Phase.LongBreak : Phase.ShortBreak;
            }
        }

        public static TimeSpan PhaseDuration(Phase phase)
        {
            switch (phase)
            {
                case Phase.ShortBreak: return TimeSpan.FromMinutes(SuiteSettings.PomodoroShortBreakMin);
                case Phase.LongBreak: return TimeSpan.FromMinutes(SuiteSettings.PomodoroLongBreakMin);
                default: return TimeSpan.FromMinutes(SuiteSettings.PomodoroFocusMin);
            }
        }

        public static string Format(TimeSpan time)
        {
            int totalSeconds = (int)Math.Ceiling(time.TotalSeconds);
            return totalSeconds >= 3600
                ? string.Format("{0}:{1:00}:{2:00}", totalSeconds / 3600, totalSeconds / 60 % 60, totalSeconds % 60)
                : string.Format("{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);
        }

        private static void Reset(TimeSpan duration)
        {
            _duration = duration;
            _remainingWhenPaused = duration;
            _endsAt = null;
        }

        // Sonido de alarma de Windows; si no está, el sonido de aviso del sistema
        private static void PlaySound()
        {
            try
            {
                string alarm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Alarm01.wav");
                if (File.Exists(alarm))
                {
                    // Se guarda en un campo: Play es asíncrono y el reproductor debe seguir vivo mientras suena
                    if (_player == null) _player = new SoundPlayer(alarm);
                    _player.Play();
                }
                else
                {
                    SystemSounds.Exclamation.Play();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[TimerService] No se pudo reproducir el sonido: {0}", ex.Message);
            }
        }
    }
}
