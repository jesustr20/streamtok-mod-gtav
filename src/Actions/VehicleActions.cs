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
                ctx => ctx.Tracker.RemoveKind(EntityTracker.KindVehicle));

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
                    float speed = Math.Min(v.Speed + ctx.Int("power"), 120f);
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
        /// Desmantela el vehículo: puertas, capó y maletero al suelo, todos los vidrios rotos,
        /// ruedas arrancadas (quedan rodando) y carrocería abollada por todos lados.
        /// </summary>
        private static void Dismantle(Vehicle v)
        {
            for (int door = 0; door <= 5; door++) // 0-3 puertas, 4 capó, 5 maletero
            {
                Function.Call(Hash.SET_VEHICLE_DOOR_BROKEN, v, door, false);
            }

            for (int window = 0; window <= 7; window++)
            {
                Function.Call(Hash.SMASH_VEHICLE_WINDOW, v, window);
            }

            BreakOffWheels(v);

            // Abolladuras: puntos relativos al centro del vehículo (adelante, atrás, costados, techo).
            float[,] hits =
            {
                { 0f, 2.2f, 0.2f }, { 0f, -2.2f, 0.2f },
                { 1.0f, 1.0f, 0.2f }, { -1.0f, 1.0f, 0.2f },
                { 1.0f, -1.0f, 0.2f }, { -1.0f, -1.0f, 0.2f },
                { 0f, 0f, 1.0f },
            };
            for (int i = 0; i < hits.GetLength(0); i++)
            {
                Function.Call(Hash.SET_VEHICLE_DAMAGE, v, hits[i, 0], hits[i, 1], hits[i, 2], 1000f, 1.5f, true);
            }
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
        private static readonly System.Reflection.MethodInfo WheelBreakOff =
            typeof(VehicleWheel).GetMethod("BreakOff", new[] { typeof(bool), typeof(bool) })
            ?? typeof(VehicleWheel).GetMethod("BreakOff", Type.EmptyTypes);

        private static bool _loggedWheelMode;

        private static void BreakOffWheels(Vehicle v)
        {
            if (!_loggedWheelMode)
            {
                _loggedWheelMode = true;
                PlayerTransform.Log(WheelBreakOff != null
                    ? "Desarmar: SHVDN soporta BreakOff, las ruedas se sueltan"
                    : "Desarmar: SHVDN sin BreakOff, las ruedas se revientan (plan B)");
            }

            if (WheelBreakOff == null)
            {
                Function.Call(Hash.SET_VEHICLE_TYRES_CAN_BURST, v, true);
                foreach (int wheel in new[] { 0, 1, 2, 3, 4, 5, 45, 47 })
                {
                    Function.Call(Hash.SET_VEHICLE_TYRE_BURST, v, wheel, true, 1000f);
                }
                return;
            }

            object[] args = WheelBreakOff.GetParameters().Length == 2
                ? new object[] { true, false } // deja escombros; la rueda queda en el mundo
                : new object[0];

            foreach (VehicleWheel wheel in v.Wheels.GetAllWheels())
            {
                WheelBreakOff.Invoke(wheel, args);
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
