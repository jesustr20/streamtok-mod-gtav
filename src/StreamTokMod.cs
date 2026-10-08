using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GTA;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Characters;
using StreamTok.GtaV.Debug;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;
using StreamTok.GtaV.Modes;
using StreamTok.GtaV.Webhook;
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
    ///   [Arena]
    ///   HealthTiers=1:20,10:25,100:30,500:40,1000:50   (desde X monedas : vida por moneda)
    ///   [Menu]
    ///   ShowParkour=false   (true = muestra el modo Parkour en el menú F7; aún sin terminar)
    ///   ShowRace=false      (true = muestra el modo Carrera en el menú F7; en desarrollo)
    ///   [Webhook]
    ///   Enabled=false       (true = acepta comandos por HTTP local, p. ej. desde TikFinity)
    ///   Port=7332
    ///   Token=              (opcional)
    ///   MaxRepeat=1000000
    ///   [Race]              (modo Carrera, en desarrollo; se muestra con [Menu] ShowRace=true)
    ///   BaseKmh=120  Laps=3  LobbySeconds=30  BoostPerStack=0.15  MaxStacks=8  BoostSeconds=6
    ///   GhostAlpha=255  CleanupSeconds=12  FinishGraceSeconds=30
    ///   AllowRecording=false   (true = herramientas del creador: grabar pistas y verlas)
    ///   [Debug]
    ///   MenuEnabled=false   (true = agrega al menú F7 la lista de acciones sueltas y la prueba de estrés)
    ///   TestNameTag=Viewer de prueba
    /// </summary>
    public sealed class StreamTokMod : Script
    {
        private const string DefaultUrl = "ws://localhost:7331";

        /// <summary>Máximo de comandos por frame, para repartir ráfagas y no congelar el juego.</summary>
        private const int MaxCommandsPerTick = 3;

        /// <summary>Máximo de comandos esperando: más que esto se descarta lo más viejo.</summary>
        private const int MaxQueuedCommands = 300;

        // Prueba de estrés
        private int _stressPending, _stressOk, _stressFail;
        private float _stressWorstFrame;
        private int _stressStarted;

        /// <summary>Versión del DLL (la pone el CI desde el tag v*).</summary>
        private static readonly string ModVersion = GetVersion();

        private readonly Util.FileLog _log;
        private readonly Dictionary<string, int> _nextModuleError = new Dictionary<string, int>();
        private readonly ActionRegistry _registry;
        private readonly ActionServices _services;
        private readonly StreamTokClient _client;
        private readonly WebhookServer _webhook;
        private readonly DebugMenu _menu;
        private readonly string _testNameTag;
        private bool _announced;

        public StreamTokMod()
        {
            _log = new Util.FileLog(BaseDirectory);
            Log($"Constructor ejecutado: SHVDN instanció StreamTokMod v{ModVersion}.");

            PlayerTransform.Log = Log;
            IReadOnlyList<CharacterDef> characters = CharacterCatalog.Load(BaseDirectory, Log);
            var rng = new Random();
            var tracker = new EntityTracker(Setting("Limits", "MaxSpawnedPeds", 100), Setting("Limits", "MaxSpawnedVehicles", 20));
            var scheduler = new FrameScheduler(Log);
            var characterManager = new CharacterManager(tracker, rng);
            var parkour = new ParkourMode(tracker, Log, BaseDirectory);
            var race = new RaceMode(tracker, rng, Log, BaseDirectory, new RaceSettings
            {
                BaseKmh = Setting("Race", "BaseKmh", 120f),
                Laps = Setting("Race", "Laps", 3),
                LobbySeconds = Setting("Race", "LobbySeconds", 30),
                BoostPerStack = Setting("Race", "BoostPerStack", 0.15f),
                MaxStacks = Setting("Race", "MaxStacks", 8),
                BoostSeconds = Setting("Race", "BoostSeconds", 6f),
                GhostAlpha = Setting("Race", "GhostAlpha", 255),
                CleanupSeconds = Setting("Race", "CleanupSeconds", 12),
                FinishGraceSeconds = Setting("Race", "FinishGraceSeconds", 30),
                Scenery = Setting("Race", "Scenery", false),
                CrowdMax = Setting("Race", "CrowdMax", 60),
                SceneryOffset = Setting("Race", "SceneryOffset", 7f),
                BarrierTurn = Setting("Race", "BarrierTurn", 0f),
                ArchTurn = Setting("Race", "ArchTurn", 0f),
                AutoCamera = Setting("Race", "AutoCamera", true),
                PlayerRaces = Setting("Race", "PlayerRaces", false),
                BarrierModel = Setting("Race", "BarrierModel", ""),
                RealDriving = Setting("Race", "RealDriving", true),
            });
            var arena = new ArenaMode(tracker, characterManager, characters, scheduler, rng, Log, BaseDirectory,
                Setting("Arena", "HealthTiers", ArenaMode.DefaultHealthTiers));
            _services = new ActionServices(
                tracker,
                new PlayerEffects(Log),
                scheduler,
                characterManager,
                new ChiliadMode(tracker, scheduler, rng, Log, BaseDirectory) { RespawnLikeGame = Setting("Chiliad", "RespawnLikeGame", true) },
                arena,
                parkour,
                race,
                rng);
            _registry = ActionRegistry.CreateDefault(characters, arena.CharacterIds, parkour.Courses, race.TrackNames, Setting("Race", "AllowRecording", false));
            _testNameTag = TextUtil.CleanTag(Setting("Debug", "TestNameTag", "Viewer de prueba"));

            string url = Setting("Connection", "Url", DefaultUrl);
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                Log($"Url inválida en el .ini ('{url}'); se usa {DefaultUrl}.");
                uri = new Uri(DefaultUrl);
            }

            _client = new StreamTokClient(uri, Protocol.BuildHello(ModVersion, _registry.All), Log);
            _client.Start();

            // Entrada opcional por webhook (TikFinity, Interactive...): apagada por defecto.
            // Comparte la cola del WebSocket: mismo límite por frame y mismo catálogo.
            if (Setting("Webhook", "Enabled", false))
            {
                var hook = new WebhookServer(Setting("Webhook", "Port", 7332), Setting("Webhook", "Token", ""),
                    Setting("Webhook", "MaxRepeat", 1000000), _registry, _client.Commands, Log);
                if (hook.Start()) _webhook = hook;
            }

            // El menú F7 SIEMPRE existe (el streamer enciende Chiliad / Pelea desde ahí).
            // MenuEnabled=true (apagado por defecto) agrega además la lista de acciones sueltas y la prueba de estrés;
            // ShowParkour=true agrega la sección Parkour (modo aún sin terminar).
            bool showTests = Setting("Debug", "MenuEnabled", false);
            bool showParkour = Setting("Menu", "ShowParkour", false);
            bool showRace = Setting("Menu", "ShowRace", false);
            _menu = new DebugMenu(_registry.All, RunFromMenu, _services.Chiliad, _services.Arena, _services.Parkour, _services.Race, showTests, showParkour, showRace, StartStressTest);

            Log($"Catálogo: {_registry.All.Count} acciones. Menú F7 activo (pruebas: {(showTests ? "sí" : "no; [Debug] MenuEnabled=true")}, Parkour: {(showParkour ? "sí" : "no; [Menu] ShowParkour=true")}). Log: {_log.FilePath}");

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

            if (_webhook != null)
            {
                while (_webhook.Notices.TryDequeue(out string notice))
                {
                    Notification.Show($"~p~StreamTok~s~ {TextUtil.CleanText(notice)}");
                }
            }

            // Protección: si llegan cientos de comandos de golpe, los más viejos se descartan (con aviso a la app).
            while (_client.Commands.Count > MaxQueuedCommands && _client.Commands.TryDequeue(out ModCommand dropped))
            {
                if (dropped.Id == null || !dropped.Id.StartsWith("hook-")) _client.Send(Protocol.BuildAck(dropped.Id, "Descartado: demasiados comandos en cola"));
                Log($"DROP {dropped.Action} ({dropped.Id}): cola llena");
            }

            ModCommand cmd;
            for (int i = 0; i < MaxCommandsPerTick && _client.Commands.TryDequeue(out cmd); i++)
            {
                string error = Run(cmd.Action, cmd.Params, cmd.NameTag, cmd.Notify);
                // Los comandos del webhook no tienen a quién responder: sin ack (no engordar la cola del WS).
                if (cmd.Id == null || !cmd.Id.StartsWith("hook-")) _client.Send(Protocol.BuildAck(cmd.Id, error));
                Log(error == null ? $"OK   {cmd.Action} ({cmd.Id})" : $"FAIL {cmd.Action} ({cmd.Id}): {error}");
                if (cmd.Id != null && cmd.Id.StartsWith("stress-")) CountStress(error == null);
            }
            if (_stressPending > 0) _stressWorstFrame = Math.Max(_stressWorstFrame, GTA.Game.LastFrameTime);

            // Cada módulo por separado: si uno falla, los demás siguen (y el error se anota sin inundar el log).
            Safe("Tareas", _services.Scheduler.Update);
            Safe("Nombres", _services.Tracker.Update);
            Safe("Personajes", _services.Characters.Update);
            Safe("Efectos", _services.Effects.Update);
            Safe("Chiliad", _services.Chiliad.Update);
            Safe("Pelea", _services.Arena.Update);
            Safe("Parkour", _services.Parkour.Update);
            Safe("Carrera", _services.Race.Update);
            if (_menu != null) Safe("Menú", _menu.Draw);
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
            _webhook?.Dispose();
            _services.Scheduler.Clear();
            _services.Characters.Clear();
            _services.Chiliad.ClearState(); // blip, radar y jugador descongelado
            _services.Arena.Clear();
            _services.Parkour.Stop(quiet: true);
            _services.Race.Clear();
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

        /// <summary>
        /// Prueba de estrés (menú F7): encola N acciones al azar del juego normal, como si llegaran
        /// donaciones seguidas, y al final informa cuántas salieron bien y el peor frame.
        /// </summary>
        private void StartStressTest(int count)
        {
            string[] skip = { "player_kill", "black_hole", "teleport", "player_skydive", "money", "remove_weapons" };
            List<ActionDef> pool = _registry.All.Where(x =>
                x.Category != ActionMeta.Chiliad && x.Category != ActionMeta.Arena && x.Category != ActionMeta.Parkour
                && !skip.Contains(x.Id) && !x.Id.EndsWith("_remove")).ToList();
            var rng = _services.Rng;

            _stressPending = count;
            _stressOk = _stressFail = 0;
            _stressWorstFrame = 0f;
            _stressStarted = GTA.Game.GameTime;
            for (int i = 0; i < count; i++)
            {
                ActionDef a = pool[rng.Next(pool.Count)];
                var values = new Dictionary<string, object>();
                foreach (ParamDef param in a.Params)
                {
                    values[param.Name] = param.Type == "enum" ? param.Options[rng.Next(param.Options.Length)] : param.Default;
                }
                _client.Commands.Enqueue(new ModCommand { Id = $"stress-{i + 1}", Action = a.Id, Params = values, NameTag = $"Estrés {i + 1}" });
            }
            Notification.Show($"~p~Prueba de estrés~s~: {count} acciones en cola");
            Log($"Prueba de estrés: {count} acciones en cola.");
        }

        private void CountStress(bool ok)
        {
            if (ok) _stressOk++; else _stressFail++;
            if (--_stressPending > 0) return;

            int secs = (GTA.Game.GameTime - _stressStarted) / 1000;
            int worstFps = _stressWorstFrame > 0f ? (int)(1f / _stressWorstFrame) : 0;
            string result = $"{_stressOk} OK · {_stressFail} con error · {secs} s · peor momento {worstFps} FPS";
            Notification.Show($"~p~Prueba de estrés~s~ terminada: {result}");
            Log($"Prueba de estrés terminada: {result}.");
        }

        /// <summary>Ejecuta un módulo del Tick; si lanza, lo anota como mucho una vez cada 10 s.</summary>
        private void Safe(string module, Action update)
        {
            try
            {
                update();
            }
            catch (Exception ex)
            {
                int now = GTA.Game.GameTime;
                if (!_nextModuleError.TryGetValue(module, out int next) || now >= next)
                {
                    _nextModuleError[module] = now + 10000;
                    Log($"Error en {module}: {ex}");
                }
            }
        }

        private void Log(string message)
        {
            try
            {
                _log.Write(message);
            }
            catch
            {
                // Nunca tumbar el juego por un fallo de log (también se llama desde el hilo del WS).
            }
        }
    }
}
