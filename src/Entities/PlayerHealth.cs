using System;
using System.Globalization;
using System.Drawing;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

namespace StreamTok.GtaV.Entities
{
    /// <summary>
    /// Vida máxima personalizada del jugador. Vale para TODO el mod (con cualquier modo encendido o apagado) y se
    /// guarda en StreamTok.PlayerHealth.txt (carpeta scripts): al volver a abrir el juego sigue igual.
    ///
    /// La vida se cuenta como en el resto del mod: 100 = la vida normal de GTA (el valor interno de GTA es esa
    /// cifra + 100, porque con 100 el personaje ya está muerto). Cuando GTA la reinicia (reaparecer, cambiar de
    /// personaje o de modelo) se vuelve a aplicar sola.
    /// </summary>
    internal static class PlayerHealth
    {
        public const int Normal = 100;
        public const int Min = 10;
        public const int Max = 200000;

        private const int InternalOffset = 100;
        private const int NormalInternalMax = 200;
        private const int CheckEveryMs = 250;

        private static string _file;
        private static Action<string> _log = _ => { };
        private static int _hp = Normal;
        private static bool _appliedCustom;
        private static int _nextCheck;
        private static bool _showHud;
        private static GTA.UI.ContainerElement _back;
        private static GTA.UI.ContainerElement _fill;
        private static GTA.UI.TextElement _text;

        /// <summary>Barra verde y número de vida sobre la cabeza del jugador (solo la ve el streamer en su pantalla).</summary>
        public static bool ShowHud => _showHud;

        /// <summary>Vida elegida (100 = normal).</summary>
        public static int Value => _hp;

        public static void Init(string baseDirectory, Action<string> log)
        {
            _log = log ?? (_ => { });
            _file = Path.Combine(baseDirectory, "StreamTok.PlayerHealth.txt");
            try
            {
                // Formato: "vida;mostrar" (ej. "1000;1"). Admite el formato viejo, solo la vida.
                if (File.Exists(_file))
                {
                    string[] parts = File.ReadAllText(_file).Trim().Split(';');
                    if (int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int saved))
                    {
                        _hp = Clamp(saved);
                    }
                    _showHud = parts.Length > 1 && parts[1].Trim() == "1";
                    _log($"Vida del jugador cargada: {_hp} (barra sobre la cabeza: {(_showHud ? "sí" : "no")})");
                }
            }
            catch (Exception ex)
            {
                _log($"No se pudo leer la vida guardada: {ex.Message}");
            }
        }

        /// <summary>Cambia la vida, la guarda y la aplica ya (el jugador queda con la vida llena).</summary>
        public static void Set(int hp)
        {
            _hp = Clamp(hp);
            Save();

            Ped p = Game.Player.Character;
            if (p != null && p.Exists())
            {
                _appliedCustom = true;
                p.MaxHealth = _hp + InternalOffset;
                if (!p.IsDead) p.Health = p.MaxHealth;
            }
            _log($"Vida del jugador: {_hp}");
        }

        /// <summary>Muestra u oculta la barra y el número de vida sobre la cabeza del jugador.</summary>
        public static void SetHud(bool on)
        {
            _showHud = on;
            Save();
            _log($"Vida sobre la cabeza del jugador: {(on ? "visible" : "oculta")}");
        }

        private static void Save()
        {
            try
            {
                if (_file != null)
                {
                    File.WriteAllText(_file, _hp.ToString(CultureInfo.InvariantCulture) + ";" + (_showHud ? "1" : "0"));
                }
            }
            catch (Exception ex)
            {
                _log($"No se pudo guardar la vida: {ex.Message}");
            }
        }

        /// <summary>Llamar cada frame: dibuja la barra verde con el número encima de la cabeza del jugador.</summary>
        public static void Draw()
        {
            if (!_showHud) return;

            Ped p = Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;

            // En primera persona la cabeza queda sobre la cámara: no se dibuja.
            int view = p.IsInVehicle()
                ? Function.Call<int>(Hash.GET_FOLLOW_VEHICLE_CAM_VIEW_MODE)
                : Function.Call<int>(Hash.GET_FOLLOW_PED_CAM_VIEW_MODE);
            if (view == 4) return;

            Vector3 head = p.Bones[Bone.SkelHead].Position + new Vector3(0f, 0f, 0.5f);
            PointF screen = GTA.UI.Screen.WorldToScreen(head);
            if (screen.IsEmpty) return;

            if (_back == null)
            {
                _back = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.FromArgb(170, 0, 0, 0));
                _fill = new GTA.UI.ContainerElement(PointF.Empty, SizeF.Empty, Color.Green);
                _text = new GTA.UI.TextElement("", PointF.Empty, 0.55f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
            }

            int max = Math.Max(1, p.MaxHealth - InternalOffset);
            int now = Math.Max(0, Math.Min(max, p.Health - InternalOffset));
            float fraction = (float)now / max;

            const float Width = 150f;
            const float Height = 14f;
            float x = screen.X - Width / 2f;
            float y = screen.Y;

            _back.Position = new PointF(x - 1f, y - 1f);
            _back.Size = new SizeF(Width + 2f, Height + 2f);
            _back.Draw();

            _fill.Color = fraction > 0.5f ? Color.FromArgb(235, 60, 200, 60)
                        : fraction > 0.25f ? Color.FromArgb(235, 230, 200, 40)
                        : Color.FromArgb(235, 220, 40, 40);
            _fill.Position = new PointF(x, y);
            _fill.Size = new SizeF(Width * fraction, Height);
            _fill.Draw();

            _text.Caption = now == max ? $"{max}" : $"{now} / {max}";
            _text.Position = new PointF(screen.X, y - 36f);
            _text.Draw();
        }

        /// <summary>Llamar cada frame: si GTA reinició la vida máxima (reaparecer, otro personaje), se vuelve a poner.</summary>
        public static void Update()
        {
            int now = Game.GameTime;
            if (now < _nextCheck) return;
            _nextCheck = now + CheckEveryMs;

            // Sin personalizar (100) no se toca nada, salvo para volver a lo normal tras haberla cambiado.
            if (_hp == Normal && !_appliedCustom) return;

            Ped p = Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;

            int want = _hp == Normal ? NormalInternalMax : _hp + InternalOffset;
            int oldMax = p.MaxHealth;
            if (oldMax != want)
            {
                p.MaxHealth = want;
                // Si sube (reaparece o cambia de personaje) se suma la diferencia; si baja, se recorta.
                p.Health = want > oldMax ? Math.Min(want, p.Health + (want - oldMax)) : Math.Min(p.Health, want);
            }

            if (_hp == Normal) _appliedCustom = false;
        }

        private static int Clamp(int hp) => Math.Max(Min, Math.Min(Max, hp));
    }
}
