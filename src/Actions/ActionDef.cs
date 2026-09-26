using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StreamTok.GtaV.Characters;
using StreamTok.GtaV.Effects;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Error esperado de una acción (ej. "el jugador no tiene vehículo", "límite alcanzado").
    /// Se informa a StreamTok en el mod-ack y en el log se guarda solo el mensaje, sin traza.
    /// </summary>
    internal sealed class ActionException : Exception
    {
        public ActionException(string message) : base(message) { }
    }

    /// <summary>Definición de un parámetro de acción. Se publica en mod-hello.</summary>
    internal sealed class ParamDef
    {
        private ParamDef() { }

        public string Name { get; private set; }

        /// <summary>"int" | "enum" | "bool"</summary>
        public string Type { get; private set; }

        public object Default { get; private set; }
        public int? Min { get; private set; }
        public int? Max { get; private set; }
        public string[] Options { get; private set; }

        public static ParamDef Int(string name, int def, int min, int max) =>
            new ParamDef { Name = name, Type = "int", Default = def, Min = min, Max = max };

        public static ParamDef Enum(string name, string def, params string[] options) =>
            new ParamDef { Name = name, Type = "enum", Default = def, Options = options };

        public static ParamDef Bool(string name, bool def) =>
            new ParamDef { Name = name, Type = "bool", Default = def };

        /// <summary>
        /// Convierte el valor recibido al tipo del parámetro y lo recorta a sus límites.
        /// Cualquier valor ausente o inválido cae al default: una mala configuración
        /// en StreamTok nunca puede pedirle al juego 5000 atacantes.
        /// </summary>
        public object Normalize(object raw)
        {
            if (raw == null)
            {
                return Default;
            }

            switch (Type)
            {
                case "int":
                    double d;
                    try { d = Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
                    catch { return Default; }
                    int i = (int)Math.Round(d);
                    if (Min.HasValue) i = Math.Max(Min.Value, i);
                    if (Max.HasValue) i = Math.Min(Max.Value, i);
                    return i;

                case "enum":
                    string s = raw.ToString();
                    string match = Options.FirstOrDefault(o => string.Equals(o, s, StringComparison.OrdinalIgnoreCase));
                    return match ?? Default;

                case "bool":
                    if (raw is bool b) return b;
                    string t = raw.ToString().Trim().ToLowerInvariant();
                    if (t == "true" || t == "1" || t == "si" || t == "sí" || t == "yes") return true;
                    if (t == "false" || t == "0" || t == "no") return false;
                    return Default;

                default:
                    return Default;
            }
        }
    }

    /// <summary>Una acción del catálogo del mod.</summary>
    internal sealed class ActionDef
    {
        public ActionDef(string id, string name, bool supportsNameTag, ParamDef[] parameters, Action<ActionContext> execute)
        {
            Id = id;
            Name = name;
            SupportsNameTag = supportsNameTag;
            Params = parameters ?? new ParamDef[0];
            Execute = execute;
        }

        /// <summary>Id estable: es el que viaja en mod-command. No cambiarlo una vez publicado.</summary>
        public string Id { get; }

        /// <summary>Nombre legible para el panel de StreamTok y el menú de pruebas.</summary>
        public string Name { get; }

        /// <summary>true si crea entidades que pueden llevar el nombre del viewer encima.</summary>
        public bool SupportsNameTag { get; }

        public ParamDef[] Params { get; }
        public Action<ActionContext> Execute { get; }
    }

    /// <summary>Servicios compartidos por todas las acciones.</summary>
    internal sealed class ActionServices
    {
        public ActionServices(EntityTracker tracker, PlayerEffects effects, FrameScheduler scheduler, CharacterManager characters, Random rng)
        {
            Characters = characters;
            Tracker = tracker;
            Effects = effects;
            Scheduler = scheduler;
            Rng = rng;
        }

        public EntityTracker Tracker { get; }
        public PlayerEffects Effects { get; }
        public FrameScheduler Scheduler { get; }
        public CharacterManager Characters { get; }
        public Random Rng { get; }
    }

    /// <summary>Lo que recibe una acción al ejecutarse: parámetros ya validados y servicios.</summary>
    internal sealed class ActionContext
    {
        private readonly Dictionary<string, object> _values;
        private readonly ActionServices _services;

        private ActionContext(Dictionary<string, object> values, string nameTag, ActionServices services)
        {
            _values = values;
            NameTag = nameTag;
            _services = services;
        }

        /// <summary>Nombre a dibujar sobre las entidades creadas (ya limpio). Puede ser null.</summary>
        public string NameTag { get; }

        public EntityTracker Tracker => _services.Tracker;
        public PlayerEffects Effects => _services.Effects;
        public FrameScheduler Scheduler => _services.Scheduler;
        public CharacterManager Characters => _services.Characters;
        public Random Rng => _services.Rng;

        public int Int(string name) => (int)_values[name];
        public string Enum(string name) => (string)_values[name];
        public bool Bool(string name) => (bool)_values[name];

        public T Pick<T>(IList<T> items) => items[Rng.Next(items.Count)];

        public static ActionContext Create(ActionDef def, IDictionary<string, object> raw, string nameTag, ActionServices services)
        {
            var values = new Dictionary<string, object>();
            foreach (ParamDef p in def.Params)
            {
                object v = null;
                raw?.TryGetValue(p.Name, out v);
                values[p.Name] = p.Normalize(v);
            }
            return new ActionContext(values, nameTag, services);
        }
    }
}
