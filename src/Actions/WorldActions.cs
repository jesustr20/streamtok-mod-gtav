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

        // ------------------------------------------------------ nitro para todos

        private static readonly Dictionary<int, float> NitroTargets = new Dictionary<int, float>();

        private static void VehiclesNitro(ActionContext ctx)
        {
            float power = ctx.Int("power");
            Ped player = GTA.Game.Player.Character;
            int frame = 0;

            foreach (Vehicle v in World.GetNearbyVehicles(player.Position, 250f))
            {
                if (v == null || !v.Exists())
                {
                    continue;
                }
                float target = v.Speed + power;
                NitroTargets[v.Handle] = target;
                Function.Call(Hash.SET_ENTITY_MAX_SPEED, v, target + 100f); // sin el tope de velocidad del juego
                Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, v, target);
            }

            ctx.Scheduler.RepeatFor("vehicles_nitro", Math.Max(1, ctx.Int("seconds")), () =>
            {
                if (++frame % 2 != 0)
                {
                    return;
                }
                foreach (KeyValuePair<int, float> kv in NitroTargets)
                {
                    if (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, kv.Key)) continue;
                    // Se sostiene la velocidad aunque la rueda pierda el suelo (así salen volando en las rampas y baches).
                    if (Function.Call<float>(Hash.GET_ENTITY_SPEED, kv.Key) < kv.Value * 0.9f)
                    {
                        Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, kv.Key, kv.Value);
                    }
                }
            },
            onEnd: () =>
            {
                foreach (int handle in NitroTargets.Keys)
                {
                    if (Function.Call<bool>(Hash.DOES_ENTITY_EXIST, handle))
                    {
                        Function.Call(Hash.SET_ENTITY_MAX_SPEED, handle, 10000f);
                    }
                }
                NitroTargets.Clear();
            });
        }

        // ------------------------------------------------------ coches rápidos

        private const int FastDrivingStyle = 786468;   // apurado: esquiva y adelanta
        private const int NormalDrivingStyle = 786603; // normal
        private static readonly HashSet<int> FastVehicles = new HashSet<int>();
        private static int _trafficFrame;
        private static float _fastSpeed = 80f; // m/s a los que pasan los "vehículos rápidos"

        private static void TrafficFastTick()
        {
            _trafficFrame++;
            Ped player = GTA.Game.Player.Character;

            // Cada frame par de 6: acelera de verdad a los que ya están en modo rápido (como un "flash").
            if (_trafficFrame % 6 == 0)
            {
                foreach (int handle in FastVehicles)
                {
                    if (!Function.Call<bool>(Hash.DOES_ENTITY_EXIST, handle)) continue;
                    int driverHandle = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, handle, -1, false);
                    if (driverHandle == 0 || driverHandle == player.Handle) continue;
                    float speed = Function.Call<float>(Hash.GET_ENTITY_SPEED, handle);
                    if (speed < _fastSpeed * 0.95f)
                    {
                        Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, handle, Math.Min(_fastSpeed, speed + _fastSpeed * 0.2f + 2f));
                    }
                }
            }

            if (_trafficFrame % 30 != 0)
            {
                return;
            }

            foreach (Vehicle v in World.GetNearbyVehicles(player.Position, 150f))
            {
                Ped driver = v.Driver;
                if (driver == null || !driver.Exists() || driver.IsPlayer || !FastVehicles.Add(v.Handle))
                {
                    continue;
                }

                Function.Call(Hash.SET_ENTITY_MAX_SPEED, v, _fastSpeed + 20f);
                Function.Call(Hash.SET_DRIVER_ABILITY, driver, 1.0f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, driver, 1.0f);
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, driver, v, _fastSpeed, FastDrivingStyle);
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
        /// Terremoto fuerte: la cámara tiembla, los autos saltan y se empujan unos contra otros (chocan),
        /// la gente cae, y se abren grietas en la pista (chorros de vapor, polvo y cráteres) cerca del jugador.
        /// El juego no puede deformar el asfalto de verdad: las grietas son efectos visuales y marcas.
        /// </summary>
        private static Effects.WorldMood _quakeMood; // clima de antes del terremoto (null = no hay terremoto activo)

        private static void Earthquake(ActionContext ctx)
        {
            int intensity = Math.Max(1, ctx.Int("intensity"));
            Random rng = ctx.Rng;
            int frame = 0;
            int nextCrackAt = 0;
            float jolt = Math.Min(intensity * 0.7f, 20f);          // m/s de sacudón lateral
            float attract = Math.Min(3f + intensity * 0.9f, 22f);  // m/s con que un auto es empujado hacia otro
            int crackEveryMs = Math.Max(250, 1400 - intensity * 110);

            Effects.Ptfx.Request(Effects.Ptfx.Core);
            float camShake = Math.Min(intensity * 0.6f, 8f);
            Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "ROAD_VIBRATION_SHAKE", camShake);

            // Cielo oscuro con lluvia y truenos mientras dura (se guarda el clima solo la primera vez).
            if (_quakeMood == null)
            {
                _quakeMood = Effects.WorldMood.Save(clock: false);
            }
            Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, "THUNDER");

            ctx.Scheduler.RepeatFor("earthquake", ctx.Int("seconds"), () =>
            {
                frame++;
                if (frame % 3 != 0)
                {
                    return;
                }

                int now = GTA.Game.GameTime;
                Ped player = GTA.Game.Player.Character;

                // Movimiento telúrico: el temblor llega en oleadas (fuerte, más suave, fuerte...).
                float wave = 0.55f + 0.45f * (float)Math.Sin(now / 900.0);
                Function.Call(Hash.SET_GAMEPLAY_CAM_SHAKE_AMPLITUDE, camShake * (0.5f + wave));

                Vehicle[] cars = World.GetNearbyVehicles(player.Position, 120f);
                int n = Math.Min(cars.Length, 30);

                for (int i = 0; i < n; i++)
                {
                    Vehicle v = cars[i];
                    if (v == null || !v.Exists() || v.IsDead)
                    {
                        continue;
                    }

                    // Sacudón: cambio de velocidad lateral y un salto de vez en cuando.
                    var dv = new Vector3(
                        (float)(rng.NextDouble() * 2 - 1) * jolt * wave,
                        (float)(rng.NextDouble() * 2 - 1) * jolt * wave,
                        rng.NextDouble() < 0.25 ? (float)rng.NextDouble() * jolt * 0.6f * wave : 0f);

                    // Cada ~0,2 s el auto es lanzado hacia el más cercano: así chocan entre sí.
                    if (frame % 12 == 0)
                    {
                        Vehicle nearest = null;
                        float best = 30f;
                        for (int j = 0; j < n; j++)
                        {
                            if (j == i || cars[j] == null || !cars[j].Exists()) continue;
                            float d = v.Position.DistanceTo(cars[j].Position);
                            if (d < best) { best = d; nearest = cars[j]; }
                        }
                        if (nearest != null)
                        {
                            Vector3 dir = nearest.Position - v.Position;
                            dir.Z = 0f;
                            if (dir.Length() > 0.1f)
                            {
                                dir.Normalize();
                                dv += dir * attract;
                            }
                        }
                    }

                    v.Velocity = v.Velocity + dv;
                }

                foreach (Ped p in World.GetNearbyPeds(player.Position, 70f))
                {
                    if (p != player && !p.IsInVehicle() && rng.NextDouble() < Math.Min(0.5, 0.05 * intensity))
                    {
                        Function.Call(Hash.SET_PED_TO_RAGDOLL, p, 2000, 2000, 0, false, false, false);
                    }
                }

                // Al jugador a pie lo tira de vez en cuando, más cuanto más fuerte.
                if (!player.IsInVehicle() && rng.NextDouble() < Math.Min(0.6, 0.04 * intensity))
                {
                    Function.Call(Hash.SET_PED_TO_RAGDOLL, player, 1500, 1500, 0, false, false, false);
                }

                // Grieta nueva: una línea de vapor, polvo y cráteres sobre la pista cerca del jugador.
                if (now >= nextCrackAt)
                {
                    nextCrackAt = now + crackEveryMs;
                    OpenCrack(player.Position, rng, intensity);
                }
            },
            onEnd: () =>
            {
                Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
                if (_quakeMood != null)
                {
                    _quakeMood.Restore();
                    _quakeMood = null;
                }
            });
        }

        /// <summary>Abre una grieta de 20-40 m con efectos a lo largo (vapor, polvo, pequeños cráteres).</summary>
        private static void OpenCrack(Vector3 around, Random rng, int intensity)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            float distance = 14f + (float)rng.NextDouble() * 55f;
            Vector3 start = around + new Vector3((float)Math.Cos(angle), (float)Math.Sin(angle), 0f) * distance;

            double dirAngle = rng.NextDouble() * Math.PI * 2;
            Vector3 dir = new Vector3((float)Math.Cos(dirAngle), (float)Math.Sin(dirAngle), 0f);
            int steps = 5 + Math.Min(intensity, 10);

            Vector3 p = start;
            for (int i = 0; i < steps; i++)
            {
                // Zigzag: la grieta no es una recta perfecta.
                dirAngle += (rng.NextDouble() - 0.5) * 0.7;
                dir = new Vector3((float)Math.Cos(dirAngle), (float)Math.Sin(dirAngle), 0f);
                p += dir * 3.5f;

                float ground = World.GetGroundHeight(p + new Vector3(0f, 0f, 30f));
                if (ground <= 0f)
                {
                    continue;
                }
                Vector3 at = new Vector3(p.X, p.Y, ground);

                // Chorro de vapor saliendo de la grieta (no hace daño) y polvo.
                Function.Call(Hash.ADD_EXPLOSION, at.X, at.Y, at.Z, 11, 0f, true, false, 0.3f, false);
                Effects.Ptfx.Burst(Effects.Ptfx.Core, "ent_amb_smoke_foundry", at, 2.5f);

                // Cada dos puntos, un pequeño cráter con marcas (rompe el asfalto visualmente).
                if (i % 2 == 0)
                {
                    Function.Call(Hash.ADD_EXPLOSION, at.X, at.Y, at.Z, 0, 0.25f, true, false, 0.6f, false);
                }
            }
        }

        private static void SetMaxWanted()
        {
            Player player = GTA.Game.Player;
            player.WantedLevel = 5;
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, player.Handle, false);
        }

        /// <summary>Quita las estrellas al instante (sin que queden parpadeando).</summary>
        private static void ClearWanted()
        {
            Player player = GTA.Game.Player;
            Function.Call(Hash.CLEAR_PLAYER_WANTED_LEVEL, player.Handle);
            player.WantedLevel = 0;
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, player.Handle, false);
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
                        case "max": SetMaxWanted(); break;
                        case "clear": ClearWanted(); break;
                    }
                });

            // Atajos de un solo botón (lo mismo que mode = max / clear).
            yield return new ActionDef("wanted_max", "Búsqueda máxima", false, null,
                ctx => SetMaxWanted());

            yield return new ActionDef("wanted_clear", "Quitar búsqueda", false, null,
                ctx => ClearWanted());

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

            yield return new ActionDef("traffic_fast", "Vehículos rápidos", false,
                new[]
                {
                    ParamDef.Bool("enabled", true),
                    ParamDef.Int("speed", 80, 5, ParamDef.NoLimit, 40, 80, 120, 200, 300), // m/s (80 ≈ 290 km/h)
                },
                ctx =>
                {
                    _fastSpeed = Math.Max(5, ctx.Int("speed"));
                    SetWorld(ctx, "traffic_fast", null, TrafficFastTick, TrafficFastEnd);
                });

            // Nitro para TODOS los vehículos cercanos (incluido el tuyo): salen disparados y se sostiene la velocidad.
            yield return new ActionDef("vehicles_nitro", "Vehículos con nitro", false,
                new[]
                {
                    ParamDef.Int("power", 80, 1, ParamDef.NoLimit, 40, 80, 150, 300),   // m/s que se suman (80 ≈ 290 km/h)
                    ParamDef.Int("seconds", 6, 1, ParamDef.NoLimit, 3, 6, 10, 20),
                },
                VehiclesNitro);

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
                ctx => Function.Call(Hash.SET_CLOCK_TIME, ((ctx.Int("hour") % 24) + 24) % 24, 0, 0));
        }
    }
}
