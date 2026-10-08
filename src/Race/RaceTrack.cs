using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using GTA;
using GTA.Math;

namespace StreamTok.GtaV.Race
{
    /// <summary>
    /// Circuito cerrado de la carrera. El streamer lo graba manejando (ver RaceMode); acá se
    /// suaviza (Catmull-Rom) y se re-muestrea cada ~3 m, para poder preguntar "¿dónde está el
    /// punto a X metros desde la salida?" y "¿hacia dónde apunta ahí?". Todo se mide en metros
    /// recorridos (distancia): la vuelta se cierra sola con el módulo de la longitud.
    /// </summary>
    internal sealed class RaceTrack
    {
        /// <summary>Separación entre muestras (m).</summary>
        private const float Spacing = 3f;

        /// <summary>Menos que esto no es una pista.</summary>
        public const int MinRawPoints = 8;
        public const float MinLength = 150f;

        private Vector3[] _pts = new Vector3[0];
        private Vector3[] _tan = new Vector3[0];
        private float _step = Spacing;

        /// <summary>Puntos tal como se grabaron (antes de suavizar).</summary>
        public List<Vector3> Raw { get; private set; } = new List<Vector3>();

        /// <summary>Largo de una vuelta (m).</summary>
        public float Length { get; private set; }

        public bool IsReady => _pts.Length >= 8 && Length >= MinLength;

        public Vector3 Start => _pts.Length > 0 ? _pts[0] : Vector3.Zero;

        // ------------------------------------------------------------ construcción

        /// <summary>Arma la pista a partir de los puntos grabados. Devuelve false si no sirve (con el motivo).</summary>
        public bool Build(List<Vector3> raw, out string error)
        {
            error = null;
            if (raw == null || raw.Count < MinRawPoints)
            {
                error = $"Pocos puntos ({raw?.Count ?? 0}); maneja más lejos (mínimo {MinRawPoints}).";
                return false;
            }

            // 1) Curva fina (Catmull-Rom cerrada) con una muestra cada ~0.75 m.
            var dense = new List<Vector3>();
            int n = raw.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = raw[(i - 1 + n) % n], p1 = raw[i], p2 = raw[(i + 1) % n], p3 = raw[(i + 2) % n];
                int steps = Math.Max(2, (int)Math.Ceiling(p1.DistanceTo(p2) / 0.75f));
                for (int k = 0; k < steps; k++)
                {
                    dense.Add(CatmullRom(p0, p1, p2, p3, k / (float)steps));
                }
            }

            // 2) Largo acumulado.
            var cum = new float[dense.Count + 1];
            for (int i = 0; i < dense.Count; i++)
            {
                cum[i + 1] = cum[i] + dense[i].DistanceTo(dense[(i + 1) % dense.Count]);
            }
            float total = cum[dense.Count];
            if (total < MinLength)
            {
                error = $"La pista mide solo {total:0} m (mínimo {MinLength:0}).";
                return false;
            }

            // 3) Re-muestreo parejo: cantidad entera de pasos para que la vuelta cierre exacto.
            int count = Math.Max(8, (int)Math.Round(total / Spacing));
            float step = total / count;
            var pts = new Vector3[count];
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                float target = i * step;
                while (j < dense.Count - 1 && cum[j + 1] < target) j++;
                float seg = cum[j + 1] - cum[j];
                float t = seg > 0.0001f ? (target - cum[j]) / seg : 0f;
                pts[i] = Vector3.Lerp(dense[j], dense[(j + 1) % dense.Count], t);
            }

