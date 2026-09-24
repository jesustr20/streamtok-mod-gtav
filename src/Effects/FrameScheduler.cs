using System;
using System.Collections.Generic;

namespace StreamTok.GtaV.Effects
{
    /// <summary>
    /// Ejecuta algo durante los próximos N frames. Sirve para lo que el juego "pisa" en el
    /// primer frame (ej. la velocidad de un vehículo recién creado) y hay que sostener un momento.
    /// </summary>
    internal sealed class FrameScheduler
    {
        private readonly List<Job> _jobs = new List<Job>();
        private readonly Action<string> _log;

        public FrameScheduler(Action<string> log)
        {
            _log = log;
        }

        /// <summary>Ejecuta <paramref name="action"/> en cada uno de los próximos <paramref name="frames"/> frames.</summary>
        public void Repeat(int frames, Action action)
        {
            _jobs.Add(new Job { Remaining = frames, Action = action });
        }

        /// <summary>
        /// Bucle sin fin con clave: <paramref name="tick"/> cada frame hasta <see cref="Cancel"/>
        /// (o hasta que el script se detenga), y entonces <paramref name="onEnd"/> para revertir.
        /// Si ya existe uno con esa clave, no hace nada. Para efectos del mundo que se activan o
        /// desactivan (vehículos invisibles, coches rápidos, gravedad).
        /// </summary>
        public void Loop(string key, Action onStart, Action tick, Action onEnd)
        {
            if (_jobs.Exists(j => j.Key == key))
            {
                return;
            }
            onStart?.Invoke();
            _jobs.Add(new Job { Key = key, Forever = true, Action = tick, OnEnd = onEnd });
        }

        /// <summary>Detiene la tarea con esa clave y ejecuta su OnEnd. false si no existía.</summary>
        public bool Cancel(string key)
        {
            Job job = _jobs.Find(j => j.Key == key);
            if (job == null)
            {
                return false;
            }
            _jobs.Remove(job);
            Finish(job);
            return true;
        }

        public bool IsRunning(string key) => _jobs.Exists(j => j.Key == key);

        /// <summary>
        /// Ejecuta <paramref name="step"/> una vez por frame hasta que devuelva true. Sirve para
        /// procesos por pasos que NO deben bloquear el juego (ej. cambiar el modelo del jugador).
        /// </summary>
        public void Until(Func<bool> step)
        {
            _jobs.Add(new Job { Step = step });
        }

        /// <summary>
        /// Ejecuta <paramref name="action"/> cada frame durante <paramref name="seconds"/> segundos
        /// y luego <paramref name="onEnd"/>. Si ya hay una con la misma clave, solo se le suma el tiempo.
        /// Para efectos del MUNDO (terremoto…); los del personaje no tienen duración.
        /// </summary>
        public void RepeatFor(string key, int seconds, Action action, Action onEnd)
        {
            int now = GTA.Game.GameTime;
            Job existing = _jobs.Find(j => j.Key == key);
            if (existing != null)
            {
                existing.EndsAt = Math.Max(existing.EndsAt, now) + seconds * 1000;
                return;
            }
            _jobs.Add(new Job { Key = key, EndsAt = now + seconds * 1000, Action = action, OnEnd = onEnd });
        }

        /// <summary>Llamar cada frame.</summary>
        public void Update()
        {
            int now = GTA.Game.GameTime;

            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                Job job = _jobs[i];
                bool done;
                try
                {
                    if (job.Step != null)
                    {
                        done = job.Step();
                    }
                    else
                    {
                        job.Action?.Invoke();
                        done = !job.Forever && (job.EndsAt > 0 ? now >= job.EndsAt : --job.Remaining <= 0);
                    }
                }
                catch (Exception ex)
                {
                    _log($"Tarea por frames: {ex.Message}");
                    done = true;
                }

                if (done)
                {
                    _jobs.RemoveAt(i);
                    Finish(job);
                }
            }
        }

        /// <summary>Termina todo ya (al recargar el script), ejecutando los OnEnd pendientes.</summary>
        public void Clear()
        {
            foreach (Job job in _jobs)
            {
                Finish(job);
            }
            _jobs.Clear();
        }

        private void Finish(Job job)
        {
            try
            {
                job.OnEnd?.Invoke();
            }
            catch (Exception ex)
            {
                _log($"Fin de tarea '{job.Key}': {ex.Message}");
            }
        }

        private sealed class Job
        {
            public string Key;
            public bool Forever;
            public int Remaining;
            public int EndsAt;
            public Action Action;
            public Func<bool> Step;
            public Action OnEnd;
        }
    }
}
