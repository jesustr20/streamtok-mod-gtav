using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;
using System.Linq;

namespace StreamTok.GtaV.Actions
{
    internal static class PlayerActions
    {
        public static IEnumerable<ActionDef> All()
        {
            yield return new ActionDef("player_health", "Vida del jugador", false,
                new[]
                {
                    ParamDef.Enum("mode", "add", "add", "remove"),
                    ParamDef.Int("amount", 25, 1, 100),
                },
                ctx =>
                {
                    Ped p = Player;
                    int amount = ctx.Int("amount");
                    p.Health = ctx.Enum("mode") == "add"
                        ? Math.Min(p.MaxHealth, p.Health + amount)
                        : Math.Max(0, p.Health - amount);
                });

            yield return new ActionDef("player_kill", "Matar jugador", false, null,
                ctx => Player.Kill());

            // --- Efectos sin duración: quedan hasta que otra acción los desactive (enabled = no).
            //     Llevan el nombre del viewer encima del personaje; el último reemplaza al anterior.

            yield return Toggle("player_invincible", "Inmortalidad", "Inmortal",
                onStart: () => GTA.Game.Player.IsInvincible = true,
                onTick: () => GTA.Game.Player.IsInvincible = true,
                onEnd: () => GTA.Game.Player.IsInvincible = false);

            yield return Toggle("player_invisible", "Modo invisible", "Invisible",
                onStart: () => Player.IsVisible = false,
                onTick: () => Player.IsVisible = false,
                onEnd: () => Player.IsVisible = true);

            yield return Toggle("player_night_vision", "Visión nocturna", "Visión nocturna",
                onStart: () => Function.Call(Hash.SET_NIGHTVISION, true),
                onTick: null,
                onEnd: () => Function.Call(Hash.SET_NIGHTVISION, false));

            yield return Toggle("player_super_jump", "Súper salto", "Súper salto",
                onStart: null,
                // El juego exige activarlo en cada frame mientras esté activo.
                onTick: () => Function.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, GTA.Game.Player.Handle),
                onEnd: null);

            yield return Toggle("player_drunk", "Modo ebrio", "Ebrio",
                onStart: () => SetDrunk(true),
                onTick: null,
                onEnd: () => SetDrunk(false));

            // "Convertir en animal" (player_transform) queda FUERA del catálogo: el cambio de modelo
            // del jugador congela el juego en GTA V Legacy 1.0.3889.0 con SHVDNE 1.1.0.6.
            // El código (Transform, PlayerTransform) se conserva para retomarlo más adelante.

            // --- Instantáneas

            yield return new ActionDef("teleport", "Teletransporte", false,
                new[]
                {
                    ParamDef.Enum("mode", "location", "up", "random", "location", "next", "previous"),
                    ParamDef.Enum("location", "maze_bank", GameData.Keys(GameData.Locations)),
                    ParamDef.Int("height", 200, 50, 1500),
                },
                Teleport);


            yield return new ActionDef("teleport_random", "TP random", false, null, TeleportRandom);

            yield return new ActionDef("teleport_location", "TP ubicación", false,
                new[] { ParamDef.Enum("location", "maze_bank", GameData.Keys(GameData.Locations)) },
                ctx => TeleportTo(GameData.Locations[ctx.Enum("location")]));

