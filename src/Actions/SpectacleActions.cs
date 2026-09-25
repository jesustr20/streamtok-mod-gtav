using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Effects;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Tanda 5 (⭐⭐⭐): efectos de espectáculo construidos desde cero (GTA no los trae).
    /// Efectos del MUNDO con duración (seconds); si se repiten, se suma el tiempo.
    /// Lo visual usa partículas del juego con luces de respaldo (ver Ptfx). Clima, hora y cámara
    /// se devuelven a como estaban al terminar.
    /// </summary>
    internal static class SpectacleActions
    {
        public static IEnumerable<ActionDef> All()
        {
            yield return new ActionDef("meteor_shower", "Lluvia de meteoritos", false,
                new[]
                {
                    ParamDef.Int("seconds", 30, 10, 180),
                    ParamDef.Int("density", 5, 1, 10),
                    ParamDef.Bool("night", true),
                },
                MeteorShower);

            yield return new ActionDef("black_hole", "Agujero negro", false,
                new[]
                {
                    ParamDef.Int("seconds", 20, 10, 90),
                    ParamDef.Int("strength", 5, 1, 10),
                },
                BlackHole);

            yield return new ActionDef("tornado", "Tornado", false,
                new[]
                {
                    ParamDef.Int("seconds", 40, 15, 180),
                    ParamDef.Int("strength", 5, 1, 10),
                },
                Tornado);
        }

        // ================================================================ utilidades

        /// <summary>Vehículos y peds cercanos (sin el jugador ni su vehículo), para aplicar fuerzas.</summary>
        private static List<Entity> Nearby(Vector3 center, float radius)
        {
            Ped player = GTA.Game.Player.Character;
            Vehicle playerVehicle = player.CurrentVehicle;

            var list = new List<Entity>();
            foreach (Vehicle v in World.GetNearbyVehicles(center, radius))
            {
                if (v != playerVehicle) list.Add(v);
            }
            foreach (Ped p in World.GetNearbyPeds(center, radius))
            {
                if (p != player && !p.IsInVehicle()) list.Add(p);
            }
            return list;
        }

        private static void Push(Entity e, Vector3 force)
        {
            Function.Call(Hash.APPLY_FORCE_TO_ENTITY, e, 1, force.X, force.Y, force.Z, 0f, 0f, 0f, 0, false, true, true, false, true);
        }

        /// <summary>A pie hay que tumbarlo para que la fuerza lo mueva.</summary>
        private static void Ragdoll(Ped p, int ms)
        {
            if (!p.IsRagdoll)
            {
                Function.Call(Hash.SET_PED_TO_RAGDOLL, p, ms, ms, 0, false, false, false);
            }
        }

        private static Vector3 OnGround(Vector3 position)
        {
            float ground = World.GetGroundHeight(position + new Vector3(0f, 0f, 50f));
            if (ground > 0f)
            {
                position.Z = ground;
            }
            return position;
        }

        /// <summary>El jugador, o su vehículo si va en uno: lo que realmente se mueve.</summary>
        private static Entity PlayerBody()
        {
            Ped player = GTA.Game.Player.Character;
            return player.IsInVehicle() ? (Entity)player.CurrentVehicle : player;
        }

        private static Vector3 RandomRing(Random rng, Vector3 center, float min, float max)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            float dist = min + (float)rng.NextDouble() * (max - min);
            return new Vector3(center.X + (float)Math.Cos(angle) * dist, center.Y + (float)Math.Sin(angle) * dist, center.Z);
        }

        // ================================================================ lluvia de meteoritos

        /// <summary>Un punto de luz en movimiento: estrella fugaz o bola de fuego.</summary>
        private sealed class Streak
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Target;
            public int Born;
            public bool Fireball;
            public Prop Anchor;
            public int Fx;
        }

        private sealed class GroundFire
        {
            public int Handle;
            public int Until;
        }

        private static readonly Color StarLight = Color.FromArgb(255, 200, 225, 255);
        private static readonly Color FireLight = Color.FromArgb(255, 255, 120, 30);

        /// <summary>Estela de fuego: la de la postcombustión de los jets.</summary>
        private const string TrailEffect = "veh_exhaust_afterburner";

        /// <summary>
        /// De noche y con cielo despejado: estrellas fugaces cruzan el cielo (no caen) y bolas de
        /// fuego caen en picada, con estela y luz, e impactan con explosión y fuego en el suelo.
        /// </summary>
        private static void MeteorShower(ActionContext ctx)
        {
            int density = ctx.Int("density");
            bool night = ctx.Bool("night");
            Random rng = ctx.Rng;

            WorldMood mood = WorldMood.Save(clock: night);
            Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, "CLEAR");
            if (night)
            {
                Function.Call(Hash.SET_CLOCK_TIME, 23, 0, 0);
            }

            Ptfx.Request(Ptfx.Core);
            Ptfx.RequestAnchor();

            var streaks = new List<Streak>();
            var fires = new List<GroundFire>();
            int nextStar = 0;
            int nextFireball = 0;
            int starEveryMs = 700 / density;       // densidad 5 ≈ 7 estrellas fugaces por segundo
            int fireballEveryMs = 4000 / density;  // densidad 5 ≈ 1 bola de fuego cada 0,8 s

            ctx.Scheduler.RepeatFor("meteor_shower", ctx.Int("seconds"), () =>
            {
                int now = GTA.Game.GameTime;
                float dt = GTA.Game.LastFrameTime;
                Vector3 origin = GTA.Game.Player.Character.Position;

                if (now >= nextStar && streaks.Count(s => !s.Fireball) < 25)
                {
                    nextStar = now + starEveryMs;
                    Vector3 start = origin + new Vector3((float)(rng.NextDouble() * 600 - 300), (float)(rng.NextDouble() * 600 - 300), 250f + (float)rng.NextDouble() * 150f);
                    double a = rng.NextDouble() * Math.PI * 2;
                    Vector3 velocity = new Vector3((float)Math.Cos(a) * 220f, (float)Math.Sin(a) * 220f, -50f);
                    streaks.Add(NewStreak(start, velocity, Vector3.Zero, false, now));
                }

                if (now >= nextFireball && streaks.Count(s => s.Fireball) < 12)
                {
                    nextFireball = now + fireballEveryMs;
                    Vector3 target = OnGround(RandomRing(rng, origin, 25f, 140f));
                    Vector3 start = RandomRing(rng, target, 120f, 220f) + new Vector3(0f, 0f, 300f);
                    Vector3 velocity = target - start;
                    velocity.Normalize();
                    streaks.Add(NewStreak(start, velocity * 150f, target, true, now));
                }

                for (int i = streaks.Count - 1; i >= 0; i--)
                {
                    Streak s = streaks[i];
                    s.Position += s.Velocity * dt;

                    // El ancla puede no existir aún (modelo cargando): se crea en cuanto se pueda.
                    if (s.Anchor == null)
                    {
                        s.Anchor = Ptfx.CreateAnchor(s.Position);
                        if (s.Anchor != null)
                        {
                            Ptfx.FaceVelocity(s.Anchor, s.Velocity);
                            s.Fx = Ptfx.OnEntity(Ptfx.Core, TrailEffect, s.Anchor, s.Fireball ? 3.0f : 1.2f);
                        }
                    }
                    else
                    {
                        Ptfx.MoveTo(s.Anchor, s.Position);
                    }

                    Ptfx.Light(s.Position, s.Fireball ? FireLight : StarLight, s.Fireball ? 70f : 45f, s.Fireball ? 15f : 10f);

                    bool done;
                    if (s.Fireball)
                    {
                        done = s.Position.Z <= s.Target.Z + 1f || now - s.Born > 6000;
                        if (done && now - s.Born <= 6000)
                        {
                            Impact(s.Target, fires, now);
                        }
                    }
                    else
                    {
                        done = now - s.Born > 1600; // la estrella fugaz se "apaga" en el cielo
                    }

                    if (done)
                    {
                        RemoveStreak(s);
                        streaks.RemoveAt(i);
                    }
                }

                for (int i = fires.Count - 1; i >= 0; i--)
                {
                    if (now >= fires[i].Until)
                    {
                        Function.Call(Hash.REMOVE_SCRIPT_FIRE, fires[i].Handle);
                        fires.RemoveAt(i);
                    }
                }
            },
            onEnd: () =>
            {
                foreach (Streak s in streaks) RemoveStreak(s);
                streaks.Clear();
                foreach (GroundFire f in fires) Function.Call(Hash.REMOVE_SCRIPT_FIRE, f.Handle);
                fires.Clear();
                mood.Restore();
            });
        }

        private static Streak NewStreak(Vector3 start, Vector3 velocity, Vector3 target, bool fireball, int now) =>
            new Streak { Position = start, Velocity = velocity, Target = target, Fireball = fireball, Born = now };

        private static void RemoveStreak(Streak s)
        {
            Ptfx.Stop(s.Fx);
            Ptfx.SafeDelete(s.Anchor);
        }

        private static void Impact(Vector3 position, List<GroundFire> fires, int now)
        {
            // 5 = proyectil de tanque: explosión grande. Audible, visible, temblor fuerte.
            Function.Call(Hash.ADD_EXPLOSION, position.X, position.Y, position.Z, 5, 1.0f, true, false, 1.5f, false);
            Ptfx.Burst(Ptfx.Core, "exp_grd_bzgas_smoke", position, 2.0f);

            if (fires.Count < 10)
            {
                int fire = Function.Call<int>(Hash.START_SCRIPT_FIRE, position.X, position.Y, position.Z, 25, false);
                fires.Add(new GroundFire { Handle = fire, Until = now + 6000 });
            }
        }

        // ================================================================ agujero negro

        private static readonly Color HoleCore = Color.FromArgb(255, 0, 0, 0);
        private static readonly Color HoleEdge = Color.FromArgb(120, 20, 0, 40);
        private static readonly Color DiskOrange = Color.FromArgb(255, 255, 130, 20);
        private static readonly Color DiskPurple = Color.FromArgb(255, 150, 40, 255);

        private const int DiskPoints = 16;
        private const float OpeningMs = 6000f; // se abre (y tiembla fuerte) durante 6 s

        /// <summary>
        /// Se abre en el cielo, 70 m sobre donde estaba el jugador: núcleo negro con un disco de
        /// acreción (humo girando en anillo con luces naranjas y moradas). Al abrirse hay un
        /// terremoto; después queda un retumbo. Tormenta con relámpagos (sin cambiar colores).
        /// La gente huye gritando y los conductores escapan. Atrae todo hacia arriba: lo que llega
        /// al centro desaparece y, si alcanza al jugador, el jugador MUERE. Sigue hasta que se
        /// acaba su tiempo aunque el jugador esté reapareciendo.
        /// </summary>
        private static void BlackHole(ActionContext ctx)
        {
            Vector3 center = OnGround(PlayerBody().Position) + new Vector3(0f, 0f, 70f);
            float strength = ctx.Int("strength");
            Random rng = ctx.Rng;
            int opened = GTA.Game.GameTime;
            int frame = 0;
            int nextLightning = 0;
            float spin = 0f;
            bool diskBuilt = false;
            bool rumbleReduced = false;
            var disk = new List<FunnelPoint>();
            var terrified = new HashSet<int>();
            List<Entity> caught = new List<Entity>();

            WorldMood mood = WorldMood.Save(clock: false);
            Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, "THUNDER");
            Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "ROAD_VIBRATION_SHAKE", 2.5f); // terremoto al abrirse
            Ptfx.Request(Ptfx.Core);
            Ptfx.RequestAnchor();

            ctx.Scheduler.RepeatFor("black_hole", ctx.Int("seconds"), () =>
            {
                int now = GTA.Game.GameTime;
                float dt = GTA.Game.LastFrameTime;
                float open = Math.Min(1f, (now - opened) / OpeningMs);
                bool opening = open < 1f;
                float size = 3f + 15f * open;

                // --- Visual: núcleo negro + borde oscuro
                World.DrawMarker(MarkerType.DebugSphere, center, Vector3.Zero, Vector3.Zero, new Vector3(size, size, size), HoleCore);
                World.DrawMarker(MarkerType.DebugSphere, center, Vector3.Zero, Vector3.Zero, new Vector3(size * 1.25f, size * 1.25f, size * 1.25f), HoleEdge);

                // --- Disco de acreción: humo en anillo plano girando rápido + luces orbitando
                if (!diskBuilt && Ptfx.Ready(Ptfx.Core))
                {
                    diskBuilt = BuildDisk(disk);
                }

                spin += dt * 1.8f;
                float diskRadius = size * 1.6f;
                for (int i = 0; i < DiskPoints; i++)
                {
                    float a = spin + i * (float)(Math.PI * 2 / DiskPoints);
                    float r = diskRadius * (i % 2 == 0 ? 1f : 1.35f); // dos anillos, para darle grosor
                    var offset = new Vector3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, (float)Math.Sin(a * 2) * 1.5f);

                    if (i < disk.Count)
                    {
                        Ptfx.MoveTo(disk[i].Anchor, center + offset);
                    }
                    if (i % 2 == 0)
                    {
                        Ptfx.Light(center + offset, i % 4 == 0 ? DiskOrange : DiskPurple, 35f * open, 12f);
                    }
                }
                Ptfx.Light(center - new Vector3(0f, 0f, size + 5f), DiskPurple, 140f * open, 4f); // resplandor hacia abajo

                // --- Clima: relámpagos frecuentes
                if (now >= nextLightning)
                {
                    Function.Call(Hash.FORCE_LIGHTNING_FLASH);
                    nextLightning = now + 1200 + rng.Next(3000);
                }

                // --- Terremoto al abrirse; después, retumbo suave
                if (opening && frame % 10 == 0)
                {
                    Quake(rng, 90f);
                }
                if (!opening && !rumbleReduced)
                {
                    Function.Call(Hash.SET_GAMEPLAY_CAM_SHAKE_AMPLITUDE, 0.6f);
                    rumbleReduced = true;
                }

                // --- Pánico: la gente huye gritando, los conductores escapan (cada ~0,5 s)
                if (frame % 30 == 0)
                {
                    Panic(center, terrified);
                }

                // --- Succión
                float radius = 40f + 140f * open;
                float swallow = size * 0.6f + 3f;

                if (++frame % 10 == 1)
                {
                    caught = Nearby(center, radius);
                }

                foreach (Entity e in caught)
                {
                    if (!e.Exists())
                    {
                        continue;
                    }

                    Vector3 toCenter = center - e.Position;
                    float distance = toCenter.Length();
                    if (distance < swallow)
                    {
                        e.Delete(); // tragado
                        continue;
                    }

                    toCenter.Normalize();
                    if (e is Ped p) Ragdoll(p, 3000);
                    Push(e, toCenter * strength * open * (2f + 250f / distance));
                }

                PullPlayer(center, strength * open, radius, swallow);
            },
            onEnd: () =>
            {
                foreach (FunnelPoint p in disk)
                {
                    Ptfx.Stop(p.Fx);
                    Ptfx.SafeDelete(p.Anchor);
                }
                disk.Clear();
                Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
                mood.Restore();
            });
        }

        private static void PullPlayer(Vector3 center, float strength, float radius, float swallow)
        {
            Ped player = GTA.Game.Player.Character;
            if (player.IsDead)
            {
                return; // ya fue tragado: el agujero sigue, pero no lo arrastra mientras reaparece
            }

            Entity body = PlayerBody();
            Vector3 toCenter = center - body.Position;
            float distance = toCenter.Length();
            if (distance > radius)
            {
                return;
            }

            if (distance < swallow + 1f)
            {
                player.Kill(); // el agujero se lo llevó
                return;
            }

            toCenter.Normalize();
            if (!player.IsInVehicle()) Ragdoll(player, 2000);
            Push(body, toCenter * strength * (1.2f + 150f / distance));
        }

        /// <summary>Anclas con humo para el disco de acreción (una por punto del anillo).</summary>
        private static bool BuildDisk(List<FunnelPoint> disk)
        {
            for (int i = 0; i < DiskPoints; i++)
            {
                Prop anchor = Ptfx.CreateAnchor(Vector3.Zero);
                if (anchor == null)
                {
                    foreach (FunnelPoint p in disk) { Ptfx.Stop(p.Fx); Ptfx.SafeDelete(p.Anchor); }
                    disk.Clear();
                    return false; // modelo del ancla aún cargando: reintentar
                }
                disk.Add(new FunnelPoint { Anchor = anchor, Fx = Ptfx.OnEntity(Ptfx.Core, FunnelSmoke, anchor, 3.5f) });
            }
            return true;
        }

        /// <summary>Sacudón de terremoto: autos que saltan y gente que cae, alrededor del jugador.</summary>
        private static void Quake(Random rng, float radius)
        {
            Ped player = GTA.Game.Player.Character;
            foreach (Vehicle v in World.GetNearbyVehicles(player.Position, radius))
            {
                Push(v, new Vector3((float)(rng.NextDouble() * 2 - 1) * 4f, (float)(rng.NextDouble() * 2 - 1) * 4f, (float)rng.NextDouble() * 4f));
            }
            foreach (Ped p in World.GetNearbyPeds(player.Position, radius))
            {
                if (p != player && !p.IsInVehicle() && rng.NextDouble() < 0.25)
                {
                    Ragdoll(p, 1500);
                }
            }
            if (!player.IsDead && !player.IsInVehicle() && rng.NextDouble() < 0.15)
            {
                Ragdoll(player, 1200);
            }
        }

        /// <summary>
        /// Cada persona cercana (una sola vez) grita aterrada y huye del agujero; si va manejando,
        /// escapa en su auto.
        /// </summary>
        private static void Panic(Vector3 center, HashSet<int> terrified)
        {
            Ped player = GTA.Game.Player.Character;
            foreach (Ped p in World.GetNearbyPeds(player.Position, 250f))
            {
                if (p == player || p.IsDead || !terrified.Add(p.Handle))
                {
                    continue;
                }

                Function.Call(Hash.PLAY_PED_AMBIENT_SPEECH_NATIVE, p, "GENERIC_FRIGHTENED_HIGH", "SPEECH_PARAMS_FORCE_SHOUTED");
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, p, 0, true);
                Function.Call(Hash.TASK_SMART_FLEE_COORD, p, center.X, center.Y, center.Z, 400f, -1, false, false);
            }
        }

        // ================================================================ tornado

        private sealed class FunnelPoint
        {
            public int Layer;
            public float Phase;
            public Prop Anchor;
            public int Fx;
        }

        private const int FunnelLayers = 10;
        private const float LayerHeight = 6f;
        private const float TornadoRadius = 30f;

        /// <summary>Humo denso y oscuro (chimeneas de fundición) para las paredes del embudo.</summary>
        private const string FunnelSmoke = "ent_amb_smoke_foundry";

        private static readonly string[] DebrisModels = { "prop_bin_05a", "prop_rub_tyre_01", "prop_cs_cardbox_01", "prop_barrel_02a" };
        private static readonly Color Funnel = Color.FromArgb(60, 90, 90, 90);

        /// <summary>
        /// Tormenta con viento fuerte. El embudo se arma con columnas de humo en espiral (angosto
        /// abajo, ancho arriba) que giran; polvo en la base y escombros girando dentro. Avanza hacia
        /// el jugador zigzagueando. Lo que atrapa gira, sube y sale despedido.
        /// Si las partículas no cargan, se dibuja el embudo de respaldo con marcadores.
        /// </summary>
        private static void Tornado(ActionContext ctx)
        {
            Random rng = ctx.Rng;
            float strength = ctx.Int("strength");
            Vector3 center = OnGround(RandomRing(rng, PlayerBody().Position, 70f, 90f));
            int started = GTA.Game.GameTime;
            int frame = 0;
            float angle = 0f;
            bool built = false;
            bool fallback = false;
            var points = new List<FunnelPoint>();
            var debris = new List<Prop>();
            List<Entity> caught = new List<Entity>();

            WorldMood mood = WorldMood.Save(clock: false);
            Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, "THUNDER");
            Function.Call(Hash.SET_WIND_SPEED, 15f);
            Ptfx.Request(Ptfx.Core);
            Ptfx.RequestAnchor();
            foreach (string m in DebrisModels) Function.Call(Hash.REQUEST_MODEL, new Model(m).Hash);

            ctx.Scheduler.RepeatFor("tornado", ctx.Int("seconds"), () =>
            {
                int now = GTA.Game.GameTime;
                float dt = GTA.Game.LastFrameTime;

                // Avanza hacia el jugador (~5 m/s) zigzagueando.
                Vector3 toPlayer = PlayerBody().Position - center;
                toPlayer.Z = 0f;
                if (toPlayer.Length() > 3f)
                {
                    toPlayer.Normalize();
                    var side = new Vector3(-toPlayer.Y, toPlayer.X, 0f);
                    float wobble = (float)Math.Sin(now / 1500.0) * 0.8f;
                    center = OnGround(center + (toPlayer + side * wobble) * (5f * dt));
                }

                if (!built && Ptfx.Ready(Ptfx.Core))
                {
                    built = BuildFunnel(points);
                    if (built) SpawnDebris(debris, center, rng);
                }
                if (!built && now - started > 4000)
                {
                    fallback = true; // las partículas no cargaron: embudo de marcadores
                }

                angle += dt * 2.5f;
                foreach (FunnelPoint p in points)
                {
                    float height = p.Layer * LayerHeight;
                    float r = FunnelRadius(height);
                    float a = angle * (1f + height / 60f) + p.Phase;
                    Ptfx.MoveTo(p.Anchor, center + new Vector3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, height));
                }

                if (fallback || points.Any(p => p.Fx == 0))
                {
                    DrawFallbackFunnel(center, angle);
                }

                if (++frame % 12 == 0)
                {
                    Ptfx.Burst(Ptfx.Core, "exp_grd_bzgas_smoke", RandomRing(rng, center, 2f, 10f), 1.5f); // polvo en la base
                }
                if (frame % 240 == 0)
                {
                    Function.Call(Hash.FORCE_LIGHTNING_FLASH);
                }

                if (frame % 10 == 1)
                {
                    caught = Nearby(center, TornadoRadius);
                }
                foreach (Entity e in caught.Concat(debris))
                {
                    if (e.Exists()) Swirl(e, center, strength);
                }

                Ped player = GTA.Game.Player.Character;
                Entity body = PlayerBody();
                if (!player.IsDead && (body.Position - center).Length() < TornadoRadius)
                {
                    if (!player.IsInVehicle()) Ragdoll(player, 2000);
                    Swirl(body, center, strength * 0.7f);
                    Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "LARGE_EXPLOSION_SHAKE", 0.05f);
                }
            },
            onEnd: () =>
            {
                foreach (FunnelPoint p in points)
                {
                    Ptfx.Stop(p.Fx);
                    Ptfx.SafeDelete(p.Anchor);
                }
                points.Clear();
                foreach (Prop d in debris) Ptfx.SafeDelete(d);
                debris.Clear();
                Function.Call(Hash.SET_WIND_SPEED, 0f);
                mood.Restore();
            });
        }

        /// <summary>Radio del embudo según la altura: angosto abajo, ancho arriba.</summary>
        private static float FunnelRadius(float height) => 1.5f + (float)Math.Pow(height / 54f, 1.6) * 18f;

        /// <summary>Crea las columnas de humo: 3 por capa, repartidas en el círculo.</summary>
        private static bool BuildFunnel(List<FunnelPoint> points)
        {
            for (int layer = 0; layer < FunnelLayers; layer++)
            {
                for (int k = 0; k < 3; k++)
                {
                    Prop anchor = Ptfx.CreateAnchor(Vector3.Zero);
                    if (anchor == null)
                    {
                        foreach (FunnelPoint p in points) { Ptfx.Stop(p.Fx); Ptfx.SafeDelete(p.Anchor); }
                        points.Clear();
                        return false; // modelo del ancla aún cargando: reintentar el próximo frame
                    }

                    float scale = 1.5f + layer * 0.45f; // más humo arriba
                    points.Add(new FunnelPoint
                    {
                        Layer = layer,
                        Phase = k * (float)(Math.PI * 2 / 3) + layer * 0.4f,
                        Anchor = anchor,
                        Fx = Ptfx.OnEntity(Ptfx.Core, FunnelSmoke, anchor, scale),
                    });
                }
            }
            return true;
        }

        private static void SpawnDebris(List<Prop> debris, Vector3 center, Random rng)
        {
            for (int i = 0; i < 8; i++)
            {
                var model = new Model(DebrisModels[rng.Next(DebrisModels.Length)]);
                if (!model.IsLoaded)
                {
                    continue;
                }
                Prop d = World.CreateProp(model, RandomRing(rng, center, 3f, 12f) + new Vector3(0f, 0f, 5f + i * 3f), true, false);
                if (d != null) debris.Add(d);
            }
        }

        /// <summary>Giro alrededor del centro + elevación; arriba de todo sale despedido hacia afuera.</summary>
        private static void Swirl(Entity e, Vector3 center, float strength)
        {
            Vector3 fromCenter = e.Position - center;
            float height = fromCenter.Z;
            fromCenter.Z = 0f;
            float distance = fromCenter.Length();
            if (distance > TornadoRadius || distance < 0.1f)
            {
                return;
            }

            fromCenter.Normalize();
            var tangent = new Vector3(-fromCenter.Y, fromCenter.X, 0f);
            float closeness = 1f - distance / TornadoRadius;

            if (e is Ped p) Ragdoll(p, 3000);

            Vector3 force;
            if (height > 45f)
            {
                force = fromCenter * strength * 4f; // llegó arriba: lo escupe hacia afuera
            }
            else
            {
                force = tangent * strength * 2.5f
                      - fromCenter * strength * 0.8f
                      + new Vector3(0f, 0f, strength * (0.8f + 2.5f * closeness));
            }
            Push(e, force);
        }

        /// <summary>Respaldo si las partículas no cargan: anillos grises apilados que giran.</summary>
        private static void DrawFallbackFunnel(Vector3 center, float spin)
        {
            for (int i = 0; i < 12; i++)
            {
                float height = i * 4.5f;
                float width = 2f * FunnelRadius(height) + 2f;
                World.DrawMarker(MarkerType.VerticalCylinder,
                    center + new Vector3(0f, 0f, height),
                    Vector3.Zero, new Vector3(0f, 0f, spin * 57f + i * 15f),
                    new Vector3(width, width, 4.5f), Funnel);
            }
        }
    }
}
