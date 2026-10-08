using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Entities;
using StreamTok.GtaV.Modes;

namespace StreamTok.GtaV.Race
{
    /// <summary>
    /// Ambiente de la carrera: arco de salida/meta y, en tramos sueltos de la pista, vallas a los dos
    /// lados con público detrás (quieto, animando). Los tramos solo existen mientras alguien (el jugador
    /// o el puntero de la carrera) está cerca: así nunca se pasa del límite de entidades del juego y
    /// el mapa lejano, que no está cargado, no se toca.
    ///
    /// Para no hacer lenta la carrera: los modelos se cargan UNA vez al abrir el lobby (y se sueltan al
    /// terminar), y los tramos se arman de a pocos objetos por frame, nunca todo de golpe.
    /// </summary>
    internal sealed class RaceScenery
    {
        private const float BuildRange = 260f;     // se arma un tramo si hay alguien a menos de esto
        private const float DropRange = 520f;      // se desarma si todos están a más de esto
        private const float StationStep = 3f;      // m entre vallas
        private const float HalfLength = 15f;      // cada tramo cubre ±15 m
        private const float CrowdChance = 0.7f;
        private const int OpsPerFrame = 5;         // objetos que se crean por frame, como máximo

        private static readonly string[] BarrierCandidates = { "prop_mp_barrier_02b", "prop_barrier_work06a", "prop_barrier_work05" };
        private static readonly string[] ArchCandidates = { "stt_prop_stunt_track_start", "stt_prop_race_start_line_01b", "prop_mp_arch_01" };
        private static readonly string[] PedModels =
        {
            "a_m_y_hipster_01", "a_f_y_hipster_01", "a_m_y_beach_01", "a_f_y_beach_01", "a_m_y_business_01",
            "a_f_y_tourist_01", "a_m_y_skater_01", "a_f_y_vinewood_01",
        };
        private static readonly string[] Scenarios =
        {
            "WORLD_HUMAN_CHEERING", "WORLD_HUMAN_CHEERING", "WORLD_HUMAN_CHEERING", "WORLD_HUMAN_MOBILE_FILM_SHOCKING",
        };

        private sealed class Section
        {
            public float Dist;
            public bool Start;
            public bool Built;
            public int Peds;
            public readonly List<Entity> Items = new List<Entity>();
            public readonly Queue<Action> Pending = new Queue<Action>();
        }

        private readonly EntityTracker _tracker;
        private readonly Random _rng;
        private readonly Action<string> _log;
        private readonly RaceSettings _cfg;
        private readonly RaceTrack _track;
        private readonly List<Section> _sections = new List<Section>();
        private readonly List<Model> _loaded = new List<Model>();
        private readonly Dictionary<string, Model> _models = new Dictionary<string, Model>();
        private readonly string _barrier;
        private readonly string _arch;
        private readonly int _perSectionPeds;
        private readonly int _maxBuilt;

        public RaceScenery(EntityTracker tracker, Random rng, Action<string> log, RaceSettings cfg, RaceTrack track)
        {
            _tracker = tracker;
            _rng = rng;
            _log = log;
            _cfg = cfg;
            _track = track;
            _barrier = !string.IsNullOrWhiteSpace(cfg.BarrierModel) && new Model(cfg.BarrierModel).IsInCdImage ? cfg.BarrierModel : FirstValid(BarrierCandidates);
            _arch = FirstValid(ArchCandidates);
            _log($"Carrera: ambiente (vallas: {_barrier ?? "ninguna"}, arco: {_arch ?? "ninguno"}).");

            _perSectionPeds = 10;
            _maxBuilt = Math.Max(1, cfg.CrowdMax / _perSectionPeds);

            // Se piden una sola vez; se sueltan en Dispose.
            if (_barrier != null) Want(_barrier);
            if (_arch != null) Want(_arch);
            if (cfg.CrowdMax > 0)
            {
                foreach (string p in PedModels) Want(p);
            }

            // Tramo de la salida/meta + tramos repartidos (uno cada ~250 m, entre 4 y 20).
            _sections.Add(new Section { Dist = 0f, Start = true });
            int n = Math.Max(4, Math.Min(20, (int)(track.Length / 250f)));
            for (int i = 0; i < n; i++)
            {
                float d = track.Length * (i + 0.5f) / n;
                if (d < 40f || d > track.Length - 40f) continue; // el de la salida ya cubre ese tramo
                _sections.Add(new Section { Dist = d });
            }
        }

        private void Want(string name)
        {
            var m = new Model(name);
            m.Request();
            _models[name] = m;
            _loaded.Add(m);
        }

        private static string FirstValid(string[] names)
        {
            foreach (string n in names)
            {
                var m = new Model(n);
                if (m.IsInCdImage && m.IsValid) return n;
            }
            return null;
        }

