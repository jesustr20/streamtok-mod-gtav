using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace StreamTok.GtaV.Actions
{
    internal static class WorldActions
    {
        /// <summary>Activa (bucle con clave) o desactiva (revierte con onEnd) un efecto del mundo.</summary>
        private static void SetWorld(ActionContext ctx, string key, Action onStart, Action onTick, Action onEnd)
        {
            if (ctx.Bool("enabled"))
            {
                ctx.Scheduler.Loop(key, onStart, onTick, onEnd);
            }
            else if (!ctx.Scheduler.Cancel(key))
            {
                throw new ActionException("El efecto no estaba activo");
            }
        }

        // ------------------------------------------------------ vehículos invisibles

        // Se usa transparencia (alpha 0) y NO "invisible": en GTA, ocultar un vehículo oculta
        // también a sus ocupantes, y el jugador debe verse siempre.
        private static readonly HashSet<int> HiddenVehicles = new HashSet<int>();
        private static int _invisibleFrame;

        private static void VehiclesInvisibleStart()
        {
            HiddenVehicles.Clear();
            _invisibleFrame = 0;
        }

        private static void VehiclesInvisibleTick()
        {
            Ped player = GTA.Game.Player.Character;
            Function.Call(Hash.RESET_ENTITY_ALPHA, player); // el jugador siempre visible

            if (++_invisibleFrame % 15 != 0)
            {
                return; // buscar vehículos nuevos cada ~15 frames basta
            }

            foreach (Vehicle v in World.GetNearbyVehicles(player.Position, 150f))
            {
                if (HiddenVehicles.Add(v.Handle))
                {
                    Function.Call(Hash.SET_ENTITY_ALPHA, v, 0, false);
                }
            }
        }

        private static void VehiclesInvisibleEnd()
        {
            foreach (int handle in HiddenVehicles)
            {
                if (Function.Call<bool>(Hash.DOES_ENTITY_EXIST, handle))
                {
                    Function.Call(Hash.RESET_ENTITY_ALPHA, handle);
                }
            }
            HiddenVehicles.Clear();
        }

        // ------------------------------------------------------ coches rápidos

        private const int FastDrivingStyle = 786468;   // apurado: esquiva y adelanta
        private const int NormalDrivingStyle = 786603; // normal
        private static readonly HashSet<int> FastVehicles = new HashSet<int>();
        private static int _trafficFrame;

        private static void TrafficFastTick()
        {
            if (++_trafficFrame % 30 != 0)
            {
                return;
            }

            Ped player = GTA.Game.Player.Character;
            foreach (Vehicle v in World.GetNearbyVehicles(player.Position, 150f))
            {
                Ped driver = v.Driver;
                if (driver == null || !driver.Exists() || driver.IsPlayer || !FastVehicles.Add(v.Handle))
                {
                    continue;
                }

                Function.Call(Hash.SET_DRIVER_ABILITY, driver, 1.0f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver, 1.0f);
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, driver, v, 70f, FastDrivingStyle);
            }
        }

        private static void TrafficFastEnd()
        {
            foreach (int handle in FastVehicles)
            {
                if (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, handle))
                {
                    continue;
                }
                int driver = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, handle, -1, false);
                if (driver != 0 && driver != GTA.Game.Player.Character.Handle)
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, driver, handle, 20f, NormalDrivingStyle);
                }
            }
            FastVehicles.Clear();
        }

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

            // --- Efectos del mundo que se activan/desactivan (enabled): quedan hasta que otra acción los apague.

            yield return new ActionDef("vehicles_invisible", "Vehículos invisibles", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => SetWorld(ctx, "vehicles_invisible", VehiclesInvisibleStart, VehiclesInvisibleTick, VehiclesInvisibleEnd));

            yield return new ActionDef("traffic_fast", "Coches rápidos", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => SetWorld(ctx, "traffic_fast", null, TrafficFastTick, TrafficFastEnd));

            yield return new ActionDef("gravity_low", "Gravedad reducida", false,
                new[]
                {
                    ParamDef.Bool("enabled", true),
                    ParamDef.Enum("level", "low", "low", "very_low", "zero"),
                },
                ctx =>
                {
                    if (ctx.Bool("enabled"))
                    {
                        int level = ctx.Enum("level") == "zero" ? 3 : ctx.Enum("level") == "very_low" ? 2 : 1;
                        Function.Call(Hash.SET_GRAVITY_LEVEL, level); // cambiar de nivel estando activa también vale
                    }
                    SetWorld(ctx, "gravity_low", null, null, () => Function.Call(Hash.SET_GRAVITY_LEVEL, 0));
                });

            yield return new ActionDef("set_weather", "Clima", false,
                new[] { ParamDef.Enum("weather", "rain", GameData.Keys(GameData.Weather)) },
                ctx => Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, GameData.Weather[ctx.Enum("weather")]));

            yield return new ActionDef("set_time", "Hora del día", false,
                new[] { ParamDef.Int("hour", 12, 0, 23) },
                ctx => Function.Call(Hash.SET_CLOCK_TIME, ctx.Int("hour"), 0, 0));
        }
    }
}
