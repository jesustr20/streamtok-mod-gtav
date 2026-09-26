using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Modes;
using Keys = System.Windows.Forms.Keys;

namespace StreamTok.GtaV.Debug
{
    /// <summary>
    /// Menú de pruebas dentro del juego (F7). Ejecuta las acciones por el MISMO camino
    /// que los comandos de StreamTok, así que lo que funciona aquí funciona desde la app.
    ///
    /// Organizado por secciones:
    ///   Juego normal   → una subsección por categoría (NPC, Vehículos, Jugador…) → acciones
    ///   Monte Chiliad  → interruptor ON/OFF del modo + sus opciones
    ///   (cada modo nuevo tendrá su propia sección)
    ///
    ///   Listas:      Arriba/Abajo elegir · Enter/Derecha entrar o ejecutar · Izquierda/Retroceso volver · F7 cerrar
    ///   Parámetros:  Arriba/Abajo elegir · Izq/Der cambiar valor (Shift = x10) · Enter ejecutar · Retroceso volver
    /// </summary>
    internal sealed class DebugMenu
    {
        private const float X = 30f;
        private const float Y = 50f;
        private const float Width = 560f;
        private const float LineHeight = 22f;

        /// <summary>Elementos visibles a la vez; el resto se ve desplazándose.</summary>
        private const int VisibleItems = 14;

        private static readonly Color Normal = Color.White;
        private static readonly Color Selected = Color.FromArgb(255, 255, 210, 60);
        private static readonly Color Dim = Color.FromArgb(255, 170, 170, 170);

        /// <summary>Nombre y orden de las categorías dentro de "Juego normal".</summary>
        private static readonly (string Id, string Title)[] Categories =
        {
            (ActionMeta.Npc, "NPC y atacantes"),
            (ActionMeta.Vehicle, "Vehículos"),
            (ActionMeta.Player, "Jugador"),
            (ActionMeta.Weapon, "Armas y dinero"),
            (ActionMeta.World, "Mundo"),
            (ActionMeta.Spectacle, "Espectáculos"),
            (ActionMeta.Character, "Personajes"),
            (ActionMeta.Other, "Otros"),
        };

        /// <summary>Opciones del modo Chiliad (además del interruptor del modo y los de GPS, tiempo y reaparición).</summary>
        private static readonly string[] ChiliadOptions =
        {
            "chiliad_time", "chiliad_gps_off", "chiliad_back_to_base", "chiliad_set_goal", "chiliad_set_start", "chiliad_set_taxi_stop",
            "road_accident", "wrecks_remove", "spawn_ramp", "ramps_remove",
        };

        private readonly Action<ActionDef, Dictionary<string, object>> _run;
        private readonly Dictionary<string, Dictionary<string, object>> _values = new Dictionary<string, Dictionary<string, object>>();
        private readonly GTA.UI.TextElement _text;
        private readonly GTA.UI.ContainerElement _background;
        private readonly Stack<Level> _path = new Stack<Level>();

        private bool _open;
        private int _paramIndex = -1; // -1 = navegando una lista

        public DebugMenu(IReadOnlyList<ActionDef> actions, Action<ActionDef, Dictionary<string, object>> run, ChiliadMode chiliad, ArenaMode arena, ParkourMode parkour, Action<int> stressTest = null)
        {
            _run = run;

            foreach (ActionDef a in actions)
            {
                _values[a.Id] = a.Params.ToDictionary(p => p.Name, p => p.Default);
            }

            Item root = BuildRoot(actions, chiliad, arena, parkour);
            if (stressTest != null)
            {
                // Prueba de estrés: muchas acciones al azar seguidas, para ver si el juego aguanta.
                var stress = new ActionDef("debug_stress", "Prueba de estrés", false,
                    new[] { ParamDef.Int("count", 50, 10, 300, 20, 50, 100, 200, 300) }, null);
                _values[stress.Id] = stress.Params.ToDictionary(x => x.Name, x => x.Default);
                root.Children.Add(new Item { Action = stress, Custom = v => stressTest((int)v["count"]) });
            }
            _path.Push(new Level { Menu = root });

            _text = new GTA.UI.TextElement("", PointF.Empty, 0.32f, Normal, GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Left, true, false);
            _background = new GTA.UI.ContainerElement(new PointF(X, Y), new SizeF(Width, 0f), Color.FromArgb(190, 0, 0, 0));
        }

        // ================================================================ estructura

