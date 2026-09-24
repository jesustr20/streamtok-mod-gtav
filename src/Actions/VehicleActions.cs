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
            yield return new ActionDef("player_vehicle", "Generar vehículo", true,
                new[] { ParamDef.Enum("type", "car", "car", "bike", "boat", "plane", "random") },
                ctx => SpawnVehicle(ctx, drive: true));

            // No afecta al jugador: aparece al lado, con su propio nombre fijo.
            // Cada viewer que lo mande genera otro vehículo con su nombre; nunca reemplaza.
            yield return new ActionDef("spawn_vehicle", "Generar carro al lado", true,
                new[] { ParamDef.Enum("type", "car", "car", "bike", "boat", "plane", "random") },
                ctx => SpawnVehicle(ctx, drive: false));

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
                ctx =>
                {
                    Vehicle v = CurrentOrLast();
                    for (int door = 0; door <= 5; door++)
                    {
                        Function.Call(Hash.SET_VEHICLE_DOOR_BROKEN, v, door, false);
                    }
                    for (int window = 0; window <= 7; window++)
                    {
                        Function.Call(Hash.SMASH_VEHICLE_WINDOW, v, window);
                    }
                });

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

            yield return new ActionDef("vehicle_boost", "Nitro", false,
                new[] { ParamDef.Int("power", 40, 10, 100) },
                ctx =>
                {
                    Vehicle v = Current();
                    float speed = Math.Min(v.Speed + ctx.Int("power"), 120f);
                    Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, v, speed);
                });
        }

        /// <summary>
        /// drive = false ("Generar carro al lado"): vehículo aparte, al frente del jugador, con su
        ///             propio nombre. No toca el vehículo en el que va el jugador.
        /// drive = true ("Generar vehículo"): es "el vehículo del jugador". Si ya va en uno, lo
        ///             REEMPLAZA entero (mismo lugar, rumbo y velocidad) y el nombre pasa a ser el
        ///             del nuevo viewer. Si va a pie, lo crea y lo sube.
        /// </summary>
        private static void SpawnVehicle(ActionContext ctx, bool drive)
        {
            string type = ctx.Enum("type");
            if (type == "random")
            {
                // Al azar solo terrestres: un barco o avión en plena ciudad no sirve de mucho.
                type = ctx.Pick(new[] { "car", "car", "bike" });
            }

            // 1) Cargar el modelo ANTES de tocar nada: puede tardar unos frames y, mientras,
            //    el jugador sigue manejando su auto normal.
            string model = ctx.Pick(GameData.Vehicles[type]);
            Spawner.Preload(model);

            // 2) Recién ahora se mira el estado actual (pudo cambiar durante la carga)
            //    y todo el reemplazo ocurre en este mismo frame, sin esperas.
            Ped player = GTA.Game.Player.Character;
            Vehicle old = drive && player.IsInVehicle() ? player.CurrentVehicle : null;

            if (old != null)
            {
                // Libera su cupo antes de revisar el límite: se va a reemplazar.
                ctx.Tracker.Untrack(old);
            }
            ctx.Tracker.ClampToLimit(EntityTracker.KindVehicle, 1);

            Vehicle vehicle;

            if (old != null)
            {
                Vector3 position = old.Position;
                float heading = old.Heading;
                Vector3 velocity = old.Velocity;

                // El viejo se vuelve invisible y sin colisión para que el nuevo aparezca justo
                // en su lugar sin chocar. Se pasa al jugador al nuevo y RECIÉN AHÍ se borra el
                // viejo: el jugador nunca queda a pie y no pierde la inercia.
                old.IsCollisionEnabled = false;
                old.IsVisible = false;

                vehicle = Spawner.SpawnVehicle(model, position, heading, placeOnGround: false);
                player.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                EntityTracker.SafeDelete(old);

                KeepMoving(ctx, vehicle, velocity);
            }
            else
            {
                float distance = type == "plane" ? 14f : 6f;
                Vector3 position = player.Position + player.ForwardVector * distance;
                vehicle = Spawner.SpawnVehicle(model, position, player.Heading, placeOnGround: true);
                if (drive)
                {
                    player.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                    Function.Call(Hash.SET_VEHICLE_ENGINE_ON, vehicle, true, true, false);
                }
            }

            ctx.Tracker.Track(vehicle, ctx.NameTag, EntityTracker.KindVehicle, GameData.VehicleTagHeight[type]);
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
