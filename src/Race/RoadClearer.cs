using System;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Race
{
    /// <summary>
    /// Deja la pista libre: sin tráfico ni peatones del juego. Cada frame pone la densidad a 0 (no nacen
    /// más) y cada medio segundo borra los que ya estaban cerca de la pista. Nunca toca lo que el mod
    /// creó (pilotos, bots, etc.), el auto del jugador ni los autos que se le pasen en <c>keep</c>.
    /// </summary>
    internal sealed class RoadClearer
    {
        private const float VehicleRange = 40f;  // m a cada lado de la pista
        private const float PedRange = 150f;
        private readonly EntityTracker _tracker;
        private int _nextAt;

        public RoadClearer(EntityTracker tracker)
        {
            _tracker = tracker;
        }

        /// <summary>Llamar cada frame mientras haya carrera/importación. <paramref name="track"/> null = usar un radio alrededor de <paramref name="center"/>.</summary>
        public void Update(RaceTrack track, Vector3 center, params Entity[] keep)
        {
            Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
            Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f, 0f);

            int now = Game.GameTime;
            if (now < _nextAt) return;
            _nextAt = now + 1000;

            Ped player = Game.Player.Character;
            Vector3 here = player.Position;
            // Barrido extra de peatones del juego alrededor de quien mira (no toca lo que creó el mod).
            Function.Call(Hash.CLEAR_AREA_OF_PEDS, here.X, here.Y, here.Z, 250f, true);
            Vehicle playerCar = player.CurrentVehicle;

            foreach (Ped p in World.GetAllPeds())
            {
                if (p == null || !p.Exists() || p == player || _tracker.IsTracked(p) || IsKept(p, keep)) continue;
                if (p.IsInVehicle() && IsKept(p.CurrentVehicle, keep)) continue;
                if (Near(p.Position, track, center, PedRange)) EntityTracker.SafeDelete(p);
            }

            foreach (Vehicle v in World.GetAllVehicles())
            {
                if (v == null || !v.Exists() || v == playerCar || _tracker.IsTracked(v) || IsKept(v, keep)) continue;
                if (Near(v.Position, track, center, VehicleRange)) EntityTracker.SafeDelete(v);
            }
        }

        private static bool IsKept(Entity e, Entity[] keep)
        {
            if (e == null) return false;
            foreach (Entity k in keep)
            {
                if (k != null && k.Exists() && k == e) return true;
            }
            return false;
        }

        private static bool Near(Vector3 pos, RaceTrack track, Vector3 center, float range)
        {
            if (track != null && track.IsReady) return track.DistanceToTrack(pos, range) < range;
            return pos.DistanceTo(center) < 350f;
        }
    }
}
