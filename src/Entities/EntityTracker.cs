using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Math;

namespace StreamTok.GtaV.Entities
{
    /// <summary>
    /// Lleva registro de todo lo que spawnea el mod:
    ///  - dibuja el nombre del viewer sobre cada entidad (cada frame),
    ///  - limpia las que murieron o desaparecieron,
    ///  - aplica un límite global de spawns para no tumbar el juego.
    /// </summary>
    internal sealed class EntityTracker
    {
        public const string KindAttacker = "attacker";
        public const string KindAnimal = "animal";

        /// <summary>A más distancia que esto no se dibuja el nombre (evita llenar la pantalla).</summary>
        private const float TagDistance = 50f;

        private readonly List<Tracked> _items = new List<Tracked>();
        private readonly int _maxPeds;
        private readonly GTA.UI.TextElement _label;
        private RelationshipGroup _hostile;
        private bool _hostileCreated;

        public EntityTracker(int maxPeds)
        {
            _maxPeds = Math.Max(1, maxPeds);
            _label = new GTA.UI.TextElement(
                "", PointF.Empty, 0.35f, Color.White,
                GTA.UI.Font.ChaletLondon, GTA.UI.Alignment.Center, true, true);
        }

        public int Count => _items.Count;

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

        /// <summary>Cuántos se pueden crear de los pedidos sin pasar el límite global.</summary>
        public int ClampToLimit(int requested)
        {
            int available = _maxPeds - _items.Count;
            if (available <= 0)
            {
                throw new InvalidOperationException($"Límite de spawns alcanzado ({_maxPeds})");
            }
            return Math.Min(requested, available);
        }

        public void Track(Ped ped, string nameTag, string kind)
        {
            _items.Add(new Tracked { Ped = ped, Tag = nameTag, Kind = kind });
        }

        /// <summary>Borra del mundo todas las entidades de un tipo. Devuelve cuántas.</summary>
        public int RemoveKind(string kind)
        {
            int removed = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].Kind == kind)
                {
                    Delete(_items[i].Ped);
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
                Delete(t.Ped);
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
                Ped ped = t.Ped;

                if (ped == null || !ped.Exists())
                {
                    _items.RemoveAt(i);
                    continue;
                }

                if (ped.IsDead)
                {
                    // Muerto: se quita el nombre y el blip, y el juego limpia el cuerpo cuando quiera.
                    ped.AttachedBlip?.Delete();
                    ped.MarkAsNoLongerNeeded();
                    _items.RemoveAt(i);
                    continue;
                }

                if (t.Tag == null || ped.Position.DistanceTo(origin) > TagDistance)
                {
                    continue;
                }

                Vector3 head = ped.Bones[Bone.SkelHead].Position + new Vector3(0f, 0f, 0.35f);
                PointF screen = GTA.UI.Screen.WorldToScreen(head);
                if (screen.IsEmpty)
                {
                    continue; // fuera de pantalla
                }

                _label.Caption = t.Tag;
                _label.Position = screen;
                _label.Draw();
            }
        }

        private static void Delete(Ped ped)
        {
            try
            {
                if (ped != null && ped.Exists())
                {
                    ped.AttachedBlip?.Delete();
                    ped.Delete();
                }
            }
            catch
            {
                // Al cerrar el juego la entidad puede ya no existir; no es un error.
            }
        }

        private sealed class Tracked
        {
            public Ped Ped;
            public string Tag;
            public string Kind;
        }
    }
}
