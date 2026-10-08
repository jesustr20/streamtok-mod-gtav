using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Web.Script.Serialization;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Race
{
    /// <summary>
    /// Herramienta del creador: convierte listas de puntos de paso (scripts\StreamTok.Race\import\*.json,
    /// formato {"name":"x","points":[[x,y,z],...]}, circuito cerrado: salida → puntos → salida) en pistas
    /// reales. Un conductor de la IA del juego maneja por las calles entre los puntos mientras el mod
    /// graba la trayectoria; al terminar guarda tracks\{nombre}.json (el mismo formato que una pista grabada a mano).
    /// El jugador viaja de acompañante para que el mapa se cargue; al final vuelve a donde estaba.
    /// </summary>
    internal sealed class RaceImporter
    {
        private const float ArriveRadius = 22f;
        private const float RecordSpacing = 6f;
        private const int StuckMs = 9000;
        private const int WaypointTimeoutMs = 120000;
        private const string CarModel = "sultan";

        private sealed class Job
        {
            public string Name;
            public List<Vector3> Points = new List<Vector3>();
        }

        private readonly string _importDir;
        private readonly string _trackDir;
        private readonly Action<string> _log;
        private readonly Action<string, RaceTrack> _onSaved;
        private readonly Queue<Job> _jobs = new Queue<Job>();

        private Job _job;
        private int _idx;
        private Vehicle _veh;
        private Ped _driver;
        private readonly List<Vector3> _rec = new List<Vector3>();
        private Vector3 _origin;
        private int _wpStart, _lastMoveAt;
        private Vector3 _lastMovePos;
        private int _skips, _done, _total;
        private string _status = "";

        public RaceImporter(string baseDirectory, Action<string> log, Action<string, RaceTrack> onSaved)
        {
            _importDir = Path.Combine(baseDirectory, "StreamTok.Race", "import");
            _trackDir = Path.Combine(baseDirectory, "StreamTok.Race", "tracks");
            _log = log;
            _onSaved = onSaved;
        }

        public bool IsRunning { get; private set; }
        public Vehicle Car => _veh;
        public Ped Driver => _driver;

        public string[] StatusLines => new[]
        {
            "IMPORTANDO PISTAS (la IA maneja, tú de acompañante)",
            _status,
            "Cancelar: F7 → Carrera → terminar importación",
        };

        public void Start()
        {
            if (IsRunning) throw new ActionException("Ya se está importando");
            if (!Directory.Exists(_importDir)) throw new ActionException("No existe la carpeta StreamTok.Race\\import");

            _jobs.Clear();
            foreach (string file in Directory.GetFiles(_importDir, "*.json"))
            {
                Job j = Parse(file);
                if (j != null) _jobs.Enqueue(j);
            }
            if (_jobs.Count == 0) throw new ActionException("No hay archivos válidos en StreamTok.Race\\import");

            _total = _jobs.Count;
            _done = 0;
            _skips = 0;
            Ped player = Game.Player.Character;
            _origin = player.Position;
            Spawner.Preload(CarModel);
            IsRunning = true;
            NextJob();
        }

        public void Cancel()
        {
            if (!IsRunning) return;
            Finish("cancelada");
        }

        private Job Parse(string file)
        {
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as IDictionary<string, object>;
                var job = new Job { Name = Path.GetFileNameWithoutExtension(file) };
                foreach (object item in (System.Collections.IEnumerable)root["points"])
                {
                    var c = new List<float>();
                    foreach (object v in (System.Collections.IEnumerable)item) c.Add(Convert.ToSingle(v, CultureInfo.InvariantCulture));
                    if (c.Count >= 3) job.Points.Add(new Vector3(c[0], c[1], c[2]));
                }
                if (job.Points.Count < 3) throw new Exception("menos de 3 puntos");
                return job;
            }
            catch (Exception ex)
            {
                _log($"Importador: archivo '{Path.GetFileName(file)}' inválido ({ex.Message}).");
                return null;
            }
        }

        private void NextJob()
        {
            DeleteCar();
            if (_jobs.Count == 0)
            {
                Finish("terminada");
                return;
            }

            _job = _jobs.Dequeue();
            _rec.Clear();
            Vector3 a = _job.Points[0], b = _job.Points[1];
            float heading = (float)(Math.Atan2(-(b.X - a.X), b.Y - a.Y) * 180.0 / Math.PI);

            // El mapa tiene que estar cargado antes de aparecer el auto.
            Function.Call(Hash.REQUEST_COLLISION_AT_COORD, a.X, a.Y, a.Z);
            Function.Call(Hash.REQUEST_ADDITIONAL_COLLISION_AT_COORD, a.X, a.Y, a.Z);
            _veh = Spawner.SpawnVehicle(CarModel, a, heading, true);
            _veh.IsInvincible = true;
            _veh.IsEngineRunning = true;
            _driver = _veh.CreateRandomPedOnSeat(VehicleSeat.Driver);
            _driver.IsInvincible = true;
            _driver.BlockPermanentEvents = true;
            Game.Player.Character.SetIntoVehicle(_veh, VehicleSeat.Passenger);

            _rec.Add(a);
            _idx = 1;
            Drive();
            _log($"Importador: '{_job.Name}' ({_job.Points.Count} puntos).");
        }

        private void Drive()
        {
            Vector3 t = _job.Points[_idx];
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE, _driver.Handle, _veh.Handle, t.X, t.Y, t.Z, 28f, 2883621, 8f);
            _wpStart = Game.GameTime;
            _lastMoveAt = _wpStart;
            _lastMovePos = _veh.Position;
        }

        public void Update()
        {
            if (!IsRunning) return;
            if (_veh == null || !_veh.Exists() || _driver == null || !_driver.Exists())
            {
                _log($"Importador: se perdió el auto en '{_job?.Name}'; se descarta esa pista.");
                NextJob();
                return;
            }

            int now = Game.GameTime;
            Vector3 p = _veh.Position;
            Vector3 target = _job.Points[_idx];

            if (p.DistanceTo(_rec[_rec.Count - 1]) >= RecordSpacing) _rec.Add(p);

            if (p.DistanceTo(_lastMovePos) > 2f)
            {
                _lastMovePos = p;
                _lastMoveAt = now;
            }

            _status = $"{_job.Name}  ·  punto {_idx}/{_job.Points.Count - 1}  ·  pista {_done + 1}/{_total}  ·  saltos: {_skips}";

            float flat = new Vector2(p.X - target.X, p.Y - target.Y).Length();
            if (flat < ArriveRadius)
            {
                _idx++;
                if (_idx >= _job.Points.Count)
                {
                    Save();
                    return;
                }
                Drive();
                return;
            }

            // Atascado o demasiado lento en este tramo: se salta al punto y se sigue (queda anotado).
            if (now - _lastMoveAt > StuckMs || now - _wpStart > WaypointTimeoutMs)
            {
                _skips++;
                _log($"Importador: tramo atascado hacia el punto {_idx} de '{_job.Name}'; se salta.");
                _veh.Position = target;
                Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, _veh.Handle, 5f);
                _rec.Add(_veh.Position);
                _idx++;
                if (_idx >= _job.Points.Count)
                {
                    Save();
                    return;
                }
                Drive();
            }
        }

        private void Save()
        {
            var track = new RaceTrack();
            if (track.Build(_rec, out string error))
            {
                string path = Path.Combine(_trackDir, _job.Name + ".json");
                try
                {
                    track.Save(path);
                    _onSaved(_job.Name, track);
                    _done++;
                    _log($"Importador: pista '{_job.Name}' guardada ({_rec.Count} puntos, {track.Length:0} m, saltos acumulados: {_skips}).");
                }
                catch (Exception ex)
                {
                    _log($"Importador: no se pudo guardar '{_job.Name}': {ex.Message}");
                }
            }
            else
            {
                _log($"Importador: '{_job.Name}' descartada ({error}).");
            }
            NextJob();
        }

        private void DeleteCar()
        {
            Ped player = Game.Player.Character;
            if (_veh != null && player.IsInVehicle(_veh))
            {
                player.Task.ClearAllImmediately();
                player.Position = _origin;
            }
            if (_driver != null && _driver.Exists()) _driver.Delete();
            if (_veh != null && _veh.Exists()) _veh.Delete();
            _driver = null;
            _veh = null;
        }

        private void Finish(string how)
        {
            DeleteCar();
            IsRunning = false;
            Game.Player.Character.Position = _origin;
            GTA.UI.Notification.Show($"~p~Carrera~s~: importación {how} ({_done}/{_total} pistas, {_skips} saltos). Recarga los scripts (Insert) para verlas en la lista.");
            _log($"Importador: {how}; {_done}/{_total} pistas, {_skips} saltos.");
        }
    }
}