            // Volantín: salta a la altura pedida y da la(s) vuelta(s) completas en el aire; cae de pie.
            yield return new ActionDef("player_flip", "Volantín", false,
                new[]
                {
                    ParamDef.Enum("direction", "back", "back", "front", "left", "right"),
                    ParamDef.Int("height", 6, 1, ParamDef.NoLimit, 3, 6, 10, 20, 50),   // metros que sube
                    ParamDef.Int("flips", 1, 1, ParamDef.NoLimit, 1, 2, 3, 5),
                },
                ctx =>
                {
                    Ped p = Player;
                    bool inVehicle = p.IsInVehicle();
                    Entity target = inVehicle ? (Entity)p.CurrentVehicle : p;
                    string direction = ctx.Enum("direction");
                    int flips = Math.Max(1, ctx.Int("flips"));

                    // Física: para subir h metros hace falta v = sqrt(2·g·h); en el aire está 2·v/g segundos.
                    const float gravity = 9.81f;
                    float height = Math.Max(1, ctx.Int("height"));
                    float up = (float)Math.Sqrt(2f * gravity * height);
                    float airMs = 2f * up / gravity * 1000f;
                    // Las vueltas ocupan ~80 % del vuelo para que termine de girar antes de tocar el suelo.
                    float spinMs = airMs * 0.8f;

                    Vector3 v = target.Velocity;
                    target.Velocity = new Vector3(v.X, v.Y, Math.Max(v.Z, 0f) + up);

                    float heading = target.Rotation.Z;
                    int startHealth = p.Health;
                    int start = GTA.Game.GameTime;
                    if (!inVehicle)
                    {
                        // A pie: que no se desplome como un muñeco al caer ni se lastime con la caída.
                        Function.Call(Hash.SET_PED_CAN_RAGDOLL, p, false);
                    }

                    ctx.Scheduler.Until(() =>
                    {
                        if (target == null || !target.Exists())
                        {
                            Function.Call(Hash.SET_PED_CAN_RAGDOLL, p, true);
                            return true;
                        }

                        int elapsed = GTA.Game.GameTime - start;
                        float t = Math.Min(1f, elapsed / spinMs);
                        if (t < 1f)
                        {
                            float angle = 360f * flips * t;
                            float pitch = 0f, roll = 0f;
                            switch (direction)
                            {
                                case "front": pitch = -angle; break;
                                case "left": roll = -angle; break;
                                case "right": roll = angle; break;
                                default: pitch = angle; break;   // back
                            }
                            Function.Call(Hash.SET_ENTITY_ROTATION, target, pitch, roll, heading, 2, true);
                            return false;
                        }

                        // Terminó de girar: derecho y mirando como antes, hasta que aterrice.
                        Function.Call(Hash.SET_ENTITY_ROTATION, target, 0f, 0f, heading, 2, true);
                        if (elapsed < airMs + 600f)
                        {
                            return false;
                        }

                        if (!inVehicle)
                        {
                            if (p.Health < startHealth) p.Health = startHealth; // sin daño por la caída
                            Function.Call(Hash.SET_PED_CAN_RAGDOLL, p, true);
                        }
                        return true;
                    });
                });

            yield return new ActionDef("player_jump", "Salto", false,
                new[] { ParamDef.Int("force", 15, 5, 60) },
                ctx =>
                {
                    Ped p = Player;
                    Entity target = p.IsInVehicle() ? (Entity)p.CurrentVehicle : p;
                    if (!p.IsInVehicle())
                    {
                        p.Ragdoll(2500);
                    }
                    Vector3 v = target.Velocity;
                    target.Velocity = new Vector3(v.X, v.Y, ctx.Int("force"));
                });

            yield return new ActionDef("player_skydive", "Paracaidismo", false,
                new[] { ParamDef.Int("height", 400, 100, 1500) },
                ctx =>
                {
                    PlayerTransform.RequireHuman(); // el paracaídas es un arma: un animal no puede usarlo
                    Ped p = Player;
                    if (p.IsInVehicle())
                    {
                        // Sacarlo al instante: si no, se teletransporta el vehículo entero.
                        Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, p);
                    }
                    p.Weapons.Give(WeaponHash.Parachute, 1, false, true);
                    p.Position = p.Position + new Vector3(0f, 0f, ctx.Int("height"));
                    p.Task.Skydive();
                });

