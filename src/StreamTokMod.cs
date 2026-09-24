using System;
using System.IO;
using GTA;
using GTA.UI;

namespace StreamTok.GtaV
{
    /// <summary>
    /// Fase 2: se conecta al sidecar de StreamTok por WebSocket y muestra en pantalla
    /// cada evento del LIVE que recibe. Todavía no ejecuta efectos (Fase 3).
    ///
    /// Configuración opcional en scripts\StreamTok.GtaV.ini:
    ///   [Connection]
    ///   Url=ws://localhost:7331
    /// </summary>
    public sealed class StreamTokMod : Script
    {
        private const string ModVersion = "0.2.0";
        private const string DefaultUrl = "ws://localhost:7331";

        /// <summary>Máximo de notificaciones por tick, para no inundar la pantalla en ráfagas de regalos.</summary>
        private const int MaxEventsPerTick = 3;

        private readonly string _logPath;
        private readonly StreamTokClient _client;
        private bool _announced;

        public StreamTokMod()
        {
            _logPath = Path.Combine(BaseDirectory, "StreamTok.GtaV.log");
            Log($"Constructor ejecutado: SHVDN instanció StreamTokMod v{ModVersion}.");

            string url = Settings.GetValue("Connection", "Url", DefaultUrl);
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                Log($"Url inválida en el .ini ('{url}'); se usa {DefaultUrl}.");
                uri = new Uri(DefaultUrl);
            }

            _client = new StreamTokClient(uri, Log);
            _client.Start();

            Interval = 100;
            Tick += OnTick;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            // Mientras el juego carga, los eventos se quedan en cola.
            if (Game.IsLoading)
            {
                return;
            }

            if (!_announced)
            {
                Notification.Show($"~p~StreamTok~s~ v{ModVersion} cargado ~g~OK");
                _announced = true;
            }

            bool connected;
            while (_client.StatusChanges.TryDequeue(out connected))
            {
                Notification.Show(connected
                    ? "~p~StreamTok~s~: ~g~conectado~s~ a la app"
                    : "~p~StreamTok~s~: ~r~desconectado~s~, reintentando…");
            }

            LiveEvent evt;
            for (int i = 0; i < MaxEventsPerTick && _client.Events.TryDequeue(out evt); i++)
            {
                string line = Describe(evt);
                Notification.Show(line);
                Log($"Evento: {line}");
            }
        }

        /// <summary>
        /// Texto de la notificación. Los valores de 'event' deben coincidir con el enum de
        /// LiveEventSchema; los desconocidos se muestran de forma genérica.
        /// </summary>
        private static string Describe(LiveEvent evt)
        {
            string user = "~b~@" + Clean(evt.Username ?? "anónimo") + "~s~";

            switch ((evt.Event ?? "").ToLowerInvariant())
            {
                case "gift":
                    string gift = Clean(evt.GiftName ?? "un regalo");
                    string amount = evt.Value.HasValue ? $" ~y~({evt.Value.Value:0.##})~s~" : "";
                    return $"{user} envió ~y~{gift}~s~{amount}";
                case "like":
                    return $"{user} dio like";
                case "follow":
                    return $"{user} te siguió";
                case "share":
                    return $"{user} compartió el LIVE";
                case "subscribe":
                    return $"{user} se suscribió";
                case "chat":
                case "comment":
                    return $"{user}: {Clean(evt.Text ?? "")}";
                default:
                    return $"{user} · {Clean(evt.Event ?? "evento")}";
            }
        }

        /// <summary>
        /// Quita '~' del texto de usuarios: GTA lo usa para códigos de formato (~r~, ~n~…)
        /// y un nombre o comentario malicioso podría romper la notificación.
        /// </summary>
        private static string Clean(string s)
        {
            s = s.Replace("~", "");
            return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _client.Dispose();
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
                // Nunca tumbar el juego por un fallo de log (también se llama desde el hilo del WS).
            }
        }
    }
}
