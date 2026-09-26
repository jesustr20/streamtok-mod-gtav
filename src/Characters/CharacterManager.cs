using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Characters
{
    /// <summary>
    /// Aplica y mantiene las habilidades de los personajes spawneados:
    ///  - super_strength: no se cae, resiste fuego y explosiones, cada golpe al jugador lo lanza lejos.
    ///  - tank: blindaje, camina lento y barra de vida de jefe.
    ///  - gunslinger: puntería casi perfecta, dispara rápido, no recarga.
    ///  - aura: brillo de color alrededor del cuerpo.
    ///  - energy_blast: ráfagas de ki y Kamehameha (carga, rayo y explosiones).
    ///  - flight: vuela, persigue por el aire y baja a pelear.
    ///  - dodge: esquiva el daño con un salto instantáneo hacia un costado (Ultra Instinto).
    ///  - speed: súper velocidad con estela.
    /// Los enemigos atacan al jugador; los aliados, al enemigo más cercano.
    /// </summary>
    internal sealed class CharacterManager
    {
        private const int MaxBossBars = 3;
        private const float HeadBarDistance = 50f;

        // energy_blast
        private const int KameChargeMs = 2200;
        private const int KameFireMs = 1300;
        private const float KameRange = 90f;
        private const float KiSpeed = 55f;
        private const string ChargeAnimDict = "missminuteman_1ig_2";
        private const string ChargeAnim = "handsup_base";

        // flight
        private const string FlyAnimDict = "skydive@base";
        private const string FlyAnim = "free_idle";
        private const float FlySpeed = 22f;
        private const float FlyHeight = 7f;

        // speed
        private const float DashSpeed = 24f;

        private readonly EntityTracker _tracker;
        private readonly Random _rng;
        private readonly List<Active> _active = new List<Active>();
        private readonly List<Projectile> _projectiles = new List<Projectile>();
        private readonly List<Afterimage> _afterimages = new List<Afterimage>();

        private readonly GTA.UI.TextElement _barText = new GTA.UI.TextElement(
            "", PointF.Empty, 0.38f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        private readonly GTA.UI.ContainerElement _barBack = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.FromArgb(170, 0, 0, 0));
        private readonly GTA.UI.ContainerElement _barFill = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.FromArgb(230, 200, 30, 30));
        private readonly GTA.UI.ContainerElement _headBack = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.FromArgb(170, 0, 0, 0));
        private readonly GTA.UI.ContainerElement _headFill = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.Green);
        private readonly GTA.UI.TextElement _headPercent = new GTA.UI.TextElement(
            "", PointF.Empty, 0.27f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Left, true, true);

        public CharacterManager(EntityTracker tracker, Random rng)
        {
            _tracker = tracker;
            _rng = rng;
        }

        // ================================================================ alta

        /// <summary>Configura un personaje recién creado según sus habilidades y lo registra.</summary>
        /// <param name="targetProvider">
        /// Opcional (modo Pelea): elige el objetivo de este personaje en vez de la regla normal
        /// (enemigos → jugador, aliados → enemigo más cercano).
        /// </param>
        public void Setup(Ped ped, CharacterDef def, bool hostile, string nameTag, Func<Ped, Ped> targetProvider = null)
        {
            // Apariencia por defecto del modelo: GTA elige piezas de ropa al azar y muchos add-on
            // peds solo traen completa la variante por defecto (si no, faltan zapatos, manos…).
            Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, ped);

            // Vida: los peds mueren al llegar a 100, por eso se suma 100 a la vida configurada.
            ped.MaxHealth = def.Health + 100;
            ped.Health = def.Health + 100;

            Configure(ped, def);

            int now = GTA.Game.GameTime;
            _active.Add(new Active
            {
                Ped = ped,
                Def = def,
                Hostile = hostile,
                NameTag = nameTag,
                TargetProvider = targetProvider,
                NextKi = now + 2000 + _rng.Next(2000),
                NextKame = now + 6000 + _rng.Next(4000),
                NextFlight = now + 5000 + _rng.Next(5000),
            });
        }

        /// <summary>
        /// Cambia las habilidades de un personaje ya creado (ej. sube de nivel en el modo Pelea).
        /// La vida no se toca: la maneja quien llama.
        /// </summary>
        public void Upgrade(Ped ped, CharacterDef def)
        {
            Active a = _active.Find(x => x.Ped == ped);
            if (a == null)
            {
                return;
            }
            a.Def = def;
            Configure(ped, def);
        }

        /// <summary>Inmunidades, estilo de combate y animaciones según las habilidades.</summary>
        private void Configure(Ped ped, CharacterDef def)
        {
            bool strong = def.Has("super_strength");
            bool tank = def.Has("tank");
            bool mobile = def.Has("flight") || def.Has("speed");

            if (strong || tank || mobile)
            {
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped, false);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT, ped, false);
                Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped, false);
            }

            // Inmunidades: SÍ recibe balas y golpes (si no, no se le gana).
            bool fireProof = strong || tank;
            bool explosionProof = strong || tank || def.Has("energy_blast"); // su propio Kamehameha no lo mata
            bool collisionProof = strong || tank || mobile;                  // caídas y choques al volar/correr
            Function.Call(Hash.SET_ENTITY_PROOFS, ped, false, fireProof, explosionProof, collisionProof, false, false, false, false);

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

            if (def.Has("energy_blast")) Function.Call(Hash.REQUEST_ANIM_DICT, ChargeAnimDict);
            if (def.Has("flight")) Function.Call(Hash.REQUEST_ANIM_DICT, FlyAnimDict);
        }

        // ================================================================ cada frame

        public void Update()
        {
            Ped player = GTA.Game.Player.Character;
            int now = GTA.Game.GameTime;
            float dt = GTA.Game.LastFrameTime;
            int bars = 0;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Active a = _active[i];
                if (!a.Ped.Exists() || a.Ped.IsDead)
                {
                    if (a.Flying && a.Ped.Exists()) Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, a.Ped, true);
                    _active.RemoveAt(i);
                    continue;
                }

                Ped target = TargetOf(a, player, now);

                if (a.Def.Has("aura"))
                {
                    Vector3 p = a.Ped.Position;
                    float boost = a.KameStage == 1 ? 2.5f : 1f; // al cargar ki el aura brilla más
                    Ptfx.Light(p + new Vector3(0f, 0f, 0.4f), a.Def.AuraColor, 4f * boost, 12f * boost);
                    Ptfx.Light(p - new Vector3(0f, 0f, 0.8f), a.Def.AuraColor, 3f * boost, 8f * boost);
                }

                if (a.Def.Has("tank"))
                {
                    Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, a.Ped, 0.8f); // camina pesado
                }

                if (a.Def.Has("dodge")) Dodge(a, target ?? player, now);
                if (a.Def.Has("speed")) Speed(a, target, now);
                if (a.Def.Has("flight")) Flight(a, target, now);
                if (a.Def.Has("energy_blast")) EnergyBlast(a, target, now);

                if (a.Def.Has("super_strength"))
                {
                    // Enemigo normal: lanza al jugador. En el modo Pelea: lanza a su objetivo.
                    Ped victim = a.TargetProvider != null ? target : a.Hostile ? player : null;
                    if (victim != null && victim.Exists())
                    {
                        Knockback(a.Ped, victim, a.Def.PowerScale);
                    }
                }

                bool bossBar = false;
                if (a.Def.Has("tank") && a.Hostile && bars < MaxBossBars)
                {
                    DrawBossBar(a, bars++);
                    bossBar = true;
                }
                if (!bossBar)
                {
                    DrawHeadBar(a, player);
                }
            }

            UpdateProjectiles(player, now, dt);
            DrawAfterimages(now);
        }

        public void Clear()
        {
            foreach (Active a in _active)
            {
                if (a.Flying && a.Ped.Exists()) Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, a.Ped, true);
            }
            _active.Clear();
            _projectiles.Clear();
            _afterimages.Clear();
        }

        // ================================================================ objetivo

        /// <summary>Enemigos: el jugador. Aliados: el enemigo vivo más cercano (se recalcula cada ~0,5 s).</summary>
        private Ped TargetOf(Active a, Ped player, int now)
        {
            if (a.TargetProvider != null)
            {
                if (now >= a.NextTargetScan || a.Target == null || !a.Target.Exists() || a.Target.IsDead)
                {
                    a.NextTargetScan = now + 500;
                    a.Target = a.TargetProvider(a.Ped);
                }
                return a.Target;
            }

            if (a.Hostile)
            {
                return player.IsDead ? null : player;
            }

            if (now < a.NextTargetScan && a.Target != null && a.Target.Exists() && !a.Target.IsDead)
            {
                return a.Target;
            }

            a.NextTargetScan = now + 500;
            int hostile = _tracker.HostileGroup.Hash;
            Ped best = null;
            float bestDistance = 80f;

            foreach (Ped p in World.GetNearbyPeds(a.Ped.Position, 80f))
            {
                if (p == a.Ped || p == player || p.IsDead) continue;
                if (Function.Call<int>(Hash.GET_PED_RELATIONSHIP_GROUP_HASH, p) != hostile) continue;

                float d = p.Position.DistanceTo(a.Ped.Position);
                if (d < bestDistance)
                {
                    best = p;
                    bestDistance = d;
                }
            }

            a.Target = best;
            return best;
        }

        // ================================================================ energy_blast

        private void EnergyBlast(Active a, Ped target, int now)
        {
            if (a.KameStage == 1)
            {
                ChargeKamehameha(a, target, now);
                return;
            }
            if (a.KameStage == 2)
            {
                FireKamehameha(a, target, now);
                return;
            }

            if (target == null)
            {
                return;
            }

            float distance = a.Ped.Position.DistanceTo(target.Position);
            bool sees = Function.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, a.Ped, target, 17);

            // Kamehameha: en tierra, a distancia media o larga, con línea de visión.
            if (!a.Flying && now >= a.NextKame && distance > 12f && distance < KameRange && sees)
            {
                a.KameStage = 1;
                a.StageEnds = now + KameChargeMs;
                a.NextKame = now + 14000 + _rng.Next(8000);
                Function.Call(Hash.CLEAR_PED_TASKS, a.Ped);
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, ChargeAnimDict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, a.Ped, ChargeAnimDict, ChargeAnim, 8f, -8f, -1, 1, 0f, false, false, false);
                }
                return;
            }

            // Ráfagas de ki: también desde el aire.
            if (now >= a.NextKi && distance > 5f && distance < 70f && sees)
            {
                a.NextKi = now + 2500 + _rng.Next(2500);
                Vector3 from = Hands(a.Ped);
                Vector3 dir = target.Position + new Vector3(0f, 0f, 0.5f) - from;
                dir.Normalize();
                _projectiles.Add(new Projectile
                {
                    Position = from + dir * 0.8f,
                    Velocity = dir * KiSpeed,
                    Target = target,
                    Owner = a.Ped,
                    Scale = a.Def.PowerScale,
                    Color = a.Def.Energy,
                    Born = now,
                });
            }
        }

        /// <summary>Quieto, mirando al objetivo; en sus manos crece una esfera de energía.</summary>
        private void ChargeKamehameha(Active a, Ped target, int now)
        {
            if (target != null) Face(a.Ped, target.Position);

            float t = 1f - Math.Max(0f, (a.StageEnds - now) / (float)KameChargeMs);
            Vector3 hands = Hands(a.Ped);
            Color c = a.Def.Energy;

            float size = 0.3f + 1.1f * t;
            World.DrawMarker(MarkerType.DebugSphere, hands, Vector3.Zero, Vector3.Zero, new Vector3(size, size, size), Color.FromArgb(220, c));
            World.DrawMarker(MarkerType.DebugSphere, hands, Vector3.Zero, Vector3.Zero, new Vector3(size * 2f, size * 2f, size * 2f), Color.FromArgb(70, c));
            Ptfx.Light(hands, c, 3f + 10f * t, 15f + 35f * t);

            if (now >= a.StageEnds)
            {
                a.KameStage = 2;
                a.StageEnds = now + KameFireMs;
                a.NextBeamBlast = 0;

                Vector3 aim = target != null ? target.Position + new Vector3(0f, 0f, 0.4f) : hands + a.Ped.ForwardVector * 10f;
                a.BeamDirection = aim - hands;
                a.BeamDirection.Normalize(); // dirección fija: se puede esquivar

                if (GTA.Game.Player.Character.Position.DistanceTo(a.Ped.Position) < 80f)
                {
                    Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "SMALL_EXPLOSION_SHAKE", 0.35f);
                }
            }
        }

        /// <summary>Rayo de energía en línea recta; explota donde choca, varias veces mientras dura.</summary>
        private void FireKamehameha(Active a, Ped target, int now)
        {
            Color c = a.Def.Energy;
            Vector3 start = Hands(a.Ped) + a.BeamDirection * 0.8f;
            Vector3 end = start + a.BeamDirection * KameRange;

            RaycastResult hit = World.Raycast(start, end, IntersectFlags.Everything, a.Ped);
            if (hit.DidHit)
            {
                end = hit.HitPosition;
            }

            float length = (end - start).Length();
            for (float s = 0f; s < length; s += 1.4f)
            {
                Vector3 p = start + a.BeamDirection * s;
                World.DrawMarker(MarkerType.DebugSphere, p, Vector3.Zero, Vector3.Zero, new Vector3(1.5f, 1.5f, 1.5f), Color.FromArgb(190, c));
                World.DrawMarker(MarkerType.DebugSphere, p, Vector3.Zero, Vector3.Zero, new Vector3(2.6f, 2.6f, 2.6f), Color.FromArgb(50, c));
            }
            for (float s = 0f; s < length; s += 6f)
            {
                Ptfx.Light(start + a.BeamDirection * s, c, 10f, 25f);
            }

            if (now >= a.NextBeamBlast)
            {
                a.NextBeamBlast = now + 260;
                Function.Call(Hash.ADD_EXPLOSION, end.X, end.Y, end.Z, 5, 1.2f * a.Def.PowerScale, true, false, 0.5f, false);
            }

            if (now >= a.StageEnds)
            {
                a.KameStage = 0;
                Function.Call(Hash.CLEAR_PED_TASKS, a.Ped);
                if (target != null)
                {
                    a.Ped.Task.FightAgainst(target);
                }
            }
        }

        private void UpdateProjectiles(Ped player, int now, float dt)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                Projectile p = _projectiles[i];

                // Leve seguimiento: corrige un poco la dirección hacia el objetivo.
                if (p.Target != null && p.Target.Exists() && !p.Target.IsDead)
                {
                    Vector3 wanted = p.Target.Position + new Vector3(0f, 0f, 0.4f) - p.Position;
                    wanted.Normalize();
                    Vector3 dir = p.Velocity;
                    dir.Normalize();
                    dir = dir * 0.9f + wanted * 0.1f;
                    dir.Normalize();
                    p.Velocity = dir * KiSpeed;
                }

                p.Position += p.Velocity * dt;

                World.DrawMarker(MarkerType.DebugSphere, p.Position, Vector3.Zero, Vector3.Zero, new Vector3(0.5f, 0.5f, 0.5f), Color.FromArgb(230, p.Color));
                World.DrawMarker(MarkerType.DebugSphere, p.Position, Vector3.Zero, Vector3.Zero, new Vector3(1.1f, 1.1f, 1.1f), Color.FromArgb(70, p.Color));
                Ptfx.Light(p.Position, p.Color, 6f, 20f);

                bool reached = p.Target != null && p.Target.Exists() && p.Position.DistanceTo(p.Target.Position) < 1.6f;
                float ground = World.GetGroundHeight(p.Position + new Vector3(0f, 0f, 1f));
                bool hitGround = ground > 0f && p.Position.Z <= ground + 0.2f;
                bool expired = now - p.Born > 2500;

                if (reached || hitGround || expired)
                {
                    if (!expired)
                    {
                        // 0 = granada: explosión chica.
                        Function.Call(Hash.ADD_EXPLOSION, p.Position.X, p.Position.Y, p.Position.Z, 0, 0.6f * p.Scale, true, false, 0.2f, false);
                    }
                    _projectiles.RemoveAt(i);
                }
            }
        }

        // ================================================================ flight

        private void Flight(Active a, Ped target, int now)
        {
            if (!a.Flying)
            {
                if (target == null || a.KameStage != 0 || now < a.NextFlight)
                {
                    return;
                }

                a.Flying = true;
                a.FlightEnds = now + 7000 + _rng.Next(4000);
                Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, a.Ped);
                Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, a.Ped, false);
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, FlyAnimDict))
                {
                    Function.Call(Hash.TASK_PLAY_ANIM, a.Ped, FlyAnimDict, FlyAnim, 8f, -8f, -1, 1, 0f, false, false, false);
                }
                return;
            }

            bool landing = now > a.FlightEnds - 1500;
            if (now >= a.FlightEnds || target == null || !target.Exists())
            {
                a.Flying = false;
                a.NextFlight = now + 10000 + _rng.Next(8000);
                Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, a.Ped, true);
                Function.Call(Hash.CLEAR_PED_TASKS, a.Ped);
                if (target != null) a.Ped.Task.FightAgainst(target);
                return;
            }

            // Vuela en círculos alrededor y por encima del objetivo; al final baja cerca de él.
            float angle = now / 1000f * 0.9f;
            Vector3 orbit = new Vector3((float)Math.Cos(angle) * 9f, (float)Math.Sin(angle) * 9f, landing ? 0.5f : FlyHeight);
            Vector3 wanted = target.Position + orbit - a.Ped.Position;
            float distance = wanted.Length();
            if (distance > 0.1f)
            {
                wanted.Normalize();
                a.Ped.Velocity = wanted * Math.Min(FlySpeed, distance * 3f);
            }
            Face(a.Ped, target.Position);
            Ptfx.Light(a.Ped.Position - a.Ped.Velocity * 0.05f, a.Def.Energy, 5f, 10f); // estela
        }

        // ================================================================ dodge

        /// <summary>
        /// Ultra Instinto: si recibe daño y el esquive está disponible, NO lo recibe (se le devuelve
        /// la vida) y aparece de golpe unos metros al costado, dejando una imagen residual.
        /// </summary>
        private void Dodge(Active a, Ped player, int now)
        {
            int health = a.Ped.Health;
            if (a.LastHealth < 0)
            {
                a.LastHealth = health;
                return;
            }

            if (health < a.LastHealth && now >= a.NextDodge && _rng.NextDouble() < 0.6)
            {
                a.Ped.Health = a.LastHealth;
                a.NextDodge = now + 1200;

                Vector3 from = a.Ped.Position;
                Vector3 away = from - player.Position;
                away.Z = 0f;
                away.Normalize();
                var side = new Vector3(-away.Y, away.X, 0f) * (_rng.NextDouble() < 0.5 ? -1f : 1f);
                Vector3 to = from + side * (4f + (float)_rng.NextDouble() * 3f);
                float ground = World.GetGroundHeight(to + new Vector3(0f, 0f, 3f));
                if (ground > 0f && !a.Flying) to.Z = ground;

                AddAfterimage(from + new Vector3(0f, 0f, 0.2f), a.Def.Energy, 1.8f, now, 350);
                Ptfx.MoveTo(a.Ped, to);
            }

            a.LastHealth = a.Ped.Health;
        }

        // ================================================================ speed

        /// <summary>Corre muchísimo más rápido hacia su objetivo, dejando una estela de su color.</summary>
        private void Speed(Active a, Ped target, int now)
        {
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, a.Ped, 1.4f);

            if (target == null || a.Flying || a.KameStage != 0 || a.Ped.IsInVehicle() || a.Ped.IsRagdoll)
            {
                return;
            }

            Vector3 toTarget = target.Position - a.Ped.Position;
            toTarget.Z = 0f;
            if (toTarget.Length() < 7f)
            {
                return; // cerca: pelea normal
            }

            toTarget.Normalize();
            Vector3 v = a.Ped.Velocity;
            a.Ped.Velocity = new Vector3(toTarget.X * DashSpeed, toTarget.Y * DashSpeed, v.Z);
            Face(a.Ped, target.Position);

            if (now >= a.NextTrail)
            {
                a.NextTrail = now + 40;
                AddAfterimage(a.Ped.Position, a.Def.Energy, 1.1f, now, 250);
            }
        }

        // ================================================================ utilidades

        /// <summary>Punto entre ambas manos, un poco hacia adelante.</summary>
        private static Vector3 Hands(Ped ped)
        {
            Vector3 left = ped.Bones[Bone.SkelLeftHand].Position;
            Vector3 right = ped.Bones[Bone.SkelRightHand].Position;
            return (left + right) * 0.5f + ped.ForwardVector * 0.3f;
        }

        private static void Face(Ped ped, Vector3 point)
        {
            Vector3 d = point - ped.Position;
            float heading = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);
            Function.Call(Hash.SET_ENTITY_HEADING, ped, heading);
        }

        private void AddAfterimage(Vector3 position, Color color, float size, int now, int ms)
        {
            if (_afterimages.Count < 60)
            {
                _afterimages.Add(new Afterimage { Position = position, Color = color, Size = size, Born = now, Until = now + ms });
            }
        }

        private void DrawAfterimages(int now)
        {
            for (int i = _afterimages.Count - 1; i >= 0; i--)
            {
                Afterimage f = _afterimages[i];
                if (now >= f.Until)
                {
                    _afterimages.RemoveAt(i);
                    continue;
                }
                float life = (f.Until - now) / (float)(f.Until - f.Born);
                World.DrawMarker(MarkerType.DebugSphere, f.Position, Vector3.Zero, Vector3.Zero,
                    new Vector3(f.Size, f.Size, f.Size), Color.FromArgb((int)(140 * life), f.Color));
            }
        }

        /// <summary>Si este personaje acaba de dañar a su víctima (el jugador u otro ped), la lanza lejos.</summary>
        private static void Knockback(Ped attacker, Ped player, float scale = 1f)
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
            Function.Call(Hash.APPLY_FORCE_TO_ENTITY, body, 1, away.X * 25f * scale, away.Y * 25f * scale, 12f * scale, 0f, 0f, 0f, 0, false, true, true, false, true);
        }

        private static int Percent(float fraction) =>
            fraction <= 0f ? 0 : Math.Max(1, (int)Math.Round(fraction * 100f));

        private static float HealthFraction(Ped ped) =>
            Math.Max(0f, (ped.Health - 100f) / Math.Max(1f, ped.MaxHealth - 100f));

        /// <summary>Barra chica bajo el nombre del viewer: verde, amarilla bajo 50 %, roja bajo 25 %, con porcentaje.</summary>
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
            float fraction = HealthFraction(a.Ped);
            float x = screen.X - Width / 2f;
            float y = screen.Y + 22f;

            _headBack.Position = new PointF(x - 1f, y - 1f);
            _headBack.Size = new SizeF(Width + 2f, Height + 2f);
            _headBack.Draw();

            _headFill.Color = fraction > 0.5f ? Color.FromArgb(230, 60, 200, 60)
                            : fraction > 0.25f ? Color.FromArgb(230, 230, 200, 40)
                            : Color.FromArgb(230, 220, 40, 40);
            _headFill.Position = new PointF(x, y);
            _headFill.Size = new SizeF(Width * fraction, Height);
            _headFill.Draw();

            _headPercent.Caption = $"{Percent(fraction)}%";
            _headPercent.Position = new PointF(x + Width + 5f, y - 5f);
            _headPercent.Draw();
        }

        /// <summary>Barra grande de jefe arriba al centro: "Nombre (viewer) · 64%".</summary>
        private void DrawBossBar(Active a, int index)
        {
            const float Width = 420f;
            const float Height = 12f;
            float x = 640f - Width / 2f;
            float y = 30f + index * 46f;
            float fraction = HealthFraction(a.Ped);

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

        // ================================================================ estado

        private sealed class Active
        {
            public Ped Ped;
            public CharacterDef Def;
            public bool Hostile;
            public string NameTag;
            public Func<Ped, Ped> TargetProvider;

            public Ped Target;
            public int NextTargetScan;

            public int NextKi;
            public int NextKame;
            public int KameStage;       // 0 nada, 1 cargando, 2 disparando
            public int StageEnds;
            public int NextBeamBlast;
            public Vector3 BeamDirection;

            public bool Flying;
            public int NextFlight;
            public int FlightEnds;

            public int LastHealth = -1;
            public int NextDodge;

            public int NextTrail;
        }

        private sealed class Projectile
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Ped Target;
            public Ped Owner;
            public float Scale = 1f;
            public Color Color;
            public int Born;
        }

        private sealed class Afterimage
        {
            public Vector3 Position;
            public Color Color;
            public float Size;
            public int Born;
            public int Until;
        }
    }
}
