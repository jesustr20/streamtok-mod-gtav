using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Characters;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Modes
{
    /// <summary>
    /// Modo "Pelea de viewers": todos contra todos en una arena cerrada; el jugador también pelea.
    ///  - Cada viewer tiene UN luchador (su nombre es la clave). Todos empiezan IGUAL: poca vida,
    ///    sin armas y sin poderes, sea cual sea el personaje (Goku incluido).
    ///  - Las donaciones suman vida (y curan) según una tabla por monedas.
    ///  - Las ARMAS que recibe se quedan (también al revivir). Los PODERES son temporales
    ///    (X segundos) y su potencia depende de la vida del luchador.
    ///  - Ronda continua: se cuentan kills; al acabar el tiempo gana quien tenga más.
    /// </summary>
    internal sealed class ArenaMode
    {
        public const string KindFighter = "arena";
        public const string PlayerName = "TÚ";

        /// <summary>Poderes temporales que se pueden activar ("random" = uno al azar).</summary>
        public static readonly string[] Powers = { "random", "ki", "fly", "strength", "speed", "dodge" };

        private static readonly Dictionary<string, string[]> PowerAbilities = new Dictionary<string, string[]>
        {
            ["ki"] = new[] { "energy_blast", "aura" },      // ráfagas de ki y Kamehameha
            ["fly"] = new[] { "flight", "aura" },           // vuela y ataca desde el aire
            ["strength"] = new[] { "super_strength", "aura" }, // cada golpe lanza lejos
            ["speed"] = new[] { "speed", "aura" },          // súper velocidad
            ["dodge"] = new[] { "dodge", "aura" },          // esquiva el daño
        };

        private static readonly Dictionary<string, string> PowerNames = new Dictionary<string, string>
        {
            ["ki"] = "Ki y Kamehameha", ["fly"] = "Vuelo", ["strength"] = "Súper fuerza", ["speed"] = "Súper velocidad", ["dodge"] = "Esquivar",
        };

        /// <summary>Armas que se pueden dar ("random" = una al azar). Se quedan para siempre.</summary>
        public static string[] WeaponChoices => new[] { "random" }.Concat(GameData.Weapons.Keys).ToArray();

        /// <summary>
        /// Tabla de vida por donación: desde X monedas, cada moneda da Y de vida. Las donaciones
        /// grandes rinden más por moneda. Se puede cambiar en el .ini ([Arena] HealthTiers).
        /// </summary>
        public const string DefaultHealthTiers = "1:20,10:25,100:30,500:40,1000:50";

        /// <summary>Lugares de la arena: fijos, "marked" (guardado con "Marcar arena aquí") o "here" (donde esté el jugador).</summary>
        public static readonly string[] Places = { "airport", "sandy_shores", "marked", "here" };

        private static readonly Dictionary<string, Vector3> FixedPlaces = new Dictionary<string, Vector3>
        {
            ["airport"] = new Vector3(-1336.6f, -3044.0f, 13.9f),     // pista del aeropuerto: plano y abierto
            ["sandy_shores"] = new Vector3(1747.0f, 3273.7f, 41.1f),  // aeródromo de Sandy Shores
        };

        /// <summary>Vida con la que empieza todo luchador (igual para todos).</summary>
        public const int StartHealth = 100;
        private const int MaxFighterHealth = 200000;
        private const int MaxFighters = 25;
        private const float SearchRadius = 120f;
        private const int WinnerShowMs = 8000;
        private const string FireworksAsset = "scr_indep_fireworks";

        /// <summary>Luchadores base (modelos del juego) para cuando no hay personajes custom o se pide "random".</summary>
        private static readonly (string Id, string Name, string Model)[] BuiltIn =
        {
            ("brawler", "Peleador", "a_m_y_musclbeac_01"),
            ("biker", "Motociclista", "g_m_y_lost_01"),
            ("soldier", "Soldado", "s_m_y_blackops_01"),
            ("gangster", "Pandillero", "g_m_y_ballasout_01"),
            ("clown", "Payaso", "s_m_y_clown_01"),
            ("boxer", "Boxeadora", "a_f_y_fitness_01"),
        };

        private static readonly Color[] AuraColors =
        {
            Color.Gold, Color.DeepSkyBlue, Color.Red, Color.LimeGreen, Color.Magenta, Color.Orange, Color.White,
        };

        private readonly EntityTracker _tracker;
        private readonly CharacterManager _characters;
        private readonly Random _rng;
        private readonly Action<string> _log;
        private readonly List<CharacterDef> _pool = new List<CharacterDef>();
        private readonly Dictionary<string, Fighter> _fighters = new Dictionary<string, Fighter>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<int, int>> _healthTiers = new List<KeyValuePair<int, int>>(); // monedas desde → vida por moneda
        private int _nextPowerCheck;

        private bool _active;
        private int _roundMs;
        private int _roundEnds;
        private bool _repeat;
        private int _round;
        private int _playerKills;
        private bool _playerWasDead;
        private bool _playerIn;                 // el jugador está peleando (si muere, queda fuera)
        private readonly HashSet<string> _roundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // quién peleó en la ronda
        private int _stopAt;                    // sin repetir: la arena se quita al terminar la celebración
        private readonly List<string> _winners = new List<string>(); // últimos ganadores (el más reciente primero)
        private int _winnerUntil;
        private int _nextRoundAt;
        private int _nextTasking;
        private string _winnerText;
        private RelationshipGroup _group;
        private bool _groupReady;

        // --- arena cerrada
        private Vector3 _center;
        private float _radius;
        private float _centerGround = float.NaN;
        private Blip _areaBlip;
        private readonly string _placeFile;
        private Vector3? _markedPlace;
        private int _nextWallCheck;
        private readonly FrameScheduler _scheduler;

        // --- HUD (1280x720): tabla a la derecha, debajo del panel del Chiliad
        private const float BoardX = 1000f, BoardY = 350f, BoardW = 265f, RowH = 20f;
        private readonly GTA.UI.ContainerElement _boardBack = new GTA.UI.ContainerElement(
            new PointF(BoardX, BoardY), new SizeF(BoardW, 0f), Color.FromArgb(170, 0, 0, 0));
        private readonly GTA.UI.ContainerElement _winnersBack = new GTA.UI.ContainerElement(
            PointF.Empty, SizeF.Empty, Color.FromArgb(190, 40, 30, 0));
        private readonly GTA.UI.TextElement _boardText = new GTA.UI.TextElement(
            "", PointF.Empty, 0.3f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Left, true, true);
        private readonly GTA.UI.TextElement _big = new GTA.UI.TextElement(
            "", new PointF(640f, 200f), 1.2f, Color.Gold, GTA.UI.Font.Pricedown, GTA.UI.Alignment.Center, true, true);

        public ArenaMode(EntityTracker tracker, CharacterManager characters, IReadOnlyList<CharacterDef> customCharacters,
            FrameScheduler scheduler, Random rng, Action<string> log, string baseDirectory, string healthTiers)
        {
            ParseTiers(healthTiers);
            _scheduler = scheduler;
            _placeFile = Path.Combine(baseDirectory, "StreamTok.ArenaPlace.txt");
            LoadPlace();
            _tracker = tracker;
            _characters = characters;
            _rng = rng;
            _log = log;

            foreach (CharacterDef c in customCharacters)
            {
                _pool.Add(c);
            }
            foreach (var b in BuiltIn)
            {
                _pool.Add(new CharacterDef { Id = b.Id, Name = b.Name, Model = b.Model, Health = StartHealth, Weapon = "none" });
            }
        }

        /// <summary>Ids elegibles al unirse ("random" + personajes custom + luchadores base).</summary>
        public string[] CharacterIds => new[] { "random" }.Concat(_pool.Select(c => c.Id)).ToArray();

        public bool IsActive => _active;

        // ================================================================ acciones

        public void Start(int minutes, bool repeat, string place, int radius, bool playerFights)
        {
            Ped player = GTA.Game.Player.Character;
            if (player.IsDead)
            {
                throw new ActionException("El jugador está muerto: espera a que reaparezca");
            }
            if (place == "marked" && !_markedPlace.HasValue)
            {
                throw new ActionException("No hay arena marcada: usa \"Marcar arena aquí\" primero");
            }

            Stop(quiet: true);
            _center = place == "here" ? player.Position
                : place == "marked" ? _markedPlace.Value
                : FixedPlaces[place];
            _radius = radius;
            _centerGround = float.NaN;
            if (place != "here")
            {
                Teleport(player, _center);
            }
            _areaBlip = World.CreateBlip(_center, _radius);
            _areaBlip.Color = BlipColor.Orange;
            _areaBlip.Alpha = 110;

            _active = true;
            SetFreeZone(true);
            _roundMs = minutes * 60000;              // 0 = sin límite: gana el último en pie
            _repeat = repeat;
            _round = 1;
            _roundEnds = _roundMs > 0 ? GTA.Game.GameTime + _roundMs : int.MaxValue;
            _playerKills = 0;
            _roundNames.Clear();
            _winners.Clear();
            _stopAt = 0;
            EnsureGroup();
            SetPlayerFighting(playerFights, teleport: false);
            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, FireworksAsset);
            ShowBig("¡PELEA DE VIEWERS!", Color.Gold, 3000);
            GTA.UI.Notification.Show(minutes > 0
                ? $"~o~Pelea de viewers~s~ activa · {minutes} min por ronda · ¡únanse!"
                : "~o~Pelea de viewers~s~ activa · gana el último en pie · ¡únanse!");
            _log($"Pelea: inicio ({minutes} min, repetir {repeat}, lugar {place}, radio {radius} m).");
        }

        public void Stop(bool quiet = false)
        {
            if (!_active && !quiet)
            {
                throw new ActionException("La pelea de viewers no está activa");
            }
            _active = false;
            _winnerUntil = 0;
            _nextRoundAt = 0;
            _winnerText = null;
            foreach (Fighter f in _fighters.Values)
            {
                if (f.Ped != null && f.Ped.Exists())
                {
                    EntityTracker.SafeDelete(f.Ped);
                }
            }
            _fighters.Clear();
            _tracker.RemoveKind(KindFighter);
            if (_areaBlip != null && _areaBlip.Exists())
            {
                _areaBlip.Delete();
            }
            _areaBlip = null;
            _stopAt = 0;
            _roundNames.Clear();
            if (_playerIn)
            {
                _playerIn = false;
                SetPlayerRelationship(false);
            }
            if (_freeZone)
            {
                SetFreeZone(false);
            }
            if (!quiet)
            {
                _log("Pelea: terminada.");
            }
        }

        /// <summary>
        /// Un viewer entra a la pelea con el personaje que elija. Todos empiezan igual: poca vida,
        /// sin armas y sin poderes. Si ya está vivo, las monedas se le suman como donación; si
        /// murió, vuelve a entrar con el mismo personaje (y sus armas).
        /// </summary>
        public void Join(string name, string characterId, int coins)
        {
            RequireActive();
            RequireRoundOpen();
            name = RequireName(name);

            if (_fighters.TryGetValue(name, out Fighter existing))
            {
                if (IsAlive(existing))
                {
                    if (coins > 0)
                    {
                        Boost(name, coins);
                    }
                    return;
                }
                existing.MaxHp = StartHealth + HealthFor(coins); // revive: vuelve a empezar con poca vida
                SpawnFighter(existing);
                return;
            }

            if (_fighters.Values.Count(IsAlive) >= MaxFighters)
            {
                throw new ActionException($"La pelea está llena ({MaxFighters} luchadores)");
            }

            CharacterDef baseDef = characterId == "random" || !_pool.Exists(c => c.Id == characterId)
                ? _pool[_rng.Next(_pool.Count)]
                : _pool.First(c => c.Id == characterId);

            var f = new Fighter
            {
                Name = name,
                Base = baseDef,
                MaxHp = StartHealth + HealthFor(coins),
                Aura = baseDef.Abilities.Contains("aura") ? baseDef.AuraColor : AuraColors[_rng.Next(AuraColors.Length)],
            };
            _fighters[name] = f;
            SpawnFighter(f);
        }

        /// <summary>Donación: más vida según la tabla por monedas (y lo cura en esa cantidad).</summary>
        public void Boost(string name, int coins)
        {
            RequireActive();
            RequireRoundOpen();
            name = RequireName(name);

            if (!_fighters.TryGetValue(name, out Fighter f))
            {
                Join(name, "random", coins); // donó sin estar: entra con personaje al azar
                return;
            }

            int extra = HealthFor(coins);
            if (!IsAlive(f))
            {
                f.MaxHp = StartHealth + extra;
                SpawnFighter(f); // la donación lo revive
                return;
            }

            f.MaxHp = Math.Min(MaxFighterHealth, f.MaxHp + extra);
            f.Ped.MaxHealth = f.MaxHp + 100;
            f.Ped.Health = Math.Min(f.Ped.MaxHealth, f.Ped.Health + extra);
            _characters.Upgrade(f.Ped, BuildDef(f)); // la potencia de sus poderes sube con la vida
            GTA.UI.Notification.Show($"~o~{f.Name}~s~ +{FormatHp(extra)} de vida · ~g~{FormatHp(f.MaxHp)}");
        }

        /// <summary>Le da un arma al luchador. Las armas se quedan (también cuando revive).</summary>
        public void GiveWeapon(string name, string weapon)
        {
            Fighter f = RequireFighter(name);
            string key = weapon == "random" ? GameData.Weapons.Keys.ElementAt(_rng.Next(GameData.Weapons.Count)) : weapon;
            if (!GameData.Weapons.TryGetValue(key, out WeaponHash hash))
            {
                throw new ActionException($"Arma desconocida: {weapon}");
            }
            if (!f.Weapons.Contains(hash))
            {
                f.Weapons.Add(hash);
            }
            if (IsAlive(f))
            {
                f.Ped.Weapons.Give(hash, 9999, true, true);
                Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, f.Ped, true);
            }
            GTA.UI.Notification.Show($"~o~{f.Name}~s~ recibe arma: ~y~{key}");
        }

        /// <summary>
        /// Poder temporal: dura unos segundos y su potencia depende de la vida del luchador.
        /// Si ya lo tenía activo, se le suma el tiempo.
        /// </summary>
        public void GivePower(string name, string power, int seconds)
        {
            Fighter f = RequireFighter(name);
            if (!IsAlive(f))
            {
                throw new ActionException($"{f.Name} está caído: primero tiene que volver a entrar");
            }

            string key = power == "random" ? PowerAbilities.Keys.ElementAt(_rng.Next(PowerAbilities.Count)) : power;
            if (!PowerAbilities.ContainsKey(key))
            {
                throw new ActionException($"Poder desconocido: {power}");
            }

            int now = GTA.Game.GameTime;
            f.Powers.TryGetValue(key, out int until);
            f.Powers[key] = Math.Max(until, now) + seconds * 1000;
            _characters.Upgrade(f.Ped, BuildDef(f));
            GTA.UI.Notification.Show($"~o~{f.Name}~s~ activa ~y~{PowerNames[key]}~s~ por {seconds} s");
        }

        /// <summary>Luchadores de prueba ("Bot 1", "Bot 2"…) con personaje al azar; empiezan igual que todos.</summary>
        public void AddBots(int count)
        {
            RequireActive();
            int n = 1;
            for (int i = 0; i < count; i++)
            {
                while (_fighters.ContainsKey($"Bot {n}")) n++;
                Join($"Bot {n}", "random", 0); // como cualquier viewer: 100 de vida, sin armas ni poderes
            }
        }

        private Fighter RequireFighter(string name)
        {
            RequireActive();
            name = RequireName(name);
            if (!_fighters.TryGetValue(name, out Fighter f))
            {
                throw new ActionException($"{name} no está en la pelea: primero tiene que unirse");
            }
            return f;
        }

        private void RequireRoundOpen()
        {
            if (_nextRoundAt != 0 || _stopAt != 0)
            {
                throw new ActionException("La ronda terminó: espera la próxima");
            }
        }

        private void RequireActive()
        {
            if (!_active)
            {
                throw new ActionException("La pelea de viewers no está activa");
            }
        }

        private static string RequireName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ActionException("Falta el nombre del viewer (nameTag)");
            }
            return name.Trim();
        }

        // ================================================================ vida por monedas

        /// <summary>Vida que dan N monedas: cada moneda vale según el tramo de la donación.</summary>
        public int HealthFor(int coins)
        {
            if (coins <= 0)
            {
                return 0;
            }
            int perCoin = _healthTiers.Count > 0 ? _healthTiers[0].Value : 20;
            foreach (var tier in _healthTiers)
            {
                if (coins >= tier.Key) perCoin = tier.Value;
            }
            return (int)Math.Min(MaxFighterHealth, (long)coins * perCoin);
        }

        /// <summary>Lee "1:20,10:25,100:30" (desde monedas : vida por moneda). Si está mal, usa el default.</summary>
        private void ParseTiers(string text)
        {
            foreach (string source in new[] { text, DefaultHealthTiers })
            {
                _healthTiers.Clear();
                try
                {
                    foreach (string part in (source ?? "").Split(','))
                    {
                        string[] kv = part.Split(':');
                        if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out int from) && int.TryParse(kv[1].Trim(), out int hp) && from > 0 && hp > 0)
                        {
                            _healthTiers.Add(new KeyValuePair<int, int>(from, hp));
                        }
                    }
                }
                catch
                {
                    _healthTiers.Clear();
                }
                if (_healthTiers.Count > 0)
                {
                    _healthTiers.Sort((a, b) => a.Key.CompareTo(b.Key));
                    return;
                }
            }
        }

        // ================================================================ luchadores

        private void SpawnFighter(Fighter f)
        {
            if (_tracker.ClampPeds(1) < 1)
            {
                throw new ActionException("Límite de personajes alcanzado");
            }

            f.Powers.Clear(); // los poderes no sobreviven a la muerte; las armas sí
            _roundNames.Add(f.Name);
            CharacterDef def = BuildDef(f);
            Ped ped = Spawner.SpawnPed(def.Model, InsideArena(0.75f));
            f.Ped = ped;
            f.WasAlive = true;
            f.Target = null;

            ped.RelationshipGroup = _group;
            ped.AlwaysKeepTask = true;
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 46, true); // pelear siempre
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 5, true);  // pelea sin armas contra armados

            Blip blip = ped.AddBlip();
            blip.Scale = 0.6f;
            blip.Color = BlipColor.Orange;
            blip.Name = f.Name;

            _characters.Setup(ped, def, true, f.Name, PickTarget);
            foreach (WeaponHash w in f.Weapons)
            {
                ped.Weapons.Give(w, 9999, true, true);
            }
            if (f.Weapons.Count > 0)
            {
                Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, ped, true);
            }
            _tracker.Track(ped, f.Name, KindFighter);
        }

        /// <summary>
        /// Solo el MODELO sale del personaje: sin poderes propios. Vida = la del luchador; poderes =
        /// los temporales activos, con potencia según la vida.
        /// </summary>
        private CharacterDef BuildDef(Fighter f)
        {
            var def = new CharacterDef
            {
                Id = f.Base.Id,
                Name = f.Base.Name,
                Model = f.Base.Model,
                Health = f.MaxHp,
                Weapon = "none",
                AuraColor = f.Aura,
                EnergyColor = f.Base.EnergyColor ?? f.Aura,
                Image = f.Base.Image,
                PowerScale = PowerScaleFor(f.MaxHp),
            };

            int now = GTA.Game.GameTime;
            foreach (var p in f.Powers)
            {
                if (p.Value > now)
                {
                    foreach (string a in PowerAbilities[p.Key]) def.Abilities.Add(a);
                }
            }
            return def;
        }

        /// <summary>Potencia de los poderes según la vida: 100 → x1 · 1 000 → x1,5 · 10 000 → x2 · 100 000 → x2,5.</summary>
        private static float PowerScaleFor(int hp) =>
            1f + 0.5f * (float)Math.Max(0.0, Math.Log10(Math.Max(1, hp) / (double)StartHealth));

        /// <summary>Quita los poderes que ya vencieron.</summary>
        private void ExpirePowers(int now)
        {
            if (now < _nextPowerCheck)
            {
                return;
            }
            _nextPowerCheck = now + 500;

            foreach (Fighter f in _fighters.Values)
            {
                if (f.Powers.Count == 0) continue;
                var expired = f.Powers.Where(p => p.Value <= now).Select(p => p.Key).ToList();
                if (expired.Count == 0) continue;
                foreach (string k in expired) f.Powers.Remove(k);
                if (IsAlive(f))
                {
                    _characters.Upgrade(f.Ped, BuildDef(f));
                }
            }
        }

        private static bool IsAlive(Fighter f) => f.Ped != null && f.Ped.Exists() && !f.Ped.IsDead;

        /// <summary>Objetivo de un luchador: el rival vivo más cercano (otro luchador o el jugador).</summary>
        private Ped PickTarget(Ped self)
        {
            Ped best = null;
            float bestDistance = SearchRadius;
            Vector3 from = self.Position;

            foreach (Fighter f in _fighters.Values)
            {
                if (!IsAlive(f) || f.Ped == self) continue;
                float d = f.Ped.Position.DistanceTo(from);
                if (d < bestDistance) { best = f.Ped; bestDistance = d; }
            }

            Ped player = GTA.Game.Player.Character;
            if (_playerIn && !player.IsDead)
            {
                float d = player.Position.DistanceTo(from);
                if (d < bestDistance) best = player;
            }
            return best;
        }

        private void EnsureGroup()
        {
            if (_groupReady)
            {
                return;
            }
            _group = World.AddRelationshipGroup("STREAMTOK_ARENA");
            // Todos contra todos: el grupo se odia a sí mismo y odia al jugador (y viceversa).
            Function.Call(Hash.SET_RELATIONSHIP_BETWEEN_GROUPS, 5, _group.Hash, _group.Hash);
            _group.SetRelationshipBetweenGroups(GTA.Game.Player.Character.RelationshipGroup, Relationship.Hate, true);
            _groupReady = true;
        }

        // ================================================================ cada frame

        public void Update()
        {
            int now = GTA.Game.GameTime;

            if (_winnerUntil != 0)
            {
                Celebrate(now);
            }

            if (!_active)
            {
                DrawBig();
                return;
            }

            if (_stopAt != 0 && now >= _stopAt)
            {
                Stop(quiet: true); // sin repetir: la arena se quita después de celebrar
                _log("Pelea: terminada tras el ganador.");
                return;
            }

            if (_nextRoundAt != 0 && now >= _nextRoundAt)
            {
                StartNextRound(now);
            }

            TrackDeaths();
            ExpirePowers(now);
            KeepFreeZone(now);
            DrawWall();
            KeepInside(now);

            // Cada ~1 s: cada luchador pelea contra su rival más cercano.
            if (now >= _nextTasking)
            {
                _nextTasking = now + 1000;
                foreach (Fighter f in _fighters.Values)
                {
                    if (!IsAlive(f)) continue;
                    Ped target = PickTarget(f.Ped);
                    if (target != null && target != f.Target)
                    {
                        f.Target = target;
                        f.Ped.Task.FightAgainst(target);
                    }
                }
            }

            if (_nextRoundAt == 0 && _stopAt == 0)
            {
                CheckRoundEnd(now);
            }

            DrawBoard(now);
            DrawBig();
        }

        /// <summary>Detecta muertes (de luchadores y del jugador) y le suma la kill a quien mató.</summary>
        private void TrackDeaths()
        {
            foreach (Fighter f in _fighters.Values)
            {
                if (!f.WasAlive || f.Ped == null)
                {
                    continue;
                }
                if (f.Ped.Exists() && !f.Ped.IsDead)
                {
                    continue;
                }

                f.WasAlive = false;
                f.Deaths++;
                if (f.Ped.Exists())
                {
                    CreditKill(Function.Call<int>(Hash.GET_PED_SOURCE_OF_DEATH, f.Ped), f.Name);
                }
            }

            Ped player = GTA.Game.Player.Character;
            if (player.IsDead && !_playerWasDead && _playerIn)
            {
                // El jugador cae: queda FUERA de la pelea (reaparece libre en el hospital) y la
                // pelea sigue sin él. Puede volver con "Yo peleo" cuando quiera.
                CreditKill(Function.Call<int>(Hash.GET_PED_SOURCE_OF_DEATH, player), PlayerName);
                _playerIn = false;
                SetPlayerRelationship(false);
                GTA.UI.Notification.Show("~o~Pelea~s~: quedaste fuera. La pelea sigue; vuelve con ~y~\"Yo peleo\"~s~ si quieres.");
            }
            _playerWasDead = player.IsDead;
        }

        private void CreditKill(int killerHandle, string victim)
        {
            if (killerHandle == 0)
            {
                return;
            }

            if (killerHandle == GTA.Game.Player.Character.Handle)
            {
                _playerKills++;
                GTA.UI.Notification.Show($"~g~{PlayerName}~s~ eliminó a ~o~{victim}~s~");
                return;
            }

            foreach (Fighter f in _fighters.Values)
            {
                if (f.Ped != null && f.Ped.Exists() && f.Ped.Handle == killerHandle)
                {
                    f.Kills++;
                    GTA.UI.Notification.Show($"~o~{f.Name}~s~ eliminó a ~o~{victim}~s~");
                    return;
                }
            }
        }

        /// <summary>
        /// La ronda termina cuando queda UNO en pie (luchadores + el jugador si está peleando),
        /// habiendo peleado al menos dos. Si hay límite de tiempo y se acaba, gana el de más kills.
        /// </summary>
        private void CheckRoundEnd(int now)
        {
            var alive = _fighters.Values.Where(IsAlive).Select(f => f.Name).ToList();
            if (_playerIn && !GTA.Game.Player.Character.IsDead)
            {
                alive.Add(PlayerName);
            }
            int participants = _roundNames.Count;

            if (participants >= 2 && alive.Count == 1)
            {
                string name = alive[0];
                int kills = name == PlayerName ? _playerKills : _fighters[name].Kills;
                RememberWinner(name, kills);
                EndRound(now, name == PlayerName ? $"¡GANASTE!  ·  {kills} kills" : $"GANADOR: {name}  ·  {kills} kills");
                return;
            }
            if (participants >= 2 && alive.Count == 0)
            {
                EndRound(now, "¡NADIE QUEDÓ EN PIE!");
                return;
            }

            if (now >= _roundEnds)
            {
                string winner = PlayerName;
                int best = _playerKills;
                foreach (Fighter f in _fighters.Values)
                {
                    if (f.Kills > best) { best = f.Kills; winner = f.Name; }
                }
                if (best > 0) RememberWinner(winner, best);
                EndRound(now, best > 0 ? (winner == PlayerName ? $"¡GANASTE!  ·  {best} kills" : $"GANADOR: {winner}  ·  {best} kills") : "RONDA SIN KILLS");
            }
        }

        private void RememberWinner(string name, int kills)
        {
            _winners.Insert(0, $"R{_round}  ·  {Short(name)}  ·  {kills} K");
            if (_winners.Count > 3) _winners.RemoveAt(3);
        }

        private void EndRound(int now, string text)
        {
            _winnerText = text;
            ShowBig(_winnerText, Color.Gold, WinnerShowMs);
            GTA.UI.Notification.Show($"~o~Pelea de viewers~s~ · ronda {_round}: ~y~{_winnerText}");
            _log($"Pelea: ronda {_round} → {_winnerText}.");
            _winnerUntil = now + WinnerShowMs;
            _nextFirework = now;

            // Los luchadores dejan de pelear mientras se celebra.
            foreach (Fighter f in _fighters.Values)
            {
                if (IsAlive(f)) f.Ped.Task.ClearAll();
            }

            if (_repeat)
            {
                _nextRoundAt = now + WinnerShowMs;
            }
            else
            {
                _stopAt = now + WinnerShowMs;
            }
            _roundEnds = int.MaxValue;
        }

        /// <summary>Nueva ronda en la misma arena: se borran los luchadores y todos se vuelven a unir.</summary>
        private void StartNextRound(int now)
        {
            _nextRoundAt = 0;
            _round++;
            _roundEnds = _roundMs > 0 ? now + _roundMs : int.MaxValue;
            _playerKills = 0;
            _roundNames.Clear();
            foreach (Fighter f in _fighters.Values)
            {
                if (f.Ped != null && f.Ped.Exists()) EntityTracker.SafeDelete(f.Ped);
            }
            _fighters.Clear();
            _tracker.RemoveKind(KindFighter);
            if (_playerIn)
            {
                _roundNames.Add(PlayerName);
            }
            ShowBig($"¡RONDA {_round}! ¡Únanse!", Color.Gold, 3000);
        }

        // ================================================================ el jugador

        /// <summary>
        /// "Yo peleo": ON = el jugador entra a la arena (lo lleva adentro) y todos pueden atacarlo;
        /// OFF = queda libre: nadie lo ataca, la pared no lo retiene y la pelea sigue sin él.
        /// </summary>
        public void SetPlayerFighting(bool on, bool teleport = true)
        {
            RequireActive();
            Ped player = GTA.Game.Player.Character;
            if (on && player.IsDead)
            {
                throw new ActionException("Estás muerto: espera a reaparecer");
            }
            _playerIn = on;
            SetPlayerRelationship(on);
            if (on)
            {
                _roundNames.Add(PlayerName);
                if (teleport && Horizontal(player.Position, _center) > _radius - 2f)
                {
                    Teleport(player, InsideArena(0.5f));
                }
                ShowBig("¡A PELEAR!", Color.Orange, 1500);
            }
            foreach (Fighter f in _fighters.Values) f.Target = null; // que recalculen rival
        }

        public bool PlayerFighting => _active && _playerIn;

        private void SetPlayerRelationship(bool hate)
        {
            if (!_groupReady)
            {
                return;
            }
            try
            {
                _group.SetRelationshipBetweenGroups(GTA.Game.Player.Character.RelationshipGroup,
                    hate ? Relationship.Hate : Relationship.Neutral, true);
                if (!hate)
                {
                    foreach (Fighter f in _fighters.Values)
                    {
                        if (IsAlive(f) && f.Target == GTA.Game.Player.Character) f.Ped.Task.ClearAll();
                    }
                }
            }
            catch
            {
                // cerrando el juego
            }
        }

        private int _nextFirework;

        private void Celebrate(int now)
        {
            if (now >= _winnerUntil)
            {
                _winnerUntil = 0;
                return;
            }
            if (now < _nextFirework)
            {
                return;
            }
            _nextFirework = now + 400 + _rng.Next(300);
            Vector3 c = GTA.Game.Player.Character.Position;
            var at = new Vector3(c.X + (float)(_rng.NextDouble() * 50 - 25), c.Y + (float)(_rng.NextDouble() * 50 - 25), c.Z + 25f + (float)(_rng.NextDouble() * 20));
            Ptfx.Burst(FireworksAsset, "scr_indep_firework_starburst", at, 1.5f);
        }

        // ================================================================ campo libre

        private bool _freeZone;
        private int _nextClean;

        /// <summary>
        /// Mientras dura la pelea: sin policía ni estrellas (ni por la zona restringida del
        /// aeropuerto), y sin tráfico ni peatones que se metan. Al terminar, todo vuelve a la normalidad.
        /// </summary>
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
            // Cada frame: sin estrellas y sin gente ni autos nuevos alrededor.
            GTA.Game.Player.WantedLevel = 0;
            Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f, 0f);
            Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);

            if (now < _nextClean)
            {
                return;
            }
            _nextClean = now + 2000;

            // Limpia la arena (y un margen): policías, peatones y vehículos del juego. Lo que creó
            // el mod (luchadores, cosas de otros viewers) y el vehículo del jugador se respetan.
            Ped player = GTA.Game.Player.Character;
            Vehicle own = player.IsInVehicle() ? player.CurrentVehicle : null;
            float range = _radius + 40f;

            foreach (Ped p in World.GetNearbyPeds(_center, range))
            {
                if (p == player || _tracker.IsTracked(p)) continue;
                if (own != null && p.IsInVehicle() && p.CurrentVehicle == own) continue; // pasajeros del jugador
                EntityTracker.SafeDelete(p);
            }
            foreach (Vehicle v in World.GetNearbyVehicles(_center, range))
            {
                if (v == own || _tracker.IsTracked(v)) continue;
                EntityTracker.SafeDelete(v);
            }
        }

        // ================================================================ arena cerrada

        /// <summary>Punto al azar dentro de la arena (hasta fraction del radio), a nivel del suelo.</summary>
        private Vector3 InsideArena(float fraction)
        {
            double angle = _rng.NextDouble() * Math.PI * 2;
            float dist = (float)Math.Sqrt(_rng.NextDouble()) * _radius * fraction;
            var pos = new Vector3(_center.X + (float)Math.Cos(angle) * dist, _center.Y + (float)Math.Sin(angle) * dist, _center.Z + 3f);
            float ground = World.GetGroundHeight(pos);
            pos.Z = ground > 0f && Math.Abs(ground - _center.Z) < 15f ? ground : _center.Z;
            return pos;
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Nadie sale: el luchador que cruza la pared vuelve adentro (con un destello); el jugador
        /// también, con aviso. Si el jugador murió y reapareció lejos, vuelve a la arena.
        /// </summary>
        private void KeepInside(int now)
        {
            Ped player = GTA.Game.Player.Character;

            if (now < _nextWallCheck)
            {
                return;
            }
            _nextWallCheck = now + 250;

            foreach (Fighter f in _fighters.Values)
            {
                if (IsAlive(f) && Horizontal(f.Ped.Position, _center) > _radius + 1f)
                {
                    Vector3 from = f.Ped.Position;
                    Vector3 to = InsideArena(0.6f);
                    Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, f.Ped);
                    f.Ped.Position = to;
                    f.Target = null; // que vuelva a buscar rival
                    Ptfx.Light(from, Color.Orange, 6f, 20f);
                }
            }

            if (_playerIn && !player.IsDead && Horizontal(player.Position, _center) > _radius + 1f)
            {
                // Empujón hacia adentro (a pie o en vehículo).
                Entity body = player.IsInVehicle() ? (Entity)player.CurrentVehicle : player;
                Vector3 back = _center - body.Position;
                back.Z = 0f;
                back.Normalize();
                Vector3 inside = new Vector3(_center.X, _center.Y, body.Position.Z) - back * (_radius - 3f);
                float ground = World.GetGroundHeight(inside + new Vector3(0f, 0f, 3f));
                if (ground > 0f) inside.Z = ground + 0.5f;
                body.Position = inside;
                body.Velocity = Vector3.Zero;
                GTA.UI.Screen.ShowSubtitle("~o~¡No puedes salir de la arena!", 1500);
            }
        }

        /// <summary>Pared de la arena: columna transparente naranja + anillo brillante en el suelo.</summary>
        private void DrawWall()
        {
            Ped player = GTA.Game.Player.Character;
            if (Horizontal(player.Position, _center) > _radius + 250f)
            {
                return;
            }
            if (float.IsNaN(_centerGround))
            {
                float g = World.GetGroundHeight(_center + new Vector3(0f, 0f, 5f));
                _centerGround = g > 0f && Math.Abs(g - _center.Z) < 10f ? g : _center.Z - 1f;
            }

            int pulse = (int)(15 * (1 + Math.Sin(GTA.Game.GameTime / 300.0)));
            float d = _radius * 2f;
            Vector3 ground = new Vector3(_center.X, _center.Y, _centerGround);
            World.DrawMarker(MarkerType.VerticalCylinder, ground - new Vector3(0f, 0f, 2f), Vector3.Zero, Vector3.Zero,
                new Vector3(d, d, 14f), Color.FromArgb(35 + pulse, 255, 120, 20));
            World.DrawMarker((MarkerType)25, ground + new Vector3(0f, 0f, 0.2f), Vector3.Zero, Vector3.Zero,
                new Vector3(d, d, 1f), Color.FromArgb(220, 255, 140, 30));
        }

        /// <summary>Teletransporte con carga del suelo: congelado hasta que el terreno esté listo.</summary>
        private void Teleport(Ped player, Vector3 pos)
        {
            Entity body = player.IsInVehicle() ? (Entity)player.CurrentVehicle : player;
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, pos.X, pos.Y, pos.Z);
            body.Position = pos + new Vector3(0f, 0f, 1f);
            body.Velocity = Vector3.Zero;
            body.IsPositionFrozen = true;
            int frames = 0;
            _scheduler.Until(() =>
            {
                if (!body.Exists())
                {
                    return true;
                }
                Function.Call(Hash.REQUEST_COLLISION_AT_COORD, pos.X, pos.Y, pos.Z);
                if (!Function.Call<bool>(Hash.HAS_COLLISION_LOADED_AROUND_ENTITY, body.Handle) && ++frames < 180)
                {
                    return false;
                }
                float ground = World.GetGroundHeight(pos + new Vector3(0f, 0f, 1.5f));
                if (ground > 0f && Math.Abs(ground - pos.Z) < 4f)
                {
                    body.Position = new Vector3(pos.X, pos.Y, ground + 1f);
                }
                body.IsPositionFrozen = false;
                return true;
            });
        }

        /// <summary>Guarda donde está parado el jugador como arena "marked".</summary>
        public void SetPlaceHere()
        {
            Vector3 pos = GTA.Game.Player.Character.Position;
            _markedPlace = pos;
            try
            {
                File.WriteAllText(_placeFile, string.Format(CultureInfo.InvariantCulture, "{0:0.00};{1:0.00};{2:0.00}", pos.X, pos.Y, pos.Z));
            }
            catch (Exception ex)
            {
                _log($"Pelea: no se pudo guardar la arena ({ex.Message}).");
            }
            GTA.UI.Notification.Show("~o~Pelea~s~: arena marcada aquí ~g~(guardada)~s~ · elige lugar \"marked\"");
        }

        private void LoadPlace()
        {
            try
            {
                if (!File.Exists(_placeFile)) return;
                string[] parts = File.ReadAllText(_placeFile).Trim().Split(';');
                if (parts.Length == 3)
                {
                    var ci = CultureInfo.InvariantCulture;
                    _markedPlace = new Vector3(float.Parse(parts[0], ci), float.Parse(parts[1], ci), float.Parse(parts[2], ci));
                }
            }
            catch (Exception ex)
            {
                _log($"Pelea: no se pudo leer la arena guardada ({ex.Message}).");
            }
        }

        // ================================================================ HUD

        private string _bigText;
        private Color _bigColor;
        private int _bigUntil;

        private void ShowBig(string text, Color color, int ms)
        {
            _bigText = text;
            _bigColor = color;
            _bigUntil = GTA.Game.GameTime + ms;
        }

        private void DrawBig()
        {
            if (_bigText == null)
            {
                return;
            }
            if (GTA.Game.GameTime >= _bigUntil)
            {
                _bigText = null;
                return;
            }
            _big.Caption = _bigText;
            _big.Color = _bigColor;
            _big.Draw();
        }

        /// <summary>Tabla: Top 5 por kills (el jugador incluido), con vida y "+P" si tiene un poder activo.</summary>
        private void DrawBoard(int now)
        {
            var rows = new List<(string Name, int Kills, bool Power, int Health, bool Alive)>();
            Ped player = GTA.Game.Player.Character;
            bool playerAlive = _playerIn && !player.IsDead;
            if (_playerIn || _playerKills > 0)
            {
                rows.Add((PlayerName, _playerKills, false, playerAlive ? Math.Max(0, player.Health - 100) : 0, playerAlive));
            }
            foreach (Fighter f in _fighters.Values)
            {
                bool alive = IsAlive(f);
                bool power = alive && f.Powers.Values.Any(u => u > now);
                rows.Add((f.Name, f.Kills, power, alive ? Math.Max(0, f.Ped.Health - 100) : 0, alive));
            }
            var top = rows.OrderByDescending(r => r.Kills).ThenByDescending(r => r.Alive).ThenByDescending(r => r.Health).Take(5).ToList();

            int standing = _fighters.Values.Count(IsAlive) + (playerAlive ? 1 : 0);
            string header;
            if (_nextRoundAt != 0) header = "PELEA DE VIEWERS  ·  próxima ronda...";
            else if (_stopAt != 0) header = "PELEA DE VIEWERS  ·  ¡terminó!";
            else if (_roundMs > 0)
            {
                int left = Math.Max(0, (_roundEnds - now) / 1000);
                header = $"PELEA DE VIEWERS  ·  R{_round}  ·  {left / 60:00}:{left % 60:00}";
            }
            else header = $"PELEA DE VIEWERS  ·  R{_round}  ·  gana el último";

            int lines = top.Count + 2;
            _boardBack.Size = new SizeF(BoardW, lines * RowH + 10f);
            _boardBack.Draw();

            DrawLine(0, header, Color.Orange);
            DrawLine(1, $"En pie: {standing}" + (_playerIn ? "" : "   (tú: fuera)"), Color.LightGray);
            for (int i = 0; i < top.Count; i++)
            {
                var r = top[i];
                string power = r.Power ? " ~y~+P~s~" : "";
                string hp = r.Alive ? $"{FormatHp(r.Health)} HP" : "caído";
                DrawLine(i + 2, $"{i + 1}. {Short(r.Name)}{power}  ·  {r.Kills} K  ·  {hp}", r.Alive ? Color.White : Color.Gray);
            }

            DrawWinners();
        }

        /// <summary>Recuadro arriba de la tabla con los últimos ganadores (el primero, el más reciente).</summary>
        private void DrawWinners()
        {
            if (_winners.Count == 0)
            {
                return;
            }
            float h = (_winners.Count + 1) * RowH + 10f;
            float y = BoardY - h - 8f;
            _winnersBack.Position = new PointF(BoardX, y);
            _winnersBack.Size = new SizeF(BoardW, h);
            _winnersBack.Draw();

            DrawAt(y + 4f, "ÚLTIMO GANADOR", Color.Gold);
            for (int i = 0; i < _winners.Count; i++)
            {
                DrawAt(y + 4f + (i + 1) * RowH, (i == 0 ? "1º  " : "     ") + _winners[i], i == 0 ? Color.Gold : Color.LightGray);
            }
        }

        private void DrawAt(float y, string text, Color color)
        {
            _boardText.Caption = text;
            _boardText.Color = color;
            _boardText.Position = new PointF(BoardX + 8f, y);
            _boardText.Draw();
        }

        private void DrawLine(int index, string text, Color color)
        {
            _boardText.Caption = text;
            _boardText.Color = color;
            _boardText.Position = new PointF(BoardX + 8f, BoardY + 4f + index * RowH);
            _boardText.Draw();
        }

        private static string Short(string name) => name.Length > 14 ? name.Substring(0, 12) + ".." : name;

        private static string FormatHp(int hp) => hp >= 1000 ? $"{hp / 1000.0:0.#}k" : hp.ToString();

        public void Clear() => Stop(quiet: true);

        // ================================================================ tipos

        private sealed class Fighter
        {
            public string Name;
            public CharacterDef Base;
            public Color Aura;
            public int MaxHp = StartHealth;                                   // vida máxima (sin el +100 interno)
            public List<WeaponHash> Weapons = new List<WeaponHash>();         // se quedan al revivir
            public Dictionary<string, int> Powers = new Dictionary<string, int>(); // poder → GameTime en que vence
            public int Kills;
            public int Deaths;
            public Ped Ped;
            public Ped Target;
            public bool WasAlive;
        }
    }
}