            // 4) Tangentes (diferencia central, ya normalizadas).
            var tan = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 d = pts[(i + 1) % count] - pts[(i - 1 + count) % count];
                d.Normalize();
                tan[i] = d;
            }

            Raw = new List<Vector3>(raw);
            _pts = pts;
            _tan = tan;
            _step = step;
            Length = total;
            return true;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ------------------------------------------------------------ salida en recta

        /// <summary>
        /// Busca en la pista el tramo más recto de <paramref name="before"/> + <paramref name="after"/> metros y
        /// devuelve una copia de la pista con la línea de salida en él: la parrilla y los metros que siguen quedan
        /// en recta, y las curvas vienen después. No toca el archivo guardado. Devuelve null si no se pudo.
        /// </summary>
        public RaceTrack WithStraightStart(float before, float after, out float startDist, out float maxDeviationDeg)
        {
            startDist = 0f;
            maxDeviationDeg = 0f;
            int n = _pts.Length;
            if (n < 16 || Raw == null || Raw.Count < MinRawPoints) return null;

            // Una pista corta no tiene tanto tramo: se reduce para que quepa en media vuelta.
            float scale = Math.Min(1f, (Length * 0.5f) / (before + after));
            int nb = Math.Max(2, (int)(before * scale / _step));
            int na = Math.Max(2, (int)(after * scale / _step));

            int best = -1;
            float bestDev = float.MaxValue, bestSum = float.MaxValue;
            for (int s = 0; s < n; s++)
            {
                Vector3 t0 = _tan[s];
                float maxDev = 0f, sum = 0f;
                for (int k = -nb; k <= na; k += 2)
                {
                    Vector3 tk = _tan[((s + k) % n + n) % n];
                    float dot = Math.Max(-1f, Math.Min(1f, t0.X * tk.X + t0.Y * tk.Y));
                    float dev = (float)(Math.Acos(dot) * 180.0 / Math.PI);
                    if (dev > maxDev) maxDev = dev;
                    sum += dev;
                }
                if (maxDev < bestDev - 0.01f || (Math.Abs(maxDev - bestDev) <= 0.01f && sum < bestSum))
                {
                    bestDev = maxDev;
                    bestSum = sum;
                    best = s;
                }
            }
            if (best < 0) return null;

            // El punto grabado más cercano al elegido pasa a ser el primero.
            Vector3 target = _pts[best];
            int j = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < Raw.Count; i++)
            {
                float d = Raw[i].DistanceTo(target);
                if (d < bestD) { bestD = d; j = i; }
            }
            var rotated = new List<Vector3>(Raw.Count);
            for (int i = 0; i < Raw.Count; i++) rotated.Add(Raw[(j + i) % Raw.Count]);

            var copy = new RaceTrack();
            if (!copy.Build(rotated, out string error)) return null;
            startDist = best * _step;
            maxDeviationDeg = bestDev;
            return copy;
        }

        // ------------------------------------------------------------ consulta

        /// <summary>Punto de la pista a <paramref name="dist"/> metros de la salida (da la vuelta sola; acepta negativos).</summary>
        public Vector3 PositionAt(float dist)
        {
            Locate(dist, out int i, out int i2, out float t);
            return Vector3.Lerp(_pts[i], _pts[i2], t);
        }

        /// <summary>Dirección de la pista en ese punto (unitaria, con pendiente).</summary>
        public Vector3 TangentAt(float dist)
        {
            Locate(dist, out int i, out int i2, out float t);
            Vector3 d = Vector3.Lerp(_tan[i], _tan[i2], t);
            d.Normalize();
            return d;
        }

        private void Locate(float dist, out int i, out int i2, out float t)
        {
            float d = dist % Length;
            if (d < 0f) d += Length;
            float f = d / _step;
            i = (int)f % _pts.Length;
            i2 = (i + 1) % _pts.Length;
            t = f - (float)Math.Floor(f);
        }

        /// <summary>
        /// Dónde va un auto real sobre la pista: devuelve la distancia continua (cuenta vueltas) más cercana a
        /// <paramref name="hint"/> (la última conocida) y a qué distancia lateral está de la pista.
        /// </summary>
        public float Project(Vector3 p, float hint, out float lateral)
        {
            int n = _pts.Length;
            float h = hint % Length;
            if (h < 0f) h += Length;
            int c = (int)(h / _step) % n;

            int best = -1;
            float bestD = float.MaxValue;
            for (int k = -25; k <= 40; k++)
            {
                int i = ((c + k) % n + n) % n;
                float dx = _pts[i].X - p.X, dy = _pts[i].Y - p.Y;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }

            if (best < 0 || bestD > 40f * 40f)
            {
                // Se perdió (teletransporte): búsqueda en toda la pista.
                bestD = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    float dx = _pts[i].X - p.X, dy = _pts[i].Y - p.Y;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }

            lateral = (float)Math.Sqrt(bestD);
            float local = best * _step;
            float baseDist = hint - h;
            float cand = baseDist + local;
            if (cand - hint > Length / 2f) cand -= Length;
            else if (hint - cand > Length / 2f) cand += Length;
            return cand;
        }

        /// <summary>Qué tan rápido se puede pasar por este punto: 1 = recta, ~0.55 = curva muy cerrada.</summary>
        public float CurveFactor(float dist)
        {
            Vector3 a = TangentAt(dist), b = TangentAt(dist + 22f);
            float dot = Math.Max(-1f, Math.Min(1f, a.X * b.X + a.Y * b.Y));
            float angle = (float)Math.Acos(dot); // radianes de giro en 22 m
            return Math.Max(0.55f, 1f - angle * 0.5f);
        }

        /// <summary>Distancia (plana) de un punto a la pista; para tramos más lejos que <paramref name="max"/> devuelve algo ≥ max.</summary>
        public float DistanceToTrack(Vector3 p, float max)
        {
            float best = max;
            for (int i = 0; i < _pts.Length; i++)
            {
                float dx = _pts[i].X - p.X, dy = _pts[i].Y - p.Y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d < best) best = d;
            }
            return best;
        }

        // ------------------------------------------------------------ dibujo de ayuda

        /// <summary>Dibuja la pista (línea amarilla; la salida en verde) cerca de <paramref name="origin"/>.</summary>
        public void DrawNear(Vector3 origin, float range)
        {
            if (_pts.Length < 2) return;
            for (int i = 0; i < _pts.Length; i++)
            {
                Vector3 a = _pts[i];
                if (a.DistanceTo(origin) > range) continue;
                Vector3 b = _pts[(i + 1) % _pts.Length];
                World.DrawLine(a + new Vector3(0f, 0f, 0.3f), b + new Vector3(0f, 0f, 0.3f), Color.Yellow);
            }
            World.DrawLine(_pts[0], _pts[0] + new Vector3(0f, 0f, 10f), Color.Lime);
        }

        // ------------------------------------------------------------ guardado

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var list = new List<float[]>();
            foreach (Vector3 p in Raw)
            {
                list.Add(new[] { p.X, p.Y, p.Z });
            }
            string json = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { ["points"] = list });
            File.WriteAllText(path, json);
        }

        /// <summary>Carga la pista guardada; devuelve null si no hay o está dañada.</summary>
        public static RaceTrack Load(string path, out string error)
        {
            error = null;
            if (!File.Exists(path)) return null;
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as IDictionary<string, object>;
                var arr = root != null && root.TryGetValue("points", out object o) ? o as System.Collections.IEnumerable : null;
                if (arr == null)
                {
                    error = "track.json sin 'points'";
                    return null;
                }

                var raw = new List<Vector3>();
                foreach (object item in arr)
                {
                    var c = new List<float>();
                    foreach (object v in (System.Collections.IEnumerable)item)
                    {
                        c.Add(Convert.ToSingle(v, CultureInfo.InvariantCulture));
                    }
                    if (c.Count == 3) raw.Add(new Vector3(c[0], c[1], c[2]));
                }

                var track = new RaceTrack();
                return track.Build(raw, out error) ? track : null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
    }
}
