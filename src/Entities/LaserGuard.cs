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

        /// <summary>¿Es el modelo del alien con traje?</summary>
        public static bool IsAlien(Ped ped)
        {
            return Function.Call<int>(Hash.GET_ENTITY_MODEL, ped) == Function.Call<int>(Hash.GET_HASH_KEY, AlienModel);
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
