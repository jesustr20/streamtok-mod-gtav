using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Entities;
using StreamTok.GtaV.Modes;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Acciones del modo Chiliad (ver Modes/ChiliadMode.cs). Mientras el modo está activo,
    /// todas las demás acciones del catálogo siguen funcionando.
    /// </summary>
    internal static class ChiliadActions
    {
        /// <summary>Vehículo con el que arranca: keep = lo que tenga, none = a pie.</summary>
        private static readonly Dictionary<string, string> StartVehicles = new Dictionary<string, string>
        {
            ["keep"] = null,
            ["none"] = "",
            ["sanchez"] = "sanchez",
            ["bf400"] = "bf400",
            ["blazer"] = "blazer",
            ["mesa"] = "mesa3",
            ["trophy_truck"] = "trophytruck",
            ["bifta"] = "bifta",
            ["bmx"] = "bmx",
        };

        /// <summary>Autos que aparecen chocados en el "accidente".</summary>
        private static readonly string[] WreckModels =
        {
            "emperor", "asea", "premier", "ingot", "bison", "rumpo", "sadler", "tornado", "voodoo2", "rebel",
        };

        public static IEnumerable<ActionDef> All()
        {
            yield return new ActionDef("chiliad_start", "Chiliad: iniciar", true,
                new[]
                {
                    ParamDef.Int("minutes", 15, 1, 60, 5, 10, 15, 20, 25, 30, 45, 60), // o cualquier número 1-60
                    ParamDef.Bool("timer", true),
                    ParamDef.Int("hold_seconds", 5, 1, 60, 3, 5, 10, 15, 20, 30), // cuánto quedarse en la cima
                    ParamDef.Bool("repeat", true),                                // al lograrlo: otra vuelta
                    ParamDef.Enum("start", "airport", ChiliadMode.StartKeys),
                    ParamDef.Enum("vehicle", "keep", GameData.Keys(StartVehicles)),
                },
                ctx => ctx.Chiliad.Start(ctx.Int("minutes"), ctx.Bool("timer"), ctx.Int("hold_seconds"), ctx.Bool("repeat"), ctx.Enum("start"),
                    StartVehicles[ctx.Enum("vehicle")], ctx.NameTag));

            yield return new ActionDef("chiliad_set_goal", "Marcar meta aquí", false,
                new[] { ParamDef.Enum("mode", "here", "here", "default") },
                ctx =>
                {
                    if (ctx.Enum("mode") == "here") ctx.Chiliad.SetGoalHere();
                    else ctx.Chiliad.ResetGoal();
                });

            yield return new ActionDef("chiliad_set_start", "Marcar salida aquí", false,
                new[] { ParamDef.Enum("mode", "here", "here", "default") },
                ctx =>
                {
                    if (ctx.Enum("mode") == "here") ctx.Chiliad.SetStartHere();
                    else ctx.Chiliad.ResetStart();
                });

            yield return new ActionDef("chiliad_set_taxi_stop", "Marcar parada del taxi aquí", false,
                new[] { ParamDef.Enum("mode", "here", "here", "default") },
                ctx =>
                {
                    if (ctx.Enum("mode") == "here") ctx.Chiliad.SetTaxiStopHere();
                    else ctx.Chiliad.ResetTaxiStop();
                });

            yield return new ActionDef("chiliad_taxi", "Taxi al Monte Chiliad", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Chiliad.SetTaxi(ctx.Bool("enabled")));

            yield return new ActionDef("chiliad_stop", "Chiliad: terminar", false, null,
                ctx => ctx.Chiliad.Stop());

            // Interruptores: al iniciar, todo queda encendido; se apagan uno por uno.
            yield return new ActionDef("chiliad_route", "Ruta marcada a la cima", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Chiliad.SetRoute(ctx.Bool("enabled")));

            yield return new ActionDef("chiliad_gps", "GPS (minimapa)", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Chiliad.SetGps(ctx.Bool("enabled")));

            yield return new ActionDef("chiliad_timer", "Tiempo límite", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Chiliad.SetTimer(ctx.Bool("enabled")));

            yield return new ActionDef("chiliad_respawn", "Reaparecer donde quedó", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Chiliad.SetRespawn(ctx.Bool("enabled")));

            yield return new ActionDef("chiliad_time", "Chiliad: tiempo", false,
                new[]
                {
                    ParamDef.Enum("mode", "add", "add", "remove"),
                    ParamDef.Int("seconds", 30, 5, 600),
                },
                ctx => ctx.Chiliad.AddTime(ctx.Enum("mode") == "add" ? ctx.Int("seconds") : -ctx.Int("seconds")));

            yield return new ActionDef("chiliad_gps_off", "Chiliad: apagar GPS unos segundos", false,
                new[] { ParamDef.Int("seconds", 30, 5, 300) },
                ctx => ctx.Chiliad.HideGps(ctx.Int("seconds")));

            yield return new ActionDef("chiliad_back_to_base", "Chiliad: volver al inicio", false, null,
                ctx => ctx.Chiliad.BackToBase());

            // Funciona también fuera del modo: bloquea el camino por donde vaya el jugador.
            yield return new ActionDef("road_accident", "Accidente en el camino", true,
                new[]
                {
                    ParamDef.Int("cars", 3, 1, 6),
                    ParamDef.Int("distance", 60, 25, 150),
                    ParamDef.Bool("fire", true),
                },
                RoadAccident);

            yield return new ActionDef("wrecks_remove", "Quitar autos chocados", false, null,
                ctx =>
                {
                    if (ctx.Tracker.RemoveKind(EntityTracker.KindWreck) == 0)
                    {
                        throw new ActionException("No hay autos chocados");
                    }
                });
        }

        /// <summary>
        /// Varios autos destrozados cruzados en el camino, "distance" metros por delante del
        /// jugador (hacia donde mira o se mueve), sobre la calle más cercana si la hay.
        /// </summary>
        private static void RoadAccident(ActionContext ctx)
        {
            int count = ctx.Tracker.ClampVehicles(ctx.Int("cars"));
            Ped p = GTA.Game.Player.Character;
            Entity mover = p.IsInVehicle() ? (Entity)p.CurrentVehicle : p;

            Vector3 dir = mover.Velocity;
            dir.Z = 0f;
            if (dir.Length() < 2f)
            {
                dir = mover.ForwardVector;
                dir.Z = 0f;
            }
            dir.Normalize();

            Vector3 center = mover.Position + dir * ctx.Int("distance");
            Vector3 street = World.GetNextPositionOnStreet(center);
            if (street != Vector3.Zero && street.DistanceTo(center) < 30f)
            {
                center = street;
            }

            // Perpendicular al camino: los autos quedan atravesados de lado a lado.
            var side = new Vector3(-dir.Y, dir.X, 0f);
            float roadHeading = (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI);
            bool fire = ctx.Bool("fire");

            for (int i = 0; i < count; i++)
            {
                float offset = (i - (count - 1) / 2f) * 4.5f;
                Vector3 pos = center + side * offset + dir * (float)(ctx.Rng.NextDouble() * 6 - 3);
                float heading = roadHeading + 90f + (float)(ctx.Rng.NextDouble() * 60 - 30);

                Vehicle v = Spawner.SpawnVehicle(ctx.Pick(WreckModels), pos, heading, true);
                Wreck(v, ctx.Rng);
                ctx.Tracker.Track(v, ctx.NameTag, EntityTracker.KindWreck, GameData.VehicleTagHeight["car"]);

                if (fire && i % 2 == 0)
                {
                    // Fuego en el motor: humea y arde un rato; puede explotar (es parte del show).
                    v.EngineHealth = 150f;
                    Vector3 engine = v.Position + v.ForwardVector * 1.6f;
                    Function.Call<int>(Hash.START_SCRIPT_FIRE, engine.X, engine.Y, engine.Z, 5, false);
                }
            }
        }

        private static void Wreck(Vehicle v, Random rng)
        {
            v.IsEngineRunning = false;
            v.EngineHealth = 300f;
            v.BodyHealth = 200f;

            for (int window = 0; window <= 7; window++)
            {
                if (rng.Next(3) > 0)
                {
                    Function.Call(Hash.SMASH_VEHICLE_WINDOW, v, window);
                }
            }
            for (int door = 0; door <= 5; door++)
            {
                if (rng.Next(3) == 0)
                {
                    Function.Call(Hash.SET_VEHICLE_DOOR_BROKEN, v, door, false);
                }
            }

            float[,] hits = { { 0f, 2.2f, 0.2f }, { 1.0f, 0.5f, 0.2f }, { -1.0f, -0.5f, 0.2f }, { 0f, 0f, 1.0f } };
            for (int i = 0; i < hits.GetLength(0); i++)
            {
                Function.Call(Hash.SET_VEHICLE_DAMAGE, v, hits[i, 0], hits[i, 1], hits[i, 2], 1000f, 1.5f, true);
            }
        }
    }
}
