using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;

namespace StreamTok.GtaV.Effects
{
    /// <summary>
    /// Efectos sobre el personaje (inmortal, invisible, súper salto…).
    ///  - NO tienen duración: quedan activos hasta que otra acción los desactive.
    ///  - Muestran encima del personaje el nombre del viewer que los activó.
    ///  - Si otro viewer activa el MISMO efecto, su nombre reemplaza al anterior.
    ///  - Al recargar el script (Insert) se revierten, para no dejar al jugador "roto".
    /// </summary>
    internal sealed class PlayerEffects
    {
        private const float LineSpacing = 20f;

        private readonly Dictionary<string, Effect> _active = new Dictionary<string, Effect>();
        private readonly Action<string> _log;
        private readonly GTA.UI.TextElement _label = new GTA.UI.TextElement(
            "", PointF.Empty, 0.35f, Color.White, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);

        public PlayerEffects(Action<string> log)
        {
            _log = log;
        }

        /// <summary>
        /// Activa o desactiva un efecto. Activarlo estando activo solo cambia el nombre del viewer.
        /// </summary>
        public void Set(string key, string label, bool enabled, string nameTag, Action onStart, Action onTick, Action onEnd)
        {
            _active.TryGetValue(key, out Effect existing);

            if (!enabled)
            {
                if (existing != null)
                {
                    Safe(existing.OnEnd, key);
                    _active.Remove(key);
                }
                return;
            }

            if (existing != null)
            {
                existing.NameTag = nameTag; // el último viewer reemplaza al anterior
                return;
            }

            Safe(onStart, key);
            _active[key] = new Effect { Label = label, NameTag = nameTag, OnTick = onTick, OnEnd = onEnd };
        }

        /// <summary>Llamar cada frame: mantiene los efectos y dibuja los nombres sobre el personaje.</summary>
        public void Update()
        {
            if (_active.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, Effect> kv in _active)
            {
                Safe(kv.Value.OnTick, kv.Key);
            }

            Ped player = GTA.Game.Player.Character;
            Vector3 head = player.Bones[Bone.SkelHead].Position + new Vector3(0f, 0f, 0.45f);
            PointF screen = GTA.UI.Screen.WorldToScreen(head);
            if (screen.IsEmpty)
            {
                return;
            }

            // Una línea por efecto, apiladas hacia arriba: "Juan Pérez · Inmortal".
            int line = 0;
            foreach (Effect e in _active.Values)
            {
                _label.Caption = e.NameTag != null ? $"{e.NameTag} · {e.Label}" : e.Label;
                _label.Position = new PointF(screen.X, screen.Y - line * LineSpacing);
                _label.Draw();
                line++;
            }
        }

        /// <summary>Desactiva todos los efectos (al recargar o cerrar el script).</summary>
        public void EndAll()
        {
            foreach (KeyValuePair<string, Effect> kv in _active.ToList())
            {
                Safe(kv.Value.OnEnd, kv.Key);
            }
            _active.Clear();
        }

        private void Safe(Action action, string key)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                _log($"Efecto '{key}': {ex.Message}");
            }
        }

        private sealed class Effect
        {
            public string Label;
            public string NameTag;
            public Action OnTick;
            public Action OnEnd;
        }
    }
}
