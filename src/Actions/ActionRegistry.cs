using System;
using System.Collections.Generic;
using System.Linq;
using StreamTok.GtaV.Characters;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Catálogo de acciones del mod. Ver docs/CATALOGO.md.
    /// Para agregar una acción: definirla en el archivo de su categoría con un id estable;
    /// aparece sola en mod-hello, en el menú de pruebas (F7) y en StreamTok.
    /// </summary>
    internal sealed class ActionRegistry
    {
        private readonly Dictionary<string, ActionDef> _byId;

        private ActionRegistry(IEnumerable<ActionDef> actions)
        {
            All = actions.ToList();
            _byId = All.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<ActionDef> All { get; }

        public ActionDef Find(string id) =>
            id != null && _byId.TryGetValue(id, out ActionDef a) ? a : null;

        public static ActionRegistry CreateDefault(IReadOnlyList<CharacterDef> characters) => new ActionRegistry(
            NpcActions.All()
                .Concat(VehicleActions.All())
                .Concat(PlayerActions.All())
                .Concat(WeaponActions.All())
                .Concat(WorldActions.All())
                .Concat(SpectacleActions.All())
                .Concat(CharacterActions.All(characters)));
    }
}
