using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Debug;
using StreamTok.GtaV.Entities;
using Notification = GTA.UI.Notification;

namespace StreamTok.GtaV
{
    /// <summary>
    /// Punto de entrada del mod. Se conecta a StreamTok por WebSocket, publica su
    /// catálogo de acciones (mod-hello) y ejecuta los comandos que recibe (mod-command).
    /// No sabe nada de TikTok: qué evento dispara qué acción lo decide StreamTok.
    ///
    /// Configuración opcional en scripts\StreamTok.GtaV.ini:
    ///   [Connection]
    ///   Url=ws://localhost:7331
    ///   [Limits]
    ///   MaxSpawnedPeds=100
    ///   [Debug]
    ///   MenuEnabled=true
    ///   TestNameTag=Viewer de prueba
    /// </summary>
    public sealed class StreamTokMod : Script
    {
        private const string ModVersion = "0.2.0";
        private const string DefaultUrl = "ws://localhost:7331";

        /// <summary>Máximo de comandos por frame, para repartir ráfagas y no congelar el juego.</summary>
        private const int MaxCommandsPerTick = 3;

        private readonly string _logPath;
        private readonly Random _rng = new Random();
        private readonly ActionRegistry _registry;
        private readonly EntityTracker _tracker;
        private readonly StreamTokClient _client;
        private readonly DebugMenu _menu;
        private readonly string _testNameTag;
        private bool _announced;

        public StreamTokMod()
        {
            _logPath = Path.Combine(BaseDirectory, "StreamTok.GtaV.log");
            Log($"Constructor ejecutado: SHVDN instanció StreamTokMod v{ModVersion}.");

            _registry = ActionRegistry.CreateDefault();
            _tracker = new EntityTracker(Setting("Limits", "MaxSpawnedPeds", 100));
            _testNameTag = TextUtil.CleanTag(Setting("Debug", "TestNameTag", "Viewer de prueba"));

            string url = Setting("Connection", "Url", DefaultUrl);
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                Log($"Url inválida en el .ini ('{url}'); se usa {DefaultUrl}.");
                uri = new Uri(DefaultUrl);
            }

            _client = new StreamTokClient(uri, Protocol.BuildHello(ModVersion, _registry.All), Log);
            _client.Start();

            if (Setting("Debug", "MenuEnabled", true))
            {
                _menu = new DebugMenu(_registry.All, RunFromMenu);
            }

            Log($"Catálogo: {_registry.All.Count} acciones. Menú de pruebas: {(_menu != null ? "F7" : "desactivado")}.");

            Interval = 0; // cada frame: los nombres y el menú se dibujan frame a frame
            Tick += OnTick;
            KeyDown += (s, e) => _menu?.OnKeyDown(e.KeyCode, e.Shift);
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            // Mientras el juego carga, los comandos se quedan en cola.
            if (GTA.Game.IsLoading)
            {
                return;
            }

            if (!_announced)
            {
                Notification.Show($"~p~StreamTok~s~ v{ModVersion} cargado ~g~OK");
                _announced = true;
            }

            while (_client.StatusChanges.TryDequeue(out bool connected))
            {
                Notification.Show(connected
                    ? "~p~StreamTok~s~: ~g~conectado~s~ a la app"
                    : "~p~StreamTok~s~: ~r~desconectado~s~, reintentando...");
            }

            ModCommand cmd;
            for (int i = 0; i < MaxCommandsPerTick && _client.Commands.TryDequeue(out cmd); i++)
            {
                string error = Run(cmd.Action, cmd.Params, cmd.NameTag, cmd.Notify);
                _client.Send(Protocol.BuildAck(cmd.Id, error));
                Log(error == null ? $"OK   {cmd.Action} ({cmd.Id})" : $"FAIL {cmd.Action} ({cmd.Id}): {error}");
            }

            _tracker.Update();
            _menu?.Draw();
        }

        /// <summary>
        /// Camino único de ejecución, para comandos de StreamTok y del menú de pruebas.
        /// Devuelve null si salió bien, o el motivo del error.
        /// </summary>
        private string Run(string actionId, IDictionary<string, object> rawParams, string nameTag, string notify)
        {
            ActionDef action = _registry.Find(actionId);
            if (action == null)
            {
                return $"Acción desconocida: '{actionId}'";
            }

            try
            {
                string tag = action.SupportsNameTag ? TextUtil.CleanTag(nameTag) : null;
                action.Execute(ActionContext.Create(action, rawParams, tag, _tracker, _rng));
            }
            catch (Exception ex)
            {
                Log($"Error en {actionId}: {ex}");
                return ex.Message;
            }

            string text = TextUtil.CleanText(notify);
            if (text != null)
            {
                Notification.Show(text);
            }
            return null;
        }

        private void RunFromMenu(ActionDef action, Dictionary<string, object> values)
        {
            string error = Run(action.Id, values, _testNameTag, null);
            Notification.Show(error == null
                ? $"~p~[Prueba]~s~ {action.Name} ~g~OK"
                : $"~p~[Prueba]~s~ {action.Name}: ~r~{TextUtil.CleanText(error)}");
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _client.Dispose();
            _tracker.RemoveAll();
            Log("Script detenido (Aborted).");
        }

        private T Setting<T>(string section, string key, T fallback)
        {
            try
            {
                return Settings != null ? Settings.GetValue(section, key, fallback) : fallback;
            }
            catch
            {
                return fallback;
            }
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
