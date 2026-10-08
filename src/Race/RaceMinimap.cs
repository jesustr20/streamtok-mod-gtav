using System;
using System.Collections.Generic;
using System.Drawing;
using GTA.Math;
using GTA.Native;

namespace StreamTok.GtaV.Race
{
    /// <summary>
    /// Mapa de la carrera: el trazado completo de la pista a escala y un punto por piloto sobre él.
    /// Va en la esquina inferior derecha, con el tamaño y los márgenes del minimapa de GTA (que está
    /// abajo a la izquierda y se apaga durante la transmisión). Se dibuja con rectángulos, sin texturas.
    /// </summary>
    internal sealed class RaceMinimap
    {
        public struct Dot
        {
            public Vector3 Position;
            public int Rank;          // 1 = líder
            public Color Color;
            public bool Finished;
            public string Label;      // nombre corto ("Bot 1", "Ana")
        }

        // Tamaño y márgenes como el minimapa de GTA, espejados (en fracción de la altura / del ancho de pantalla).
        private const float Side = 0.27f;          // lado del recuadro, en fracción de la altura
        private const float MarginX = 0.0125f;     // fracción del ancho
        private const float MarginY = 0.04f;       // fracción de la altura
        private const float Pad = 0.018f;          // aire entre el recuadro y la pista
        private const int Samples = 110;

        private readonly GTA.UI.TextElement _rank =
            new GTA.UI.TextElement("", PointF.Empty, 0.25f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Left, true, true);

        private RaceTrack _track;
        private PointF[] _line;      // trazado en unidades "de altura" dentro del recuadro (0..Side)
        private float _minX, _minY, _scale, _offX, _offY;

        public void Build(RaceTrack track)
        {
            _track = track;
            var pts = new Vector3[Samples];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < Samples; i++)
            {
                pts[i] = track.PositionAt(track.Length * i / Samples);
                minX = Math.Min(minX, pts[i].X); maxX = Math.Max(maxX, pts[i].X);
                minY = Math.Min(minY, pts[i].Y); maxY = Math.Max(maxY, pts[i].Y);
            }

            float inner = Side - 2f * Pad;
            float dx = Math.Max(1f, maxX - minX), dy = Math.Max(1f, maxY - minY);
            _scale = inner / Math.Max(dx, dy);
            _minX = minX; _minY = minY; _spanY = dy;
            _offX = Pad + (inner - dx * _scale) / 2f;   // centrada en el recuadro
            _offY = Pad + (inner - dy * _scale) / 2f;

            _line = new PointF[Samples];
            for (int i = 0; i < Samples; i++)
            {
                _line[i] = Map(pts[i]);
            }
        }

        public bool IsFor(RaceTrack track) => ReferenceEquals(track, _track) && _line != null;

        public void Draw(IList<Dot> dots)
        {
            if (_line == null) return;

            float aspect = Function.Call<float>(Hash.GET_ASPECT_RATIO, false);
            if (aspect < 1f) aspect = 16f / 9f;

            float boxW = Side / aspect;                       // ancho del recuadro en fracción del ancho
            float left = 1f - MarginX - boxW;
            float top = 1f - MarginY - Side;

            // Fondo y borde.
            Rect(left + boxW / 2f, top + Side / 2f, boxW, Side, 0, 0, 0, 140);
            Rect(left + boxW / 2f, top, boxW, 0.003f, 255, 255, 255, 120);
            Rect(left + boxW / 2f, top + Side, boxW, 0.003f, 255, 255, 255, 120);
            Rect(left, top + Side / 2f, 0.003f / aspect, Side, 255, 255, 255, 120);
            Rect(left + boxW, top + Side / 2f, 0.003f / aspect, Side, 255, 255, 255, 120);

            // Trazado: rectángulos pequeños a lo largo de cada tramo.
            for (int i = 0; i < _line.Length; i++)
            {
                PointF a = _line[i], b = _line[(i + 1) % _line.Length];
                float len = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                int steps = Math.Max(1, (int)(len / 0.0035f));
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps;
                    float px = a.X + (b.X - a.X) * t, py = a.Y + (b.Y - a.Y) * t;
                    Rect(left + px / aspect, top + py, 0.0042f / aspect, 0.0042f, 215, 215, 215, 230);
                }
            }

            // Salida / meta: cuadro blanco con borde negro.
            PointF sf = Map(_track.PositionAt(0f));
            Rect(left + sf.X / aspect, top + sf.Y, 0.014f / aspect, 0.014f, 0, 0, 0, 255);
            Rect(left + sf.X / aspect, top + sf.Y, 0.010f / aspect, 0.010f, 255, 255, 255, 255);

            // Pilotos: del último al líder, para que el líder quede encima.
            for (int i = dots.Count - 1; i >= 0; i--)
            {
                Dot d = dots[i];
                PointF p = Map(d.Position);
                float size = d.Rank == 1 ? 0.020f : d.Rank <= 3 ? 0.017f : 0.012f;
                Color c = d.Rank == 1 ? Color.Gold : d.Color;
                float cx = left + p.X / aspect, cy = top + p.Y;
                Rect(cx, cy, (size + 0.005f) / aspect, size + 0.005f, 0, 0, 0, 230);
                Rect(cx, cy, size / aspect, size, c.R, c.G, c.B, d.Finished ? (byte)130 : (byte)255);
                // Puesto y nombre junto a cada punto; a la izquierda si el punto está en el borde derecho.
                string text = $"{d.Rank}° {d.Label}";
                bool flip = p.X > Side * 0.62f;
                _rank.Caption = text;
                _rank.Color = d.Rank == 1 ? Color.Gold : Color.White;
                _rank.Alignment = flip ? GTA.UI.Alignment.Right : GTA.UI.Alignment.Left;
                float tx = cx + (flip ? -1f : 1f) * ((size / 2f + 0.004f) / aspect);
                _rank.Position = new PointF(tx * 1280f, (cy - 0.011f) * 720f);
                _rank.Draw();
            }
        }

        /// <summary>Mundo → recuadro (norte arriba).</summary>
        private PointF Map(Vector3 world)
        {
            float x = _offX + (world.X - _minX) * _scale;
            float y = _offY + (_spanY - (world.Y - _minY)) * _scale;
            x = Math.Max(Pad, Math.Min(Side - Pad, x));
            y = Math.Max(Pad, Math.Min(Side - Pad, y));
            return new PointF(x, y);
        }

        private float _spanY;

        private static void Rect(float cx, float cy, float w, float h, int r, int g, int b, int a) =>
            Function.Call(Hash.DRAW_RECT, cx, cy, w, h, r, g, b, a);
    }
}
