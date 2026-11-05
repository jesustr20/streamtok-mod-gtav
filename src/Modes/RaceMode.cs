using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;
using StreamTok.GtaV.Race;
using Notification = GTA.UI.Notification;

namespace StreamTok.GtaV.Modes
{
    /// <summary>Ajustes de la carrera (ini: sección [Race]).</summary>
    internal sealed class RaceSettings
    {
        /// <summary>Velocidad de crucero de todos los pilotos, en km/h.</summary>
        public float BaseKmh = 120f;

        public int Laps = 3;

        /// <summary>Cada "turbo" activo suma este porcentaje de velocidad (0.15 = +15 %).</summary>
        public float BoostPerStack = 0.20f;

        public int MaxStacks = 6;

        /// <summary>Cuánto dura cada turbo.</summary>
        public float BoostSeconds = 7f;

        /// <summary>255 = autos normales; menos = translúcidos (se nota que se atraviesan).</summary>
        public int GhostAlpha = 255;

        /// <summary>Segundos que se queda el resultado en pantalla antes de borrar los autos.</summary>
        public int CleanupSeconds = 12;

        /// <summary>Después de que llega el primero, los demás tienen estos segundos para terminar.</summary>
        public int FinishGraceSeconds = 30;

        /// <summary>Arco de salida/meta, vallas y público en tramos de la pista.</summary>
        public bool Scenery = false;

        /// <summary>Máximo de espectadores en el mundo a la vez.</summary>
        public int CrowdMax = 60;

        /// <summary>Distancia (m) del centro de la pista a las vallas; el público va un poco más atrás.</summary>
        public float SceneryOffset = 7f;

        /// <summary>Giro (grados) de las vallas / del arco si salen atravesados (prueba 90).</summary>
        public float BarrierTurn = 0f;
        public float ArchTurn = 0f;

        /// <summary>Cámara de transmisión: oculta al personaje y sigue la carrera.</summary>
        public bool AutoCamera = true;

        /// <summary>El streamer corre también, con su propio auto (si no, solo mira).</summary>
        public bool PlayerRaces = false;

        /// <summary>Modelo de las vallas (vacío = automático). Prueba otros si no te gusta el que sale.</summary>
        public string BarrierModel = "";

        /// <summary>true = cada piloto es un auto real que maneja la IA del juego (más natural); false = autos "sobre rieles".</summary>
        public bool RealDriving = true;

        /// <summary>Derrape en las curvas: 0 = agarre normal, 1-3 = el auto pierde agarre y se desliza más.</summary>
        public int DriftLevel = 1;

        /// <summary>Autos de lado a lado en la parrilla (4 = ocupa todo el ancho de una pista de 2 carriles por lado).</summary>
        public int GridColumns = 4;

        /// <summary>Separación entre columnas de la parrilla, en metros (un carril ≈ 3,4 m).</summary>
        public float GridLaneWidth = 3.4f;

        /// <summary>Mitad del ancho de pista que usan los pilotos al correr (m). Se reparten en 4 carriles dentro de ese ancho.</summary>
        public float RoadHalfWidth = 4.5f;

        /// <summary>Los bots usan poderes al azar (misil, meteorito, escudo, jet, portal, turbo) para ver cómo funcionan.</summary>
        public bool BotPowers = true;

        /// <summary>true = desfile lento hacia la línea antes del 3-2-1; false = al dar "empezar" cuenta 3-2-1 en la parrilla y salen.</summary>
        public bool Parade = false;
    }

    /// <summary>
    /// Modo Carrera (fase 1): pista grabada por el streamer, lobby con inscripción, cuenta
    /// regresiva, vueltas, posiciones y turbo. Los pilotos son autos de los viewers (NO el del
    /// jugador) que el mod conduce solo, "sobre rieles": cada piloto lleva su propia distancia
    /// recorrida; el auto se empuja cada frame hacia ese punto de la pista (velocidad + corrección)
    /// y se orienta con la pista. Sin choques entre autos (se atraviesan, como en el original);
    /// con el mundo sí colisionan.
    /// </summary>
    internal sealed class RaceMode
    {
        public const string Kind = "race";

        private enum Phase { Idle, Recording, Lobby, Countdown, Racing, Finished }

        private sealed class Racer
        {
            public string Name;
            public Vehicle Veh;
            public bool IsBot;
            public float Dist;          // metros recorridos desde la salida (negativo = parrilla)
            public float Speed;         // m/s
            public float Lane;          // desplazamiento lateral base (m)
            public float WeavePhase;    // para el vaivén lateral
            public float Yaw;           // rumbo suavizado (grados)
            public List<int> BoostEnds = new List<int>();
            public bool Finished;
            public int FinishMs;
            public int Place;
            public int NextBotBoost;
            public float LapsDone;      // vueltas completas (para el HUD)
            public Ped Driver;          // conductor de la IA (modo RealDriving)
            public bool PreTasked;
            public int NextCollisionAt;
            public float WpDist;        // distancia de pista del destino actual de la IA
            public int WpSince;
            public int NextTaskAt;      // próxima vez que se le da un nuevo punto al conductor
            public int StuckSince;      // desde cuándo está detenido (ms), 0 = no
            public float LastCruise;
            public float Skill = 1f;    // ritmo propio del piloto (para que haya adelantamientos)
            public int SlowUntil;       // golpeado por misil/meteorito: va más lento hasta este momento
            public float SlowFactor = 1f;
            public int ShieldUntil;     // escudo: bloquea el próximo golpe hasta este momento
            public int BoostFx = -1;    // efecto de partículas del turbo (-1 = sin efecto)
            public bool BoostOn;        // empuje de motor del turbo aplicado
            public int NextGripAt;      // próxima vez que se revisa si hay curva
            public bool Drifting;       // agarre reducido (está girando)
            public int DriftSince, NextDriftAt;
            public float LaneOffset;    // carril elegido (m a la izquierda del centro de la pista)
            public int LaneChangeAt;    // cuándo vuelve a elegir carril
            public int FlipSince;       // desde cuándo está volcado / patas arriba (ms), 0 = no
        }

        private readonly EntityTracker _tracker;
        private readonly Random _rng;
        private readonly Action<string> _log;
        private readonly string _trackDir;
        private readonly string _legacyTrackFile;
        private readonly Dictionary<string, RaceTrack> _tracks = new Dictionary<string, RaceTrack>(StringComparer.OrdinalIgnoreCase);
        private readonly RaceSettings _cfg;
        private readonly List<Racer> _racers = new List<Racer>();
        private readonly List<Vector3> _recording = new List<Vector3>();
        private readonly RaceImporter _importer;
        private readonly RoadClearer _clearer;
        private RaceScenery _scenery;
        private RaceCamera _camera;
        private bool _playerHidden;
        private Vector3 _playerOrigin;
        private int _nextPlayerMoveAt;
        private const string PlayerRacerName = "Tú";
        private readonly GTA.UI.TextElement _text;
        private readonly GTA.UI.TextElement _big;

        private RaceTrack _track;        // la pista de la carrera en curso
        private string _trackName;
        private Phase _phase = Phase.Idle;
        private int _laps;
        private int _countdownEnd, _raceStart, _firstFinishAt, _finishedAt, _nextTagAt;
        private int _finishedCount, _botCounter;
        private int _lastCountdownShown;
        private bool _showTrack;
        private bool _steeringBroken;
        private List<string> _pool;
        private List<string> _racePool;
        private int _lastSlowLogAt;
        private int _fpsSamples, _fpsLogAt;
        private double _fpsSum;
        private float _fpsMin = 999f;
        private GTA.UI.TextElement _fpsText;

        public RaceMode(EntityTracker tracker, Random rng, Action<string> log, string baseDirectory, RaceSettings settings)
        {
            _tracker = tracker;
            _rng = rng;
            _log = log;
            _cfg = settings ?? new RaceSettings();
            _trackDir = Path.Combine(baseDirectory, "StreamTok.Race", "tracks");
            _legacyTrackFile = Path.Combine(baseDirectory, "StreamTok.Race", "track.json");

            _text = new GTA.UI.TextElement("", PointF.Empty, 0.36f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Left, true, true);
            _big = new GTA.UI.TextElement("", new PointF(640f, 250f), 2.2f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);

            LoadTracks();
            _clearer = new RoadClearer(tracker);
            _importer = new RaceImporter(baseDirectory, log, (name, track) => _tracks[name] = Aligned(name, track));
        }

