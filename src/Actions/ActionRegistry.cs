using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Native;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Catálogo de acciones del mod (tanda 1). Ver docs/CATALOGO.md.
    /// Para agregar una acción: definirla aquí con un id estable y sus parámetros;
    /// aparece sola en mod-hello, en el menú de pruebas (F7) y en StreamTok.
    /// </summary>
    internal sealed class ActionRegistry
    {
        private readonly Dictionary<string, ActionDef> _byId;

        private ActionRegistry(IEnumerable<ActionDef> actions)
        {
            All = actions.ToList();
            _byId = All.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<ActionDef> All { get; }

        public ActionDef Find(string id) =>
            id != null && _byId.TryGetValue(id, out ActionDef a) ? a : null;

        // ---------------------------------------------------------------- datos

        private static readonly Dictionary<string, string> AnimalModels = new Dictionary<string, string>
        {
            ["dog"] = "a_c_shepherd",
            ["rottweiler"] = "a_c_rottweiler",
            ["husky"] = "a_c_husky",
            ["cow"] = "a_c_cow",
            ["pig"] = "a_c_pig",
            ["boar"] = "a_c_boar",
            ["deer"] = "a_c_deer",
            ["coyote"] = "a_c_coyote",
            ["cougar"] = "a_c_mtlion",
            ["chimp"] = "a_c_chimp",
            ["rabbit"] = "a_c_rabbit_01",
        };

        private static readonly string[] NormalAttackerModels =
        {
            "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_mexgoon_01", "g_m_y_ballasout_01",
        };

        private static readonly string[] RandomAttackerModels =
        {
            "g_m_y_lost_01", "g_m_y_mexgoon_01", "g_m_y_ballasout_01", "s_m_y_clown_01",
            "u_m_y_zombie_01", "s_m_m_movalien_01", "a_m_m_beach_01",
        };

        private static readonly Dictionary<string, WeaponHash> Weapons = new Dictionary<string, WeaponHash>
        {
            ["pistol"] = WeaponHash.Pistol,
            ["smg"] = WeaponHash.SMG,
            ["rifle"] = WeaponHash.CarbineRifle,
            ["mg"] = WeaponHash.CombatMG,
            ["sniper"] = WeaponHash.SniperRifle,
            ["rpg"] = WeaponHash.RPG,
            ["bat"] = WeaponHash.Bat,
            ["knife"] = WeaponHash.Knife,
        };

        private static readonly Dictionary<string, string> WeatherTypes = new Dictionary<string, string>
        {
            ["extrasunny"] = "EXTRASUNNY",
            ["clear"] = "CLEAR",
            ["clouds"] = "CLOUDS",
            ["overcast"] = "OVERCAST",
            ["rain"] = "RAIN",
            ["thunder"] = "THUNDER",
            ["foggy"] = "FOGGY",
            ["snow"] = "SNOW",
            ["blizzard"] = "BLIZZARD",
            ["xmas"] = "XMAS",
        };

        // ------------------------------------------------------------- catálogo

        public static ActionRegistry CreateDefault()
        {
            string[] animalOptions = new[] { "random" }.Concat(AnimalModels.Keys).ToArray();
            string[] attackerWeaponOptions = { "pistol", "smg", "rifle", "mg", "rpg", "bat", "knife", "none", "random" };
            string[] giveWeaponOptions = { "pistol", "smg", "rifle", "mg", "sniper", "rpg", "bat", "knife", "random", "all" };

            return new ActionRegistry(new[]
            {
                new ActionDef("spawn_animal", "Spawn de animal", true,
                    new[]
                    {
                        ParamDef.Enum("animal", "random", animalOptions),
                        ParamDef.Int("count", 1, 1, 20),
                        ParamDef.Bool("hostile", false),
                    },
                    SpawnAnimal),

                new ActionDef("spawn_attackers", "Spawn de atacantes", true,
                    new[]
                    {
                        ParamDef.Int("count", 3, 1, 50),
                        ParamDef.Enum("weapon", "pistol", attackerWeaponOptions),
                        ParamDef.Enum("model", "normal", "normal", "random", "chimp", "alien"),
                    },
                    SpawnAttackers),

                new ActionDef("attackers_remove", "Remover atacantes", false,
                    null,
                    ctx => ctx.Tracker.RemoveKind(EntityTracker.KindAttacker)),

                new ActionDef("give_weapon", "Dar arma", false,
                    new[] { ParamDef.Enum("weapon", "pistol", giveWeaponOptions) },
                    GiveWeapon),

                new ActionDef("player_health", "Vida del jugador", false,
                    new[]
                    {
                        ParamDef.Enum("mode", "add", "add", "remove"),
                        ParamDef.Int("amount", 25, 1, 100),
                    },
                    PlayerHealth),

                new ActionDef("wanted_level", "Nivel de búsqueda", false,
                    new[]
                    {
                        ParamDef.Enum("mode", "add", "add", "remove", "max", "clear"),
                        ParamDef.Int("stars", 1, 1, 5),
                    },
                    WantedLevel),

                new ActionDef("set_weather", "Clima", false,
                    new[] { ParamDef.Enum("weather", "rain", WeatherTypes.Keys.ToArray()) },
                    ctx => Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, WeatherTypes[ctx.Enum("weather")])),

                new ActionDef("set_time", "Hora del día", false,
                    new[] { ParamDef.Int("hour", 12, 0, 23) },
                    ctx => Function.Call(Hash.SET_CLOCK_TIME, ctx.Int("hour"), 0, 0)),

                new ActionDef("vehicle_repair", "Reparar vehículo", false,
                    null,
                    ctx => RequireVehicle().Repair()),

                new ActionDef("vehicle_explode", "Explotar vehículo", false,
                    null,
                    ctx => RequireVehicle().Explode()),
            });
        }

        // ------------------------------------------------------------- acciones

        private static void SpawnAnimal(ActionContext ctx)
        {
            string key = ctx.Enum("animal");
            int count = ctx.Tracker.ClampToLimit(ctx.Int("count"));
            bool hostile = ctx.Bool("hostile");

            for (int i = 0; i < count; i++)
            {
                // "random" elige un animal distinto para cada spawn.
                string modelName = key == "random"
                    ? AnimalModels.Values.ElementAt(ctx.Rng.Next(AnimalModels.Count))
                    : AnimalModels[key];

                Ped ped = Spawner.SpawnPed(modelName, Spawner.NearPlayer(ctx.Rng, 4f, 8f));
                if (hostile)
                {
                    ped.RelationshipGroup = ctx.Tracker.HostileGroup;
                    ped.Task.FightAgainst(GTA.Game.Player.Character);
                    ped.AlwaysKeepTask = true;
                }
                else
                {
                    ped.Task.WanderAround();
                }

                ctx.Tracker.Track(ped, ctx.NameTag, EntityTracker.KindAnimal);
            }
        }

        private static void SpawnAttackers(ActionContext ctx)
        {
            int count = ctx.Tracker.ClampToLimit(ctx.Int("count"));
            string weapon = ctx.Enum("weapon");
            string model = ctx.Enum("model");
            Ped player = GTA.Game.Player.Character;

            for (int i = 0; i < count; i++)
            {
                string modelName;
                switch (model)
                {
                    case "chimp": modelName = "a_c_chimp"; break;
                    case "alien": modelName = "s_m_m_movalien_01"; break;
                    case "random": modelName = RandomAttackerModels[ctx.Rng.Next(RandomAttackerModels.Length)]; break;
                    default: modelName = NormalAttackerModels[ctx.Rng.Next(NormalAttackerModels.Length)]; break;
                }

                Ped ped = Spawner.SpawnPed(modelName, Spawner.NearPlayer(ctx.Rng, 12f, 25f));
                ped.RelationshipGroup = ctx.Tracker.HostileGroup;

                // Los animales (el mono) no pueden usar armas: pelean cuerpo a cuerpo.
                if (weapon != "none" && modelName != "a_c_chimp")
                {
                    string w = weapon == "random"
                        ? Weapons.Keys.Where(k => k != "sniper").ElementAt(ctx.Rng.Next(Weapons.Count - 1))
                        : weapon;
                    ped.Weapons.Give(Weapons[w], 9999, true, true);
                }

                ped.Task.FightAgainst(player);
                ped.AlwaysKeepTask = true;

                Blip blip = ped.AddBlip();
                blip.Color = BlipColor.Red;
                blip.Scale = 0.7f;

                ctx.Tracker.Track(ped, ctx.NameTag, EntityTracker.KindAttacker);
            }
        }

        private static void GiveWeapon(ActionContext ctx)
        {
            Ped player = GTA.Game.Player.Character;
            string weapon = ctx.Enum("weapon");

            IEnumerable<string> toGive;
            if (weapon == "all")
            {
                toGive = Weapons.Keys;
            }
            else if (weapon == "random")
            {
                toGive = new[] { Weapons.Keys.ElementAt(ctx.Rng.Next(Weapons.Count)) };
            }
            else
            {
                toGive = new[] { weapon };
            }

            foreach (string w in toGive)
            {
                int ammo = w == "rpg" ? 20 : 250;
                player.Weapons.Give(Weapons[w], ammo, true, true);
            }
        }

        private static void PlayerHealth(ActionContext ctx)
        {
            Ped player = GTA.Game.Player.Character;
            int amount = ctx.Int("amount");

            player.Health = ctx.Enum("mode") == "add"
                ? Math.Min(player.MaxHealth, player.Health + amount)
                : Math.Max(0, player.Health - amount);
        }

        private static void WantedLevel(ActionContext ctx)
        {
            Player player = GTA.Game.Player;
            int current = player.WantedLevel;
            int stars = ctx.Int("stars");

            switch (ctx.Enum("mode"))
            {
                case "add": player.WantedLevel = Math.Min(5, current + stars); break;
                case "remove": player.WantedLevel = Math.Max(0, current - stars); break;
                case "max": player.WantedLevel = 5; break;
                case "clear": player.WantedLevel = 0; break;
            }
        }

        private static Vehicle RequireVehicle()
        {
            Ped player = GTA.Game.Player.Character;
            Vehicle v = player.CurrentVehicle ?? player.LastVehicle;
            if (v == null || !v.Exists())
            {
                throw new InvalidOperationException("El jugador no tiene un vehículo");
            }
            return v;
        }
    }
}
