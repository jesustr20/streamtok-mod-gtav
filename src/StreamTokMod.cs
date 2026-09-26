using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Characters;
using StreamTok.GtaV.Debug;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;
using StreamTok.GtaV.Modes;
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
    ///   MaxSpawnedVehicles=20
    ///   [Arena]
    ///   HealthTiers=1:20,10:25,100:30,500:40,1000:50   (desde X monedas : vida por moneda)
    ///   [Debug]
    ///   MenuEnabled=true
    ///   TestNameTag=Viewer de prueba
    /// </summary>
    public sealed class StreamTokMod : Script
    {
        private const string DefaultUrl = "ws://localhost:7331";

        /// <summary>Máximo de comandos por frame, para repartir ráfagas y no congelar el juego.</summary>
        private const int MaxCommandsPerTick = 3;

        /// <summary>Versión del DLL (la pone el CI desde el tag v*).</summary>
        private static readonly string ModVersion = GetVersion();

        private readonly string _logPath;
        private readonly ActionRegistry _registry;
        private readonly ActionServices _services;
        private readonly StreamTokClient _client;
        private readonly DebugMenu _menu;
        private readonly string _testNameTag;
        private bool _announced;

        public StreamTokMod()
        {
            _logPath = Path.Combine(BaseDirectory, "StreamTok.GtaV.log");
            Log($"Constructor ejecutado: SHVDN instanció StreamTokMod v{ModVersion}.");

            PlayerTransform.Log = Log;
            IReadOnlyList<CharacterDef> characters = CharacterCatalog.Load(BaseDirectory, Log);
            var rng = new Random();
            var tracker = new EntityTracker(Setting("Limits", "MaxSpawnedPeds", 100), Setting("Limits", "MaxSpawnedVehicles", 20));
            var scheduler = new FrameScheduler(Log);
            var characterManager = new CharacterManager(tracker, rng);
            var arena = new ArenaMode(tracker, characterManager, characters, scheduler, rng, Log, BaseDirectory,
                Setting("Arena", "HealthTiers", ArenaMode.DefaultHealthTiers));
            _services = new ActionServices(
                tracker,
                new PlayerEffects(Log),
                scheduler,
                characterManager,
                new ChiliadMode(tracker, scheduler, rng, Log, BaseDirectory),
                arena,
                rng);
            _registry = ActionRegistry.CreateDefault(characters, arena.CharacterIds);
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
                _menu = new DebugMenu(_registry.All, RunFromMenu, _services.Chiliad, _services.Arena);
            }

            Log($"Catálogo: {_registry.All.Count} acciones. Menú de pruebas: {(_menu != null ? "F7" : "desactivado")}.");

            Interval = 0; // cada frame: nombres, efectos y menú se dibujan frame a frame
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
                // Recién cargado no hay efectos activos: el jugador SIEMPRE debe verse.
                // (Corrige restos de una sesión anterior; el estado queda guardado en el ped.)
                GTA.Game.Player.Character.IsVisible = true;
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

            _services.Scheduler.Update();
            _services.Tracker.Update();
            _services.Characters.Update();
            _services.Effects.Update();
            _services.Chiliad.Update();
            _services.Arena.Update();
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
                action.Execute(ActionContext.Create(action, rawParams, tag, _services));
            }
            catch (ActionException ex)
            {
                // Error esperado (sin vehículo, límite…): basta con el motivo.
                return ex.Message;
            }
            catch (Exception ex)
            {
                // Error inesperado: traza completa para depurar.
                Log($"Error inesperado en {actionId}: {ex}");
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
            Log(error == null ? $"OK   {action.Id} (menú F7)" : $"FAIL {action.Id} (menú F7): {error}");
            Notification.Show(error == null
                ? $"~p~[Prueba]~s~ {action.Name} ~g~OK"
                : $"~p~[Prueba]~s~ {action.Name}: ~r~{TextUtil.CleanText(error)}");
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _client.Dispose();
            _services.Scheduler.Clear();
            _services.Characters.Clear();
            _services.Chiliad.ClearState(); // blip, radar y jugador descongelado
            _services.Arena.Clear();
            _services.Effects.EndAll();
            PlayerTransform.RestoreNow(); // no habrá más frames: sin pasos

            // Con todos los efectos apagados, el jugador tiene que quedar visible.
            try { GTA.Game.Player.Character.IsVisible = true; } catch { /* cerrando el juego */ }
            _services.Tracker.RemoveAll();
            Log("Script detenido (Aborted).");
        }

        private static string GetVersion()
        {
            Version v = typeof(StreamTokMod).Assembly.GetName().Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
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