        private static Item BuildRoot(IReadOnlyList<ActionDef> actions, ChiliadMode mode, ArenaMode arenaMode, ParkourMode parkourMode)
        {
            var byId = actions.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

            // --- Juego normal: todo lo que no pertenece a un modo, agrupado por categoría.
            var normal = new Item { Title = "Juego normal" };
            foreach (var cat in Categories)
            {
                List<ActionDef> list = actions.Where(a => a.Category == cat.Id).ToList();
                if (list.Count > 0)
                {
                    normal.Children.Add(new Item
                    {
                        Title = cat.Title,
                        Children = list.Select(a => new Item { Action = a }).ToList(),
                    });
                }
            }

            var root = new Item { Title = "StreamTok" };
            root.Children.Add(normal);

            // --- Monte Chiliad: interruptor + opciones.
            if (byId.TryGetValue("chiliad_start", out ActionDef start) && byId.TryGetValue("chiliad_stop", out ActionDef stop))
            {
                Func<bool> chiliadActive = () => mode.IsActive;
                var chiliad = new Item
                {
                    Title = "Monte Chiliad",
                    IsOn = chiliadActive,
                };
                chiliad.Children.Add(new Item { Action = start, OffAction = stop, IsOn = chiliadActive });

                // Interruptores: al iniciar todo queda en ON; Enter lo cambia.
                AddSwitch(chiliad, byId, "chiliad_gps", () => mode.GpsOn, chiliadActive);
                AddSwitch(chiliad, byId, "chiliad_route", () => mode.RouteOn, chiliadActive);
                AddSwitch(chiliad, byId, "chiliad_timer", () => mode.TimerOn, chiliadActive);
                AddSwitch(chiliad, byId, "chiliad_respawn", () => mode.RespawnOn, chiliadActive);
                AddSwitch(chiliad, byId, "chiliad_taxi", () => mode.TaxiOn, chiliadActive);

                foreach (string id in ChiliadOptions)
                {
                    if (byId.TryGetValue(id, out ActionDef a))
                    {
                        chiliad.Children.Add(new Item { Action = a, NeedsOn = id.StartsWith("chiliad_") && !id.StartsWith("chiliad_set_") ? chiliadActive : null });
                    }
                }
                root.Children.Add(chiliad);
            }

            // --- Pelea de viewers: interruptor + opciones.
            if (byId.TryGetValue("arena_start", out ActionDef aStart) && byId.TryGetValue("arena_stop", out ActionDef aStop))
            {
                Func<bool> arenaActive = () => arenaMode.IsActive;
                var arena = new Item { Title = "Pelea de viewers", IsOn = arenaActive };
                arena.Children.Add(new Item { Action = aStart, OffAction = aStop, IsOn = arenaActive });
                AddSwitch(arena, byId, "arena_player", () => arenaMode.PlayerFighting, arenaActive);
                foreach (string id in new[] { "arena_join", "arena_boost", "arena_weapon", "arena_power", "arena_bots", "arena_set_place" })
                {
                    if (byId.TryGetValue(id, out ActionDef a))
                    {
                        arena.Children.Add(new Item { Action = a, NeedsOn = id == "arena_set_place" ? null : arenaActive });
                    }
                }
                root.Children.Add(arena);
            }

            // --- Parkour: interruptor + opciones.
            if (byId.TryGetValue("parkour_start", out ActionDef pStart) && byId.TryGetValue("parkour_stop", out ActionDef pStop))
            {
                Func<bool> parkourActive = () => parkourMode.IsActive;
                var parkour = new Item { Title = "Parkour", IsOn = parkourActive };
                parkour.Children.Add(new Item { Action = pStart, OffAction = pStop, IsOn = parkourActive });
                foreach (string id in new[] { "parkour_wind", "parkour_ragdoll", "parkour_remove_floor", "parkour_super_jump",
                                              "parkour_highest", "parkour_back_to_start", "parkour_set_place" })
                {
                    if (byId.TryGetValue(id, out ActionDef a))
                    {
                        parkour.Children.Add(new Item { Action = a, NeedsOn = id == "parkour_set_place" ? null : parkourActive });
                    }
                }
                root.Children.Add(parkour);
            }

            return root;
        }

        private static void AddSwitch(Item parent, Dictionary<string, ActionDef> byId, string id, Func<bool> isOn, Func<bool> needsOn)
        {
            if (byId.TryGetValue(id, out ActionDef a))
            {
                parent.Children.Add(new Item { Action = a, IsOn = isOn, NeedsOn = needsOn, IsSwitch = true });
            }
        }

        // ================================================================ teclado

