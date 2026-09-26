using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Effects;

namespace StreamTok.GtaV.Modes
{
    /// <summary>
    /// Modo Parkour estilo "Only Up!": una torre de objetos del juego que sube desde el suelo
    /// hasta el cielo, generada al azar con SEMILLA (misma semilla = misma torre).
    ///  - Sin checkpoints: si cae, cae hasta donde se agarre y sigue desde ahí.
    ///  - Sin daño por caída: no muere, pierde progreso.
    ///  - Solo existen las piezas cercanas al jugador (se crean y borran al moverse), así la
    ///    torre puede tener cientos de piezas sin cargar el juego.
    /// </summary>
    internal sealed class ParkourMode
    {
        /// <summary>
        /// Lugares abiertos, sin edificios alrededor (en la ciudad la torre se mete entre ellos).
        /// </summary>
        public static readonly string[] Places = { "sandy_shores", "airport", "marked", "here" };

        private static readonly Dictionary<string, Vector3> FixedPlaces = new Dictionary<string, Vector3>
        {
            ["sandy_shores"] = new Vector3(1747.0f, 3273.7f, 41.1f),  // aeródromo del desierto: plano y despejado
            ["airport"] = new Vector3(-1336.6f, -3044.0f, 13.9f),     // pista del aeropuerto de Los Santos
        };

        /// <summary>Piezas: objetos planos y firmes del juego base.</summary>
        private static readonly string[] CandidateModels =
        {
            "prop_container_01a", "prop_container_02a", "prop_container_03a", "prop_container_04a",
            "prop_container_05a", "prop_byard_float_01", "prop_byard_float_02", "prop_woodpile_01a",
            "prop_skip_06a", "prop_boxpile_07d",
        };

        /// <summary>Piso mínimo (m) de ancho y de largo: lo más chico se descarta para tener espacio al saltar.</summary>
        private const float MinPieceSize = 2.2f;

        private string[] PieceModels = new string[0];

        private const string GoalModel = "prop_container_01a";
        private const float SpawnRadius = 90f;
        private const float GoalRadius = 3.5f;
        private const string FireworksAsset = "scr_indep_fireworks";

        private readonly Random _fx = new Random();
        private readonly Action<string> _log;
        private readonly Entities.EntityTracker _tracker;
        private bool _freeZone;
        private int _nextClean;
        private readonly string _placeFile;
        private readonly string _baseDirectory;
        private Vector3? _marked;

        private readonly List<Piece> _pieces = new List<Piece>();
        private int _goalIndex = -1;
        private string _course = "random";
        private readonly Dictionary<string, Vector3[]> _dims = new Dictionary<string, Vector3[]>();
        private bool _active;
        private Vector3 _start;
        private int _seed;
        private int _startTime;
        private int _nextStream;
        private float _best;         // altura máxima alcanzada (sobre la salida)
        private float _groundedAt;   // altura del último apoyo
        private int _falls;
        private int _highestPiece = -1;
        private int _superJumpUntil;
        private int _safeUntil;      // invencible un momento tras caer
        private int _celebrateUntil;
        private int _nextFirework;
        private string _bigText;
        private Color _bigColor;
        private int _bigUntil;

        private readonly GTA.UI.ContainerElement _panel = new GTA.UI.ContainerElement(
            new PointF(1000f, 190f), new SizeF(265f, 110f), Color.FromArgb(170, 0, 0, 0));
        private readonly GTA.UI.TextElement _text = new GTA.UI.TextElement(
            "", PointF.Empty, 0.32f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.TextElement _big = new GTA.UI.TextElement(
            "", new PointF(640f, 220f), 1.2f, Color.Gold, GTA.UI.Font.Pricedown, GTA.UI.Alignment.Center, true, true);

        public ParkourMode(Entities.EntityTracker tracker, Action<string> log, string baseDirectory)
        {
            _tracker = tracker;
            _log = log;
            _placeFile = Path.Combine(baseDirectory, "StreamTok.ParkourPlace.txt");
            _baseDirectory = baseDirectory;
            try
            {
                string[] p = File.Exists(_placeFile) ? File.ReadAllText(_placeFile).Trim().Split(';') : new string[0];
                if (p.Length == 3)
                {
                    var ci = CultureInfo.InvariantCulture;
                    _marked = new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci));
                }
            }
            catch (Exception ex)
            {
                _log($"Parkour: no se pudo leer el lugar guardado ({ex.Message}).");
            }
        }

        /// <summary>Activo o con la torre todavía armada (tras llegar arriba, "terminar" la quita).</summary>
        public bool IsActive => _active || _pieces.Count > 0;

        // ================================================================ acciones