        /// <summary>
        /// Carga todas las pistas de scripts\StreamTok.Race\tracks\*.json. El nombre de la pista es
        /// el nombre del archivo (renombrarlo cambia cómo se ve en la lista). Una pista suelta de
        /// una versión anterior (StreamTok.Race\track.json) se trae como "pista-1".
        /// </summary>
        private void LoadTracks()
        {
            try
            {
                if (Directory.Exists(_trackDir))
                {
                    foreach (string file in Directory.GetFiles(_trackDir, "*.json"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        RaceTrack t = RaceTrack.Load(file, out string error);
                        if (t != null)
                        {
                            t = Aligned(name, t);
                            _tracks[name] = t;
                            _log($"Carrera: pista '{name}' cargada ({t.Raw.Count} puntos, {t.Length:0} m).");
                        }
                        else
                        {
                            _log($"Carrera: no se pudo cargar la pista '{name}' ({error}).");
                        }
                    }
                }

                if (_tracks.Count == 0 && File.Exists(_legacyTrackFile))
                {
                    RaceTrack t = RaceTrack.Load(_legacyTrackFile, out string error);
                    if (t != null)
                    {
                        _tracks["pista-1"] = Aligned("pista-1", t);
                        t.Save(Path.Combine(_trackDir, "pista-1.json"));
                        _log("Carrera: pista anterior traída como 'pista-1'.");
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"Carrera: error leyendo las pistas: {ex.Message}");
            }
        }

        /// <summary>
        /// Pone la línea de salida de la pista en su tramo más recto (parrilla + desfile + arranque en recta,
        /// las curvas después). Solo en memoria: el archivo de la pista no cambia.
        /// </summary>
        private RaceTrack Aligned(string name, RaceTrack t)
        {
            try
            {
                RaceTrack a = t.WithStraightStart(FormationMeters + 40f, 200f, out float at, out float dev);
                if (a == null) return t;
                _log($"Carrera: pista '{name}': salida movida al tramo más recto (metro {at:0} de {t.Length:0}, desviación máxima {dev:0}°).");
                return a;
            }
            catch (Exception ex)
            {
                _log($"Carrera: no se pudo buscar el tramo recto de '{name}': {ex.Message}");
                return t;
            }
        }

        /// <summary>Pistas disponibles, ordenadas. Siempre hay al menos una entrada (para la lista de opciones).</summary>
        public string[] TrackNames
        {
            get
            {
                string[] names = _tracks.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
                return names.Length > 0 ? names : new[] { NoTrack };
            }
        }

        public const string NoTrack = "ninguna";

        // ============================================================ estado público

        /// <summary>Hay una carrera en curso (lobby, cuenta, carrera o resultados).</summary>
        public bool IsActive => _phase == Phase.Lobby || _phase == Phase.Countdown || _phase == Phase.Racing || _phase == Phase.Finished;

        public bool IsRecording => _phase == Phase.Recording;
        public bool IsImporting => _importer.IsRunning;

        /// <summary>Herramienta del creador: la IA maneja los puntos de scripts\StreamTok.Race\import y se guardan como pistas.</summary>
        public void ImportStart()
        {
            if (_phase != Phase.Idle) throw new ActionException("Termina la carrera o la grabación antes de importar");
            _importer.Start();
        }

        public void ImportStop() => _importer.Cancel();
        public bool HasTrack => _tracks.Values.Any(t => t.IsReady);
        public bool ShowTrackOn => _showTrack;
        public bool InLobby => _phase == Phase.Lobby;
        public int RacerCount => _racers.Count;

        // ============================================================ pista

        public void RecordStart()
        {
            if (_phase != Phase.Idle)
            {
                throw new ActionException(_phase == Phase.Recording ? "Ya se está grabando la pista" : "Termina la carrera antes de grabar una pista");
            }

            _phase = Phase.Recording;
            _recording.Clear();
            _recording.Add(PlayerPoint());
            Notification.Show("~p~Carrera~s~: grabando pista. Maneja una vuelta completa y vuelve a la salida.");
        }

        public void RecordStop()
        {
            if (_phase != Phase.Recording)
            {
                throw new ActionException("No se está grabando ninguna pista");
            }

            _phase = Phase.Idle;
            var track = new RaceTrack();
            if (!track.Build(_recording, out string error))
            {
                throw new ActionException(error);
            }

            int n = 1;
            while (_tracks.ContainsKey($"pista-{n}")) n++;
            string name = $"pista-{n}";
            _tracks[name] = track;
            try
            {
                track.Save(Path.Combine(_trackDir, name + ".json"));
            }
            catch (Exception ex)
            {
                _log($"Carrera: no se pudo guardar la pista: {ex.Message}");
            }

            float gap = _recording[0].DistanceTo(_recording[_recording.Count - 1]);
            string warn = gap > 80f ? $" ~y~(el final queda a {gap:0} m de la salida: se unirá en línea recta)~s~" : "";
            Notification.Show($"~p~Carrera~s~: pista '{name}' guardada ({track.Length:0} m).{warn} Recarga los scripts (Insert) para verla en la lista.");
            _log($"Carrera: pista grabada, {_recording.Count} puntos, {track.Length:0} m, cierre a {gap:0} m.");
        }

        public void SetShowTrack(bool on) => _showTrack = on;

        // ============================================================ carrera

        public void Open(int laps, string trackName)
        {
            RequireTrack();
            if (_phase != Phase.Idle)
            {
                throw new ActionException(_phase == Phase.Recording ? "Termina de grabar la pista primero" : "Ya hay una carrera en curso");
            }

            // Pista pedida; si no existe (o no se pidió una) se usa la primera disponible.
            if (trackName == null || !_tracks.TryGetValue(trackName, out RaceTrack chosen))
            {
                string first = TrackNames[0];
                chosen = _tracks[first];
                trackName = first;
            }
            _track = chosen;
            _trackName = trackName;

            _laps = Math.Max(1, laps);
            _phase = Phase.Lobby;
            _botCounter = 0;
            Notification.Show($"~p~Carrera abierta~s~: {_laps} vueltas. ¡Inscríbanse! Empieza cuando el streamer lo diga.");
            _log($"Carrera: lobby abierto en '{trackName}', {_laps} vueltas (empieza con race_start).");

            if (_cfg.AutoCamera) BeginBroadcast();
            if (_cfg.PlayerRaces) AddRacer(PlayerRacerName, false);
        }

        // ------------------------------------------------------------ transmisión (cámara + streamer oculto)

        /// <summary>Cámara automática ON/OFF; en plena carrera se aplica de inmediato.</summary>
        public void SetCamera(bool on)
        {
            _cfg.AutoCamera = on;
            if (!IsActive) return;
            if (on) BeginBroadcast(); else EndBroadcast();
        }

        /// <summary>El streamer corre con su auto. Con el lobby abierto entra/sale ya; si no, vale para la próxima carrera.</summary>
        public void SetPlayerRaces(bool on)
        {
            _cfg.PlayerRaces = on;
            if (_phase != Phase.Lobby) return;

            Racer me = Find(PlayerRacerName);
            if (on && me == null)
            {
                AddRacer(PlayerRacerName, false);
            }
            else if (!on && me != null)
            {
                EntityTracker.SafeDelete(me.Veh);
                _racers.Remove(me);
            }
        }

        public bool AutoCameraOn => _cfg.AutoCamera;
        public bool PlayerRacesOn => _cfg.PlayerRaces;

        private void BeginBroadcast()
        {
            if (_camera == null) _camera = new RaceCamera(_rng, _log);
            if (_playerHidden) return;

            Ped player = GTA.Game.Player.Character;
            _playerOrigin = player.Position;
            if (player.IsInVehicle()) player.Task.ClearAllImmediately();
            player.IsVisible = false;
            player.IsInvincible = true;
            Function.Call(Hash.SET_ENTITY_COLLISION, player.Handle, false, false);
            Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, GTA.Game.Player.Handle, true);
            player.IsPositionFrozen = true;
            Function.Call(Hash.DISPLAY_RADAR, false);
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0); // sin policía mientras dure la carrera
            GTA.Game.Player.WantedLevel = 0;
            _playerHidden = true;
            _tracker.TagsFromCamera = true;
            _nextPlayerMoveAt = 0;
        }

