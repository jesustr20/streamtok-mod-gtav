using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using StreamTok.GtaV.Actions;
using Keys = System.Windows.Forms.Keys;

namespace StreamTok.GtaV.Debug
{
    /// <summary>
    /// Menú de pruebas dentro del juego (F7). Ejecuta las acciones por el MISMO camino
    /// que los comandos de StreamTok, así que lo que funciona aquí funciona desde la app.
    ///
    ///   Lista de acciones:  Arriba/Abajo elegir · Derecha ver parámetros · Enter ejecutar · F7 cerrar
    ///   Parámetros:         Arriba/Abajo elegir · Izq/Der cambiar valor (Shift = x10) · Retroceso volver · Enter ejecutar
    /// </summary>
    internal sealed class DebugMenu
    {
        private const float X = 30f;
        private const float Y = 60f;
        private const float Width = 470f;
        private const float LineHeight = 22f;

        private static readonly Color Normal = Color.White;
        private static readonly Color Selected = Color.FromArgb(255, 255, 210, 60);
        private static readonly Color Dim = Color.FromArgb(255, 170, 170, 170);

        private readonly IReadOnlyList<ActionDef> _actions;
        private readonly Action<ActionDef, Dictionary<string, object>> _run;
        private readonly Dictionary<string, Dictionary<string, object>> _values = new Dictionary<string, Dictionary<string, object>>();
        private readonly GTA.UI.TextElement _text;
        private readonly GTA.UI.ContainerElement _background;

        private bool _open;
        private int _actionIndex;
        private int _paramIndex = -1; // -1 = navegando la lista de acciones

        public DebugMenu(IReadOnlyList<ActionDef> actions, Action<ActionDef, Dictionary<string, object>> run)
        {
            _actions = actions;
            _run = run;

            foreach (ActionDef a in actions)
            {
                _values[a.Id] = a.Params.ToDictionary(p => p.Name, p => p.Default);
            }

            _text = new GTA.UI.TextElement("", PointF.Empty, 0.32f, Normal, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Left, true, false);
            _background = new GTA.UI.ContainerElement(new PointF(X, Y), new SizeF(Width, 0f), Color.FromArgb(190, 0, 0, 0));
        }

        public void OnKeyDown(Keys key, bool shift)
        {
            if (key == Keys.F7)
            {
                _open = !_open;
                _paramIndex = -1;
                return;
            }

            if (!_open || _actions.Count == 0)
            {
                return;
            }

            ActionDef action = _actions[_actionIndex];
            int paramCount = action.Params.Length;

            switch (key)
            {
                case Keys.Up:
                    if (_paramIndex < 0) _actionIndex = (_actionIndex - 1 + _actions.Count) % _actions.Count;
                    else _paramIndex = (_paramIndex - 1 + paramCount) % paramCount;
                    break;

                case Keys.Down:
                    if (_paramIndex < 0) _actionIndex = (_actionIndex + 1) % _actions.Count;
                    else _paramIndex = (_paramIndex + 1) % paramCount;
                    break;

                case Keys.Right:
                    if (_paramIndex < 0) { if (paramCount > 0) _paramIndex = 0; }
                    else ChangeValue(action, +1, shift);
                    break;

                case Keys.Left:
                    if (_paramIndex >= 0) ChangeValue(action, -1, shift);
                    break;

                case Keys.Back:
                    _paramIndex = -1;
                    break;

                case Keys.Enter:
                    _run(action, new Dictionary<string, object>(_values[action.Id]));
                    break;
            }
        }

        /// <summary>Llamar cada frame.</summary>
        public void Draw()
        {
            if (!_open)
            {
                return;
            }

            // La flecha arriba abre el teléfono en GTA: bloquearlo mientras el menú está abierto.
            GTA.Game.DisableControlThisFrame(GTA.Control.Phone);

            var lines = new List<KeyValuePair<string, Color>>
            {
                Line("StreamTok · Menú de pruebas", Selected),
                Line(_paramIndex < 0
                    ? "Arriba/Abajo: elegir  ·  Derecha: parámetros  ·  Enter: ejecutar  ·  F7: cerrar"
                    : "Arriba/Abajo: parámetro  ·  Izq/Der: valor (Shift x10)  ·  Retroceso: volver", Dim),
                Line("", Normal),
            };

            for (int i = 0; i < _actions.Count; i++)
            {
                ActionDef a = _actions[i];
                bool sel = i == _actionIndex;
                lines.Add(Line($"{(sel ? "> " : "   ")}{a.Name}  ({a.Id})", sel && _paramIndex < 0 ? Selected : sel ? Normal : Dim));

                if (sel && _paramIndex >= 0)
                {
                    for (int p = 0; p < a.Params.Length; p++)
                    {
                        ParamDef def = a.Params[p];
                        object value = _values[a.Id][def.Name];
                        string shown = def.Type == "bool" ? ((bool)value ? "sí" : "no") : value.ToString();
                        lines.Add(Line($"         {(p == _paramIndex ? "> " : "   ")}{def.Name}: {shown}", p == _paramIndex ? Selected : Normal));
                    }
                }
            }

            _background.Size = new SizeF(Width, lines.Count * LineHeight + 12f);
            _background.Draw();

            for (int i = 0; i < lines.Count; i++)
            {
                _text.Caption = lines[i].Key;
                _text.Color = lines[i].Value;
                _text.Position = new PointF(X + 10f, Y + 6f + i * LineHeight);
                _text.Draw();
            }
        }

        private void ChangeValue(ActionDef action, int direction, bool shift)
        {
            ParamDef def = action.Params[_paramIndex];
            Dictionary<string, object> values = _values[action.Id];
            object current = values[def.Name];

            switch (def.Type)
            {
                case "int":
                    int step = shift ? 10 : 1;
                    int next = (int)current + direction * step;
                    if (def.Min.HasValue) next = Math.Max(def.Min.Value, next);
                    if (def.Max.HasValue) next = Math.Min(def.Max.Value, next);
                    values[def.Name] = next;
                    break;

                case "enum":
                    int idx = Array.IndexOf(def.Options, (string)current);
                    idx = (idx + direction + def.Options.Length) % def.Options.Length;
                    values[def.Name] = def.Options[idx];
                    break;

                case "bool":
                    values[def.Name] = !(bool)current;
                    break;
            }
        }

        private static KeyValuePair<string, Color> Line(string text, Color color) =>
            new KeyValuePair<string, Color>(text, color);
    }
}
