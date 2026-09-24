using System;
using System.IO;
using GTA;
using GTA.UI;

namespace StreamTok.GtaV
{
    /// <summary>
    /// Fase 1 del roadmap: SOLO confirma que ScriptHookVDotNet carga el script.
    /// Sin WebSocket ni efectos todavía (eso es Fase 2 y 3).
    ///
    /// Dos señales de que cargó, a propósito redundantes:
    ///   1. Notificación en pantalla al terminar de cargar la partida.
    ///   2. Archivo scripts\StreamTok.GtaV.log (sirve aunque la notificación no se vea).
    /// </summary>
    public sealed class StreamTokMod : Script
    {
        private const string ModVersion = "0.1.0";

        private readonly string _logPath;
        private bool _announced;

        public StreamTokMod()
        {
            _logPath = Path.Combine(BaseDirectory, "StreamTok.GtaV.log");
            Log($"Constructor ejecutado: SHVDN instanció StreamTokMod v{ModVersion}.");

            // No necesitamos correr cada frame en esta fase.
            Interval = 500;

            Tick += OnTick;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            // Esperar a que termine la pantalla de carga; si no, la notificación se pierde.
            if (_announced || Game.IsLoading)
            {
                return;
            }

            Notification.Show($"~p~StreamTok~s~ v{ModVersion} cargado ~g~OK");
            Log("Notificación mostrada en pantalla.");
            _announced = true;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            // Se dispara al recargar scripts (tecla Insert) o al cerrar el juego.
            Log("Script detenido (Aborted).");
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Nunca tumbar el juego por un fallo de log.
            }
        }
    }
}