        public void OnKeyDown(Keys key, bool shift)
        {
            if (key == Keys.F7)
            {
                _open = !_open;
                _paramIndex = -1;
                return;
            }

            if (!_open)
            {
                return;
            }

            Level level = _path.Peek();
            List<Item> items = level.Menu.Children;
            if (items.Count == 0)
            {
                return;
            }
            Item item = items[level.Index];
            ActionDef action = item.EditableAction;
            int paramCount = action?.Params.Length ?? 0;

            if (_paramIndex >= 0 && action == null)
            {
                _paramIndex = -1; // el modo se encendió desde StreamTok mientras se editaban sus ajustes
            }

            if (_paramIndex >= 0)
            {
                // --- Editando parámetros
                switch (key)
                {
                    case Keys.Up: _paramIndex = (_paramIndex - 1 + paramCount) % paramCount; break;
                    case Keys.Down: _paramIndex = (_paramIndex + 1) % paramCount; break;
                    case Keys.Right: ChangeValue(action, +1, shift); break;
                    case Keys.Left: ChangeValue(action, -1, shift); break;
                    case Keys.Back: _paramIndex = -1; break;
                    case Keys.Enter: Activate(item); break;
                }
                return;
            }

            // --- Navegando una lista
            switch (key)
            {
                case Keys.Up:
                    level.Index = (level.Index - 1 + items.Count) % items.Count;
                    break;

                case Keys.Down:
                    level.Index = (level.Index + 1) % items.Count;
                    break;

                case Keys.Right:
                    if (item.IsMenu) Enter(item);
                    else if (paramCount > 0) _paramIndex = 0;
                    break;

                case Keys.Enter:
                    if (item.IsMenu) Enter(item);
                    else Activate(item);
                    break;

                case Keys.Left:
                case Keys.Back:
                    if (_path.Count > 1) _path.Pop();
                    break;
            }
        }

        private void Enter(Item menu)
        {
            if (menu.Children.Count > 0)
            {
                _path.Push(new Level { Menu = menu });
            }
        }

        private void Activate(Item item)
        {
            ActionDef action = item.IsToggle && item.IsOn() ? item.OffAction : item.Action;
            var values = new Dictionary<string, object>(_values[action.Id]);
            if (item.Custom != null)
            {
                item.Custom(values);
                return;
            }
            if (item.IsSwitch)
            {
                values["enabled"] = !item.IsOn(); // ON -> OFF y viceversa
            }
            _run(action, values);
            if (item.IsToggle)
            {
                _paramIndex = -1;
            }
        }

        // ================================================================ dibujo

