using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace StreamTok.GtaV.Actions
{
    internal static class WorldActions
    {
        /// <summary>
        /// Cámara temblando + sacudones periódicos a vehículos y peds cercanos (y a veces al jugador).
        /// </summary>
        private static void Earthquake(ActionContext ctx)
        {
            int intensity = ctx.Int("intensity");
            Random rng = ctx.Rng;
            int frame = 0;

            Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "ROAD_VIBRATION_SHAKE", intensity * 0.3f);

            ctx.Scheduler.RepeatFor("earthquake", ctx.Int("seconds"), () =>
            {
                // Un sacudón cada ~10 frames: suficiente para sentirse, sin castigar el rendimiento.
                if (++frame % 10 != 0)
                {
                    return;
                }

                Ped player = GTA.Game.Player.Character;
                float force = intensity * 0.6f;

                foreach (Vehicle v in World.GetNearbyVehicles(player.Position, 80f))
                {
                    Shove(v, rng, force);
                }

                foreach (Ped p in World.GetNearbyPeds(player.Position, 60f))
                {
                    if (p != player && !p.IsInVehicle() && rng.NextDouble() < 0.1 * intensity / 5.0)
                    {
                        Function.Call(Hash.SET_PED_TO_RAGDOLL, p, 1500, 1500, 0, false, false, false);
                    }
                }

                // Al jugador a pie lo tira de vez en cuando, más cuanto más fuerte.
                if (!player.IsInVehicle() && rng.NextDouble() < 0.03 * intensity)
                {
                    Function.Call(Hash.SET_PED_TO_RAGDOLL, player, 1200, 1200, 0, false, false, false);
                }
            },
            onEnd: () => Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true));
        }

        private static void Shove(Entity e, Random rng, float force)
        {
            var push = new Vector3(
                (float)(rng.NextDouble() * 2 - 1) * force,
                (float)(rng.NextDouble() * 2 - 1) * force,
                (float)rng.NextDouble() * force);
            Function.Call(Hash.APPLY_FORCE_TO_ENTITY, e, 1, push.X, push.Y, push.Z, 0f, 0f, 0f, 0, false, true, true, false, true);
        }

        public static IEnumerable<ActionDef> All()
        {
            yield return new ActionDef("wanted_level", "Nivel de búsqueda", false,
                new[]
                {
                    ParamDef.Enum("mode", "add", "add", "remove", "max", "clear"),
                    ParamDef.Int("stars", 1, 1, 5),
                },
                ctx =>
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
                });

            yield return new ActionDef("money", "Dinero", false,
                new[]
                {
                    ParamDef.Enum("mode", "add", "add", "set"),
                    ParamDef.Int("amount", 1000, 1, 10000000),
                },
                ctx =>
                {
                    Player player = GTA.Game.Player;
                    long amount = ctx.Int("amount");
                    long next = ctx.Enum("mode") == "add" ? player.Money + amount : amount;
                    player.Money = (int)Math.Min(next, 2000000000L);
                });

            // Efecto del MUNDO: este sí dura (seconds). Si llega otro, se suma el tiempo.
            yield return new ActionDef("earthquake", "Terremoto", false,
                new[]
                {
                    ParamDef.Int("seconds", 15, 3, 120),
                    ParamDef.Int("intensity", 5, 1, 10),
                },
                Earthquake);

            yield return new ActionDef("set_weather", "Clima", false,
                new[] { ParamDef.Enum("weather", "rain", GameData.Keys(GameData.Weather)) },
                ctx => Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, GameData.Weather[ctx.Enum("weather")]));

            yield return new ActionDef("set_time", "Hora del día", false,
                new[] { ParamDef.Int("hour", 12, 0, 23) },
                ctx => Function.Call(Hash.SET_CLOCK_TIME, ctx.Int("hour"), 0, 0));
        }
    }
}