        /// <param name="seed">0 = semilla al azar (se muestra para poder repetir la torre).</param>
        public void Start(int seed, int height, string place, string course = "random")
        {
            Ped player = GTA.Game.Player.Character;
            if (player.IsDead) throw new ActionException("El jugador está muerto");
            if (place == "marked" && !_marked.HasValue) throw new ActionException("No hay lugar marcado: usa \"Marcar parkour aquí\"");

            Stop(quiet: true);
            _course = course;
            if (course != "random")
            {
                LoadMenyoo(course); // su propio lugar de salida y su meta (lo más alto del mapa)
            }
            else
            {
            _start = place == "here" ? player.Position : place == "marked" ? _marked.Value : FixedPlaces[place];
            _seed = seed > 0 ? seed : new Random().Next(1, 99999);

            // Solo piezas grandes que existan en esta versión del juego.
            var usable = new List<string>();
            foreach (string m in CandidateModels)
            {
                if (!new Model(m).IsInCdImage) continue;
                LoadModel(m);
                Vector3[] d = Dims(m);
                if (d[1].X - d[0].X >= MinPieceSize && d[1].Y - d[0].Y >= MinPieceSize) usable.Add(m);
            }
            if (usable.Count == 0) usable.Add(GoalModel);
            PieceModels = usable.ToArray();
            LoadModel(GoalModel);
            Generate(new Random(_seed), height);
            _goalIndex = _pieces.Count - 1;
            }

            if (player.IsInVehicle()) Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, player.Handle);
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, _start.X, _start.Y, _start.Z);
            player.Position = _start + new Vector3(0f, 0f, 1f);

            _active = true;
            SetFreeZone(true);
            _startTime = GTA.Game.GameTime;
            _best = 0f;
            _groundedAt = 0f;
            _falls = 0;
            _highestPiece = -1;
            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, FireworksAsset);
            ShowBig("¡SOLO HACIA ARRIBA!", Color.Gold, 3000);
            GTA.UI.Notification.Show(course == "random"
                ? $"~b~Parkour~s~ · semilla ~y~{_seed}~s~ · {_pieces.Count} piezas · {height} m"
                : $"~b~Parkour~s~ · mapa ~y~{course}~s~ · {_pieces.Count} piezas");
            _log($"Parkour: semilla {_seed}, {_pieces.Count} piezas, {height} m, lugar {place}.");
        }

        public void Stop(bool quiet = false)
        {
            if (!_active && _pieces.Count == 0 && !quiet) throw new ActionException("El parkour no está activo");
            _active = false;
            foreach (Piece p in _pieces) Despawn(p);
            _pieces.Clear();
            _goalIndex = -1;
            _kitChecked = false;
            _kitCheckAt = 0;
            if (_freeZone) SetFreeZone(false);
            try { GTA.Game.Player.IsInvincible = false; } catch { }
        }

        public void SetPlaceHere()
        {
            Vector3 pos = GTA.Game.Player.Character.Position;
            _marked = pos;
            try
            {
                File.WriteAllText(_placeFile, string.Format(CultureInfo.InvariantCulture, "{0:0.00};{1:0.00};{2:0.00}", pos.X, pos.Y, pos.Z));
            }
            catch (Exception ex)
            {
                _log($"Parkour: no se pudo guardar el lugar ({ex.Message}).");
            }
            GTA.UI.Notification.Show("~b~Parkour~s~: lugar marcado aquí ~g~(guardado)~s~ · elige \"marked\"");
        }

        /// <summary>Viento: empujón hacia un costado (a veces fatal).</summary>
        public void Wind(int strength)
        {
            RequireActive();
            Ped p = GTA.Game.Player.Character;
            double a = _fx.NextDouble() * Math.PI * 2;
            Function.Call(Hash.APPLY_FORCE_TO_ENTITY, p, 1, (float)Math.Cos(a) * strength, (float)Math.Sin(a) * strength, 2f,
                0f, 0f, 0f, 0, false, true, true, false, true);
            ShowBig("¡VIENTO!", Color.LightSkyBlue, 1200);
        }

        public void Ragdoll()
        {
            RequireActive();
            Function.Call(Hash.SET_PED_TO_RAGDOLL, GTA.Game.Player.Character, 1500, 1500, 0, false, false, false);
        }

        /// <summary>La pieza donde está parado desaparece unos segundos.</summary>
        public void RemoveFloor(int seconds)
        {
            RequireActive();
            Piece under = PieceUnder(GTA.Game.Player.Character.Position);
            if (under == null) throw new ActionException("No está parado sobre una pieza");
            under.RemovedUntil = GTA.Game.GameTime + seconds * 1000;
            Despawn(under);
            ShowBig("¡EL PISO DESAPARECIÓ!", Color.OrangeRed, 1500);
        }

        public void SuperJump(int seconds)
        {
            RequireActive();
            _superJumpUntil = Math.Max(_superJumpUntil, GTA.Game.GameTime) + seconds * 1000;
            ShowBig("¡SÚPER SALTO!", Color.LimeGreen, 1500);
        }

        /// <summary>Ayuda: vuelve a la pieza más alta que alcanzó.</summary>
        public void ToHighest()
        {
            RequireActive();
            if (_highestPiece < 0) throw new ActionException("Todavía no subió a ninguna pieza");
            Piece p = _pieces[_highestPiece];
            Spawn(p);
            GTA.Game.Player.Character.Position = new Vector3(p.Pos.X, p.Pos.Y, p.Top + 1f);
            ShowBig("¡DE VUELTA ARRIBA!", Color.LimeGreen, 1500);
        }

        public void BackToStart()
        {
            RequireActive();
            GTA.Game.Player.Character.Position = _start + new Vector3(0f, 0f, 1f);
            ShowBig("¡AL INICIO!", Color.OrangeRed, 2000);
        }

        private void RequireActive()
        {
            if (!_active) throw new ActionException("El parkour no está activo");
        }

        // ================================================================ mapas de Menyoo

        /// <summary>Carpeta donde el streamer pone mapas de parkour de Menyoo (.xml).</summary>
        private string MapsFolder => Path.Combine(_baseDirectory, "StreamTok.Parkour");

        /// <summary>"random" + los .xml de la carpeta de mapas (sin extensión). Se lee al cargar el mod.</summary>
        public string[] Courses
        {
            get
            {
                var list = new List<string> { "random" };
                try
                {
                    if (Directory.Exists(MapsFolder))
                    {
                        list.AddRange(Directory.GetFiles(MapsFolder, "*.xml").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n));
                    }
                }
                catch (Exception ex)
                {
                    _log($"Parkour: no se pudo leer la carpeta de mapas ({ex.Message}).");
                }
                return list.ToArray();
            }
        }

        /// <summary>
        /// Lee un mapa de Menyoo (SpoonerPlacements): objetos (Type 3) y vehículos (Type 2) con su
        /// posición y giro exactos. La salida es ReferenceCoords y la meta, la pieza más alta.
        /// </summary>
        /// <summary>
        /// Equivalentes del juego para objetos de mods de props usados en mapas conocidos.
        /// misc_stairsp1a y misc_tower1stair (escaleras metálicas del mapa Only Up) → escalera del puerto.
        /// </summary>
        private static readonly Dictionary<string, string> Substitutes = new Dictionary<string, string>
        {
            // (las escaleras misc_stairsp1a / misc_tower1stair se arman con BuildStairKit)
        };

        private static float F(XmlNode n, string tag) =>
            float.Parse(n.SelectSingleNode(tag)?.InnerText ?? "0", CultureInfo.InvariantCulture);

        private void LoadMenyoo(string name)
        {
            string path = Path.Combine(MapsFolder, name + ".xml");
            if (!File.Exists(path)) throw new ActionException($"No existe el mapa {name}.xml en scripts\\StreamTok.Parkour");

            var doc = new XmlDocument();
            doc.Load(path);

            XmlNode reference = doc.SelectSingleNode("/SpoonerPlacements/ReferenceCoords");
            int missing = 0;
            foreach (XmlNode pl in doc.SelectNodes("/SpoonerPlacements/Placement"))
            {
                string type = pl.SelectSingleNode("Type")?.InnerText;
                if (type != "3" && type != "2") continue; // sin peds
                string hash = (pl.SelectSingleNode("ModelHash")?.InnerText ?? "").Replace("0x", "").Replace("0X", "");
                XmlNode pr = pl.SelectSingleNode("PositionRotation");
                if (pr == null || hash.Length == 0) continue;

                string key = "#" + hash.ToLowerInvariant();
                Model m = ToModel(key);
                if (!m.IsInCdImage && Substitutes.TryGetValue(key, out string sub) && new Model(sub).IsInCdImage)
                {
                    key = sub; // objeto de un mod de props → su equivalente del juego, en el mismo lugar
                    m = ToModel(key);
                    LoadModel(key);
                }
                if (!m.IsInCdImage) missing++;
                var pos = new Vector3(F(pr, "X"), F(pr, "Y"), F(pr, "Z"));
                Vector3[] d = m.IsInCdImage ? Dims(key) : new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 1f) };
                _pieces.Add(new Piece
                {
                    Model = key,
                    Pos = pos,
                    Rot = new Vector3(F(pr, "Pitch"), F(pr, "Roll"), F(pr, "Yaw")),
                    Heading = F(pr, "Yaw"),
                    Top = pos.Z + d[1].Z,
                    FromMap = true,
                    Vehicle = type == "2",
                    Missing = !m.IsInCdImage,
                });
            }
            if (_pieces.Count == 0) throw new ActionException($"El mapa {name} no tiene objetos");
            // Los reemplazos armados (escaleras) no se parecían al mapa original: desactivados.
            // Para el mapa completo hay que instalar el mod de props que usa (ver aviso).

            _start = reference != null ? new Vector3(F(reference, "X"), F(reference, "Y"), F(reference, "Z")) : _pieces[0].Pos;
            _goalIndex = 0;
            for (int i = 1; i < _pieces.Count; i++)
            {
                if (!_pieces[i].Missing && _pieces[i].Top > _pieces[_goalIndex].Top) _goalIndex = i;
            }
            _seed = 0;
            if (missing > 0)
            {
                GTA.UI.Notification.Show($"~b~Parkour~s~: faltan ~o~{missing} objetos~s~ del mapa. Instala el mod de props que pide el mapa (ej. Custom Props Add-On).");
            }
            _log($"Parkour: mapa {name}: {_pieces.Count} piezas, {missing} sin modelo.");
        }

        // ================================================================ generación

        /// <summary>
        /// Recorrido en zigzag: cada pieza a un salto de la anterior (1-2 m de hueco entre bordes,
        /// hasta 1 m más alta), con la parte de arriba de cada objeto como piso.
        /// </summary>
        private void Generate(Random rng, int height)
        {
            _pieces.Clear();
            float ground = World.GetGroundHeight(_start + new Vector3(0f, 0f, 3f));
            float top = (ground > 0f ? ground : _start.Z) + 0.8f;
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            Vector3 center = _start;
            Piece prev = null;
            float goalTop = top + height;

            while (top < goalTop && _pieces.Count < 1500)
            {
                bool goal = top + 0.6f >= goalTop;
                string model = goal ? GoalModel : PieceModels[rng.Next(PieceModels.Length)];
                float heading = (float)(rng.NextDouble() * 360);

                if (prev == null)
                {
                    // Primera pieza pegada a la salida: se sube desde el suelo.
                    center = _start + Dir(angle) * (Extent(model, heading, angle) + 1.2f);
                }
                else
                {
                    // Busca hacia dónde seguir SIN chocar con piezas anteriores de altura parecida:
                    // así nunca queda una pieza encima tapando el camino y siempre hay dónde pisar.
                    float nextTop = top + 0.2f + (float)rng.NextDouble() * 0.3f;
                    bool found = false;
                    for (int attempt = 0; attempt < 16 && !found; attempt++)
                    {
                        float spread = (float)Math.PI / 4 * (1 + attempt / 4);
                        float tryAngle = angle + (float)((rng.NextDouble() * 2 - 1) * spread);
                        float step = Extent(prev.Model, prev.Heading, tryAngle) + Extent(model, heading, tryAngle) - 0.4f;
                        Vector3 tryCenter = new Vector3(prev.Pos.X, prev.Pos.Y, 0f) + Dir(tryAngle) * step;

                        Vector3 off = new Vector3(tryCenter.X - _start.X, tryCenter.Y - _start.Y, 0f);
                        if (off.Length() > 45f && attempt < 12) continue; // que no se aleje demasiado

                        if (IsClear(tryCenter, Radius(model), nextTop))
                        {
                            angle = tryAngle;
                            center = tryCenter;
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        // Sin lugar libre: sale hacia afuera, lejos de la columna.
                        Vector3 off = new Vector3(prev.Pos.X - _start.X, prev.Pos.Y - _start.Y, 0f);
                        angle = off.Length() > 0.1f ? (float)Math.Atan2(off.Y, off.X) : angle;
                        float step = Extent(prev.Model, prev.Heading, angle) + Extent(model, heading, angle) - 0.4f;
                        center = new Vector3(prev.Pos.X, prev.Pos.Y, 0f) + Dir(angle) * step;
                    }
                    top = nextTop;
                }

                Vector3[] d = Dims(model);
                var piece = new Piece
                {
                    Model = model,
                    Top = top,
                    Pos = new Vector3(center.X, center.Y, top - d[1].Z), // su techo queda en "top"
                    Heading = heading,
                    Goal = goal,
                };
                _pieces.Add(piece);
                prev = piece;

            }
        }

        /// <summary>Radio que ocupa la pieza en planta (su medio lado más largo).</summary>
        private float Radius(string model)
        {
            Vector3[] d = Dims(model);
            return Math.Max(d[1].X - d[0].X, d[1].Y - d[0].Y) / 2f;
        }

        /// <summary>
        /// true si una pieza en <paramref name="center"/> con techo en <paramref name="top"/> no choca con
        /// las anteriores (salvo la última, a la que se pega): deja 3,5 m libres encima y debajo.
        /// </summary>
        private bool IsClear(Vector3 center, float radius, float top)
        {
            // Las piezas van de menor a mayor altura: se revisan desde la última hacia abajo
            // y se corta al llegar a las que ya quedan muy por debajo (rápido aunque sean cientos).
            for (int i = _pieces.Count - 2; i >= 0; i--)
            {
                Piece p = _pieces[i];
                if (top - p.Top > 9f) break;
                float dz = Math.Abs(p.Top - top);
                Vector3[] d = _dims[p.Model];
                float thickness = d[1].Z - d[0].Z;
                if (dz > thickness + 3.5f) continue; // muy arriba o muy abajo: hay espacio para pasar
                float dx = center.X - p.Pos.X, dy = center.Y - p.Pos.Y;
                float minDist = radius + Radius(p.Model) + 0.5f;
                if (dx * dx + dy * dy < minDist * minDist) return false;
            }
            return true;
        }

        private static Vector3 Dir(float angle) => new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f);

        /// <summary>Medio largo de la pieza en la dirección <paramref name="angle"/>, según su giro.</summary>
        private float Extent(string model, float headingDeg, float angle)
        {
            Vector3[] d = Dims(model);
            float hx = (d[1].X - d[0].X) / 2f, hy = (d[1].Y - d[0].Y) / 2f;
            double rel = angle - headingDeg * Math.PI / 180.0;
            // En GTA el eje Y del objeto apunta a su frente (heading); el X, a su derecha.
            return (float)(Math.Abs(Math.Sin(rel)) * hy + Math.Abs(Math.Cos(rel)) * hx);
        }

        /// <summary>Clave de modelo: nombre ("prop_container_01a") o hash de un mapa ("#33b317f5").</summary>
        private static Model ToModel(string key) =>
            key.StartsWith("#") ? new Model(unchecked((int)uint.Parse(key.Substring(1), NumberStyles.HexNumber))) : new Model(key);

        private Vector3[] Dims(string model)
        {
            if (_dims.TryGetValue(model, out Vector3[] d)) return d;
            var min = new OutputArgument();
            var max = new OutputArgument();
            Function.Call(Hash.GET_MODEL_DIMENSIONS, ToModel(model).Hash, min, max);
            d = new[] { min.GetResult<Vector3>(), max.GetResult<Vector3>() };
            if (d[1].X - d[0].X < 0.3f) d = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 1f) }; // por si falla
            _dims[model] = d;
            return d;
        }

        private static void LoadModel(string name)
        {
            var m = ToModel(name);
            if (m.IsInCdImage) m.Request(2000);
        }

        // ================================================================ cada frame

        public void Update()
        {
            int now = GTA.Game.GameTime;
            if (_celebrateUntil != 0) Celebrate(now);
            if (!_active)
            {
                DrawBig(now);
                return;
            }

            Ped p = GTA.Game.Player.Character;
            KeepFreeZone(now);
            if (now >= _nextStream)
            {
                _nextStream = now + 400;
                StreamPieces(p.Position, now);
                CheckKitOrientation(now);
            }

            if (now < _superJumpUntil) Function.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, GTA.Game.Player.Handle);

            // Sin daño por caída: invencible mientras cae y un momento después de aterrizar.
            bool falling = p.IsFalling || p.IsRagdoll || p.IsInAir;
            if (falling) _safeUntil = Math.Max(_safeUntil, now + 800);
            GTA.Game.Player.IsInvincible = now < _safeUntil;

            float h = p.Position.Z - _start.Z;
            if (!falling && !p.IsDead)
            {
                if (_groundedAt - h > 8f)
                {
                    _falls++;
                    ShowBig($"¡CAÍSTE {(int)(_groundedAt - h)} m!", Color.OrangeRed, 2000);
                }
                _groundedAt = h;
                Piece under = PieceUnder(p.Position);
                if (under != null)
                {
                    int idx = _pieces.IndexOf(under);
                    if (idx > _highestPiece) _highestPiece = idx;
                }
                if (_goalIndex >= 0)
                {
                    Piece g = _pieces[_goalIndex];
                    float gx = p.Position.X - g.Pos.X, gy = p.Position.Y - g.Pos.Y, gz = p.Position.Z - g.Top;
                    if (gx * gx + gy * gy < 16f && gz > -1f && gz < 3f) Win(now);
                }
            }
            if (h > _best) _best = h;

            DrawGoal();
            DrawHud(now, h);
            DrawBig(now);
        }

        /// <summary>Crea las piezas cercanas y borra las lejanas.</summary>
        private void StreamPieces(Vector3 at, int now)
        {
            foreach (Piece piece in _pieces)
            {
                // Los mapas cargados (pocas piezas) se ven completos desde cualquier lado, como en Menyoo;
                // la torre al azar (cientos de piezas) solo crea las cercanas.
                bool near = piece.FromMap || piece.Pos.DistanceTo(at) < SpawnRadius;
                bool removed = piece.RemovedUntil > now;
                if (near && !removed) Spawn(piece);
                else if (piece.Prop != null && (!near || removed)) Despawn(piece);
            }
        }

        private void Spawn(Piece p)
        {
            if (p.Prop != null && p.Prop.Exists()) return;
            if (p.Missing) return;
            Model m = ToModel(p.Model);
            if (!m.IsInCdImage)
            {
                p.Missing = true; // objeto de un mod que no está instalado
                _log($"Parkour: falta el modelo {p.Model} en ({p.Pos.X:0.0}, {p.Pos.Y:0.0}, {p.Pos.Z:0.0}).");
                return;
            }
            if (!m.IsLoaded) { m.Request(); return; }        // se crea en la próxima pasada

            if (p.Vehicle)
            {
                Vehicle v = World.CreateVehicle(m, p.Pos, p.Rot.Z);
                if (v != null)
                {
                    v.IsInvincible = true;
                    v.IsEngineRunning = false;
                    Function.Call(Hash.SET_VEHICLE_DOORS_LOCKED, v.Handle, 2);
                }
                p.Prop = v;
            }
            else
            {
                p.Prop = World.CreateProp(m, p.Pos, new Vector3(0f, 0f, p.Heading), false, false);
            }
            if (p.Prop == null)
            {
                if (++p.Fails == 5)
                {
                    _log($"Parkour: el juego no pudo crear {p.Model} en ({p.Pos.X:0.0}, {p.Pos.Y:0.0}, {p.Pos.Z:0.0}).");
                    GTA.UI.Notification.Show("~b~Parkour~s~: ~o~una pieza no se pudo crear~s~ (ver StreamTok.GtaV.log)");
                }
                return;
            }

            if (p.FromMap)
            {
                // Posición y giro exactos del mapa (Menyoo: X = pitch, Y = roll, Z = yaw).
                Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, p.Prop.Handle, p.Pos.X, p.Pos.Y, p.Pos.Z, false, false, false);
                Function.Call(Hash.SET_ENTITY_ROTATION, p.Prop.Handle, p.Rot.X, p.Rot.Y, p.Rot.Z, 2, true);
            }
            p.Prop.IsPositionFrozen = true;
            if (p.FromMap)
            {
                Function.Call(Hash.SET_ENTITY_LOD_DIST, p.Prop.Handle, 16960); // visible de lejos (igual que Menyoo)
            }
        }

        private static void Despawn(Piece p)
        {
            if (p.Prop != null && p.Prop.Exists()) p.Prop.Delete();
            p.Prop = null;
        }

        /// <summary>Pieza sobre la que está parado (hasta 2,5 m de su techo, dentro de su ancho).</summary>
        private Piece PieceUnder(Vector3 pos)
        {
            Piece best = null;
            float bestD = 99f;
            foreach (Piece piece in _pieces)
            {
                if (piece.Missing) continue;
                float dz = pos.Z - piece.Top;
                if (dz < -0.5f || dz > 2.5f) continue;
                float dx = pos.X - piece.Pos.X, dy = pos.Y - piece.Pos.Y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                Vector3[] dims = _dims[piece.Model];
                float half = Math.Max(dims[1].X - dims[0].X, dims[1].Y - dims[0].Y) / 2f + 0.5f;
                if (d < half && d < bestD) { best = piece; bestD = d; }
            }
            return best;
        }

        // ================================================================ campo libre

        /// <summary>Sin policía, estrellas, tráfico ni peatones mientras dura el parkour (como en la Pelea).</summary>
        private void SetFreeZone(bool on)
        {
            _freeZone = on;
            try
            {
                Player player = GTA.Game.Player;
                Function.Call(Hash.SET_MAX_WANTED_LEVEL, on ? 0 : 5);
                if (on)
                {
                    Function.Call(Hash.CLEAR_PLAYER_WANTED_LEVEL, player.Handle);
                    player.WantedLevel = 0;
                }
                Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, player.Handle, on);
                Function.Call(Hash.SET_DISPATCH_COPS_FOR_PLAYER, player.Handle, !on);
            }
            catch
            {
                // cerrando el juego
            }
        }

        private void KeepFreeZone(int now)
        {
            GTA.Game.Player.WantedLevel = 0;
            Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f, 0f);
            Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);

            if (now < _nextClean) return;
            _nextClean = now + 2000;

            // Limpia la base de la torre: gente, policías y autos del juego (lo del mod se respeta).
            Ped player = GTA.Game.Player.Character;
            var basePoint = new Vector3(_start.X, _start.Y, player.Position.Z);
            foreach (Ped ped in World.GetNearbyPeds(basePoint, 70f))
            {
                if (ped != player && !_tracker.IsTracked(ped)) Entities.EntityTracker.SafeDelete(ped);
            }
            foreach (Vehicle v in World.GetNearbyVehicles(basePoint, 70f))
            {
                if (!_tracker.IsTracked(v) && !(player.IsInVehicle() && player.CurrentVehicle == v)) Entities.EntityTracker.SafeDelete(v);
            }
        }

        private void Win(int now)
        {
            int secs = (now - _startTime) / 1000;
            ShowBig($"¡LLEGASTE! {secs / 60:00}:{secs % 60:00}", Color.Gold, 8000);
            GTA.UI.Notification.Show($"~b~Parkour~s~ completado · {secs / 60:00}:{secs % 60:00} · {_falls} caídas · semilla {_seed}");
            _log($"Parkour: completado en {secs} s, {_falls} caídas, semilla {_seed}.");
            _celebrateUntil = now + 8000;
            _nextFirework = now;
            _active = false; // la torre se queda hasta "terminar" o hasta iniciar otra
            GTA.Game.Player.IsInvincible = false;
        }

        private void Celebrate(int now)
        {
            if (now >= _celebrateUntil) { _celebrateUntil = 0; return; }
            if (now < _nextFirework) return;
            _nextFirework = now + 400;
            Vector3 c = GTA.Game.Player.Character.Position;
            Ptfx.Burst(FireworksAsset, "scr_indep_firework_starburst",
                c + new Vector3((float)(_fx.NextDouble() * 40 - 20), (float)(_fx.NextDouble() * 40 - 20), 20f), 1.5f);
        }

        // ================================================================ dibujo

        // ================================================================ escaleras de reemplazo

        /// <summary>
        /// Los objetos de un mapa que no existen en este juego (vienen de mods de props, ej. las
        /// escaleras "misc_stairsp1a" del mapa Only Up) se reemplazan por una ESCALERA normal de
        /// escalones planos (0,35 m de alto): tramos rectos de ida y vuelta con descanso, desde donde
        /// estaba el objeto hasta la pieza siguiente. Así el mapa se completa sin instalar esos mods.
        /// </summary>
        private void AddJumpPads()
        {
            string step = PickStepModel();
            if (step == null) return;
            LoadModel(step);
            Vector3[] d = Dims(step);
            float sx = d[1].X - d[0].X, sy = d[1].Y - d[0].Y;
            float width = Math.Max(sx, sy);          // ancho del escalón (de costado)
            float depth = Math.Min(sx, sy);          // huella (hacia adelante)
            float run = Math.Max(0.4f, depth - 0.15f);
            const float Rise = 0.35f;
            const int PerFlight = 10;

            int count = _pieces.Count;
            int stairs = 0;
            for (int i = 0; i < count; i++)
            {
                Piece missing = _pieces[i];
                if (!missing.Missing) continue;
                int next = NextPieceAbove(i, count);
                if (next < 0) continue;
                Piece target = _pieces[next];

                // Escaleras del mapa (misc_stairsp1a, misc_tower1stair): escalera de verdad con descanso.
                if ((missing.Model == "#33b317f5" || missing.Model == "#d390a944") && BuildStairKit(missing, target))
                {
                    stairs++;
                    continue;
                }

                // Dirección de subida: desde donde estaba el objeto hacia la pieza siguiente.
                Vector3 dir = new Vector3(target.Pos.X - missing.Pos.X, target.Pos.Y - missing.Pos.Y, 0f);
                if (dir.Length() < 0.1f) dir = new Vector3(0f, 1f, 0f);
                dir.Normalize();
                var side = new Vector3(-dir.Y, dir.X, 0f);

                // Se arma de arriba hacia abajo: el último escalón queda pegado a la pieza siguiente.
                float top = target.Top - 0.05f;
                float bottom = missing.Pos.Z + 0.1f;
                Vector3 pos = new Vector3(target.Pos.X, target.Pos.Y, 0f) - dir * (Radius(target.Model) + depth / 2f - 0.2f);
                Vector3 goingDown = -dir;
                int inFlight = 0;

                for (int k = 0; top - k * Rise >= bottom && k < 120; k++)
                {
                    float stepTop = top - k * Rise;
                    float heading = (float)(Math.Atan2(-goingDown.X, goingDown.Y) * 180.0 / Math.PI) + (sx > sy ? 0f : 90f);
                    _pieces.Add(new Piece
                    {
                        Model = step,
                        Top = stepTop,
                        Pos = new Vector3(pos.X, pos.Y, stepTop - d[1].Z),
                        Heading = heading,
                    });

                    if (++inFlight >= PerFlight)
                    {
                        // Descanso: el tramo siguiente va al costado y en sentido contrario.
                        inFlight = 0;
                        pos += side * (width + 0.2f);
                        side = -side;
                        goingDown = -goingDown;
                    }
                    else
                    {
                        pos += goingDown * run;
                    }
                }
                stairs++;
            }
            if (stairs > 0)
            {
                GTA.UI.Notification.Show($"~b~Parkour~s~: {stairs} objetos que faltan se reemplazaron por ~g~escaleras~s~");
                _log($"Parkour: {stairs} escaleras ({step}) en lugar de objetos que faltan.");
            }
        }

        // --- Escalera con descanso (como la del mapa original), con escaleras de embarque del aeropuerto.

        private const float KitGap = 0.3f;
        private string _kitModel;
        private bool _kitChecked;          // ya se verificó hacia dónde sube el modelo
        private int _kitCheckAt;

        /// <summary>
        /// Tramos de escalera de ida y vuelta con descansos, armados de arriba hacia abajo: el último
        /// tramo llega a la altura de la pieza siguiente, pegado a ella; el primero arranca en el suelo.
        /// Se asume que el modelo sube hacia su frente (+Y); si no, se corrige al verlo en el juego.
        /// </summary>
        private bool BuildStairKit(Piece missing, Piece target)
        {
            if (_kitModel == null)
            {
                foreach (string m in new[] { "prop_air_stair_01", "prop_air_stair_02", "prop_air_stair_03" })
                {
                    if (!new Model(m).IsInCdImage) continue;
                    LoadModel(m);
                    Vector3[] dm = Dims(m);
                    float hm = dm[1].Z - dm[0].Z;
                    if (hm >= 2f && hm <= 9f) { _kitModel = m; break; }
                }
                if (_kitModel == null) return false;
            }
            string landing = PickStepModel() ?? "prop_byard_float_02";
            LoadModel(landing);

            Vector3[] d = Dims(_kitModel);
            float height = d[1].Z - d[0].Z;
            float length = d[1].Y - d[0].Y;
            float width = d[1].X - d[0].X;
            Vector3[] ld = Dims(landing);

            Vector3 dir = new Vector3(target.Pos.X - missing.Pos.X, target.Pos.Y - missing.Pos.Y, 0f);
            if (dir.Length() < 0.1f) dir = new Vector3(0f, 1f, 0f);
            dir.Normalize();
            var side = new Vector3(-dir.Y, dir.X, 0f);

            float topZ = target.Top;
            Vector3 topEnd = new Vector3(target.Pos.X, target.Pos.Y, 0f) - dir * (Radius(target.Model) + 0.2f);
            Vector3 up = dir;          // hacia dónde se sube en este tramo
            float ground = missing.Pos.Z;

            for (int flight = 0; flight < 8; flight++)
            {
                Vector3 center = topEnd - up * (length / 2f);
                float heading = (float)(Math.Atan2(-up.X, up.Y) * 180.0 / Math.PI); // frente (+Y) hacia arriba
                _pieces.Add(new Piece
                {
                    Model = _kitModel,
                    Top = topZ,
                    Pos = new Vector3(center.X, center.Y, topZ - d[1].Z),
                    Heading = heading,
                    Kit = true,
                });

                Vector3 bottomEnd = topEnd - up * length;
                float bottomZ = topZ - height;
                if (bottomZ <= ground + 0.3f) break; // llegó al suelo

                // Descanso: plataforma al pie de este tramo, que da paso al tramo de abajo (al costado).
                Vector3 nextTop = bottomEnd + side * (width + KitGap);
                Vector3 landingCenter = (bottomEnd + nextTop) / 2f - up * 0.6f;
                _pieces.Add(new Piece
                {
                    Model = landing,
                    Top = bottomZ,
                    Pos = new Vector3(landingCenter.X, landingCenter.Y, bottomZ - ld[1].Z),
                    Heading = heading,
                });

                topEnd = nextTop;
                topZ = bottomZ;
                up = -up;      // el tramo de abajo sube en sentido contrario
                side = -side;
            }
            return true;
        }

        /// <summary>
        /// Una vez creado el primer tramo, mira con un rayo hacia dónde sube de verdad el modelo. Si sube
        /// hacia atrás, gira 180° todos los tramos (quedan en el mismo lugar, bien orientados).
        /// </summary>
        private void CheckKitOrientation(int now)
        {
            if (_kitChecked || _kitModel == null || now < _kitCheckAt) return;
            Piece kit = _pieces.FirstOrDefault(x => x.Kit && x.Prop != null && x.Prop.Exists());
            if (kit == null) return;
            if (_kitCheckAt == 0) { _kitCheckAt = now + 500; return; } // que cargue su colisión

            Vector3[] d = Dims(_kitModel);
            float reach = (d[1].Y - d[0].Y) * 0.4f;
            Vector3 fwd = kit.Prop.ForwardVector;
            float front = HitHeight(kit.Prop, kit.Prop.Position + fwd * reach);
            float back = HitHeight(kit.Prop, kit.Prop.Position - fwd * reach);
            if (float.IsNaN(front) || float.IsNaN(back)) { _kitCheckAt = now + 500; return; }

            _kitChecked = true;
            if (front < back)
            {
                foreach (Piece x in _pieces.Where(x => x.Kit))
                {
                    x.Heading += 180f;
                    Despawn(x); // se vuelve a crear girado en la próxima pasada
                }
                _log("Parkour: la escalera subía al revés; se giró 180°.");
            }
        }

        /// <summary>Altura de la superficie de la entidad bajo un punto (NaN si el rayo no la toca).</summary>
        private static float HitHeight(Entity e, Vector3 at)
        {
            RaycastResult hit = World.Raycast(at + new Vector3(0f, 0f, 12f), at - new Vector3(0f, 0f, 12f), IntersectFlags.Everything, GTA.Game.Player.Character);
            return hit.DidHit && hit.HitEntity == e ? hit.HitPosition.Z : float.NaN;
        }

        /// <summary>Escalón: una tabla o plataforma plana y delgada (el más delgado que exista).</summary>
        private string PickStepModel()
        {
            string best = null;
            float bestThickness = float.MaxValue;
            foreach (string m in new[] { "prop_byard_float_02", "prop_byard_float_01", "prop_pallet_02a", "prop_pallet_03a", "prop_pallet_01a", "prop_boxpile_07d" })
            {
                if (!new Model(m).IsInCdImage) continue;
                LoadModel(m);
                Vector3[] d = Dims(m);
                float w = d[1].X - d[0].X, l = d[1].Y - d[0].Y, h = d[1].Z - d[0].Z;
                if (Math.Min(w, l) < 0.8f) continue;
                if (h < bestThickness) { bestThickness = h; best = m; }
            }
            return best;
        }

        /// <summary>La pieza existente más cercana que está más arriba (hasta 30 m al costado y 25 m arriba).</summary>
        private int NextPieceAbove(int from, int count)
        {
            Piece missing = _pieces[from];
            int next = -1;
            float bestScore = float.MaxValue;
            for (int j = 0; j < count; j++)
            {
                Piece c = _pieces[j];
                if (c.Missing || j == from) continue;
                float dz = c.Top - missing.Pos.Z;
                if (dz < 1f || dz > 25f) continue;
                float dx = c.Pos.X - missing.Pos.X, dy = c.Pos.Y - missing.Pos.Y;
                float h = (float)Math.Sqrt(dx * dx + dy * dy);
                if (h > 30f) continue;
                float score = h + dz * 0.3f;
                if (score < bestScore) { bestScore = score; next = j; }
            }
            return next;
        }

        private void DrawGoal()
        {
            Piece g = _goalIndex >= 0 && _goalIndex < _pieces.Count ? _pieces[_goalIndex] : null;
            if (g == null) return;
            var at = new Vector3(g.Pos.X, g.Pos.Y, g.Top);
            World.DrawMarker(MarkerType.VerticalCylinder, at, Vector3.Zero, Vector3.Zero,
                new Vector3(GoalRadius * 2f, GoalRadius * 2f, 4f), Color.FromArgb(70, 255, 200, 40));
            Ptfx.Light(at + new Vector3(0f, 0f, 2f), Color.Gold, 12f, 6f);
        }

        private void DrawHud(int now, float h)
        {
            _panel.Draw();
            float goal = _goalIndex >= 0 ? _pieces[_goalIndex].Top - _start.Z : 1f;
            int secs = (now - _startTime) / 1000;
            Line(0, _course == "random" ? "PARKOUR  ·  semilla " + _seed : "PARKOUR  ·  " + _course, Color.DeepSkyBlue);
            Line(1, $"Altura {Math.Max(0, (int)h)} / {(int)goal} m", Color.White);
            Line(2, $"Récord {(int)_best} m  ·  Caídas {_falls}", Color.White);
            Line(3, $"Tiempo {secs / 60:00}:{secs % 60:00}" + (now < _superJumpUntil ? "  ·  ~g~SÚPER SALTO" : ""), Color.LightGray);
        }

        private void Line(int i, string text, Color color)
        {
            _text.Caption = text;
            _text.Color = color;
            _text.Position = new PointF(1132f, 196f + i * 24f);
            _text.Draw();
        }

        private void ShowBig(string text, Color color, int ms)
        {
            _bigText = text;
            _bigColor = color;
            _bigUntil = GTA.Game.GameTime + ms;
        }

        private void DrawBig(int now)
        {
            if (_bigText == null || now >= _bigUntil) { _bigText = null; return; }
            _big.Caption = _bigText;
            _big.Color = _bigColor;
            _big.Draw();
        }

        private sealed class Piece
        {
            public string Model;
            public Vector3 Pos;
            public float Heading;
            public float Top;
            public bool Goal;
            public Entity Prop;
            public bool FromMap;     // viene de un mapa de Menyoo: posición y giro exactos
            public bool Vehicle;     // en los mapas, algunos "pisos" son vehículos congelados
            public Vector3 Rot;
            public bool Missing;     // su modelo no existe en este juego (mod de props no instalado)
            public int Fails;
            public bool Kit;         // tramo de la escalera armada (se puede girar 180° al verificarla)
            public int RemovedUntil;
        }
    }
}
