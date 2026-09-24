using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Actions;

namespace StreamTok.GtaV.Entities
{
    /// <summary>
    /// Lleva registro de todo lo que spawnea el mod:
    ///  - dibuja el nombre del viewer sobre cada entidad (cada frame),
    ///  - limpia las que murieron, se destruyeron o desaparecieron,
    ///  - aplica límites globales (peds y vehículos por separado) para no tumbar el juego.
    /// </summary>
    internal sealed class EntityTracker
    {
        public const string KindAttacker = "attacker";
        public const string KindAnimal = "animal";
        public const string KindVehicle = "vehicle";
        public const string KindCompanion = "companion";
        public const string KindRamp = "ramp";

        /// <summary>Máximo de objetos (rampas) spawneados a la vez.</summary>
        private const int MaxProps = 20;

        /// <summary>A más distancia que esto no se dibuja el nombre (evita llenar la pantalla).</summary>
        private const float TagDistance = 50f;

        private readonly List<Tracked> _items = new List<Tracked>();
        private readonly int _maxPeds;
        private readonly int _maxVehicles;
        private readonly GTA.UI.TextElement _label;
        private RelationshipGroup _hostile;
        private bool _hostileCreated;

        public EntityTracker(int maxPeds, int maxVehicles)
        {
            _maxPeds = Math.Max(1, maxPeds);
            _maxVehicles = Math.Max(1, maxVehicles);
            _label = new GTA.UI.TextElement(
                "", PointF.Empty, 0.35f, Color.White,
                GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        }

        /// <summary>Grupo de relación hostil al jugador (atacantes y animales furiosos).</summary>
        public RelationshipGroup HostileGroup
        {
            get
            {
                if (!_hostileCreated)
                {
                    _hostile = World.AddRelationshipGroup("STREAMTOK_HOSTILE");
                    _hostile.SetRelationshipBetweenGroups(GTA.Game.Player.Character.RelationshipGroup, Relationship.Hate, true);
                    _hostileCreated = true;
                }
                return _hostile;
            }
        }

        /// <summary>Cuántos peds se pueden crear de los pedidos sin pasar el límite.</summary>
        public int ClampPeds(int requested) => Clamp(requested, vehicles: false);

        /// <summary>Cuántos vehículos se pueden crear de los pedidos sin pasar el límite.</summary>
        public int ClampVehicles(int requested) => Clamp(requested, vehicles: true);

        /// <summary>Cuántos objetos (rampas…) se pueden crear sin pasar el límite.</summary>
        public int ClampProps(int requested)
        {
            int available = MaxProps - _items.Count(t => t.Entity is Prop);
            if (available <= 0)
            {
                throw new ActionException($"Límite de objetos spawneados alcanzado ({MaxProps})");
            }
            return Math.Min(requested, available);
        }

        /// <summary>Peds vivos de un tipo (ej. atacantes), para acciones que los modifican.</summary>
        public List<Ped> AlivePeds(string kind) =>
            _items.Where(t => t.Kind == kind && t.Entity is Ped && t.Entity.Exists() && !t.Entity.IsDead)
                  .Select(t => (Ped)t.Entity)
                  .ToList();

        /// <summary>Reemplaza una entidad por otra CONSERVANDO su nombre y tipo, y borra la vieja.</summary>
        public void Replace(Entity old, Entity replacement)
        {
            Tracked t = _items.Find(i => i.Entity == old);
            if (t == null)
            {
                return;
            }
            t.Entity = replacement;
            SafeDelete(old);
        }

        private int Clamp(int requested, bool vehicles)
        {
            int max = vehicles ? _maxVehicles : _maxPeds;
            int current = _items.Count(t => vehicles ? t.Entity is Vehicle : t.Entity is Ped);
            int available = max - current;

            if (available <= 0)
            {
                throw new ActionException(vehicles
                    ? $"Límite de vehículos spawneados alcanzado ({max})"
                    : $"Límite de spawns alcanzado ({max})");
            }
            return Math.Min(requested, available);
        }

        /// <summary>Registra una entidad. tagHeight = metros sobre su posición (solo no-peds).</summary>
        public void Track(Entity entity, string nameTag, string kind, float tagHeight = 0f)
        {
            _items.Add(new Tracked { Entity = entity, Tag = nameTag, Kind = kind, TagHeight = tagHeight });
        }

        public void Untrack(Entity entity)
        {
            _items.RemoveAll(t => t.Entity == entity);
        }

        /// <summary>Borra del mundo todas las entidades de un tipo. Devuelve cuántas.</summary>
        public int RemoveKind(string kind)
        {
            int removed = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].Kind == kind)
                {
                    SafeDelete(_items[i].Entity);
                    _items.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>Al recargar el script (Insert) o cerrarlo: no dejar entidades huérfanas.</summary>
        public void RemoveAll()
        {
            foreach (Tracked t in _items)
            {
                SafeDelete(t.Entity);
            }
            _items.Clear();
        }

        /// <summary>Llamar cada frame: limpia y dibuja los nombres.</summary>
        public void Update()
        {
            Vector3 origin = GTA.Game.Player.Character.Position;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                Tracked t = _items[i];
                Entity e = t.Entity;

                if (e == null || !e.Exists())
                {
                    _items.RemoveAt(i);
                    continue;
                }

                if (e.IsDead)
                {
                    // Muerto o destruido: se quita el nombre y el blip; el juego lo limpia cuando quiera.
                    e.AttachedBlip?.Delete();
                    e.MarkAsNoLongerNeeded();
                    _items.RemoveAt(i);
                    continue;
                }

                if (t.Tag == null || e.Position.DistanceTo(origin) > TagDistance)
                {
                    continue;
                }

                Vector3 anchor = e is Ped ped
                    ? ped.Bones[Bone.SkelHead].Position + new Vector3(0f, 0f, 0.35f)
                    : e.Position + new Vector3(0f, 0f, t.TagHeight);

                PointF screen = GTA.UI.Screen.WorldToScreen(anchor);
                if (screen.IsEmpty)
                {
                    continue; // fuera de pantalla
                }

                _label.Caption = t.Tag;
                _label.Position = screen;
                _label.Draw();
            }
        }

        /// <summary>Borra una entidad sin fallar; si es el vehículo del jugador, primero lo saca.</summary>
        public static void SafeDelete(Entity entity)
        {
            try
            {
                if (entity == null || !entity.Exists())
                {
                    return;
                }

                Ped player = GTA.Game.Player.Character;
                if (entity is Vehicle vehicle && player.IsInVehicle(vehicle))
                {
                    Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, player);
                }

                entity.AttachedBlip?.Delete();
                entity.Delete();
            }
            catch
            {
                // Al cerrar el juego la entidad puede ya no existir; no es un error.
            }
        }

        private sealed class Tracked
        {
            public Entity Entity;
            public string Tag;
            public string Kind;
            public float TagHeight;
        }
    }
}
