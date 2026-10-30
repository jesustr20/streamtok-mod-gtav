using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Actions
{
    internal static class VehicleActions
    {
        public static IEnumerable<ActionDef> All()
        {
            // Afecta al jugador: lo sube a un vehículo nuevo. Si ya va en uno, lo REEMPLAZA entero
            // (mismo lugar, rumbo y velocidad) y el nombre pasa a ser el del último viewer.
            // Sin params: el tipo (car/bike/boat/plane/helicopter) se sortea SIEMPRE adentro de
            // SpawnVehicle, el cliente no lo configura — igual que vehicles_remove, vehicle_repair, etc.
            yield return new ActionDef("player_vehicle", "Generar vehículo", true, null,
                ctx =>
                {
                    PlayerTransform.RequireHuman(); // un animal no puede conducir
                    SpawnVehicle(ctx, drive: true);
                });

            // No afecta al jugador: aparece al lado, con su propio nombre fijo.
            // Cada viewer que lo mande genera otro vehículo con su nombre; nunca reemplaza.
            yield return new ActionDef("spawn_vehicle", "Generar vehículo al lado", true, null,
                ctx => SpawnVehicle(ctx, drive: false));

            yield return new ActionDef("spawn_ramp", "Generar rampa", true,
                new[] { ParamDef.Int("distance", 25, 10, 60) },
                SpawnRamp);

            yield return new ActionDef("ramps_remove", "Remover rampas", false, null,
                ctx => ctx.Tracker.RemoveKind(EntityTracker.KindRamp));

            yield return new ActionDef("vehicles_remove", "Remover vehículos", false, null,
                RemoveVehicles);

            yield return new ActionDef("vehicle_repair", "Reparar vehículo", false, null,
                ctx => CurrentOrLast().Repair());

            yield return new ActionDef("vehicle_explode", "Explotar vehículo", false, null,
                ctx => CurrentOrLast().Explode());

            yield return new ActionDef("vehicle_delete", "Eliminar vehículo del jugador", false, null,
                ctx =>
                {
                    Vehicle v = CurrentOrLast();
                    ctx.Tracker.Untrack(v);
                    EntityTracker.SafeDelete(v);
                });

            yield return new ActionDef("vehicle_eject", "Sacar del vehículo", false, null,
                ctx => GTA.Game.Player.Character.Task.LeaveVehicle(Current(), false));

            yield return new ActionDef("vehicle_break", "Desarmar vehículo", false, null,
                ctx => Dismantle(CurrentOrLast()));

            // Desmantelar: lo deja en el esqueleto (sin puertas, capó, maletero, vidrios ni ruedas) y sin poder manejarse.
            yield return new ActionDef("vehicle_dismantle", "Desmantelar vehículo", false, null,
                ctx => DismantleFull(ctx, CurrentOrLast()));

            yield return new ActionDef("vehicle_burst_tires", "Romper ruedas", false, null,
                ctx =>
                {
                    Vehicle v = CurrentOrLast();
                    Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, v, true);
                    foreach (int wheel in new[] { 0, 1, 2, 3, 4, 5, 45, 47 })
                    {
                        Function.Call(Hash.SET_VEHICLE_TYRE_BURST, v, wheel, true, 1000f);
                    }
                });

            yield return new ActionDef("vehicle_tuning", "Tuning random", false,
                new[] { ParamDef.Enum("mode", "partial", "partial", "full") },
                ctx => Tune(CurrentOrLast(), ctx.Enum("mode") == "full", ctx.Rng));

            yield return new ActionDef("vehicle_boost", "Nitro", false,
                new[] { ParamDef.Int("power", 40, 10, 100) },
                ctx =>
                {
                    Vehicle v = Current();
                    float speed = v.Speed + ctx.Int("power");
                    Function.Call(Hash.SET_ENTITY_MAX_SPEED, v, Math.Max(speed + 50f, 500f)); // quita el tope de velocidad del juego
                    Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, v, speed);
                });
        }

        /// <summary>Las 5 categorías de GameData.Vehicles, para el sorteo parejo en SpawnVehicle.</summary>
        private static readonly string[] VehicleTypes = { "car", "bike", "boat", "plane", "helicopter" };

        /// <summary>
        /// Último vehículo que "Generar vehículo" (drive:true) le creó al jugador. Se guarda acá en
        /// vez de depender de player.CurrentVehicle al momento del siguiente comando: si los
        /// comandos llegan muy seguidos, el juego puede no haber terminado de registrar que el
        /// jugador ya se sentó en el anterior, y entonces player.IsInVehicle() da false — ahí, en
        /// vez de reemplazar, se creaba uno nuevo aparte y el viejo quedaba huérfano (nunca se
        /// borraba), acumulando vehículos hasta pegar en el límite. Con esta referencia propia,
        /// SIEMPRE se sabe cuál es el anterior y SIEMPRE se borra, sin importar si el jugador ya
        /// aparece sentado en él o no.
        /// </summary>
        private static Vehicle _lastPlayerVehicle;

        /// <summary>
        /// drive = false ("Generar vehículo al lado"): vehículo aparte, al frente del jugador, con su
        ///             propio nombre. No toca el vehículo en el que va el jugador.
        /// drive = true ("Generar vehículo"): es "el vehículo del jugador". Si ya va en uno, lo
        ///             REEMPLAZA entero (mismo lugar, rumbo y velocidad) y el nombre pasa a ser el
        ///             del nuevo viewer. Si va a pie, lo crea y lo sube.
        /// </summary>
        private static void SpawnVehicle(ActionContext ctx, bool drive)
        {
            // Siempre al azar entre las 5 categorías, parejo (sin configuración del cliente).
            string type = ctx.Pick(VehicleTypes);

            // 1) Cargar el modelo ANTES de tocar nada: puede tardar unos frames y, mientras,
            //    el jugador sigue manejando su auto normal.
            string model = ctx.Pick(GameData.Vehicles[type]);
            Spawner.Preload(model);

            // 2) Recién ahora se mira el estado actual (pudo cambiar durante la carga)
            //    y todo el reemplazo ocurre en este mismo frame, sin esperas.
            Ped player = GTA.Game.Player.Character;

            // "toDelete": el vehículo anterior del jugador que hay que borrar, pase lo que pase
            // (la referencia propia es la fuente de verdad — ver _lastPlayerVehicle). Independiente
            // de esto, "replaceInPlace" dice si el jugador sigue REALMENTE sentado ahí ahora mismo:
            // solo en ese caso se usa su posición/rumbo/velocidad para un reemplazo suave; si el
            // jugador se bajó y caminó lejos, el nuevo vehículo aparece junto al JUGADOR (no en el
            // lugar del viejo), igual que la primera vez.
            Vehicle toDelete = null;
            bool replaceInPlace = false;
            if (drive)
            {
                if (_lastPlayerVehicle != null && _lastPlayerVehicle.Exists())
                {
                    toDelete = _lastPlayerVehicle;
                }
                else if (player.IsInVehicle())
                {
                    toDelete = player.CurrentVehicle;
                }
                replaceInPlace = toDelete != null && player.IsInVehicle() && player.CurrentVehicle == toDelete;
            }

            if (toDelete != null)
            {
                ctx.Tracker.Untrack(toDelete);
            }

            Vehicle vehicle;

            if (replaceInPlace)
            {
                Vector3 position = toDelete.Position;
                float heading = toDelete.Heading;
                Vector3 velocity = toDelete.Velocity;

                // Al viejo solo se le quita la colisión para que el nuevo aparezca justo en su
                // lugar sin chocar. NO se oculta: en GTA ocultar un vehículo oculta también a
                // quien va dentro. No hace falta: todo ocurre en este frame, antes de dibujarse.
                // Se pasa al jugador al nuevo y RECIÉN AHÍ se borra el viejo: nunca queda a pie.
                toDelete.IsCollisionEnabled = false;

                vehicle = Spawner.SpawnVehicle(model, position, heading, placeOnGround: false);
                player.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                EntityTracker.SafeDelete(toDelete);

                // El jugador siempre visible, salvo que "Modo invisible" esté activo.
                if (!ctx.Effects.IsActive("player_invisible"))
                {
                    player.IsVisible = true;
                }

                KeepMoving(ctx, vehicle, velocity);
            }
            else if (drive && player.HeightAboveGround > 3f)
            {
                // El jugador está en el aire (lo mandaron a volar, paracaídas, caída libre): el vehículo
                // aparece AHÍ MISMO, con su misma velocidad, y no se baja al suelo.
                Vehicle current = player.IsInVehicle() ? player.CurrentVehicle : null;
                Vector3 position = current != null ? current.Position : player.Position + new Vector3(0f, 0f, 0.5f);
                Vector3 velocity = current != null ? current.Velocity : player.Velocity;
                float heading = current != null ? current.Heading : player.Heading;

                if (current != null)
                {
                    current.IsCollisionEnabled = false; // el nuevo aparece justo en su lugar sin chocar
                }

                vehicle = Spawner.SpawnVehicle(model, position, heading, placeOnGround: false);
                player.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle, true, true, false);
                vehicle.Velocity = velocity;

                if (vehicle.Model.IsPlane)
                {
                    Function.Call(Hash.CONTROL_LANDING_GEAR, vehicle, 3);
                    Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, vehicle, Math.Max(40f, velocity.Length()));
                }
                else if (vehicle.Model.IsHelicopter)
                {
                    Function.Call(Hash.SET_HELI_BLADES_FULL_SPEED, vehicle);
                }

                if (current != null)
                {
                    EntityTracker.SafeDelete(current);
                }
                if (toDelete != null && toDelete != current)
                {
                    EntityTracker.SafeDelete(toDelete);
                }
                if (!ctx.Effects.IsActive("player_invisible"))
                {
                    player.IsVisible = true;
                }
            }
            else
            {
                float distance = (type == "plane" || type == "helicopter") ? 14f : 6f;
                Vector3 position = player.Position + player.ForwardVector * distance;
                vehicle = Spawner.SpawnVehicle(model, position, player.Heading, placeOnGround: true);
                if (drive)
                {
                    player.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle, true, true, false);
                }

                // El jugador no estaba realmente adentro de "toDelete" (se bajó, o el juego aún no
                // lo había registrado): igual se borra para no dejarlo huérfano en el mundo.
                if (toDelete != null)
                {
                    EntityTracker.SafeDelete(toDelete);
                }
            }

            ctx.Tracker.Track(vehicle, ctx.NameTag, EntityTracker.KindVehicle, GameData.VehicleTagHeight[type]);

            // Queda guardado como "el último que le generamos al jugador", para el próximo
            // player_vehicle — sea cual sea el camino (reemplazo o primera vez).
            if (drive)
            {
                _lastPlayerVehicle = vehicle;
            }
        }

        /// <summary>
        /// Deja al vehículo recién creado moviéndose como el anterior:
        ///  - motor encendido al instante,
        ///  - velocidad completa (con dirección) solo en el primer frame,
        ///  - durante ~medio segundo evita que la velocidad BAJE de la que llevaba, sin forzar
        ///    la dirección: el jugador puede girar y acelerar, y la caja sube de marcha.
        /// </summary>
        private static void KeepMoving(ActionContext ctx, Vehicle vehicle, Vector3 velocity)
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle, true, true, false);

            float speed = velocity.Length();
            if (speed < 1f)
            {
                return; // estaba detenido: nada que conservar
            }

            vehicle.Velocity = velocity;

            if (vehicle.Model.IsPlane)
            {
                Function.Call(Hash.CONTROL_LANDING_GEAR, vehicle, 3); // subir el tren de aterrizaje
            }

            Ped player = GTA.Game.Player.Character;
            ctx.Scheduler.Repeat(KeepSpeedFrames, () =>
            {
                if (!vehicle.Exists() || !player.IsInVehicle(vehicle))
                {
                    return;
                }

                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle, true, true, false);
                if (vehicle.Speed < speed * 0.95f)
                {
                    Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, vehicle, speed);
                }
            });
        }

        /// <summary>Frames en los que se sostiene la velocidad tras reemplazar (~0,5 s a 60 fps).</summary>
        private const int KeepSpeedFrames = 30;

        /// <summary>
        /// Desmantela CUALQUIER vehículo (auto, moto, bici, lancha, helicóptero, avión, camión…): puertas, capó
        /// y maletero al suelo, vidrios rotos, ruedas arrancadas, rotores de helicóptero destrozados y carrocería
        /// abollada por todos lados (en aviones y lanchas también alas, popa y proa).
        /// Cada paso va protegido: lo que un tipo de vehículo no tiene simplemente se ignora.
        /// </summary>
        private static void Dismantle(Vehicle v)
        {
            Step(() =>
            {
                for (int door = 0; door <= 7; door++) // puertas, capó, maletero y compuertas (camiones, aviones…)
                {
                    Function.Call(Hash.SET_VEHICLE_DOOR_BROKEN, v, door, false);
                }
            });

            Step(() =>
            {
                for (int window = 0; window <= 7; window++)
                {
                    Function.Call(Hash.SMASH_VEHICLE_WINDOW, v, window);
                }
            });

            Step(() => BreakOffWheels(v)); // autos, motos, bicis, aviones (tren de aterrizaje)

            // Helicópteros: aspas paradas, rotor principal y de cola destruidos y cola que se puede soltar.
            Step(() =>
            {
                if (v.Model.IsHelicopter)
                {
                    Function.Call(Hash.SET_HELI_MAIN_ROTOR_HEALTH, v, 0f);
                    Function.Call(Hash.SET_HELI_TAIL_ROTOR_HEALTH, v, 0f);
                    Native("SET_HELI_BLADES_SPEED", v.Handle, 0f);
                    Native("SET_HELI_TAIL_BOOM_CAN_BREAK_OFF", v.Handle, true);
                    Native("BREAK_OFF_VEHICLE_TAIL_BOOM", v.Handle);
                }
                else if (v.Model.IsPlane)
                {
                    // Aviones: hélices y motores muertos (cada nombre solo se usa si esta versión de SHVDN lo tiene).
                    for (int engine = 0; engine < 4; engine++)
                    {
                        Native("SET_PLANE_PROPELLER_HEALTH", v.Handle, 0f);
                        Native("SET_PLANE_ENGINE_HEALTH", v.Handle, 0f);
                    }
                }
            });

            // Carrocería y motor destrozados (sin fuego ni explosión: el vehículo queda ahí, hecho pedazos).
            Step(() =>
            {
                Function.Call(Hash.SET_VEHICLE_ENGINE_HEALTH, v, 0f);
                Function.Call(Hash.SET_VEHICLE_BODY_HEALTH, v, 0f);
            });

            // Abolladuras: puntos relativos al centro del vehículo (adelante, atrás, costados, techo).
            // Se escalan al tamaño real del vehículo: así también sirven en lanchas, aviones, camiones y motos.
            Step(() =>
            {
                // Tamaño real del modelo (el método Model.GetDimensions no existe en SHVDN 3.6): se pide por native y,
                // si no estuviera, se usa el tamaño de un auto normal.
                float hx = 1.0f, hy = 2.4f, hz = 0.8f;
                Hash dimHash;
                if (Enum.TryParse("GET_MODEL_DIMENSIONS", out dimHash) && Enum.IsDefined(typeof(Hash), dimHash))
                {
                    var outMin = new OutputArgument();
                    var outMax = new OutputArgument();
                    Function.Call(dimHash, v.Model.Hash, outMin, outMax);
                    Vector3 min = outMin.GetResult<Vector3>();
                    Vector3 max = outMax.GetResult<Vector3>();
                    hx = Math.Max(0.5f, (max.X - min.X) / 2f);
                    hy = Math.Max(1.0f, (max.Y - min.Y) / 2f);
                    hz = Math.Max(0.5f, (max.Z - min.Z) / 2f);
                }

                float[,] hits =
                {
                    { 0f, hy, 0.2f }, { 0f, -hy, 0.2f },
                    { hx, hy * 0.5f, 0.2f }, { -hx, hy * 0.5f, 0.2f },
                    { hx, -hy * 0.5f, 0.2f }, { -hx, -hy * 0.5f, 0.2f },
                    { hx, 0f, 0.2f }, { -hx, 0f, 0.2f },
                    { 0f, 0f, hz }, { 0f, 0f, -hz * 0.5f },
                    { hx, hy * 0.9f, hz * 0.5f }, { -hx, hy * 0.9f, hz * 0.5f },
                    { hx, -hy * 0.9f, hz * 0.5f }, { -hx, -hy * 0.9f, hz * 0.5f },
                };
                for (int pass = 0; pass < 2; pass++) // dos pasadas: la segunda termina de hundir lo que quedó entero
                {
                    for (int i = 0; i < hits.GetLength(0); i++)
                    {
                        Function.Call(Hash.SET_VEHICLE_DAMAGE, v, hits[i, 0], hits[i, 1], hits[i, 2], 1000f, 2.5f, true);
                    }
                }
            });
        }

        /// <summary>
        /// Llama a un native POR NOMBRE solo si existe en el enum Hash de la versión de SHVDN cargada; si no, no hace nada
        /// (así no hay error de compilación ni se llama a un hash desconocido). Acepta int, float y bool.
        /// </summary>
        private static bool Native(string name, params object[] args)
        {
            Hash hash;
            if (!Enum.TryParse(name, out hash) || !Enum.IsDefined(typeof(Hash), hash))
            {
                return false;
            }

            var input = new InputArgument[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] is int) input[i] = (int)args[i];
                else if (args[i] is float) input[i] = (float)args[i];
                else if (args[i] is bool) input[i] = (bool)args[i];
                else return false;
            }
            Function.Call(hash, input);
            return true;
        }

        /// <summary>
        /// Desmantelar: todo lo del desarme y además las RUEDAS SALEN DESPEDIDAS. En este juego las ruedas solo se arrancan
        /// cuando el vehículo explota con "las ruedas pueden salir al explotar" activado, así que: se suelta todo lo demás,
        /// el jugador sale del vehículo (protegido de la explosión), el vehículo explota y las ruedas vuelan; después se apaga
        /// el fuego y queda el esqueleto (sin puertas, capó, maletero, vidrios ni ruedas) inservible.
        /// </summary>
        private static void DismantleFull(ActionContext ctx, Vehicle v)
        {
            LogPartsDiagnostic();

            Dismantle(v); // puertas, capó, maletero, vidrios, abolladuras, motor y carrocería a 0

            Ped player = GTA.Game.Player.Character;
            int vehicleHandle = v.Handle;

            Step(() =>
            {
                Native("SET_VEHICLE_WHEELS_CAN_BREAK", vehicleHandle, true);
                Native("SET_VEHICLE_WHEELS_CAN_BREAK_OFF_WHEN_BLOW_UP", vehicleHandle, true);
                Function.Call(Hash.SET_VEHICLE_UNDRIVEABLE, v, true);
            });

            // El jugador sale del vehículo y queda a prueba de explosión y fuego mientras dura el efecto.
            Step(() =>
            {
                player.IsExplosionProof = true;
                player.IsFireProof = true;
                if (player.IsInVehicle(v))
                {
                    Function.Call(Hash.TASK_LEAVE_VEHICLE, player, v, 16); // 16 = salir de golpe
                }
            });

            string key = "dismantle_" + vehicleHandle;

            // 1 segundo después (ya fuera): explota, las ruedas salen despedidas.
            ctx.Scheduler.RepeatFor(key, 1, () => { }, () =>
            {
                Step(() =>
                {
                    Vehicle target = v; // el mismo objeto: sirve mientras el vehículo exista (Exists())
                    if (target.Exists())
                    {
                        Native("EXPLODE_VEHICLE", vehicleHandle, true, true);
                    }
                });

                // 4 segundos después: se apaga el fuego y el jugador vuelve a ser vulnerable.
                ctx.Scheduler.RepeatFor(key + "_end", 4, () => { }, () =>
                {
                    Step(() =>
                    {
                        Vehicle target = v; // el mismo objeto: sirve mientras el vehículo exista (Exists())
                        if (target.Exists())
                        {
                            Function.Call(Hash.STOP_ENTITY_FIRE, target);
                            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, target, false, true, true);
                        }
                        Ped p = GTA.Game.Player.Character;
                        p.IsExplosionProof = false;
                        p.IsFireProof = false;
                    });
                });
            });
        }

        private static bool _loggedPartsDiag;

        /// <summary>
        /// Diagnóstico (una sola vez por sesión): qué natives y métodos tiene ESTA versión de SHVDN para ruedas y piezas
        /// sueltas. Sirve para saber cómo arrancar las ruedas sin adivinar nombres.
        /// </summary>
        private static void LogPartsDiagnostic()
        {
            if (_loggedPartsDiag)
            {
                return;
            }
            _loggedPartsDiag = true;

            try
            {
                var hashes = new List<string>();
                foreach (string name in Enum.GetNames(typeof(Hash)))
                {
                    if (name.Contains("VEHICLE_WHEEL") || name.Contains("BREAK_OFF") || name.Contains("DETACH_VEHICLE") ||
                        name.Contains("TYRE_RADIUS") || name.Contains("WHEEL_SIZE") || name.Contains("WHEEL_WIDTH") || name.Contains("RIM_RADIUS") ||
                        name.Contains("VEHICLE_DOOR_BROKEN") || name.Contains("FIX_VEHICLE_WHEEL"))
                    {
                        hashes.Add(name);
                    }
                }
                PlayerTransform.Log("Desmantelar diag: Hash (" + hashes.Count + ") = " + string.Join(", ", hashes));

                var vehMethods = new List<string>();
                foreach (System.Reflection.MethodInfo m in typeof(Vehicle).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    string n = m.Name.ToLowerInvariant();
                    if (n.Contains("wheel") || n.Contains("break") || n.Contains("detach") || n.Contains("part") || n.Contains("door") || n.Contains("bumper"))
                    {
                        vehMethods.Add(m.Name + "(" + m.GetParameters().Length + ")");
                    }
                }
                PlayerTransform.Log("Desmantelar diag: Vehicle métodos = " + string.Join(", ", vehMethods));

                var collMethods = new List<string>();
                foreach (System.Reflection.MethodInfo m in typeof(VehicleWheelCollection).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    collMethods.Add(m.Name + "(" + m.GetParameters().Length + ")");
                }
                PlayerTransform.Log("Desmantelar diag: VehicleWheelCollection métodos = " + string.Join(", ", collMethods));
            }
            catch (Exception ex)
            {
                PlayerTransform.Log("Desmantelar diag: " + ex.Message);
            }
        }

        /// <summary>Ejecuta un paso del desarme sin que un vehículo "raro" (sin ruedas, sin puertas…) corte el resto.</summary>
        private static void Step(Action step)
        {
            try { step(); }
            catch (Exception ex) { PlayerTransform.Log($"Desarmar: paso omitido ({ex.Message})"); }
        }

        /// <summary>
        /// Rampa delante del jugador (o de su vehículo), mirando hacia donde va, a "distance" metros.
        /// </summary>
        private static void SpawnRamp(ActionContext ctx)
        {
            ctx.Tracker.ClampProps(1);

            Ped player = GTA.Game.Player.Character;
            Entity reference = player.IsInVehicle() ? (Entity)player.CurrentVehicle : player;
            Vector3 position = reference.Position + reference.ForwardVector * ctx.Int("distance");

            float ground = World.GetGroundHeight(position + new Vector3(0f, 0f, 5f));
            if (ground > 0f)
            {
                position.Z = ground;
            }

            // La rampa queda mirando hacia donde va el jugador: se sube de frente.
            Prop ramp = Spawner.SpawnProp(RampModel, position, reference.Heading + RampHeadingOffset);
            ctx.Tracker.Track(ramp, ctx.NameTag, EntityTracker.KindRamp, 2.5f);
        }

        private const string RampModel = "prop_mp_ramp_03";

        /// <summary>Ajuste de orientación del modelo de rampa (0 o 180 según el modelo).</summary>
        private const float RampHeadingOffset = 0f;

        /// <summary>
        /// VehicleWheel.BreakOff no existe en SHVDN 3.6 (contra el que compilamos), pero sí en el
        /// SHVDN Enhanced que corre en el juego (API 3.9). Se busca en tiempo de ejecución:
        /// si está, las ruedas salen volando; si no, se revientan y el auto queda sobre las llantas.
        /// NO llamar natives por su código crudo: si ScriptHookV no lo reconoce, corta el juego.
        /// </summary>
        private static readonly System.Reflection.MethodInfo WheelBreakOff = FindWheelBreakOff();

        /// <summary>
        /// Busca en VehicleWheel el método que arranca la rueda, sea cual sea su firma en la versión de SHVDN cargada:
        /// BreakOff(), BreakOff(bool, bool), Detach… Se elige el de nombre BreakOff/Detach/TearOff con menos parámetros.
        /// </summary>
        private static System.Reflection.MethodInfo FindWheelBreakOff()
        {
            System.Reflection.MethodInfo best = null;
            foreach (System.Reflection.MethodInfo m in typeof(VehicleWheel).GetMethods())
            {
                string n = m.Name.ToLowerInvariant();
                if (!(n.Contains("breakoff") || n.Contains("detach") || n.Contains("tearoff") || n.Contains("remove")))
                {
                    continue;
                }
                if (best == null || m.GetParameters().Length < best.GetParameters().Length)
                {
                    best = m;
                }
            }
            return best;
        }

        private static object[] DefaultArgs(System.Reflection.MethodInfo m)
        {
            System.Reflection.ParameterInfo[] ps = m.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type t = ps[i].ParameterType;
                args[i] = t == typeof(bool) ? (object)(i == 0) : t.IsValueType ? Activator.CreateInstance(t) : null;
            }
            return args;
        }

        private static bool _loggedWheelMode;

        private static void BreakOffWheels(Vehicle v)
        {
            if (!_loggedWheelMode)
            {
                _loggedWheelMode = true;
                // Diagnóstico: qué métodos tiene VehicleWheel en la versión de SHVDN que corre en el juego.
                var names = new List<string>();
                foreach (System.Reflection.MethodInfo m in typeof(VehicleWheel).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    names.Add(m.Name + "(" + m.GetParameters().Length + ")");
                }
                PlayerTransform.Log("Desarmar: VehicleWheel métodos = " + string.Join(", ", names));
                PlayerTransform.Log(WheelBreakOff != null
                    ? "Desarmar: se usará " + WheelBreakOff.Name + " para soltar las ruedas"
                    : "Desarmar: SHVDN sin método para soltar ruedas, las ruedas se revientan (plan B)");
            }

            if (WheelBreakOff == null)
            {
                // Si esta versión de SHVDN trae el native de arrancar ruedas, se usa por nombre en las 8 posiciones.
                for (int wheel = 0; wheel < 8; wheel++)
                {
                    Native("BREAK_OFF_VEHICLE_WHEEL", v.Handle, wheel, true, false, true, false);
                }
                Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, v, true);
                foreach (int wheel in new[] { 0, 1, 2, 3, 4, 5, 45, 47 })
                {
                    Function.Call(Hash.SET_VEHICLE_TYRE_BURST, v, wheel, true, 1000f);
                }
                return;
            }

            foreach (VehicleWheel wheel in v.Wheels.GetAllWheels())
            {
                WheelBreakOff.Invoke(wheel, DefaultArgs(WheelBreakOff));
            }
        }

        /// <summary>
        /// full = todas las piezas al máximo; partial = la mitad de las piezas, al azar.
        /// Siempre: turbo, xenón y colores al azar.
        /// </summary>
        private static void Tune(Vehicle v, bool full, Random rng)
        {
            Function.Call(Hash.SET_VEHICLE_MOD_KIT, v, 0);

            for (int type = 0; type < 50; type++)
            {
                if (type >= 17 && type <= 22)
                {
                    continue; // 17-22 son interruptores (turbo, xenón…), no piezas
                }

                int count = Function.Call<int>(Hash.GET_NUM_VEHICLE_MODS, v, type);
                if (count <= 0 || (!full && rng.NextDouble() < 0.5))
                {
                    continue;
                }

                Function.Call(Hash.SET_VEHICLE_MOD, v, type, full ? count - 1 : rng.Next(count), false);
            }

            Function.Call(Hash.TOGGLE_VEHICLE_MOD, v, 18, true); // turbo
            Function.Call(Hash.TOGGLE_VEHICLE_MOD, v, 22, true); // xenón
            Function.Call(Hash.SET_VEHICLE_COLOURS, v, rng.Next(0, 160), rng.Next(0, 160));
            if (full)
            {
                Function.Call(Hash.SET_VEHICLE_WINDOW_TINT, v, 1);
            }
        }

        /// <summary>El vehículo en el que va el jugador ahora.</summary>
        private static Vehicle Current()
        {
            Vehicle v = GTA.Game.Player.Character.CurrentVehicle;
            if (v == null || !v.Exists())
            {
                throw new ActionException("El jugador no está en un vehículo");
            }
            return v;
        }

        /// <summary>
        /// Borra: los vehículos generados por el mod, el vehículo del jugador (el que maneja o el último que usó)
        /// y los vehículos VACÍOS a su alrededor (también los que ya estaban antes de cargar el mod, o que el
        /// jugador agarró). No toca a los autos con gente adentro. Con una carrera activa solo borra los generados.
        /// </summary>
        private static void RemoveVehicles(ActionContext ctx)
        {
            int removed = ctx.Tracker.RemoveKind(EntityTracker.KindVehicle);

            if (!ctx.Race.IsActive)
            {
                Ped player = GTA.Game.Player.Character;
                Vehicle mine = player.CurrentVehicle ?? player.LastVehicle;
                if (mine != null && !mine.Exists())
                {
                    mine = null;
                }

                var toDelete = new List<Vehicle>();
                if (mine != null)
                {
                    toDelete.Add(mine);
                }

                foreach (Vehicle v in World.GetNearbyVehicles(player.Position, NearbyRemoveRadius))
                {
                    if (v != null && v.Exists() && (mine == null || v.Handle != mine.Handle) && IsEmptyVehicle(v))
                    {
                        toDelete.Add(v);
                    }
                }

                foreach (Vehicle v in toDelete)
                {
                    ctx.Tracker.Untrack(v);
                    EntityTracker.SafeDelete(v);
                    removed++;
                }

                if (_lastPlayerVehicle != null && !_lastPlayerVehicle.Exists())
                {
                    _lastPlayerVehicle = null;
                }
            }

            PlayerTransform.Log($"Remover vehículos: {removed} borrados");
            if (removed == 0)
            {
                throw new ActionException("No hay vehículos para borrar");
            }
        }

        private const float NearbyRemoveRadius = 150f;

        /// <summary>Sin conductor ni pasajeros.</summary>
        private static bool IsEmptyVehicle(Vehicle v)
        {
            return Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, v, -1, false)
                && Function.Call<int>(Hash.GET_VEHICLE_NUMBER_OF_PASSENGERS, v) == 0;
        }

        /// <summary>El vehículo actual o, si va a pie, el último que usó.</summary>
        private static Vehicle CurrentOrLast()
        {
            Ped player = GTA.Game.Player.Character;
            Vehicle v = player.CurrentVehicle ?? player.LastVehicle;
            if (v == null || !v.Exists())
            {
                throw new ActionException("El jugador no tiene un vehículo");
            }
            return v;
        }
    }
}
