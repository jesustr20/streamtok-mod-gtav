using System;
using System.IO;
using System.Text;

namespace StreamTok.GtaV.Util
{
    /// <summary>
    /// Log del mod en %LOCALAPPDATA%\StreamTok\logs\StreamTok.GtaV.log (siempre se puede escribir,
    /// aunque el juego esté en Program Files). Rota al pasar 1 MB: guarda hasta 3 archivos viejos
    /// (.1.log, .2.log, .3.log). Seguro entre hilos (lo usa también el WebSocket).
    /// Si esa carpeta falla, escribe junto al DLL (scripts\).
    /// </summary>
    internal sealed class FileLog
    {
        private const long MaxBytes = 1024 * 1024;
        private const int Keep = 3;

        private readonly object _lock = new object();
        private readonly string _path;

        public FileLog(string fallbackDirectory)
        {
            string dir;
            try
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamTok", "logs");
                Directory.CreateDirectory(dir);
            }
            catch
            {
                dir = fallbackDirectory;
            }
            _path = Path.Combine(dir, "StreamTok.GtaV.log");
        }

        public string FilePath => _path;

        public void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    Rotate();
                    File.AppendAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch
            {
                // Nunca tumbar el juego por un fallo de log.
            }
        }

        private static string Old(string baseName, int n) => $"{baseName}.{n}.log";

        private void Rotate()
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxBytes) return;

            string baseName = Path.Combine(info.DirectoryName ?? "", Path.GetFileNameWithoutExtension(_path));
            if (File.Exists(Old(baseName, Keep))) File.Delete(Old(baseName, Keep));
            for (int n = Keep - 1; n >= 1; n--)
            {
                if (File.Exists(Old(baseName, n))) File.Move(Old(baseName, n), Old(baseName, n + 1));
            }
            File.Move(_path, Old(baseName, 1));
        }
    }
}
