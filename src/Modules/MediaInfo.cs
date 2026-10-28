using System;
using System.Drawing;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Lo que suena ahora mismo en cualquier reproductor (Spotify, YouTube en el navegador, Apple Music...),
    /// según el control multimedia de Windows. Instantánea inmutable: se sustituye entera al cambiar algo.
    /// </summary>
    internal class MediaInfo
    {
        public static readonly MediaInfo Nothing = new MediaInfo();

        public string Title = "Sin reproducción";
        public string Artist = "";
        public string AppName = "";
        public bool IsPlaying;
        public bool IsAvailable;          // Hay una sesión multimedia (sonando o en pausa)
        public Bitmap Art;                // Carátula reducida, o null si el reproductor no la da
        public Color Accent = Color.FromArgb(34, 197, 94);
        public TimeSpan Position;
        public TimeSpan Duration;
        public DateTime PositionSampledAt;

        /// <summary>
        /// "Artista • Reproduciendo" para mostrar bajo el título.
        /// </summary>
        public string Subtitle
        {
            get
            {
                if (!IsAvailable) return "Nada sonando ahora";
                string state = IsPlaying ? "Reproduciendo" : "En pausa";
                return string.IsNullOrEmpty(Artist) ? state : Artist + " • " + state;
            }
        }

        /// <summary>
        /// Progreso de 0 a 1 (o -1 si el reproductor no informa de la duración), extrapolado desde la última lectura.
        /// </summary>
        public float Progress
        {
            get
            {
                if (Duration.TotalSeconds < 1) return -1;
                TimeSpan position = Position;
                if (IsPlaying) position += DateTime.Now - PositionSampledAt;
                return (float)Math.Max(0, Math.Min(1, position.TotalSeconds / Duration.TotalSeconds));
            }
        }

        public bool SameTrackAs(MediaInfo other)
        {
            return other != null && Title == other.Title && Artist == other.Artist && AppName == other.AppName;
        }
    }
}