            yield return new ActionDef("player_random_outfit", "Ropa random", false, null,
                ctx =>
                {
                    Function.Call(Hash.SET_PED_RANDOM_COMPONENT_VARIATION, Player, 0);
                    Function.Call(Hash.SET_PED_RANDOM_PROPS, Player);
                });
        }

        private static void Transform(ActionContext ctx)
        {
            string key = ctx.Enum("animal");
            string name = key == "random" ? ctx.Pick(GameData.TransformAnimals.Keys.ToList()) : key;
            string model = GameData.TransformAnimals[name];
            PlayerEffects effects = ctx.Effects;
            FrameScheduler scheduler = ctx.Scheduler;
            bool diedTransformed = false;

            effects.Set("transform", $"Animal ({name})", ctx.Bool("enabled"), ctx.NameTag,
                onStart: () => PlayerTransform.TransformTo(model, scheduler),
                onTick: () =>
                {
                    // Si muere siendo animal, vuelve a su personaje apenas reaparece.
                    Ped p = GTA.Game.Player.Character;
                    if (p.IsDead) diedTransformed = true;
                    else if (diedTransformed) effects.Stop("transform");
                },
                onEnd: () => PlayerTransform.Restore(scheduler),
                onRepeat: () => PlayerTransform.TransformTo(model, scheduler));
        }

        private static void SetDrunk(bool drunk)
        {
            const string clipSet = "move_m@drunk@verydrunk";
            Ped p = Player;

            // La animación de caminar ebrio es de humano: en un animal solo cámara y filtro.
            bool human = !PlayerTransform.IsTransformed;

            if (drunk)
            {
                Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "DRUNK_SHAKE", 1.5f);
                Function.Call(Hash.SET_TIMECYCLE_MODIFIER, "Drunk");
                if (!human)
                {
                    return;
                }

                Function.Call(Hash.REQUEST_CLIP_SET, clipSet);
                for (int i = 0; i < 100 && !Function.Call<bool>(Hash.HAS_CLIP_SET_LOADED, clipSet); i++)
                {
                    Script.Wait(0);
                }
                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, p, clipSet, 1.0f);
                Function.Call(Hash.SET_PED_IS_DRUNK, p, true);
            }
            else
            {
                Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
                Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                if (human)
                {
                    Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, p, 0.0f);
                    Function.Call(Hash.SET_PED_IS_DRUNK, p, false);
                }
            }
        }

        /// <summary>Índice del último lugar visitado con "siguiente"/"anterior".</summary>
        private static int _locationCursor = -1;

        /// <summary>
        /// Si va en un vehículo, se teletransporta CON el vehículo. "up" lo sube en el aire
        /// (a pie, con paracaídas).
        /// </summary>
        private static void Teleport(ActionContext ctx)
        {
            Ped p = Player;
            Entity target = p.IsInVehicle() ? (Entity)p.CurrentVehicle : p;
            string[] keys = GameData.Keys(GameData.Locations);
            Vector3 destination;

            switch (ctx.Enum("mode"))
            {
                case "up":
                    if (!p.IsInVehicle() && !PlayerTransform.IsTransformed)
                    {
                        p.Weapons.Give(WeaponHash.Parachute, 1, false, true);
                    }
                    target.Position = target.Position + new Vector3(0f, 0f, ctx.Int("height"));
                    return;

                case "random":
                    destination = GameData.Locations[ctx.Pick(keys)];
                    break;

                case "next":
                    _locationCursor = (_locationCursor + 1) % keys.Length;
                    destination = GameData.Locations[keys[_locationCursor]];
                    break;

                case "previous":
                    _locationCursor = (_locationCursor - 1 + keys.Length) % keys.Length;
                    destination = GameData.Locations[keys[_locationCursor]];
                    break;

                default:
                    destination = GameData.Locations[ctx.Enum("location")];
                    break;
            }

            // Pide al juego cargar el suelo del destino para no caer a través del mapa.
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
            target.Position = destination + new Vector3(0f, 0f, 1f);
            target.Velocity = Vector3.Zero;
        }

        /// <summary>Teletransporta al jugador (con su vehículo) a un punto, pidiendo antes el suelo del destino.</summary>
        private static void TeleportTo(Vector3 destination)
        {
            Ped p = Player;
            Entity target = p.IsInVehicle() ? (Entity)p.CurrentVehicle : p;
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, destination.X, destination.Y, destination.Z);
            target.Position = destination + new Vector3(0f, 0f, 1f);
            target.Velocity = Vector3.Zero;
        }

        /// <summary>
        /// Cualquier calle del mapa al azar: se sortea un punto y se usa la calle más cercana (así el suelo
        /// existe siempre). Si cae en el mar o en un monte sin calles, se sortea otro.
        /// </summary>
        private static void TeleportRandom(ActionContext ctx)
        {
            for (int attempt = 0; attempt < 25; attempt++)
            {
                float x = -3300f + (float)ctx.Rng.NextDouble() * 6600f;
                float y = -3200f + (float)ctx.Rng.NextDouble() * 10400f;
                var node = new OutputArgument();
                bool found = Function.Call<bool>(Hash.GET_CLOSEST_VEHICLE_NODE, x, y, 100f, node, 1, 3.0f, 0f);
                if (!found)
                {
                    continue;
                }
                Vector3 pos = node.GetResult<Vector3>();
                if (new Vector2(pos.X - x, pos.Y - y).Length() > 350f)
                {
                    continue; // el punto estaba en el mar o lejos de toda calle
                }
                TeleportTo(pos);
                return;
            }
            string[] keys = GameData.Keys(GameData.Locations);
            TeleportTo(GameData.Locations[ctx.Pick(keys)]);
        }

        /// <summary>Efecto sobre el personaje que se activa (enabled = sí) o desactiva (enabled = no).</summary>
        private static ActionDef Toggle(string id, string name, string label, Action onStart, Action onTick, Action onEnd) =>
            new ActionDef(id, name, true,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Effects.Set(id, label, ctx.Bool("enabled"), ctx.NameTag, onStart, onTick, onEnd));

        private static Ped Player => GTA.Game.Player.Character;
    }
}