        /// <summary>Llamar cada frame.</summary>
        public void Draw()
        {
            if (!_open)
            {
                return;
            }

            // La flecha arriba abre el teléfono en GTA: bloquearlo mientras el menú está abierto.
            GTA.Game.DisableControlThisFrame(GTA.Control.Phone);

            Level level = _path.Peek();
            List<Item> items = level.Menu.Children;

            string crumbs = string.Join("  >  ", _path.Reverse().Skip(1).Select(l => l.Menu.Title));
            var lines = new List<KeyValuePair<string, Color>>
            {
                Line(crumbs.Length == 0 ? "StreamTok · Menú de pruebas" : $"StreamTok  >  {crumbs}", Selected),
            };

            if (_paramIndex < 0)
            {
                lines.Add(Line("Arriba/Abajo: elegir  ·  Enter: entrar/ejecutar  ·  Derecha: parámetros", Dim));
                lines.Add(Line(_path.Count > 1 ? "Izquierda/Retroceso: volver  ·  F7: cerrar" : "F7: cerrar", Dim));
            }
            else
            {
                lines.Add(Line("Arriba/Abajo: parámetro  ·  Izq/Der: valor (Shift: x10 o de 1 en 1)", Dim));
                lines.Add(Line("Enter: ejecutar  ·  Retroceso: volver", Dim));
            }
            lines.Add(Line("", Normal));

            int first = Math.Max(0, Math.Min(level.Index - VisibleItems / 2, items.Count - VisibleItems));
            int last = Math.Min(items.Count, first + VisibleItems);

            if (first > 0) lines.Add(Line("   ...", Dim));

            for (int i = first; i < last; i++)
            {
                Item it = items[i];
                bool sel = i == level.Index;
                bool locked = it.NeedsOn != null && !it.NeedsOn();
                Color color = sel && _paramIndex < 0 ? Selected : sel ? Normal : locked ? Dim : Normal;
                lines.Add(Line($"{(sel ? "> " : "   ")}{Label(it, locked)}", color));

                ActionDef editable = it.EditableAction;
                if (sel && _paramIndex >= 0 && editable != null)
                {
                    for (int p = 0; p < editable.Params.Length; p++)
                    {
                        ParamDef def = editable.Params[p];
                        object value = _values[editable.Id][def.Name];
                        string shown = def.Type == "bool" ? ((bool)value ? "sí" : "no") : value.ToString();
                        lines.Add(Line($"         {(p == _paramIndex ? "> " : "   ")}{def.Name}: {shown}", p == _paramIndex ? Selected : Normal));
                    }
                }
            }

            if (last < items.Count) lines.Add(Line("   ...", Dim));

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

        private static string Label(Item it, bool locked)
        {
            if (it.IsToggle)
            {
                return it.IsOn()
                    ? "Modo: ~g~ACTIVO~s~   (Enter = terminar)"
                    : "Modo: ~r~APAGADO~s~   (Enter = activar · Derecha = ajustes)";
            }
            if (it.IsSwitch)
            {
                return locked
                    ? $"{it.Action.Name}   (activa el modo)"
                    : $"{it.Action.Name}:   {(it.IsOn() ? "~g~ON~s~" : "~r~OFF~s~")}   (Enter = cambiar)";
            }
            if (it.IsMenu)
            {
                if (it.IsOn != null)
                {
                    return $"{it.Title}   {(it.IsOn() ? "~g~[ON]~s~" : "~r~[OFF]~s~")}   >";
                }
                bool ofActions = it.Children.Count > 0 && !it.Children[0].IsMenu;
                return ofActions ? $"{it.Title}   ({it.Children.Count})   >" : $"{it.Title}   >";
            }
            return locked ? $"{it.Action.Name}   (activa el modo)" : it.Action.Name;
        }

        private void ChangeValue(ActionDef action, int direction, bool shift)
        {
            ParamDef def = action.Params[_paramIndex];
            Dictionary<string, object> values = _values[action.Id];
            object current = values[def.Name];

            if (def.Type == "int" && def.Presets != null)
            {
                int cur = (int)current;
                int next;
                if (!shift)
                {
                    // Salta entre los valores sugeridos (5, 10, 15…).
                    next = direction > 0
                        ? def.Presets.Where(v => v > cur).DefaultIfEmpty(def.Presets.Last()).Min()
                        : def.Presets.Where(v => v < cur).DefaultIfEmpty(def.Presets.First()).Max();
                }
                else
                {
                    // Con Shift, de 1 en 1: valor personalizado.
                    next = cur + direction;
                    if (def.Min.HasValue) next = Math.Max(def.Min.Value, next);
                    if (def.Max.HasValue) next = Math.Min(def.Max.Value, next);
                }
                values[def.Name] = next;
                return;
            }

            switch (def.Type)
            {
                case "int":
                    int range = def.Max.HasValue && def.Min.HasValue ? def.Max.Value - def.Min.Value : 0;
                    // Rangos grandes (ej. dinero hasta 10 millones) avanzan en pasos más grandes.
                    int step = range > 10000 ? 1000 : range > 1000 ? 100 : 1;
                    if (shift) step *= 10;
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

        // ================================================================ tipos

        /// <summary>Un elemento del menú: una sección (con hijos), una acción o un interruptor de modo.</summary>
        private sealed class Item
        {
            public string Title;
            public List<Item> Children = new List<Item>();

            /// <summary>Acción a ejecutar. En un interruptor, la que lo ENCIENDE (y sus ajustes).</summary>
            public ActionDef Action;

            /// <summary>Solo interruptores: la acción que lo APAGA.</summary>
            public ActionDef OffAction;

            /// <summary>Estado del modo (interruptor o sección de modo).</summary>
            public Func<bool> IsOn;

            /// <summary>Opción que solo funciona con el modo encendido (se ve atenuada si no).</summary>
            public Func<bool> NeedsOn;

            /// <summary>Interruptor ON/OFF de una opción (acción con parámetro "enabled").</summary>
            public bool IsSwitch;

            /// <summary>Ítem propio del menú (no es una acción del catálogo), ej. la prueba de estrés.</summary>
            public Action<Dictionary<string, object>> Custom;

            public bool IsMenu => Action == null;
            public bool IsToggle => Action != null && OffAction != null;

            /// <summary>Acción cuyos parámetros se editan. Un interruptor encendido no tiene ajustes.</summary>
            public ActionDef EditableAction => IsMenu || IsSwitch || (IsToggle && IsOn()) ? null : Action;
        }

        private sealed class Level
        {
            public Item Menu;
            public int Index;
        }
    }
}
