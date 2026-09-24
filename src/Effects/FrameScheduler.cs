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

        /// <summary>Llamar cada frame.</summary>
        public void Update()
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                Job job = _jobs[i];
                try
                {
                    job.Action();
                }
                catch (Exception ex)
                {
                    _log($"Tarea por frames: {ex.Message}");
                    job.Remaining = 0;
                }

                if (--job.Remaining <= 0)
                {
                    _jobs.RemoveAt(i);
                }
            }
        }

        public void Clear() => _jobs.Clear();

        private sealed class Job
        {
            public int Remaining;
            public Action Action;
        }
    }
}
