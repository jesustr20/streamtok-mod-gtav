using System.Collections.Generic;
using GTA;
using GTA.Native;

namespace StreamTok.GtaV.Entities
{
    /// <summary>
    /// Las armas láser (Up-n-Atomizer, Unholy Hellbringer, Widowmaker) son exclusivas de los aliens.
    /// Cada segundo revisa los NPC cercanos y se las quita a cualquiera que no sea alien,
    /// venga de donde venga (otra acción, otro modo o un arma recogida).
    /// </summary>
    internal static class LaserGuard
    {
        public const string AlienModel = "s_m_m_movalien_01";

        private static readonly uint[] LaserHashes = { 0xAF3696A1u, 0x476BF155u, 0xB62D1F67u };

        private const int IntervalMs = 1000;
        private const float Radius = 150f;
        private static int _nextCheck;

        // Aliens generados a propósito (con su traje completo). Un NPC con el modelo de alien pero con ropa normal
        // (los "hombres de negro") NO cuenta: no puede llevar láser.
        private static readonly HashSet<int> Aliens = new HashSet<int>();

        /// <summary>Marca a un NPC como alien de verdad (modelo + traje completo).</summary>
        public static void MarkAlien(Ped ped)
        {
            Aliens.Add(ped.Handle);
        }

        /// <summary>¿Es un alien generado a propósito, con su traje?</summary>
        public static bool IsAlien(Ped ped)
        {
            return Aliens.Contains(ped.Handle);
        }

        /// <summary>Llamar cada frame: se ejecuta solo una vez por segundo.</summary>
        public static void Update()
        {
            int now = GTA.Game.GameTime;
            if (now < _nextCheck)
            {
                return;
            }
            _nextCheck = now + IntervalMs;

            Aliens.RemoveWhere(h => !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, h));

            Ped player = GTA.Game.Player.Character;
            foreach (Ped ped in World.GetNearbyPeds(player.Position, Radius))
            {
                if (ped == null || !ped.Exists() || ped.IsDead || ped.Handle == player.Handle || IsAlien(ped))
                {
                    continue;
                }

                foreach (uint hash in LaserHashes)
                {
                    if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, ped, hash, false))
                    {
                        Function.Call(Hash.REMOVE_WEAPON_FROM_PED, ped, hash);
                        PlayerTransform.Log($"Arma láser quitada a un NPC que no es alien (arma=0x{hash:X})");
                    }
                }
            }
        }
    }
}
