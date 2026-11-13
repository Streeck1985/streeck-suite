namespace StreeckSuite.Core
{
    /// <summary>
    /// Versión de la suite. installer\build_installer.ps1 la lee de aquí para el instalador y la versión publicada
    /// en GitHub (etiqueta v + versión). Súbela antes de publicar una versión nueva.
    /// </summary>
    internal static class AppInfo
    {
        public const string Version = "1.4.4";

        /// <summary>
        /// Repositorio público con los instaladores (las versiones se publican como «releases»).
        /// </summary>
        public const string GitHubRepo = "Streeck1985/streeck-suite";
    }
}