        private void EndBroadcast()
        {
            _camera?.Stop();
            _camera = null;
            if (!_playerHidden) return;

            Ped player = GTA.Game.Player.Character;
            _tracker.TagsFromCamera = false;
            Function.Call(Hash.CLEAR_FOCUS);
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, _playerOrigin.X, _playerOrigin.Y, _playerOrigin.Z);
            player.Position = _playerOrigin;
            player.IsPositionFrozen = false;
            Function.Call(Hash.SET_ENTITY_COLLISION, player.Handle, true, true);
            Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, GTA.Game.Player.Handle, false);
            Function.Call(Hash.DISPLAY_RADAR, true);
            Function.Call(Hash.SET_MAX_WANTED_LEVEL, 5);
            player.IsInvincible = false;
            player.IsVisible = true;
            _playerHidden = false;
        }

        private void UpdateBroadcast()
        {
            if (_camera == null) return;

            var ranked = new List<KeyValuePair<Vehicle, float>>();
            foreach (Racer r in _racers.OrderByDescending(x => x.Dist))
            {
                if (r.Veh != null && r.Veh.Exists()) ranked.Add(new KeyValuePair<Vehicle, float>(r.Veh, r.Dist));
            }
            _camera.Update(ranked, _track);

            // El personaje (invisible) se queda junto al líder para que el mapa de ahí esté cargado.
            // Cada frame (no cada 0,5 s): si el "foco" del juego se queda atrás, los autos lejanos pasan a modo
            // simplificado (dummy / timeslicing) y se ven a pocos FPS aunque el juego vaya a 60.
            if (_playerHidden)
            {
                Vector3 spot = ranked.Count > 0 ? ranked[0].Key.Position : _track.PositionAt(0f);
                Ped pl = GTA.Game.Player.Character;
                pl.Position = spot + new Vector3(0f, 0f, 3f);
                Function.Call(Hash.SET_FOCUS_POS_AND_VEL, spot.X, spot.Y, spot.Z, 0f, 0f, 0f);
            }

            // Que ningún auto de la carrera caiga en modo simplificado.
            foreach (var kv in ranked)
            {
                Vehicle v = kv.Key;
                Function.Call(Hash.SET_DISABLE_SUPERDUMMY, v.Handle, true);
                Function.Call(Hash.SET_VEHICLE_LOD_MULTIPLIER, v.Handle, 4f);
                Function.Call(Hash.SET_ENTITY_LOD_DIST, v.Handle, 1500);
            }
        }

        /// <summary>Cierra las inscripciones y empieza la cuenta regresiva (la carrera nunca empieza sola).</summary>
        public void StartNow()
        {
            if (_phase != Phase.Lobby)
            {
                throw new ActionException("No hay un lobby abierto");
            }
            if (_racers.Count == 0)
            {
                throw new ActionException("No hay pilotos inscritos");
            }
            BeginCountdown();
        }

        public void Stop()
        {
            if (_importer.IsRunning)
            {
                _importer.Cancel();
                return;
            }

            if (_phase == Phase.Recording)
            {
                _phase = Phase.Idle;
                _recording.Clear();
                Notification.Show("~p~Carrera~s~: grabación cancelada.");
                return;
            }

            bool was = IsActive;
            Cleanup();
            if (was) Notification.Show("~p~Carrera~s~ terminada.");
        }

        public void Join(string name)
        {
            if (_phase == Phase.Idle || _phase == Phase.Recording)
            {
                throw new ActionException("No hay carrera abierta");
            }
            if (_phase != Phase.Lobby)
            {
                throw new ActionException("Inscripciones cerradas: la carrera ya empezó");
            }

            name = string.IsNullOrWhiteSpace(name) ? $"Piloto {_racers.Count + 1}" : name;
            if (Find(name) != null)
            {
                throw new ActionException("Ya estás inscrito");
            }
            AddRacer(name, false);
        }

        /// <summary>Turbo: suma "stacks" de velocidad por unos segundos (hasta el tope), con empujón y efecto visible.</summary>
        public void Boost(string name, int stacks)
        {
            Racer r = RequireRacer(name);
            if (r == null) return;
            stacks = Math.Max(1, stacks);
            AddBoost(r, stacks);
            Kick(r, 2.5f * Math.Min(stacks, _cfg.MaxStacks));
            Notification.Show($"~o~TURBO x{r.BoostEnds.Count}~s~ {Safe(r.Name)}");
        }

        /// <summary>Acelerar (la rosa): un empujón de velocidad. Para entrar a la carrera está race_join.</summary>
        public void Accelerate(string name)
        {
            Racer r = RequireRacer(name);
            if (r == null) return;
            AddBoost(r, 1);
            Kick(r, 3f);
            Notification.Show($"~y~ACELERA~s~ {Safe(r.Name)} x{r.BoostEnds.Count}");
        }

        /// <summary>El piloto en carrera, o error si la carrera no está en marcha / no está inscrito. null si ya llegó.</summary>
        private Racer RequireRacer(string name)
        {
            if (_phase != Phase.Racing)
            {
                throw new ActionException("La carrera no está en marcha");
            }

            Racer r = Find(name);
            if (r == null)
            {
                throw new ActionException("Primero tienes que inscribirte");
            }
            return r.Finished ? null : r;
        }

        /// <summary>Empujón inmediato de velocidad (m/s) para que el turbo se note al instante.</summary>
        private static void Kick(Racer r, float extra)
        {
            Vehicle v = r.Veh;
            if (v == null || !v.Exists()) return;
            Vector3 fwd = v.ForwardVector;
            v.Velocity = v.Velocity + fwd * extra;
        }

        // ------------------------------------------------------------ ataques y poderes

        /// <summary>Misil: golpea al piloto que va justo delante del viewer.</summary>
        public void Missile(string name, int seconds)
        {
            Racer me = RequireRacer(name);
            if (me == null) return;
            List<Racer> order = Ranking();
            int idx = order.IndexOf(me);
            Racer victim = idx > 0 && !order[idx - 1].Finished ? order[idx - 1] : null;
            if (victim == null)
            {
                throw new ActionException("Vas primero: no hay nadie adelante a quien dispararle");
            }
            Hit(victim, me, "un misil", 0.25f, Math.Max(1, seconds) * 1000);
        }

        /// <summary>Meteorito: cae sobre el líder de la carrera (ayuda a los de atrás).</summary>
        public void Meteor(string name, int seconds)
        {
            Racer me = RequireRacer(name);
            if (me == null) return;
            Racer lead = Ranking().FirstOrDefault(x => !x.Finished);
            if (lead == null || ReferenceEquals(lead, me))
            {
                throw new ActionException("Vas primero: el meteorito es para el líder");
            }
            Hit(lead, me, "un meteorito", 0.2f, Math.Max(1, seconds) * 1000);
        }

        /// <summary>Escudo: bloquea el próximo golpe (misil o meteorito) durante 20 s.</summary>
        public void Shield(string name)
        {
            Racer me = RequireRacer(name);
            if (me == null) return;
            me.ShieldUntil = GTA.Game.GameTime + 20000;
            Notification.Show($"~b~ESCUDO~s~ {Safe(me.Name)} (20 s)");
        }

        /// <summary>Jet / portal: salta adelante un porcentaje de la pista.</summary>
        public void JumpAhead(string name, int percent, string label)
        {
            Racer me = RequireRacer(name);
            if (me == null) return;
            float total = _laps * _track.Length;
            float target = Math.Min(me.Dist + _track.Length * Math.Max(1, percent) / 100f, total - 15f);
            if (target <= me.Dist + 5f)
            {
                throw new ActionException("Ya estás casi en la meta");
            }
            TeleportTo(me, target);
            Notification.Show($"~g~{label}~s~ {Safe(me.Name)} +{percent}%");
        }

        /// <summary>WIN: el piloto aparece pegado a la meta con turbo y gana casi al instante.</summary>
        public void Win(string name)
        {
            Racer me = RequireRacer(name);
            if (me == null) return;
            float total = _laps * _track.Length;
            TeleportTo(me, Math.Max(me.Dist, total - 40f));
            AddBoost(me, _cfg.MaxStacks);
            Notification.Show($"~y~WIN~s~ {Safe(me.Name)} va directo a la meta");
        }

        private void Hit(Racer victim, Racer by, string what, float factor, int ms)
        {
            int now = GTA.Game.GameTime;
            Vehicle v = victim.Veh;
            if (v == null || !v.Exists()) return;

            if (now < victim.ShieldUntil)
            {
                victim.ShieldUntil = 0;
                Notification.Show($"~b~ESCUDO~s~ {Safe(victim.Name)} bloqueó {what} de {Safe(by.Name)}");
                return;
            }

            victim.SlowFactor = factor;
            victim.SlowUntil = now + ms;
            victim.BoostEnds.Clear();
            v.Velocity = v.Velocity * factor;
            Vector3 p = v.Position;
            // Explosión visual y sonora; los autos de la carrera son invencibles y la explosión no hace daño.
            Function.Call(Hash.ADD_EXPLOSION, p.X, p.Y, p.Z, 4, 0f, true, false, 0.4f, true);
            Notification.Show($"~r~BOOM~s~ {Safe(by.Name)} le dio {what} a {Safe(victim.Name)}");
        }

        private void TeleportTo(Racer r, float dist)
        {
            Vehicle v = r.Veh;
            float keep = Math.Max(12f, v.Speed);
            Vector3 tan = _track.TangentAt(dist);
            Vector3 left = new Vector3(-tan.Y, tan.X, 0f);
            if (left.Length() > 0.001f) left.Normalize();
            v.Position = _track.PositionAt(dist) + left * (r.Lane * 0.5f); // más cerca del centro: la pista puede ser angosta
            float heading = (float)(Math.Atan2(-tan.X, tan.Y) * 180.0 / Math.PI);
            v.Rotation = new Vector3(0f, 0f, heading);
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v.Handle, 5f);
            v.Velocity = tan * keep;
            r.Dist = dist;
            r.Yaw = heading;
            r.WpDist = 0f;
            r.NextTaskAt = 0;
        }

        public void AddBots(int count)
        {
            if (_phase != Phase.Lobby)
            {
                throw new ActionException("Los bots se agregan con el lobby abierto");
            }
            for (int i = 0; i < count; i++)
            {
                AddRacer($"Bot {++_botCounter}", true);
            }
        }

        // ============================================================ cada frame

        public void Update()
        {
            int now = GTA.Game.GameTime;

            if (_importer.IsRunning)
            {
                _importer.Update();
                if (_importer.IsRunning)
                {
                    // Calles vacías también al importar: la IA elige mejor camino sin tráfico.
                    if (_importer.Car != null) _clearer.Update(null, _importer.Car.Position, _importer.Car, _importer.Driver);
                    DrawLines(_importer.StatusLines);
                }
                return;
            }

            // Carrera en curso: la pista queda libre de tráfico y peatones (solo corren los pilotos).
            if (_track != null && IsActive)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _clearer.Update(_track, GTA.Game.Player.Character.Position);
                long tClear = sw.ElapsedMilliseconds;
                UpdateBroadcast();
                long tCam = sw.ElapsedMilliseconds - tClear;

                if (_cfg.Scenery)
                {
                    Racer lead = null;
                    foreach (Racer r in _racers)
                    {
                        if (r.Veh != null && r.Veh.Exists() && (lead == null || r.Dist > lead.Dist)) lead = r;
                    }
                    if (_scenery == null) _scenery = new RaceScenery(_tracker, _rng, _log, _cfg, _track);
                    long t0 = sw.ElapsedMilliseconds;
                    _scenery.Update(GTA.Game.Player.Character.Position, lead != null ? lead.Veh.Position : (Vector3?)null);
                    long tScn = sw.ElapsedMilliseconds - t0;
                    if (sw.ElapsedMilliseconds > 30 && now - _lastSlowLogAt > 1000)
                    {
                        _lastSlowLogAt = now;
                        _log($"Carrera: frame lento {sw.ElapsedMilliseconds} ms (limpieza {tClear}, cámara {tCam}, ambiente {tScn}).");
                    }
                }
            }

            if (IsActive) MeasureFps(now);

            if (_showTrack)
            {
                if (IsActive && _track != null)
                {
                    _track.DrawNear(GTA.Game.Player.Character.Position, 250f);
                }
                else
                {
                    foreach (RaceTrack t in _tracks.Values)
                    {
                        t.DrawNear(GTA.Game.Player.Character.Position, 250f);
                    }
                }
            }

            switch (_phase)
            {
                case Phase.Recording:
                    UpdateRecording();
                    break;

                case Phase.Lobby:
                    // Espera hasta que el streamer da race_start: no empieza ni cuenta solo.
                    HoldGrid();
                    DrawHud(now);
                    break;

                case Phase.Countdown:
                    UpdateCountdown(now);
                    if (_phase == Phase.Countdown && _formation) UpdateRacing(now);
                    HoldGrid();
                    DrawHud(now);
                    break;

                case Phase.Racing:
                    UpdateRacing(now);
                    UpdateBotPowers(now);
                    DrawHud(now);
                    if (now < _goUntil)
                    {
                        _big.Caption = "¡YA!";
                        _big.Color = Color.LimeGreen;
                        _big.Draw();
                    }
                    break;

                case Phase.Finished:
                    UpdateRacing(now);
                    DrawHud(now);
                    if (now - _finishedAt >= _cfg.CleanupSeconds * 1000)
                    {
                        ResetToLobby(); // no se cierra el modo: vuelve a la parrilla y espera inscripciones
                    }
                    break;
            }
        }

        /// <summary>FPS real del juego durante la carrera: se ve arriba a la derecha y el promedio/mínimo se anota en el log cada 10 s.</summary>
        private void MeasureFps(int now)
        {
            float fps = GTA.Game.FPS;
            _fpsSum += fps;
            _fpsSamples++;
            if (fps < _fpsMin) _fpsMin = fps;

            if (_fpsText == null)
            {
                _fpsText = new GTA.UI.TextElement("", new PointF(1180f, 8f), 0.4f, Color.Yellow, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Right, true, true);
            }
            _fpsText.Caption = $"{fps:0} FPS";
            _fpsText.Draw();

            if (now - _fpsLogAt >= 10000)
            {
                if (_fpsLogAt != 0 && _fpsSamples > 0)
                {
                    _log($"Carrera: FPS medio {_fpsSum / _fpsSamples:0}, mínimo {_fpsMin:0} (pilotos {_racers.Count}).");
                }
                _fpsLogAt = now;
                _fpsSum = 0;
                _fpsSamples = 0;
                _fpsMin = 999f;
            }
        }

        // ------------------------------------------------------------ grabación

        private void UpdateRecording()
        {
            Vector3 p = PlayerPoint();
            if (p.DistanceTo(_recording[_recording.Count - 1]) >= 7f)
            {
                _recording.Add(p);
            }

            float len = 0f;
            for (int i = 1; i < _recording.Count; i++) len += _recording[i].DistanceTo(_recording[i - 1]);
            DrawLines(new[] { "GRABANDO PISTA", $"{_recording.Count} puntos · {len:0} m", "Vuelve a la salida y termina la grabación (F7)" });
        }

        private static Vector3 PlayerPoint()
        {
            Ped player = GTA.Game.Player.Character;
            return player.IsInVehicle() && player.CurrentVehicle != null ? player.CurrentVehicle.Position : player.Position;
        }

        // ------------------------------------------------------------ cuenta regresiva

        private void BeginCountdown()
        {
            _phase = Phase.Countdown;
            int tnow = GTA.Game.GameTime;
            _lastCountdownShown = 99;

            // Con conductores de la IA: desfile. Los autos ya están en fila sobre la pista, 200 m antes de la
            // línea; salen todos a paso lento y en orden, y el 3-2-1 se cuenta al acercarse el líder a la línea.
            // Sin conductores (rieles): cuenta clásica con los autos quietos.
            _formation = _cfg.RealDriving && _cfg.Parade;
            _countdownEnd = tnow + (_formation ? 60000 : 4000);

            if (_formation)
            {
                foreach (Racer r in _racers)
                {
                    if (r.Driver == null || !r.Driver.Exists() || r.Veh == null || !r.Veh.Exists()) continue;
                    WakeCar(r.Veh);
                    r.WpDist = r.Dist + 140f;
                    r.WpSince = tnow + 3000;
                    r.NextTaskAt = tnow + 1500;
                    Vector3 t = _track.PositionAt(r.WpDist);
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, r.Driver.Handle, r.Veh.Handle, t.X, t.Y, t.Z, RollPace, 2883621, 8f);
                    r.PreTasked = true;
                }
            }
            Notification.Show($"~p~Carrera~s~: ¡empieza con {_racers.Count} pilotos!");
        }

        private void UpdateCountdown(int now)
        {
            if (_formation)
            {
                float lead = float.MinValue;
                foreach (Racer r in _racers) lead = Math.Max(lead, r.Dist);
                float toLine = -lead;                       // metros que le faltan al líder para la línea (≈17 m/s)
                if (toLine <= 0f || now >= _countdownEnd)
                {
                    StartRacing(now);
                    return;
                }
                int num = toLine > 51f ? 0 : toLine > 34f ? 3 : toLine > 17f ? 2 : 1;
                if (num > 0)
                {
                    _big.Caption = num.ToString();
                    _big.Color = Color.White;
                    _big.Draw();
                }
                return;
            }

            int left = _countdownEnd - now;
            int shown = left > 3000 ? 3 : left > 2000 ? 2 : left > 1000 ? 1 : 0;

            if (left <= 0)
            {
                StartRacing(now);
                return;
            }

            _big.Caption = shown > 0 ? shown.ToString() : "¡YA!";
            _big.Color = shown > 0 ? Color.White : Color.LimeGreen;
            _big.Draw();
        }

        private bool _formation;
        private int _goUntil;

        private int _diagAt;
        private const float FormationMeters = 200f; // la parrilla está tantos metros antes de la línea
        private const float RollPace = 17f;      // m/s (~60 km/h)

        private void StartRacing(int now)
        {
            _phase = Phase.Racing;
            _goUntil = now + 1500;
            _nextBotPowerAt = now + 6000;
            _raceStart = now;
            _firstFinishAt = 0;
            _finishedCount = 0;
            _nextTagAt = 0;
            foreach (Racer r in _racers)
            {
                if (r.Veh != null && r.Veh.Exists()) WakeCar(r.Veh);
                if (!r.PreTasked)
                {
                    r.WpDist = 0f;
                    r.NextTaskAt = 0;
                }
                r.Speed = 0f;
                r.NextBotBoost = now + 3000 + _rng.Next(0, 6000);
                r.LaneChangeAt = now + 2000 + _rng.Next(0, 4000);
            }
            _log($"Carrera: salida con {_racers.Count} pilotos.");
        }

        /// <summary>Descongela el auto y despierta su física (un auto congelado en la parrilla puede quedar "dormido").</summary>
        private static void WakeCar(Vehicle v)
        {
            Function.Call(Hash.FREEZE_ENTITY_POSITION, v.Handle, false);
            Function.Call(Hash.SET_ENTITY_DYNAMIC, v.Handle, true);
            Function.Call(Hash.ACTIVATE_PHYSICS, v.Handle);
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, v.Handle, false);
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v.Handle, true, true, false);
        }

        /// <summary>En lobby/cuenta, los autos esperan quietos en su lugar de la parrilla.</summary>
        private void HoldGrid()
        {
            // La cuenta regresiva puede pasar a Racing en este mismo frame: ahí ya no se congela nada.
            if (_phase != Phase.Lobby && !(_phase == Phase.Countdown && !_formation)) return;
            foreach (Racer r in _racers)
            {
                if (r.Veh != null && r.Veh.Exists() && !r.Veh.IsPositionFrozen) r.Veh.IsPositionFrozen = true;
            }
        }

        // ------------------------------------------------------------ conducción

        private void UpdateRacing(int now)
        {
            float dt = Math.Min(0.1f, Math.Max(0.001f, GTA.Game.LastFrameTime));
            float baseMs = _cfg.BaseKmh / 3.6f;
            float total = _laps * _track.Length;
            float leadDist = 0f;
            foreach (Racer q in _racers)
            {
                if (q.Dist > leadDist) leadDist = q.Dist;
            }

            for (int i = _racers.Count - 1; i >= 0; i--)
            {
                Racer r = _racers[i];
                if (r.Veh == null || !r.Veh.Exists())
                {
                    _racers.RemoveAt(i); // el juego lo borró: queda fuera
                    continue;
                }

                if (_phase == Phase.Racing)
                {
                    if (r.IsBot && !r.Finished && now >= r.NextBotBoost)
                    {
                        AddBoost(r, 1);
                        r.NextBotBoost = now + 4000 + _rng.Next(0, 7000);
                    }

                    r.BoostEnds.RemoveAll(end => end <= now);
                }

                ApplyBoostPhysics(r, now);

                float target;
                if (r.Finished || _phase == Phase.Finished)
                {
                    target = 0f; // llegó: frena suave
                }
                else
                {
                    // Ritmo propio (con una oscilación lenta) y freno en las curvas cerradas.
                    float form = r.Skill * (1f + 0.035f * (float)Math.Sin(now / 5500.0 + r.WeavePhase));
                    target = baseMs * (1f + _cfg.BoostPerStack * r.BoostEnds.Count) * form * CurveSpeed(r.Dist); // frena antes de las curvas cerradas (hasta ~60 % del ritmo) y acelera al salir
                    if (now < r.SlowUntil) target *= r.SlowFactor; // golpeado por un misil o meteorito
                }

                if (r.Driver != null && r.Driver.Exists())
                {
                    // Auto real: la IA lo maneja; aquí solo se le dice a qué velocidad y hacia dónde.
                    DriveAi(r, now, target, leadDist);
                    if (_phase == Phase.Racing && !r.Finished)
                    {
                        ApplyLane(r, dt, now);
                        ApplyDrift(r, now);
                    }
                }
                else
                {
                    float rate = target > r.Speed ? 11f : 16f;
                    r.Speed += Math.Max(-rate * dt, Math.Min(rate * dt, target - r.Speed));
                    r.Dist += r.Speed * dt;

                    Drive(r, dt, now);
                }

                r.LapsDone = Math.Max(0f, (float)Math.Floor(r.Dist / _track.Length));

                if (!r.Finished && r.Dist >= total)
                {
                    r.Finished = true;
                    r.FinishMs = now - _raceStart;
                    r.Place = ++_finishedCount;
                    if (_firstFinishAt == 0) _firstFinishAt = now;
                    Notification.Show($"~y~{r.Place}°~s~ {Safe(r.Name)} · {FormatTime(r.FinishMs)}");
                }
            }

            AvoidCollisions();

            if (_phase == Phase.Racing)
            {
                bool allDone = _racers.Count > 0 && _racers.All(x => x.Finished);
                bool graceOver = _firstFinishAt != 0 && now - _firstFinishAt >= _cfg.FinishGraceSeconds * 1000;
                bool tooLong = now - _raceStart >= 20 * 60 * 1000;
                if (_racers.Count == 0 || allDone || graceOver || tooLong)
                {
                    EndRace(now);
                }
                else if (now >= _nextTagAt)
                {
                    _nextTagAt = now + 500;
                    List<Racer> order = Ranking();
                    for (int p = 0; p < order.Count; p++)
                    {
                        Racer o = order[p];
                        string mark = now < o.ShieldUntil ? " [ESCUDO]" : now < o.SlowUntil ? " [GOLPEADO]" : o.BoostEnds.Count > 0 ? $" [TURBO x{o.BoostEnds.Count}]" : "";
                        _tracker.Retag(o.Veh, $"{p + 1}° {o.Name}{mark}");
                    }
                }
            }
        }

        /// <summary>
        /// Auto real conducido por la IA del juego. Cada ~0.35 s se le da un punto de la pista unos metros más
        /// adelante y una velocidad de crucero; la posición en la carrera sale de dónde está el auto de verdad.
        /// Si se atasca o se sale de la pista, se devuelve a ella.
        /// </summary>
        private void DriveAi(Racer r, int now, float cruise, float leadDist)
        {
            Vehicle v = r.Veh;
            Ped d = r.Driver;

            r.Dist = _track.Project(v.Position, r.Dist, out float lateral);
            r.Speed = v.Speed;

            // El mapa bajo cada auto tiene que estar cargado o el juego lo deja quieto.
            if (now >= r.NextCollisionAt)
            {
                r.NextCollisionAt = now + 250;
                Vector3 vp = v.Position;
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, vp.X, vp.Y, vp.Z);
            }

            if (_racers.Count > 0 && ReferenceEquals(r, _racers[0]) && now >= _diagAt)
            {
                _diagAt = now + 2000;
                _log($"Diag auto {r.Name}: vel {r.Speed:0.0} m/s, congelado {v.IsPositionFrozen}, motor {v.IsEngineRunning}, " +
                     $"esperaMapa {Function.Call<bool>(Hash.IS_ENTITY_WAITING_FOR_WORLD_COLLISION, v.Handle)}, " +
                     $"mapaCargado {Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, v.Handle)}, " +
                     $"conductorDentro {(d != null && d.IsInVehicle(v))}, lateral {lateral:0.0} m, dist {r.Dist:0}, " +
                     $"vivo {v.IsAlive}, ruedas {v.IsOnAllWheels}, salud {v.EngineHealth:0}.");
            }

            bool stop = r.Finished || _phase == Phase.Finished;

            // Los que van atrás aprietan un poco para no quedar aislados (hasta +12 %).
            if (!stop)
            {
                cruise *= 1f + Math.Max(0f, Math.Min(0.12f, (leadDist - r.Dist) / 500f));
            }

            // Desfile: todos al mismo paso, en su orden de parrilla y sin pasarse, hasta que suena el ¡YA!
            if (!stop && _phase == Phase.Countdown)
            {
                cruise = Math.Min(cruise, RollPace);
                foreach (Racer q in _racers)
                {
                    if (ReferenceEquals(q, r) || q.Veh == null || Math.Sign(q.Lane) != Math.Sign(r.Lane)) continue;
                    float gap = q.Dist - r.Dist;
                    if (gap > 0f && gap < 25f)
                    {
                        // Más cerca de 9 m del de adelante: levanta; más lejos: lo alcanza sin pasarlo.
                        cruise = Math.Min(cruise, Math.Max(3f, q.Speed + (gap - 9f) * 0.8f));
                    }
                }
            }

            if (Math.Abs(cruise - r.LastCruise) > 0.3f)
            {
                r.LastCruise = cruise;
                Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, d.Handle, cruise);
            }

            // Atascado (casi parado en plena carrera) o fuera de la pista: de vuelta al lugar que le toca.
            bool racing = _phase == Phase.Racing && !r.Finished && now - _raceStart > 4000;
            if (racing && r.Speed < 1.5f)
            {
                if (r.StuckSince == 0) r.StuckSince = now;
            }
            else
            {
                r.StuckSince = 0;
            }

            // Volcado o encima de algo (muro, poste): vuelve a la pista a los 1,5 s.
            bool flipped = racing && !v.IsOnAllWheels && (Math.Abs(v.Rotation.Y) > 50f || r.Speed < 3f);
            if (flipped)
            {
                if (r.FlipSince == 0) r.FlipSince = now;
            }
            else
            {
                r.FlipSince = 0;
            }

            if (racing && (lateral > 14f || (r.StuckSince != 0 && now - r.StuckSince > 2000) || (r.FlipSince != 0 && now - r.FlipSince > 1500)))
            {
                r.FlipSince = 0;
                Respawn(r);
                r.StuckSince = 0;
                r.NextTaskAt = 0;
                return;
            }

            if (stop)
            {
                if (now >= r.NextTaskAt)
                {
                    r.NextTaskAt = now + 1000;
                    Function.Call(Hash.TASK_VEHICLE_TEMP_ACTION, d.Handle, v.Handle, 1, 3000);
                }
                return;
            }

            // Una orden por tramo (como el importador): cada orden nueva obliga a la IA a recalcular la ruta,
            // y si se repite cada fracción de segundo el auto no llega a arrancar. Se da la siguiente orden
            // cuando se acerca al punto de destino, o cada 10 s por seguridad.
            if (now >= r.NextTaskAt && (r.WpDist - r.Dist < 45f || now - r.WpSince > 10000))
            {
                r.NextTaskAt = now + 1500;
                r.WpDist = r.Dist + (CurveSpeed(r.Dist) < 0.85f ? 80f : 140f); // destino más cerca en curvas: corta menos
                r.WpSince = now;
                Vector3 t = LanePoint(r.WpDist, r.LaneOffset); // el destino va en su carril: ya no todos por el centro
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, d.Handle, v.Handle, t.X, t.Y, t.Z, Math.Max(cruise, 5f), 2883621, 8f);
            }
        }

        private void Respawn(Racer r)
        {
            Vehicle v = r.Veh;
            Vector3 pos = _track.PositionAt(r.Dist + 6f);
            Vector3 tan = _track.TangentAt(r.Dist + 6f);
            v.Position = pos;
            v.Rotation = new Vector3(0f, 0f, (float)(Math.Atan2(-tan.X, tan.Y) * 180.0 / Math.PI));
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, v.Handle, 5f);
            v.Velocity = tan * 12f;
            r.WpDist = 0f;
            r.NextTaskAt = 0;
            _log($"Carrera: {r.Name} se atascó o se salió; vuelve a la pista.");
        }

        /// <summary>Mueve el auto sobre la pista: velocidad + corrección hacia el punto ideal + orientación.</summary>
        private void Drive(Racer r, float dt, int now)
        {
            Vehicle v = r.Veh;
            Vector3 tan = _track.TangentAt(r.Dist);
            Vector3 left = new Vector3(-tan.Y, tan.X, 0f);
            if (left.Length() > 0.001f) left.Normalize();

            float weave = (float)Math.Sin(now * 0.0004 + r.WeavePhase) * 1.1f;
            Vector3 targetPos = _track.PositionAt(r.Dist) + left * (r.Lane + weave);

            Vector3 err = targetPos - v.Position;
            float errLen = err.Length();
            if (errLen > 40f)
            {
                // Se salió de la pista (o el mundo no cargó): vuelve a su lugar.
                v.Position = targetPos;
                v.Velocity = tan * r.Speed;
                return;
            }

            Vector3 pull = err * 3.5f;
            if (pull.Length() > 25f)
            {
                pull.Normalize();
                pull *= 25f;
            }
            v.Velocity = tan * r.Speed + pull;

            float yawTarget = (float)(Math.Atan2(-tan.X, tan.Y) * 180.0 / Math.PI);
            float diff = AngleDiff(yawTarget, r.Yaw);
            float maxTurn = 200f * dt;
            r.Yaw += Math.Max(-maxTurn, Math.Min(maxTurn, diff));
            float pitch = (float)(Math.Asin(Math.Max(-1f, Math.Min(1f, tan.Z))) * 180.0 / Math.PI);
            v.Rotation = new Vector3(pitch, 0f, r.Yaw);

            if (!_steeringBroken)
            {
                try
                {
                    v.SteeringAngle = Math.Max(-30f, Math.Min(30f, diff * 1.5f));
                }
                catch
                {
                    _steeringBroken = true; // sin volante animado; no es grave
                }
            }
        }

        /// <summary>
        /// Los autos de la carrera se atraviesan entre sí. Se reafirma cada frame solo para los
        /// pares cercanos: es barato y no depende de si el juego recuerda la marca entre frames.
        /// </summary>
        private void AvoidCollisions()
        {
            for (int a = 0; a < _racers.Count; a++)
            {
                Vehicle va = _racers[a].Veh;
                if (va == null || !va.Exists()) continue;
                Vector3 pa = va.Position;
                for (int b = a + 1; b < _racers.Count; b++)
                {
                    Vehicle vb = _racers[b].Veh;
                    if (vb == null || !vb.Exists()) continue;
                    if (pa.DistanceTo(vb.Position) < 12f)
                    {
                        Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, va.Handle, vb.Handle, true);
                        Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, vb.Handle, va.Handle, true);
                    }
                }
            }
        }

        // ------------------------------------------------------------ carriles y poderes de bots

        /// <summary>Punto de la pista a esa distancia, corrido hacia un carril (0 = centro). En curvas cerradas se acerca un poco al centro, sin dejar su carril.</summary>
        private Vector3 LanePoint(float dist, float offset)
        {
            Vector3 tan = _track.TangentAt(dist);
            Vector3 left = new Vector3(-tan.Y, tan.X, 0f);
            if (left.Length() > 0.001f) left.Normalize();
            return _track.PositionAt(dist) + left * (offset * LaneScale(dist - 40f));
        }

        /// <summary>
        /// Cada piloto usa su carril de todo el ancho de la pista y cambia de carril cada pocos segundos o cuando
        /// tiene a alguien adelante en el suyo (así se adelantan en vez de ir en fila). Además de la orden a la IA,
        /// un empujón lateral suave lo lleva a su carril. En curvas cerradas se acercan un poco al centro, sin dejar su carril.
        /// </summary>
        private void ApplyLane(Racer r, float dt, int now)
        {
            Vehicle v = r.Veh;
            if (v == null || !v.Exists() || r.Drifting || !v.IsOnAllWheels || v.Speed < 10f) return;

            if (now >= r.LaneChangeAt || LaneBlocked(r)) ChooseLane(r, now);

            Vector3 tan = _track.TangentAt(r.Dist);
            Vector3 left = new Vector3(-tan.Y, tan.X, 0f);
            if (left.Length() > 0.001f) left.Normalize();
            float scale = LaneScale(r.Dist);
            float signed = Vector3.Dot(v.Position - _track.PositionAt(r.Dist), left);
            float wanted = Math.Max(-3f, Math.Min(3f, (r.LaneOffset * scale - signed) * 0.9f)); // m/s de lado
            float current = Vector3.Dot(v.Velocity, left);
            v.Velocity = v.Velocity + left * ((wanted - current) * Math.Min(1f, dt * 3f));
        }

        /// <summary>Cuánto del carril se usa: 1 en recta, 0,75 en una curva muy cerrada (siguen en su carril, un poco más cerca del centro).</summary>
        private float LaneScale(float dist) => 0.75f + 0.25f * Math.Max(0f, Math.Min(1f, (CurveSpeed(dist) - 0.6f) / 0.4f));

        private bool LaneBlocked(Racer r)
        {
            foreach (Racer q in _racers)
            {
                if (ReferenceEquals(q, r) || q.Finished) continue;
                float gap = q.Dist - r.Dist;
                if (gap > 0f && gap < 25f && Math.Abs(q.LaneOffset - r.LaneOffset) < 1.4f) return true;
            }
            return false;
        }

        private void ChooseLane(Racer r, int now)
        {
            float w = _cfg.RoadHalfWidth;
            float[] lanes = { -0.75f * w, -0.25f * w, 0.25f * w, 0.75f * w };
            var free = new List<float>();
            foreach (float l in lanes)
            {
                if (Math.Abs(l - r.LaneOffset) < 0.01f) continue;
                bool blocked = false;
                foreach (Racer q in _racers)
                {
                    if (ReferenceEquals(q, r) || q.Finished) continue;
                    float gap = q.Dist - r.Dist;
                    if (gap > -6f && gap < 40f && Math.Abs(q.LaneOffset - l) < 1.4f) { blocked = true; break; }
                }
                if (!blocked) free.Add(l);
            }

            if (free.Count > 0)
            {
                r.LaneOffset = free[_rng.Next(free.Count)];
                r.LaneChangeAt = now + 4000 + _rng.Next(0, 6000);
            }
            else
            {
                r.LaneChangeAt = now + 1500; // sin hueco: reintenta pronto
            }
        }

        private int _nextBotPowerAt;

        /// <summary>Para probar: un bot al azar usa un poder cada 3-7 s (misil, meteorito, escudo, jet, portal, turbo).</summary>
        private void UpdateBotPowers(int now)
        {
            if (!_cfg.BotPowers || now < _nextBotPowerAt) return;
            _nextBotPowerAt = now + 3000 + _rng.Next(0, 4000);

            List<Racer> bots = _racers.Where(x => x.IsBot && !x.Finished).ToList();
            if (bots.Count == 0) return;
            Racer b = bots[_rng.Next(bots.Count)];
            int roll = _rng.Next(100);
            try
            {
                if (roll < 30) Missile(b.Name, 4);
                else if (roll < 45) Meteor(b.Name, 4);
                else if (roll < 65) Shield(b.Name);
                else if (roll < 80) JumpAhead(b.Name, 10, "JET");
                else if (roll < 90) JumpAhead(b.Name, 20, "PORTAL");
                else Boost(b.Name, 3);
            }
            catch (ActionException)
            {
                // sin objetivo (va primero, etc.): se ignora
            }
        }

        private const string NitroAsset = "veh_xs_vehicle_mods";

        /// <summary>
        /// Derrape solo mientras el auto está GIRANDO de verdad (volante girado a velocidad alta), no al ver una curva.
        /// Dura como mucho 1,8 s y luego recupera el agarre y descansa 1,5 s: nada de derrapes largos.
        /// </summary>
        private void ApplyDrift(Racer r, int now)
        {
            if (_cfg.DriftLevel <= 0 || now < r.NextGripAt) return;
            r.NextGripAt = now + 100;
            Vehicle v = r.Veh;
            if (v == null || !v.Exists()) return;

            float steer = Math.Abs(v.SteeringAngle);
            bool turning = v.Speed > 14f && steer > (r.Drifting ? 9f : 16f); // con margen para que no parpadee
            if (r.Drifting && now - r.DriftSince > 1800)
            {
                turning = false;
                r.NextDriftAt = now + 1500;
            }
            if (!r.Drifting && now < r.NextDriftAt) turning = false;
            if (turning == r.Drifting) return;

            r.Drifting = turning;
            if (turning) r.DriftSince = now;

            if (TryHash("SET_VEHICLE_REDUCE_GRIP", out Hash grip)) Function.Call(grip, v.Handle, turning);
            if (TryHash("SET_VEHICLE_REDUCE_GRIP_LEVEL", out Hash level)) Function.Call(level, v.Handle, turning ? Math.Min(3, _cfg.DriftLevel) : 0);
            if (TryHash("SET_DRIFT_TYRES", out Hash drift)) Function.Call(drift, v.Handle, turning);
        }

        /// <summary>
        /// Multiplicador de velocidad según la curva que viene: 1 en recta, ~0.6 en una curva muy cerrada. Mira un poco
        /// adelante para frenar ANTES de entrar, como en una carrera.
        /// </summary>
        private float CurveSpeed(float dist)
        {
            float cf = Math.Min(_track.CurveFactor(dist + 12f), _track.CurveFactor(dist + 35f)); // 1 = recta, 0.55 = muy cerrada
            return 0.6f + 0.4f * Math.Max(0f, Math.Min(1f, (cf - 0.55f) / 0.45f));
        }

        private static bool TryHash(string name, out Hash hash) =>
            Enum.TryParse(name, out hash) && Enum.IsDefined(typeof(Hash), hash);

        /// <summary>
        /// Con turbo activo el motor del auto rinde más (la IA solo recibe una velocidad de crucero y un auto normal
        /// no pasa de su tope), más llamas de nitro en el escape. Al acabar se devuelve todo a lo normal.
        /// </summary>
        private void ApplyBoostPhysics(Racer r, int now)
        {
            Vehicle v = r.Veh;
            if (v == null || !v.Exists()) return;

            int stacks = r.Finished || _phase != Phase.Racing ? 0 : r.BoostEnds.Count;
            if (stacks > 0)
            {
                if (TryHash("SET_VEHICLE_CHEAT_POWER_INCREASE", out Hash power))
                {
                    Function.Call(power, v.Handle, Math.Min(2.5f, 1f + 0.35f * stacks)); // más potencia, sin que patinen las ruedas
                }
                if (TryHash("MODIFY_VEHICLE_TOP_SPEED", out Hash top))
                {
                    Function.Call(top, v.Handle, stacks * _cfg.BoostPerStack * 100f);
                }
                r.BoostOn = true;

                // Llamas de nitro en la cola del auto (si el efecto no existe no se ve nada, pero no falla).
                if (r.BoostFx < 0)
                {
                    Ptfx.Request(NitroAsset);
                    if (Ptfx.Ready(NitroAsset))
                    {
                        Function.Call(Hash.USE_PARTICLE_FX_ASSET, NitroAsset);
                        r.BoostFx = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY, "veh_nitrous", v.Handle,
                            0f, -2.3f, 0.1f, 0f, 0f, 0f, 1.6f, false, false, false);
                    }
                }

                // Luz naranja detrás del auto: siempre se ve, aunque falle la partícula.
                Ptfx.Light(v.GetOffsetPosition(new Vector3(0f, -2.6f, 0.3f)), Color.FromArgb(255, 140, 20), 7f, 9f);
            }
            else if (r.BoostOn || r.BoostFx >= 0)
            {
                StopBoostEffects(r);
            }
        }

        private static void StopBoostEffects(Racer r)
        {
            Vehicle v = r.Veh;
            if (r.BoostOn && v != null && v.Exists() && TryHash("SET_VEHICLE_CHEAT_POWER_INCREASE", out Hash power))
            {
                Function.Call(power, v.Handle, 1f);
            }
            r.BoostOn = false;
            if (r.BoostFx >= 0)
            {
                Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, r.BoostFx, false);
                r.BoostFx = -1;
            }
        }

        private void AddBoost(Racer r, int stacks)
        {
            int end = GTA.Game.GameTime + (int)(_cfg.BoostSeconds * 1000f);
            for (int i = 0; i < stacks && r.BoostEnds.Count < _cfg.MaxStacks; i++)
            {
                r.BoostEnds.Add(end);
            }
        }

        // ------------------------------------------------------------ final

        private void EndRace(int now)
        {
            _phase = Phase.Finished;
            _finishedAt = now;

            List<Racer> order = Ranking();
            for (int i = 0; i < order.Count; i++)
            {
                _tracker.Retag(order[i].Veh, $"{i + 1}° {order[i].Name}");
            }

            string[] medals = { "~y~1°", "~c~2°", "~o~3°" };
            var lines = new List<string>();
            for (int i = 0; i < Math.Min(3, order.Count); i++)
            {
                lines.Add($"{medals[i]}~s~ {Safe(order[i].Name)}");
            }
            Notification.Show("~p~Carrera terminada~s~: " + string.Join("  ", lines));
            _log($"Carrera: terminada. Podio: {string.Join(", ", order.Take(3).Select(x => x.Name))}.");
        }

        /// <summary>
        /// Terminada la carrera: borra los autos y vuelve a abrir las inscripciones en la misma pista (con la cámara
        /// y el streamer oculto como estaban). Solo race_stop cierra el modo y devuelve al personaje a su lugar.
        /// </summary>
        private void ResetToLobby()
        {
            foreach (Racer r in _racers) StopBoostEffects(r);
            _tracker.RemoveKind(Kind);
            _racers.Clear();
            _botCounter = 0;
            _finishedCount = 0;
            _firstFinishAt = 0;
            _phase = Phase.Lobby;
            Notification.Show($"~p~Nueva carrera~s~: {_laps} vueltas en '{_trackName}'. ¡Inscríbanse! Empieza cuando el streamer lo diga.");
            _log("Carrera: terminada; vuelve al lobby para la siguiente.");
            if (_cfg.PlayerRaces) AddRacer(PlayerRacerName, false);
        }

        private void Cleanup()
        {
            EndBroadcast();
            foreach (Racer r in _racers) StopBoostEffects(r);
            _racePool = null;
            _scenery?.Dispose();
            _scenery = null;
            _tracker.RemoveKind(Kind);
            _racers.Clear();
            _phase = Phase.Idle;
        }

        /// <summary>Al recargar el script o cerrar el juego.</summary>
        public void Clear()
        {
            Cleanup();
        }

        // ------------------------------------------------------------ pilotos

        private void AddRacer(string name, bool isBot)
        {
            if (_track == null)
            {
                throw new ActionException("No hay una pista elegida");
            }

            int idx = _racers.Count;
            int cols = Math.Max(1, _cfg.GridColumns);
            int row = idx / cols;
            float lane = (idx % cols - (cols - 1) / 2f) * _cfg.GridLaneWidth; // centrada en la pista: 4 columnas = -5,1 / -1,7 / 1,7 / 5,1
            float startDist = -((_cfg.RealDriving && _cfg.Parade ? Math.Min(FormationMeters, _track.Length * 0.35f) : 10f) + 7f * row);

            Vector3 tan = _track.TangentAt(startDist);
            Vector3 left = new Vector3(-tan.Y, tan.X, 0f);
            if (left.Length() > 0.001f) left.Normalize();
            Vector3 pos = _track.PositionAt(startDist) + left * lane;
            float heading = (float)(Math.Atan2(-tan.X, tan.Y) * 180.0 / Math.PI);

            string model = PickModel();
            Vehicle v = Spawner.SpawnVehicle(model, pos, heading, false);

            v.IsEngineRunning = true;
            v.IsInvincible = true;
            Function.Call(Hash.SET_VEHICLE_COLOURS, v.Handle, _rng.Next(0, 160), _rng.Next(0, 160));
            if (_cfg.GhostAlpha < 255) Function.Call(Hash.SET_ENTITY_ALPHA, v.Handle, _cfg.GhostAlpha, false);

            // Atraviesa a los demás pilotos desde el primer instante.
            foreach (Racer other in _racers)
            {
                if (other.Veh != null && other.Veh.Exists())
                {
                    Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, v.Handle, other.Veh.Handle, true);
                    Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, other.Veh.Handle, v.Handle, true);
                }
            }

            v.IsPositionFrozen = true; // espera la salida
            _tracker.Track(v, name, Kind, GameData.VehicleTagHeight["car"]);

            Ped driver = null;
            if (_cfg.RealDriving)
            {
                driver = v.CreateRandomPedOnSeat(VehicleSeat.Driver);
                if (driver != null)
                {
                    driver.IsInvincible = true;
                    driver.BlockPermanentEvents = true;
                    Function.Call(Hash.SET_DRIVER_ABILITY, driver.Handle, 1.0f);
                    Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver.Handle, 1.0f);
                    _tracker.Track(driver, null, Kind);
                }
            }

            _racers.Add(new Racer
            {
                Driver = driver,
                Name = name,
                Veh = v,
                IsBot = isBot,
                Skill = isBot ? 0.93f + (float)_rng.NextDouble() * 0.14f : 0.98f + (float)_rng.NextDouble() * 0.04f,
                Dist = startDist,
                Lane = lane,
                LaneOffset = Math.Max(-_cfg.RoadHalfWidth * 0.8f, Math.Min(_cfg.RoadHalfWidth * 0.8f, lane)),
                WeavePhase = (float)(_rng.NextDouble() * Math.PI * 2),
                Yaw = heading,
            });

            _log($"Carrera: se inscribió '{name}' con {model}{(isBot ? " (bot)" : "")}. Pilotos: {_racers.Count}.");
        }

        /// <summary>Autos de las clases Deportivos (6) y Súper (7), los que parecen de carrera.</summary>
        private string PickModel()
        {
            if (_pool == null)
            {
                _pool = new List<string>();
                foreach (string name in GameData.Vehicles["car"])
                {
                    try
                    {
                        var m = new Model(name);
                        int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS_FROM_NAME, m.Hash);
                        if (cls == 6 || cls == 7) _pool.Add(name);
                    }
                    catch
                    {
                        // modelo raro: se omite
                    }
                }

                if (_pool.Count == 0)
                {
                    _log("Carrera: no se encontraron autos deportivos; se usa cualquier auto.");
                    _pool.AddRange(GameData.Vehicles["car"]);
                }
                else
                {
                    _log($"Carrera: {_pool.Count} modelos de carrera disponibles.");
                }
            }
            // Pocos modelos distintos por carrera: el juego los carga una vez y la carrera no se traba.
            if (_racePool == null)
            {
                _racePool = new List<string>();
                var copy = new List<string>(_pool);
                while (_racePool.Count < 10 && copy.Count > 0)
                {
                    int k = _rng.Next(copy.Count);
                    _racePool.Add(copy[k]);
                    copy.RemoveAt(k);
                }
            }
            return _racePool[_rng.Next(_racePool.Count)];
        }

        private Racer Find(string name) =>
            name == null ? null : _racers.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

        private List<Racer> Ranking() =>
            _racers.OrderBy(r => r.Finished ? 0 : 1)
                   .ThenBy(r => r.Finished ? r.Place : 0)
                   .ThenByDescending(r => r.Dist)
                   .ToList();

        private void RequireTrack()
        {
            if (!HasTrack)
            {
                throw new ActionException("No hay pistas instaladas para la carrera");
            }
        }

        // ------------------------------------------------------------ pantalla

        private void DrawHud(int now)
        {
            var lines = new List<string>();
            if (_phase == Phase.Lobby)
            {
                lines.Add($"CARRERA · {_trackName} · {_laps} vueltas");
                lines.Add($"Inscripciones abiertas · {_racers.Count} pilotos · esperando que el streamer empiece");
            }
            else if (_phase == Phase.Countdown)
            {
                lines.Add(_formation ? $"DESFILE · {_laps} vueltas · {_racers.Count} pilotos" : $"CARRERA · {_laps} vueltas · {_racers.Count} pilotos");
            }
            else
            {
                List<Racer> order = Ranking();
                int lead = order.Count > 0 ? (int)Math.Min(_laps, order[0].LapsDone + 1) : 1;
                lines.Add($"VUELTA {lead}/{_laps} · {FormatTime(now - _raceStart)}");
                for (int i = 0; i < Math.Min(8, order.Count); i++)
                {
                    Racer r = order[i];
                    string extra = r.Finished ? " (llegó)" : r.BoostEnds.Count > 0 ? $" x{r.BoostEnds.Count}" : "";
                    lines.Add($"{i + 1}° {Safe(r.Name)}{extra}");
                }
            }
            DrawLines(lines);
            DrawMinimap();
        }

        private static readonly Color[] DotColors =
        {
            Color.FromArgb(255, 80, 80), Color.FromArgb(80, 170, 255), Color.FromArgb(90, 220, 120), Color.FromArgb(255, 150, 50),
            Color.FromArgb(200, 110, 255), Color.FromArgb(0, 220, 220), Color.FromArgb(255, 110, 190), Color.FromArgb(190, 230, 60),
        };

        /// <summary>Mapa de la pista completa con un punto por piloto (esquina inferior derecha).</summary>
        private void DrawMinimap()
        {
            if (_track == null || !_track.IsReady) return;
            if (_minimap == null) _minimap = new RaceMinimap();
            if (!_minimap.IsFor(_track)) _minimap.Build(_track);

            List<Racer> order = Ranking();
            var dots = new List<RaceMinimap.Dot>();
            for (int i = 0; i < order.Count; i++)
            {
                Racer r = order[i];
                if (r.Veh == null || !r.Veh.Exists()) continue;
                dots.Add(new RaceMinimap.Dot
                {
                    Position = r.Veh.Position,
                    Rank = i + 1,
                    Finished = r.Finished,
                    Label = ShortName(r.Name),
                    Color = DotColors[((r.Name ?? "").GetHashCode() & 0x7fffffff) % DotColors.Length],
                });
            }
            _minimap.Draw(dots);
        }

        private RaceMinimap _minimap;

        /// <summary>"Bot 7" → "Bot 7"; nombres largos se recortan para que quepan en el mapa.</summary>
        private static string ShortName(string name)
        {
            string n = Safe(name);
            return n.Length > 10 ? n.Substring(0, 10) : n;
        }

        private void DrawLines(IList<string> lines)
        {
            float y = 24f;
            foreach (string line in lines)
            {
                _text.Caption = line;
                _text.Position = new PointF(24f, y);
                _text.Draw();
                y += 22f;
            }
        }

        private static string Safe(string s) => TextUtil.CleanText(s) ?? "";

        private static string FormatTime(int ms)
        {
            ms = Math.Max(0, ms);
            return $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000 / 100}";
        }

        private static float AngleDiff(float target, float current)
        {
            float d = (target - current) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }
    }
}
