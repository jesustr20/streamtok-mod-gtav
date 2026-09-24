using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

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

            // --- Instantáneas

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

        /// <summary>Efecto sobre el personaje que se activa (enabled = sí) o desactiva (enabled = no).</summary>
        private static ActionDef Toggle(string id, string name, string label, Action onStart, Action onTick, Action onEnd) =>
            new ActionDef(id, name, true,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Effects.Set(id, label, ctx.Bool("enabled"), ctx.NameTag, onStart, onTick, onEnd));

        private static Ped Player => GTA.Game.Player.Character;
    }
}