        public void Update(Vector3 focusA, Vector3? focusB)
        {
            // 1) Terminar lo que ya se empezó (de a pocos objetos por frame).
            int ops = OpsPerFrame;
            foreach (Section s in _sections)
            {
                while (ops > 0 && s.Pending.Count > 0)
                {
                    Action next = s.Pending.Dequeue();
                    try
                    {
                        next();
                    }
                    catch (Exception ex)
                    {
                        _log($"Carrera: ambiente, no se pudo crear un objeto: {ex.Message}");
                    }
                    ops--;
                }
            }

            // 2) Desarmar lo lejano y elegir el próximo tramo a armar.
            int built = 0;
            foreach (Section s in _sections)
            {
                if (s.Built) built++;
            }

            Section toBuild = null;
            foreach (Section s in _sections)
            {
                Vector3 c = _track.PositionAt(s.Dist);
                float d = Math.Min(c.DistanceTo(focusA), focusB.HasValue ? c.DistanceTo(focusB.Value) : float.MaxValue);

                if (s.Built && d > DropRange)
                {
                    Destroy(s);
                    built--;
                }
                else if (!s.Built && d < BuildRange && toBuild == null)
                {
                    toBuild = s;
                }
            }

            // Un tramo a la vez, y solo si queda cupo (el de la salida siempre entra).
            if (toBuild != null && (built < _maxBuilt || toBuild.Start) && AllPending() == 0)
            {
                Plan(toBuild);
            }
        }

        private int AllPending()
        {
            int n = 0;
            foreach (Section s in _sections) n += s.Pending.Count;
            return n;
        }

        public void Dispose()
        {
            foreach (Section s in _sections)
            {
                Destroy(s);
            }
            _sections.Clear();
            foreach (Model m in _loaded)
            {
                m.MarkAsNoLongerNeeded();
            }
            _loaded.Clear();
            _models.Clear();
        }

        private void Destroy(Section s)
        {
            s.Pending.Clear();
            foreach (Entity e in s.Items)
            {
                _tracker.Untrack(e);
                EntityTracker.SafeDelete(e);
            }
            s.Items.Clear();
            s.Peds = 0;
            s.Built = false;
        }

        /// <summary>Decide qué va en el tramo y lo deja en cola; los objetos se crean de a pocos por frame.</summary>
        private void Plan(Section s)
        {
            s.Built = true;

            if (s.Start && _arch != null)
            {
                Vector3 p = _track.PositionAt(0f);
                float h = Heading(_track.TangentAt(0f)) + _cfg.ArchTurn;
                s.Pending.Enqueue(() => MakeProp(s, _arch, Ground(p), h));
            }

            float offset = _cfg.SceneryOffset;
            int pedBudget = _perSectionPeds + (s.Start ? 6 : 0);
            int plannedPeds = 0;
            for (float k = -HalfLength; k <= HalfLength; k += StationStep)
            {
                float d = s.Dist + k;
                if (s.Start && Math.Abs(k) < 5f) continue; // despejado bajo el arco

                Vector3 center = _track.PositionAt(d);
                Vector3 tan = _track.TangentAt(d);
                Vector3 left = new Vector3(-tan.Y, tan.X, 0f);
                if (left.Length() > 0.001f) left.Normalize();
                float barrierHeading = Heading(tan) + _cfg.BarrierTurn;

                for (int side = -1; side <= 1; side += 2)
                {
                    if (_barrier != null)
                    {
                        Vector3 bp = center + left * (side * offset);
                        s.Pending.Enqueue(() => MakeProp(s, _barrier, Ground(bp), barrierHeading));
                    }

                    if (CrowdMaxOk() && plannedPeds < pedBudget && _rng.NextDouble() < CrowdChance)
                    {
                        plannedPeds++;
                        Vector3 pp = center + left * (side * (offset + 1.8f + (float)_rng.NextDouble() * 1.2f));
                        string model = PedModels[_rng.Next(PedModels.Length)];
                        string scenario = Scenarios[_rng.Next(Scenarios.Length)];
                        Vector3 look = center;
                        s.Pending.Enqueue(() => MakePed(s, model, Ground(pp), look, scenario));
                    }
                }
            }
        }

        private bool CrowdMaxOk() => _cfg.CrowdMax > 0;

        private void MakeProp(Section s, string name, Vector3 pos, float heading)
        {
            if (!s.Built) return; // el tramo se desarmó mientras esperaba
            Model m = _models[name];
            if (!m.IsLoaded) { m.Request(); s.Pending.Enqueue(() => MakeProp(s, name, pos, heading)); return; }

            Prop prop = World.CreateProp(m, pos, new Vector3(0f, 0f, heading), false, true);
            if (prop == null) return;
            prop.IsPositionFrozen = true;
            Register(s, prop);
        }

        private void MakePed(Section s, string name, Vector3 pos, Vector3 lookAt, string scenario)
        {
            if (!s.Built) return;
            Model m = _models[name];
            if (!m.IsLoaded) { m.Request(); s.Pending.Enqueue(() => MakePed(s, name, pos, lookAt, scenario)); return; }

            Ped ped = World.CreatePed(m, pos, Heading(lookAt - pos));
            if (ped == null) return;
            ped.IsInvincible = true;
            ped.BlockPermanentEvents = true;
            Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, false);
            Function.Call(Hash.TASK_START_SCENARIO_IN_PLACE, ped.Handle, scenario, 0, true);
            ped.IsPositionFrozen = true;
            s.Peds++;
            Register(s, ped);
        }

        private void Register(Section s, Entity e)
        {
            s.Items.Add(e);
            _tracker.Track(e, null, RaceMode.Kind);
        }

        private static Vector3 Ground(Vector3 p)
        {
            float g = World.GetGroundHeight(new Vector3(p.X, p.Y, p.Z + 3f));
            if (g > 0f) p.Z = g;
            return p;
        }

        private static float Heading(Vector3 dir) => (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI);
    }
}
