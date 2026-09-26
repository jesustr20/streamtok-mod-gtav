using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Modes
{
    /// <summary>
    /// Modo Chiliad: llegar desde la base hasta la cima del Monte Chiliad antes de que se acabe
    /// el tiempo. Reglas:
    ///  - Libre: a pie o en cualquier vehículo; los viewers pueden ayudar o estorbar con
    ///    cualquier otra acción del catálogo.
    ///  - Cuenta regresiva: si llega a cero, se pierde el intento y vuelve a la base con el
    ///    reloj completo.
    ///  - Si muere o lo arrestan, reaparece cerca del último punto seguro (con un vehículo del
    ///    mismo modelo si iba manejando) y sigue. El reloj se pausa mientras está muerto.
    /// Todo corre en el Tick del script: nada bloquea el juego.
    /// </summary>
    internal sealed class ChiliadMode
    {
        /// <summary>Meta por defecto: cima del Monte Chiliad (se puede mover con "marcar meta aquí").</summary>
        public static readonly Vector3 DefaultGoal = new Vector3(501.8f, 5604.4f, 797.9f);

        /// <summary>
        /// Parada del taxi por defecto: salida del túnel del camino de tierra del Monte Chiliad
        /// (marcada en el juego). Se puede cambiar con "Marcar parada del taxi aquí".
        /// </summary>
        public static readonly Vector3 DefaultTaxiStop = new Vector3(-508.90f, 4936.64f, 146.88f);
        public const float DefaultTaxiHeading = 243.4f;

        /// <summary>
        /// Puntos de salida, todos lejos del monte (el clásico es el aeropuerto de Los Santos).
        /// "random" = uno de ellos al azar; "current" = donde esté el jugador.
        /// </summary>
        public static readonly string[] StartKeys =
        {
            "airport", "random", "del_perro_pier", "grove_street", "vinewood_sign", "sandy_shores", "current",
        };

        /// <summary>Salidas lejanas para "random" (de ~3 km a ~9 km de la cima).</summary>
        private static readonly string[] FarStarts =
        {
            "airport", "del_perro_pier", "grove_street", "vinewood_sign", "sandy_shores",
        };

        /// <summary>
        /// Salida principal: vereda de la entrada de la terminal del aeropuerto, en el PRIMER piso
        /// (llegadas, donde la gente sale a esperar su auto). Se puede reemplazar con "Marcar salida aquí".
        /// </summary>
        public static readonly Vector3 AirportEntrance = new Vector3(-1037.6f, -2737.8f, 13.9f);

        private Vector3 StartPoint(string key)
        {
            if (key == "airport")
            {
                return _markedStart ?? AirportEntrance;
            }
            return GameData.Locations.TryGetValue(key, out Vector3 pos) ? pos : AirportEntrance;
        }

        private string StartTitle(string key)
        {
            if (key == "airport" && _markedStart.HasValue)
            {
                return "Salida marcada";
            }
            switch (key)
            {
                case "del_perro_pier": return "Muelle de Del Perro";
                case "sandy_shores": return "Sandy Shores";
                case "vinewood_sign": return "Letrero de Vinewood";
                case "grove_street": return "Grove Street";
                case "airport": return "Aeropuerto";
                default: return "Aquí mismo";
            }
        }

        private const float ZoneRadius = 5.5f;        // círculo de la meta (metros en horizontal, ~11 m de ancho)
        private const float ZoneBelow = 6f, ZoneAbove = 8f; // tolerancia de altura respecto a la meta
        private const int RestartDelayMs = 6500;      // tras una cima, pausa antes de la nueva vuelta
        private const int SafeSampleMs = 2000;        // cada cuánto se guarda un punto seguro
        private const int CelebrationMs = 6000;
        private const string FireworksAsset = "scr_indep_fireworks";

        private static readonly string[] FireworkEffects =
        {
            "scr_indep_firework_starburst",
            "scr_indep_firework_trailburst",
            "scr_indep_firework_shotburst",
        };

        private readonly EntityTracker _tracker;
        private readonly FrameScheduler _scheduler;
        private readonly Random _rng;
        private readonly Action<string> _log;

        // --- estado de la partida
        private bool _active;
        private Vector3 _base;
        private float _baseHeading;
        private int _limitMs;       // duración de un intento
        private int _remainingMs;
        private int _elapsedMs;     // tiempo del intento actual (para el récord)
        private int _attempt;
        private int _bestMs;        // 0 = sin récord
        private int _lastTick;
        private int _gpsOffUntil;   // GameTime; 0 = sin apagón temporal
        private bool _gpsOn;        // interruptores: al iniciar, todo encendido
        private bool _timerOn;
        private bool _routeOn;
        private bool _respawnOn;
        private Blip _blip;
        private Vector3 _goal = DefaultGoal;
        private float _goalGround = float.NaN;
        private int _previewUntil;  // tras marcar la meta con el modo apagado: se ve unos segundos
        private bool _ownWaypoint;  // el waypoint actual lo puso el modo (se quita al terminar)

        // --- taxi: viaje rápido hasta la salida del túnel del monte
        private Vector3 _taxiStop = DefaultTaxiStop;
        private float _taxiHeading = DefaultTaxiHeading;
        private readonly string _taxiFile;
        private readonly string _startFile;
        private Vector3? _markedStart;       // salida principal marcada por el streamer (reemplaza al aeropuerto)
        private float _markedStartHeading;
        private bool _taxiOn;
        private bool _taxiTraveling;
        private bool _waypointAtTaxi;   // el waypoint apunta al túnel mientras va en taxi

        private readonly string _goalFile;
        private Blip _zoneBlip;

        // --- cima: hay que quedarse dentro del círculo hasta que el contador llegue a cero
        private int _holdMs;        // configurado al iniciar
        private int _holdLeftMs;
        private bool _inZone;
        private bool _repeat;       // al completar: nueva vuelta desde la salida
        private int _summits;       // cimas logradas en esta sesión
        private int _restartAt;     // GameTime de la próxima vuelta; 0 = no hay

        // --- reaparición
        private bool _wasDown;
        private Vector3 _safeLatest, _safePrevious;
        private float _safeHeadingLatest, _safeHeadingPrevious;
        private int _safeModelLatest, _safeModelPrevious; // 0 = a pie
        private int _nextSafeSample;
        private bool _teleporting;

        // --- mensajes y celebración
        private string _bigText;
        private Color _bigColor;
        private int _bigUntil;
        private int _celebrateUntil;
        private int _nextFirework;

        // --- HUD (coordenadas de pantalla 1280x720)
        private const float PanelX = 1000f, PanelY = 190f, PanelW = 265f, PanelH = 150f;
        private const float PanelCenter = PanelX + PanelW / 2f;
        private readonly GTA.UI.ContainerElement _panel = new GTA.UI.ContainerElement(
            new PointF(PanelX, PanelY), new SizeF(PanelW, PanelH), Color.FromArgb(170, 0, 0, 0));
        private readonly GTA.UI.TextElement _title = new GTA.UI.TextElement(
            "MONTE CHILIAD", new PointF(PanelCenter, PanelY + 4f), 0.34f, Color.Gold,
            GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.TextElement _clock = new GTA.UI.TextElement(
            "", new PointF(PanelCenter, PanelY + 22f), 0.9f, Color.White,
            GTA.UI.Font.Pricedown, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.TextElement _height = new GTA.UI.TextElement(
            "", new PointF(PanelCenter, PanelY + 68f), 0.3f, Color.White,
            GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.ContainerElement _barBack = new GTA.UI.ContainerElement(
            new PointF(PanelX + 12f, PanelY + 90f), new SizeF(PanelW - 24f, 8f), Color.FromArgb(200, 40, 40, 40));
        private readonly GTA.UI.ContainerElement _barFill = new GTA.UI.ContainerElement(
            new PointF(PanelX + 12f, PanelY + 90f), new SizeF(0f, 8f), Color.FromArgb(230, 255, 190, 40));
        private readonly GTA.UI.TextElement _footer = new GTA.UI.TextElement(
            "", new PointF(PanelCenter, PanelY + 104f), 0.28f, Color.White,
            GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.TextElement _hold = new GTA.UI.TextElement(
            "", new PointF(640f, 140f), 1.2f, Color.LimeGreen,
            GTA.UI.Font.Pricedown, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.TextElement _noTimer = new GTA.UI.TextElement(
            "SIN LÍMITE DE TIEMPO", new PointF(PanelCenter, PanelY + 36f), 0.42f, Color.LightSkyBlue,
            GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.ContainerElement _taxiBack = new GTA.UI.ContainerElement(
            new PointF(440f, 610f), new SizeF(400f, 34f), Color.FromArgb(190, 0, 0, 0));
        private readonly GTA.UI.TextElement _taxiText = new GTA.UI.TextElement(
            "~y~[E]~s~  Viajar en taxi al Monte Chiliad (túnel)", new PointF(640f, 615f), 0.4f, Color.White,
            GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.TextElement _switches = new GTA.UI.TextElement(
            "", new PointF(PanelCenter, PanelY + 124f), 0.26f, Color.White,
            GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.TextElement _big = new GTA.UI.TextElement(
            "", new PointF(640f, 250f), 1.6f, Color.Gold,
            GTA.UI.Font.Pricedown, GTA.UI.Alignment.Center, true, true);

        public ChiliadMode(EntityTracker tracker, FrameScheduler scheduler, Random rng, Action<string> log, string baseDirectory)
        {
            _tracker = tracker;
            _scheduler = scheduler;
            _rng = rng;
            _log = log;
            _goalFile = Path.Combine(baseDirectory, "StreamTok.ChiliadGoal.txt");
            _taxiFile = Path.Combine(baseDirectory, "StreamTok.ChiliadTaxi.txt");
            _startFile = Path.Combine(baseDirectory, "StreamTok.ChiliadStart.txt");
            LoadStart();
            LoadGoal();
            LoadTaxiStop();
        }

        public bool IsActive => _active;
        public bool GpsOn => _active && _gpsOn && _gpsOffUntil == 0; // OFF también durante un apagón temporal
        public bool TimerOn => _active && _timerOn;
        public bool RouteOn => _active && _routeOn;
        public bool RespawnOn => _active && _respawnOn;
        public bool TaxiOn => _active && _taxiOn;

        // ================================================================ acciones

        /// <param name="startKey">airport, random, … o current (ver StartKeys)</param>
        /// <param name="vehicleModel">null = conservar lo que tenga; "" = a pie; o un modelo.</param>
        public void Start(int minutes, bool timer, int holdSeconds, bool repeat, string startKey, string vehicleModel, string nameTag)
        {
            Ped p = Player;
            if (p.IsDead)
            {
                throw new ActionException("El jugador está muerto: espera a que reaparezca");
            }

            if (vehicleModel != null && vehicleModel.Length > 0)
            {
                Spawner.Preload(vehicleModel); // puede tardar: antes de tocar nada
            }

            if (startKey == "random")
            {
                startKey = FarStarts[_rng.Next(FarStarts.Length)];
            }

            ClearState();
            _active = true;
            _gpsOn = true;
            _timerOn = timer;
            _routeOn = true;
            _respawnOn = true;
            _taxiOn = true;
            _limitMs = minutes * 60 * 1000;
            _remainingMs = _limitMs;
            _elapsedMs = 0;
            _attempt = 1;
            _holdMs = holdSeconds * 1000;
            _holdLeftMs = _holdMs;
            _inZone = false;
            _repeat = repeat;
            _summits = 0;
            _restartAt = 0;
            _bestMs = 0;
            _lastTick = GTA.Game.GameTime;

            if (startKey == "current")
            {
                _base = p.Position;
            }
            else
            {
                _base = StartPoint(startKey);
            }
            _baseHeading = startKey == "airport" && _markedStart.HasValue
                ? _markedStartHeading
                : HeadingTowards(_base, _goal);
            ResetSafePoints(_base, _baseHeading);

            CreateBlips(); // meta + círculo en el mapa, con la ruta según los interruptores

            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, FireworksAsset);

            if (vehicleModel != null && p.IsInVehicle())
            {
                // A pie o con un vehículo nuevo: sale del actual al instante.
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, p);
            }

            Action after = null;
            if (vehicleModel != null && vehicleModel.Length > 0)
            {
                after = () =>
                {
                    Vehicle v = Spawner.SpawnVehicle(vehicleModel, _base, _baseHeading, true);
                    _tracker.Track(v, nameTag, EntityTracker.KindVehicle, GameData.VehicleTagHeight["car"]);
                    Player.SetIntoVehicle(v, VehicleSeat.Driver);
                };
            }

            if (startKey == "current")
            {
                after?.Invoke();
            }
            else
            {
                SafeTeleport(_base, _baseHeading, after);
            }

            ShowBig("¡A LA CIMA!", Color.Gold, 3000);
            GTA.UI.Notification.Show($"~y~Modo Chiliad~s~ activo · salida: {StartTitle(startKey)} · {(timer ? $"{minutes} min" : "sin límite de tiempo")}");
            _log($"Chiliad: inicio ({minutes} min, salida {startKey}).");
        }

        public void Stop()
        {
            if (!_active && _celebrateUntil == 0)
            {
                throw new ActionException("El modo Chiliad no está activo");
            }
            ClearState();
            _log("Chiliad: terminado.");
        }

        /// <summary>
        /// Interruptor del GPS (minimapa). Encenderlo también cancela un "apagar GPS unos segundos".
        /// </summary>
        public void SetGps(bool on)
        {
            RequireActive();
            _gpsOn = on;
            if (on)
            {
                _gpsOffUntil = 0;
            }
            RefreshGps();
        }

        /// <summary>Interruptor de la ruta trazada hasta la cima (la línea en el mapa).</summary>
        public void SetRoute(bool on)
        {
            RequireActive();
            _routeOn = on;
            RefreshGps();
        }

        /// <summary>
        /// Interruptor del tiempo límite. Apagado, el reloj desaparece del HUD y el tiempo ya no
        /// afecta el progreso (no se pierde por tiempo). Encenderlo de nuevo sigue desde donde quedó.
        /// </summary>
        public void SetTimer(bool on)
        {
            RequireActive();
            _timerOn = on;
            if (on && _remainingMs <= 0)
            {
                _remainingMs = _limitMs;
            }
        }

        /// <summary>
        /// Interruptor de reaparecer donde quedó. Apagado, morir lo manda de vuelta a la salida
        /// y cuenta como intento perdido.
        /// </summary>
        public void SetTaxi(bool on)
        {
            RequireActive();
            _taxiOn = on;
        }

        public void SetRespawn(bool on)
        {
            RequireActive();
            _respawnOn = on;
        }

        /// <summary>Oculta el minimapa y la ruta. Si ya estaba oculto, se suma el tiempo.</summary>
        public void HideGps(int seconds)
        {
            RequireActive();
            int now = GTA.Game.GameTime;
            _gpsOffUntil = Math.Max(_gpsOffUntil, now) + seconds * 1000;
            RefreshGps();
        }

        /// <summary>Suma (o resta, con negativo) tiempo al reloj. Puede llegar a cero y perder.</summary>
        public void AddTime(int seconds)
        {
            RequireActive();
            if (!_timerOn)
            {
                throw new ActionException("El tiempo límite está desactivado");
            }
            _remainingMs = Math.Max(0, _remainingMs + seconds * 1000);
            ShowBig(seconds >= 0 ? $"+{seconds} s" : $"{seconds} s",
                seconds >= 0 ? Color.LimeGreen : Color.Red, 1500);
        }

        /// <summary>Lo manda a la base. El reloj sigue corriendo (no cuenta como intento).</summary>
        public void BackToBase()
        {
            RequireActive();
            ResetSafePoints(_base, _baseHeading);
            SafeTeleport(_base, _baseHeading, null);
            ShowBig("¡DE VUELTA AL INICIO!", Color.OrangeRed, 2500);
        }

        public void RequireActive()
        {
            if (!_active)
            {
                throw new ActionException("El modo Chiliad no está activo");
            }
        }

        // ================================================================ cada frame

        public void Update()
        {
            int now = GTA.Game.GameTime;

            if (_celebrateUntil != 0)
            {
                Celebrate(now);
            }

            if (_active)
            {
                UpdateRun(now);
            }
            else if (now < _previewUntil)
            {
                DrawZone(); // vista previa de la meta recién marcada
            }

            DrawBig(now);
        }

        private void UpdateRun(int now)
        {
            int delta = Math.Min(1000, Math.Max(0, now - _lastTick)); // tope: pantallas de carga
            _lastTick = now;

            DrawZone();

            // --- Cima lograda: pausa de celebración y nueva vuelta desde la salida.
            if (_restartAt != 0)
            {
                if (now >= _restartAt)
                {
                    StartNewRound();
                }
                DrawHud(now);
                return;
            }

            Ped p = Player;
            bool down = p.IsDead
                || Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, GTA.Game.Player.Handle, true)
                || (!_taxiTraveling && Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT));

            // --- GPS apagado
            if (_gpsOffUntil != 0)
            {
                if (now >= _gpsOffUntil)
                {
                    _gpsOffUntil = 0;
                    RefreshGps();
                }
            }
            if (_gpsOffUntil != 0 || !_gpsOn)
            {
                Function.Call(Hash.DISPLAY_RADAR, false); // el juego lo vuelve a prender en algunas escenas
            }

            // --- Muerte o arresto: el reloj se pausa; al volver, reaparece donde iba.
            if (down)
            {
                _wasDown = true;
                DrawHud(now);
                return;
            }
            if (_wasDown)
            {
                _wasDown = false;
                if (_respawnOn)
                {
                    RespawnAtSafePoint();
                }
                else
                {
                    LoseAttempt("¡A EMPEZAR DE NUEVO!");
                }
            }

            if (!GTA.Game.IsPaused && !_teleporting)
            {
                if (_timerOn)
                {
                    _remainingMs -= delta;
                }
                _elapsedMs += delta;
            }

            // --- Taxi: CADA vez que sube de pasajero se ofrece el viaje al túnel del monte.
            //     Mientras va en taxi, el destino marcado en el mapa es el túnel (así el taxi del
            //     juego también lo lleva ahí, manejando o saltando el viaje); al bajarse vuelve a la meta.
            bool inTaxi = IsTaxiPassenger(p);
            if (inTaxi && _taxiOn)
            {
                if (!_waypointAtTaxi && _routeOn && _gpsOffUntil == 0)
                {
                    World.WaypointPosition = _taxiStop;
                    _ownWaypoint = true;
                    _waypointAtTaxi = true;
                }

                if (!_taxiTraveling && !_teleporting && p.Position.DistanceTo(_taxiStop) > 60f)
                {
                    // La E es nuestra mientras va en taxi: se le quita al juego para que no choque.
                    GTA.Game.DisableControlThisFrame(GTA.Control.Context);
                    DrawTaxiPrompt();
                    if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)GTA.Control.Context))
                    {
                        TaxiTravel();
                    }
                }
            }
            else if (_waypointAtTaxi)
            {
                _waypointAtTaxi = false;
                RemoveOwnWaypoint();
                RefreshGps(); // vuelve a marcar la meta
            }

            // --- Punto seguro para reaparecer (y, de paso, mantener el waypoint)
            if (now >= _nextSafeSample && !_teleporting)
            {
                _nextSafeSample = now + SafeSampleMs;
                SampleSafePoint(p);
                KeepWaypoint(p.Position);
            }

            // --- Cima: dentro del círculo el contador baja; si sale, se reinicia y desaparece.
            bool inZone = InZone(p.Position);
            if (inZone)
            {
                if (!_inZone)
                {
                    _holdLeftMs = _holdMs;
                }
                if (!GTA.Game.IsPaused && !_teleporting)
                {
                    _holdLeftMs -= delta;
                }
                if (_holdLeftMs <= 0)
                {
                    _inZone = false;
                    Complete(now);
                    DrawHud(now);
                    return;
                }
            }
            else if (_inZone)
            {
                _holdLeftMs = _holdMs; // lo sacaron: vuelve a empezar cuando regrese
                ShowBig("¡VUELVE AL CÍRCULO!", Color.OrangeRed, 1500);
            }
            _inZone = inZone;

            // --- ¿Se acabó el tiempo?
            if (_timerOn && _remainingMs <= 0)
            {
                LoseAttempt("¡SE ACABÓ EL TIEMPO!");
            }

            DrawHud(now);
        }

        /// <summary>Intento perdido: vuelve a la salida con el reloj completo.</summary>
        private void LoseAttempt(string message)
        {
            _attempt++;
            _remainingMs = _limitMs;
            _elapsedMs = 0;
            if (Player.IsInVehicle() && Player.CurrentVehicle.IsDead)
            {
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, Player.Handle);
            }
            ResetSafePoints(_base, _baseHeading);
            SafeTeleport(_base, _baseHeading, null);
            ShowBig(message, Color.Red, 3500);
            _log($"Chiliad: {message} Intento {_attempt}.");
        }

        /// <summary>Se quedó en la cima hasta que el contador llegó a cero.</summary>
        private void Complete(int now)
        {
            _summits++;
            if (_bestMs == 0 || _elapsedMs < _bestMs)
            {
                _bestMs = _elapsedMs;
            }
            ShowBig($"¡CIMA! {FormatTime(_elapsedMs)}", Color.Gold, CelebrationMs);
            GTA.UI.Notification.Show($"~y~Monte Chiliad~s~ conquistado en ~g~{FormatTime(_elapsedMs)}~s~ (intento {_attempt})"
                + (_repeat ? " · nueva vuelta en unos segundos" : ""));
            _log($"Chiliad: cima #{_summits} en {FormatTime(_elapsedMs)}, intento {_attempt}.");

            _celebrateUntil = now + CelebrationMs;
            _nextFirework = now;

            if (_repeat)
            {
                _restartAt = now + RestartDelayMs;
                return;
            }

            // Sin repetir: el modo termina aquí.
            _active = false;
            RemoveBlip();
            _gpsOffUntil = 0;
            Function.Call(Hash.DISPLAY_RADAR, true);
        }

        /// <summary>Nueva vuelta: de regreso a la salida con el reloj completo.</summary>
        private void StartNewRound()
        {
            _restartAt = 0;
            _attempt++;
            _remainingMs = _limitMs;
            _elapsedMs = 0;
            _holdLeftMs = _holdMs;
            _inZone = false;
            ResetSafePoints(_base, _baseHeading);
            SafeTeleport(_base, _baseHeading, null);
            ShowBig("¡OTRA VEZ A LA CIMA!", Color.Gold, 3000);
        }

        private bool InZone(Vector3 pos)
        {
            float dx = pos.X - _goal.X, dy = pos.Y - _goal.Y;
            return Math.Sqrt(dx * dx + dy * dy) < ZoneRadius
                && pos.Z > _goal.Z - ZoneBelow && pos.Z < _goal.Z + ZoneAbove;
        }

        /// <summary>
        /// Zona de meta estilo checkpoint: anillo brillante en el suelo + columna transparente del
        /// mismo color que "respira". Amarillo afuera, verde cuando está adentro.
        /// </summary>
        private void DrawZone()
        {
            Vector3 player = Player.Position;
            if (player.DistanceTo(_goal) > 400f)
            {
                return; // lejos: no hace falta dibujarla (en el mapa se ve el círculo)
            }

            if (float.IsNaN(_goalGround))
            {
                // Suelo real bajo la meta (la cima es una loma): se calcula una vez ya cargada la zona.
                float g = World.GetGroundHeight(_goal + new Vector3(0f, 0f, 5f));
                _goalGround = g > 0f ? g : _goal.Z - 1f;
            }

            int pulse = (int)(25 * (1 + Math.Sin(GTA.Game.GameTime / 250.0))); // 0-50
            Color baseColor = _inZone ? Color.FromArgb(60, 255, 90) : Color.FromArgb(255, 200, 40);
            float diameter = ZoneRadius * 2f;
            Vector3 ground = new Vector3(_goal.X, _goal.Y, _goalGround);

            // Columna transparente (arranca un poco bajo tierra por la pendiente).
            World.DrawMarker(MarkerType.VerticalCylinder,
                ground - new Vector3(0f, 0f, 1.5f), Vector3.Zero, Vector3.Zero,
                new Vector3(diameter, diameter, 5f),
                Color.FromArgb(55 + pulse, baseColor));

            // Anillo brillante a ras del suelo.
            World.DrawMarker((MarkerType)25, // HorizontalCircleSkinny
                ground + new Vector3(0f, 0f, 0.15f), Vector3.Zero, Vector3.Zero,
                new Vector3(diameter, diameter, 1f),
                Color.FromArgb(200, baseColor));

            // Resplandor del mismo color.
            Ptfx.Light(ground + new Vector3(0f, 0f, 2f), baseColor, ZoneRadius * 2.5f, 3f);
        }

        /// <summary>
        /// Mueve la meta a donde está parado el jugador y la guarda para las próximas sesiones.
        /// </summary>
        public void SetGoalHere()
        {
            Ped p = Player;
            if (p.IsDead)
            {
                throw new ActionException("El jugador está muerto");
            }
            SetGoal(p.Position);
            SaveGoal();
            if (_active)
            {
                RemoveBlip();
                CreateBlips();
            }
            _previewUntil = GTA.Game.GameTime + 15000;
            GTA.UI.Notification.Show("~y~Chiliad~s~: meta marcada aquí ~g~(guardada)");
        }

        /// <summary>Vuelve a la meta por defecto (cima del Monte Chiliad).</summary>
        public void ResetGoal()
        {
            SetGoal(DefaultGoal);
            try { if (File.Exists(_goalFile)) File.Delete(_goalFile); } catch { }
            if (_active)
            {
                RemoveBlip();
                CreateBlips();
            }
        }

        private void SetGoal(Vector3 pos)
        {
            _goal = pos;
            _goalGround = float.NaN;
            _inZone = false;
            _holdLeftMs = _holdMs;
        }

        private void LoadGoal()
        {
            try
            {
                if (!File.Exists(_goalFile))
                {
                    return;
                }
                string[] parts = File.ReadAllText(_goalFile).Trim().Split(';');
                if (parts.Length == 3)
                {
                    var ci = CultureInfo.InvariantCulture;
                    SetGoal(new Vector3(float.Parse(parts[0], ci), float.Parse(parts[1], ci), float.Parse(parts[2], ci)));
                    _log($"Chiliad: meta cargada de {Path.GetFileName(_goalFile)}.");
                }
            }
            catch (Exception ex)
            {
                _log($"Chiliad: no se pudo leer la meta guardada ({ex.Message}); se usa la cima.");
            }
        }

        private void SaveGoal()
        {
            try
            {
                var ci = CultureInfo.InvariantCulture;
                File.WriteAllText(_goalFile, string.Format(ci, "{0:0.00};{1:0.00};{2:0.00}", _goal.X, _goal.Y, _goal.Z));
            }
            catch (Exception ex)
            {
                _log($"Chiliad: no se pudo guardar la meta ({ex.Message}).");
            }
        }

        private void CreateBlips()
        {
            _blip = World.CreateBlip(_goal);
            _blip.Color = BlipColor.Yellow;
            _blip.Name = "Meta del Monte Chiliad";
            _zoneBlip = World.CreateBlip(_goal, ZoneRadius * 3f); // más grande para que se note en el mapa
            _zoneBlip.Color = BlipColor.Yellow;
            _zoneBlip.Alpha = 110;
            RefreshGps();
        }

        private void Celebrate(int now)
        {
            if (now >= _celebrateUntil)
            {
                _celebrateUntil = 0;
                return;
            }
            if (now < _nextFirework)
            {
                return;
            }
            _nextFirework = now + 350 + _rng.Next(300);

            Vector3 center = Player.Position;
            var at = new Vector3(
                center.X + (float)(_rng.NextDouble() * 50 - 25),
                center.Y + (float)(_rng.NextDouble() * 50 - 25),
                center.Z + 25f + (float)(_rng.NextDouble() * 25));
            Ptfx.Burst(FireworksAsset, FireworkEffects[_rng.Next(FireworkEffects.Length)], at, 1.5f);
        }

        // ================================================================ reaparición

        private void SampleSafePoint(Ped p)
        {
            if (p.IsInWater || p.IsRagdoll || p.IsFalling || p.IsInAir)
            {
                return;
            }

            int model = 0;
            Vector3 pos;
            float heading;
            if (p.IsInVehicle())
            {
                Vehicle v = p.CurrentVehicle;
                if (v.IsDead || v.IsInAir || !v.IsOnAllWheels || v.IsInWater)
                {
                    return;
                }
                model = v.Model.Hash;
                pos = v.Position;
                heading = v.Heading;
            }
            else
            {
                pos = p.Position;
                heading = p.Heading;
            }

            // Se guarda el ANTERIOR: si justo después se cae por un barranco, reaparece antes.
            _safePrevious = _safeLatest;
            _safeHeadingPrevious = _safeHeadingLatest;
            _safeModelPrevious = _safeModelLatest;
            _safeLatest = pos;
            _safeHeadingLatest = heading;
            _safeModelLatest = model;
        }

        private void ResetSafePoints(Vector3 pos, float heading)
        {
            _safeLatest = _safePrevious = pos;
            _safeHeadingLatest = _safeHeadingPrevious = heading;
            _safeModelLatest = _safeModelPrevious = 0;
            _nextSafeSample = GTA.Game.GameTime + SafeSampleMs;
        }

        private void RespawnAtSafePoint()
        {
            Vector3 pos = _safePrevious;
            float heading = _safeHeadingPrevious;
            int model = _safeModelPrevious;
            ResetSafePoints(pos, heading);

            Action after = null;
            if (model != 0)
            {
                // Iba manejando: le devolvemos un vehículo del mismo modelo (carga sin bloquear).
                after = () =>
                {
                    var m = new Model(model);
                    m.Request();
                    int frames = 0;
                    _scheduler.Until(() =>
                    {
                        if (!m.IsLoaded && ++frames < 300)
                        {
                            return false;
                        }
                        if (m.IsLoaded && _active)
                        {
                            Vehicle v = World.CreateVehicle(m, pos + new Vector3(0f, 0f, 0.5f), heading);
                            if (v != null)
                            {
                                v.PlaceOnGround();
                                _tracker.Track(v, null, EntityTracker.KindVehicle, GameData.VehicleTagHeight["car"]);
                                Player.SetIntoVehicle(v, VehicleSeat.Driver);
                            }
                        }
                        m.MarkAsNoLongerNeeded();
                        return true;
                    });
                };
            }

            if (Player.IsInVehicle())
            {
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, Player.Handle);
            }
            SafeTeleport(pos, heading, after);
            ShowBig("¡SIGUE SUBIENDO!", Color.White, 2000);
        }

        /// <summary>
        /// Teletransporta al jugador (o a su vehículo) y lo sostiene congelado hasta que el suelo
        /// del destino esté cargado, para que no caiga a través del mapa.
        /// </summary>
        private void SafeTeleport(Vector3 pos, float heading, Action after)
        {
            Ped p = Player;
            Entity target = p.IsInVehicle() ? (Entity)p.CurrentVehicle : p;

            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, pos.X, pos.Y, pos.Z);
            target.Position = pos + new Vector3(0f, 0f, 1f);
            target.Heading = heading;
            target.Velocity = Vector3.Zero;
            target.IsPositionFrozen = true;
            _teleporting = true;

            int frames = 0;
            _scheduler.Until(() =>
            {
                frames++;
                bool exists = target != null && target.Exists();
                if (exists)
                {
                    Function.Call(Hash.REQUEST_COLLISION_AT_COORD, pos.X, pos.Y, pos.Z);
                    bool loaded = Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, target.Handle);
                    if (!loaded && frames < 180)
                    {
                        return false;
                    }

                    // Suelo justo bajo el destino. Se busca desde poco más arriba y solo se acepta
                    // si está cerca: bajo un puente o un segundo piso (aeropuerto) no salta arriba.
                    float ground = World.GetGroundHeight(pos + new Vector3(0f, 0f, 1.5f));
                    if (ground > 0f && Math.Abs(ground - pos.Z) < 4f)
                    {
                        target.Position = new Vector3(pos.X, pos.Y, ground + 1f);
                    }
                    target.IsPositionFrozen = false;
                }

                _teleporting = false;
                _lastTick = GTA.Game.GameTime;
                try
                {
                    after?.Invoke();
                }
                catch (Exception ex)
                {
                    _log($"Chiliad: tras teletransporte: {ex.Message}");
                }
                return true;
            });
        }

        // ================================================================ HUD

        private void DrawHud(int now)
        {
            _panel.Draw();
            _title.Draw();

            if (_timerOn)
            {
                bool low = _remainingMs < 30000;
                bool blinkOff = low && (now / 400) % 2 == 0;
                _clock.Caption = FormatTime(Math.Max(0, _remainingMs));
                _clock.Color = low ? (blinkOff ? Color.DarkRed : Color.Red) : Color.White;
                _clock.Draw();
            }
            else
            {
                _noTimer.Draw(); // sin reloj: el tiempo no cuenta
            }

            float startZ = _base.Z;
            float z = Player.Position.Z;
            float fraction = _goal.Z - startZ > 1f ? (z - startZ) / (_goal.Z - startZ) : 0f;
            fraction = Math.Max(0f, Math.Min(1f, fraction));
            _height.Caption = $"Altura {Math.Max(0, (int)z)} / {(int)_goal.Z} m";
            _height.Draw();

            _barBack.Draw();
            _barFill.Size = new SizeF((PanelW - 24f) * fraction, 8f);
            _barFill.Draw();

            if (_inZone)
            {
                _hold.Caption = $"¡AGUANTA!  {(_holdLeftMs + 999) / 1000}";
                _hold.Draw();
            }

            string footer = $"Intento {_attempt}";
            if (_summits > 0)
            {
                footer += $"  ·  Cimas {_summits}";
            }
            if (_bestMs > 0)
            {
                footer += $"  ·  Récord {FormatTime(_bestMs)}";
            }
            if (_gpsOffUntil != 0)
            {
                footer += $"  ·  ~r~SIN GPS {(_gpsOffUntil - now + 999) / 1000}s";
            }
            _footer.Caption = footer;
            _footer.Draw();

            _switches.Caption = $"GPS {OnOff(_gpsOn && _gpsOffUntil == 0)} · Ruta {OnOff(_routeOn)} · Tiempo {OnOff(_timerOn)} · Reaparecer {OnOff(_respawnOn)}";
            _switches.Draw();
        }

        private void ShowBig(string text, Color color, int ms)
        {
            _bigText = text;
            _bigColor = color;
            _bigUntil = GTA.Game.GameTime + ms;
        }

        private void DrawBig(int now)
        {
            if (_bigText == null)
            {
                return;
            }
            if (now >= _bigUntil)
            {
                _bigText = null;
                return;
            }
            _big.Caption = _bigText;
            _big.Color = _bigColor;
            _big.Draw();
        }

        // ================================================================ utilidades

        /// <summary>
        /// Aplica GPS (minimapa) y ruta según los interruptores y el apagón temporal.
        /// La ruta es un WAYPOINT del juego (la marca del mapa): así el GPS la traza y los taxis
        /// ofrecen "ir al destino marcado" y te dejan en la carretera más cercana a la meta.
        /// </summary>
        private void RefreshGps()
        {
            bool blackout = _gpsOffUntil != 0;
            Function.Call(Hash.DISPLAY_RADAR, _gpsOn && !blackout);
            if (_blip != null && _blip.Exists())
            {
                _blip.ShowRoute = false; // la ruta la dibuja el waypoint
            }

            if (_active && _routeOn && !blackout)
            {
                World.WaypointPosition = _goal;
                _ownWaypoint = true;
                _waypointAtTaxi = false; // si va en taxi, el próximo frame vuelve a apuntar al túnel
            }
            else
            {
                RemoveOwnWaypoint();
            }
        }

        /// <summary>
        /// El juego borra el waypoint al llegar cerca o si el jugador lo quita en el mapa:
        /// mientras la ruta esté en ON, se vuelve a poner (salvo ya en la cima).
        /// </summary>
        private void KeepWaypoint(Vector3 playerPos)
        {
            if (_routeOn && _gpsOffUntil == 0 && !GTA.Game.IsWaypointActive && playerPos.DistanceTo(_goal) > 150f
                && !_waypointAtTaxi)
            {
                World.WaypointPosition = _goal;
                _ownWaypoint = true;
            }
        }

        private void DrawTaxiPrompt()
        {
            _taxiBack.Draw();
            _taxiText.Draw();
        }

        private static bool IsTaxiPassenger(Ped p)
        {
            if (!p.IsInVehicle())
            {
                return false;
            }
            Vehicle v = p.CurrentVehicle;
            return v.Model == new Model(VehicleHash.Taxi) && v.Driver != p;
        }

        /// <summary>
        /// Viaje rápido en taxi: pantalla a negro, el taxi aparece a la salida del túnel, vuelve
        /// la imagen y el jugador se baja. El reloj se pausa durante el salto.
        /// </summary>
        private void TaxiTravel()
        {
            _taxiTraveling = true;
            Function.Call(Hash.DO_SCREEN_FADE_OUT, 700);
            int frames = 0;
            _scheduler.Until(() =>
            {
                if (!Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT) && ++frames < 120)
                {
                    return false;
                }

                if (!_active || !Player.IsInVehicle())
                {
                    Function.Call(Hash.DO_SCREEN_FADE_IN, 700);
                    _taxiTraveling = false;
                    return true;
                }

                ResetSafePoints(_taxiStop, _taxiHeading);
                SafeTeleport(_taxiStop, _taxiHeading, () =>
                {
                    Function.Call(Hash.DO_SCREEN_FADE_IN, 900);
                    _taxiTraveling = false;
                    if (Player.IsInVehicle())
                    {
                        Player.Task.LeaveVehicle();
                    }
                    ShowBig("¡A SUBIR!", Color.Gold, 2000);
                });
                return true;
            });
        }

        /// <summary>Pone la parada del taxi donde está el jugador (mirando hacia donde mira) y la guarda.</summary>
        public void SetTaxiStopHere()
        {
            Ped p = Player;
            Entity e = p.IsInVehicle() ? (Entity)p.CurrentVehicle : p;
            _taxiStop = e.Position;
            _taxiHeading = e.Heading;
            try
            {
                File.WriteAllText(_taxiFile, string.Format(CultureInfo.InvariantCulture,
                    "{0:0.00};{1:0.00};{2:0.00};{3:0.0}", _taxiStop.X, _taxiStop.Y, _taxiStop.Z, _taxiHeading));
            }
            catch (Exception ex)
            {
                _log($"Chiliad: no se pudo guardar la parada del taxi ({ex.Message}).");
            }
            string where = string.Format(CultureInfo.InvariantCulture, "{0:0.0}, {1:0.0}, {2:0.0}", _taxiStop.X, _taxiStop.Y, _taxiStop.Z);
            GTA.UI.Notification.Show($"~y~Chiliad~s~: parada del taxi marcada aquí ~g~(guardada)~s~ · {where}");
            _log($"Chiliad: parada del taxi = {where} (rumbo {_taxiHeading:0}).");
        }

        public void ResetTaxiStop()
        {
            _taxiStop = DefaultTaxiStop;
            _taxiHeading = DefaultTaxiHeading;
            try { if (File.Exists(_taxiFile)) File.Delete(_taxiFile); } catch { }
        }

        private void LoadTaxiStop()
        {
            try
            {
                if (!File.Exists(_taxiFile))
                {
                    return;
                }
                string[] parts = File.ReadAllText(_taxiFile).Trim().Split(';');
                if (parts.Length == 4)
                {
                    var ci = CultureInfo.InvariantCulture;
                    _taxiStop = new Vector3(float.Parse(parts[0], ci), float.Parse(parts[1], ci), float.Parse(parts[2], ci));
                    _taxiHeading = float.Parse(parts[3], ci);
                }
            }
            catch (Exception ex)
            {
                _log($"Chiliad: no se pudo leer la parada del taxi ({ex.Message}); se usa la de por defecto.");
            }
        }

        /// <summary>Pone la salida principal donde está el jugador (mirando hacia donde mira) y la guarda.</summary>
        public void SetStartHere()
        {
            Ped p = Player;
            Entity e = p.IsInVehicle() ? (Entity)p.CurrentVehicle : p;
            _markedStart = e.Position;
            _markedStartHeading = e.Heading;
            try
            {
                File.WriteAllText(_startFile, string.Format(CultureInfo.InvariantCulture,
                    "{0:0.00};{1:0.00};{2:0.00};{3:0.0}", e.Position.X, e.Position.Y, e.Position.Z, e.Heading));
            }
            catch (Exception ex)
            {
                _log($"Chiliad: no se pudo guardar la salida ({ex.Message}).");
            }
            GTA.UI.Notification.Show("~y~Chiliad~s~: salida principal marcada aquí ~g~(guardada)");
        }

        /// <summary>Vuelve a la salida por defecto (entrada del aeropuerto).</summary>
        public void ResetStart()
        {
            _markedStart = null;
            try { if (File.Exists(_startFile)) File.Delete(_startFile); } catch { }
        }

        private void LoadStart()
        {
            try
            {
                if (!File.Exists(_startFile))
                {
                    return;
                }
                string[] parts = File.ReadAllText(_startFile).Trim().Split(';');
                if (parts.Length == 4)
                {
                    var ci = CultureInfo.InvariantCulture;
                    _markedStart = new Vector3(float.Parse(parts[0], ci), float.Parse(parts[1], ci), float.Parse(parts[2], ci));
                    _markedStartHeading = float.Parse(parts[3], ci);
                }
            }
            catch (Exception ex)
            {
                _log($"Chiliad: no se pudo leer la salida guardada ({ex.Message}); se usa el aeropuerto.");
            }
        }

        private void RemoveOwnWaypoint()
        {
            _waypointAtTaxi = false;
            if (_ownWaypoint)
            {
                World.RemoveWaypoint();
                _ownWaypoint = false;
            }
        }

        private void RemoveBlip()
        {
            if (_blip != null && _blip.Exists())
            {
                _blip.ShowRoute = false;
                _blip.Delete();
            }
            _blip = null;
            if (_zoneBlip != null && _zoneBlip.Exists())
            {
                _zoneBlip.Delete();
            }
            _zoneBlip = null;
            RemoveOwnWaypoint();
        }

        /// <summary>Deja todo como antes del modo (también al recargar el script).</summary>
        public void ClearState()
        {
            _active = false;
            _celebrateUntil = 0;
            _restartAt = 0;
            _inZone = false;
            if (_taxiTraveling)
            {
                _taxiTraveling = false;
                try { Function.Call(Hash.DO_SCREEN_FADE_IN, 300); } catch { }
            }
            _bigText = null;
            _wasDown = false;
            _teleporting = false;
            try
            {
                RemoveBlip();
                Function.Call(Hash.DISPLAY_RADAR, true); // por si el GPS quedó apagado
                if (Player.IsPositionFrozen)
                {
                    Player.IsPositionFrozen = false;
                }
                if (Player.IsInVehicle() && Player.CurrentVehicle.IsPositionFrozen)
                {
                    Player.CurrentVehicle.IsPositionFrozen = false;
                }
            }
            catch
            {
                // cerrando el juego
            }
            _gpsOffUntil = 0;
        }

        private static string OnOff(bool on) => on ? "~g~ON~s~" : "~r~OFF~s~";

        private static string FormatTime(int ms)
        {
            int total = ms / 1000;
            return $"{total / 60:00}:{total % 60:00}";
        }

        private static float HeadingTowards(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);
        }

        private static Ped Player => GTA.Game.Player.Character;
    }
}
