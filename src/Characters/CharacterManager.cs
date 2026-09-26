using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Characters
{
    /// <summary>
    /// Aplica y mantiene las habilidades de los personajes spawneados:
    ///  - super_strength: no se cae, resiste explosiones y fuego, y cada golpe al jugador lo lanza lejos.
    ///  - tank: blindaje, no se cae, resiste explosiones, camina lento y muestra barra de vida de jefe.
    ///  - gunslinger: puntería casi perfecta, dispara rápido, no recarga y avanza agresivo.
    ///  - aura: brillo de color alrededor del cuerpo.
    /// </summary>
    internal sealed class CharacterManager
    {
        private const int MaxBossBars = 3;

        private readonly List<Active> _active = new List<Active>();
        private readonly GTA.UI.TextElement _barText = new GTA.UI.TextElement(
            "", PointF.Empty, 0.38f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.ContainerElement _barBack = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.FromArgb(170, 0, 0, 0));
        private readonly GTA.UI.ContainerElement _barFill = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.FromArgb(230, 200, 30, 30));

        /// <summary>Barra chica sobre la cabeza (debajo del nombre del viewer).</summary>
        private readonly GTA.UI.ContainerElement _headBack = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.FromArgb(170, 0, 0, 0));
        private readonly GTA.UI.ContainerElement _headFill = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.Green);
        private readonly GTA.UI.TextElement _headPercent = new GTA.UI.TextElement(
            "", PointF.Empty, 0.27f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Left, true, true);

        /// <summary>A más distancia no se dibuja la barra de la cabeza (igual que el nombre).</summary>
        private const float HeadBarDistance = 50f;

        /// <summary>Configura un personaje recién creado según sus habilidades y lo registra.</summary>
        public void Setup(Ped ped, CharacterDef def, bool hostile, string nameTag)
        {
            // Apariencia por defecto del modelo: GTA elige piezas de ropa al azar y muchos add-on
            // peds solo traen completa la variante por defecto (si no, faltan zapatos, manos…).
            Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, ped);

            // Vida: los peds mueren al llegar a 100, por eso se suma 100 a la vida configurada.
            ped.MaxHealth = def.Health + 100;
            ped.Health = def.Health + 100;

            bool strong = def.Has("super_strength");
            bool tank = def.Has("tank");

            if (strong || tank)
            {
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped, false);
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, ped, false);
                // Inmune a fuego, explosiones y choques; SÍ recibe balas y golpes (si no, no se le gana).
                Function.Call(Hash.SET_ENTITY_PROOFS, ped, false, true, true, true, false, false, false, false);
            }

            if (tank)
            {
                ped.Armor = 100;
            }

            if (def.Has("gunslinger"))
            {
                Function.Call(Hash.SET_PED_ACCURACY, ped, 95);
                Function.Call(Hash.SET_PED_SHOOT_RATE, ped, 1000);
                Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped, 2);   // profesional
                Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped, 2);  // ofensivo: avanza
                Function.Call(Hash.SET_PED_COMBAT_RANGE, ped, 1);     // media distancia
                Function.Call(Hash.SET_PED_INFINITE_AMMO_CLIP, ped, true);
            }

            if (strong && def.Weapon == "none")
            {
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 5, true);  // pelea sin armas contra armados
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 46, true); // pelear siempre
            }

            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped, 0, false); // nunca huye

            _active.Add(new Active { Ped = ped, Def = def, Hostile = hostile, NameTag = nameTag });
        }

        /// <summary>Llamar cada frame.</summary>
        public void Update()
        {
            Ped player = GTA.Game.Player.Character;
            int bars = 0;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Active a = _active[i];
                if (!a.Ped.Exists() || a.Ped.IsDead)
                {
                    _active.RemoveAt(i);
                    continue;
                }

                if (a.Def.Has("aura"))
                {
                    Vector3 p = a.Ped.Position;
                    Ptfx.Light(p + new Vector3(0f, 0f, 0.4f), a.Def.AuraColor, 4f, 12f);
                    Ptfx.Light(p - new Vector3(0f, 0f, 0.8f), a.Def.AuraColor, 3f, 8f);
                }

                bool bossBar = false;
                if (a.Def.Has("tank"))
                {
                    Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, a.Ped, 0.8f); // camina pesado
                    if (a.Hostile && bars < MaxBossBars)
                    {
                        DrawBossBar(a, bars++);
                        bossBar = true;
                    }
                }

                // Todos los demás: barra chica sobre la cabeza (el jefe ya tiene la grande arriba).
                if (!bossBar)
                {
                    DrawHeadBar(a, player);
                }

                if (a.Hostile && a.Def.Has("super_strength"))
                {
                    Knockback(a.Ped, player);
                }
            }
        }

        public void Clear() => _active.Clear();

        /// <summary>Si este personaje acaba de dañar al jugador, lo lanza lejos.</summary>
        private static void Knockback(Ped attacker, Ped player)
        {
            if (player.IsDead || !Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, player, attacker, true))
            {
                return;
            }
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, player);

            Entity body = player.IsInVehicle() ? (Entity)player.CurrentVehicle : player;
            Vector3 away = body.Position - attacker.Position;
            away.Z = 0f;
            away.Normalize();

            if (!player.IsInVehicle())
            {
                Function.Call(Hash.SET_PED_TO_RAGDOLL, player, 2500, 2500, 0, false, false, false);
            }
            Function.Call(Hash.APPLY_FORCE_TO_ENTITY, body, 1, away.X * 25f, away.Y * 25f, 12f, 0f, 0f, 0f, 0, false, true, true, false, true);
        }

        /// <summary>
        /// Barra chica justo debajo del nombre del viewer: verde, amarilla bajo el 50 % y roja
        /// bajo el 25 %. Solo de cerca y si está en pantalla.
        /// </summary>
        private void DrawHeadBar(Active a, Ped player)
        {
            if (a.Ped.Position.DistanceTo(player.Position) > HeadBarDistance)
            {
                return;
            }

            Vector3 head = a.Ped.Bones[Bone.SkelHead].Position + new Vector3(0f, 0f, EntityTracker.HeadTagHeight);
            PointF screen = GTA.UI.Screen.WorldToScreen(head);
            if (screen.IsEmpty)
            {
                return;
            }

            const float Width = 70f;
            const float Height = 6f;
            float fraction = Math.Max(0f, (a.Ped.Health - 100f) / Math.Max(1f, a.Ped.MaxHealth - 100f));
            float x = screen.X - Width / 2f;
            float y = screen.Y + 22f; // debajo del texto del nombre, con un poco de aire

            _headBack.Position = new PointF(x - 1f, y - 1f);
            _headBack.Size = new SizeF(Width + 2f, Height + 2f);
            _headBack.Draw();

            _headFill.Color = fraction > 0.5f ? Color.FromArgb(230, 60, 200, 60)
                            : fraction > 0.25f ? Color.FromArgb(230, 230, 200, 40)
                            : Color.FromArgb(230, 220, 40, 40);
            _headFill.Position = new PointF(x, y);
            _headFill.Size = new SizeF(Width * fraction, Height);
            _headFill.Draw();

            // Porcentaje a la derecha de la barra: cuánto le queda.
            _headPercent.Caption = $"{Percent(fraction)}%";
            _headPercent.Position = new PointF(x + Width + 5f, y - 5f);
            _headPercent.Draw();
        }

        private static int Percent(float fraction) =>
            fraction <= 0f ? 0 : Math.Max(1, (int)Math.Round(fraction * 100f));

        /// <summary>Barra de vida arriba al centro: "Nombre del personaje (viewer)".</summary>
        private void DrawBossBar(Active a, int index)
        {
            const float Width = 420f;
            const float Height = 12f;
            float x = 640f - Width / 2f;
            float y = 30f + index * 46f;

            float fraction = Math.Max(0f, (a.Ped.Health - 100f) / Math.Max(1f, a.Ped.MaxHealth - 100f));

            string who = a.NameTag != null ? $"{a.Def.Name}  ({a.NameTag})" : a.Def.Name;
            _barText.Caption = $"{who}  ·  {Percent(fraction)}%";
            _barText.Position = new PointF(640f, y - 2f);
            _barText.Draw();

            _barBack.Position = new PointF(x, y + 22f);
            _barBack.Size = new SizeF(Width, Height);
            _barBack.Draw();

            _barFill.Position = new PointF(x, y + 22f);
            _barFill.Size = new SizeF(Width * fraction, Height);
            _barFill.Draw();
        }

        private sealed class Active
        {
            public Ped Ped;
            public CharacterDef Def;
            public bool Hostile;
            public string NameTag;
        }
    }
}
