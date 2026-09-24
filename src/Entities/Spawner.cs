using System;
using GTA;
using GTA.Math;

namespace StreamTok.GtaV.Entities
{
    /// <summary>Ayudas para crear entidades de forma segura.</summary>
    internal static class Spawner
    {
        /// <summary>Carga un modelo por nombre (ej. "a_c_cow") y crea el ped mirando al jugador.</summary>
        public static Ped SpawnPed(string modelName, Vector3 position)
        {
            var model = new Model(modelName);
            if (!model.IsInCdImage || !model.IsValid)
            {
                throw new InvalidOperationException($"El modelo '{modelName}' no existe en esta versión del juego");
            }

            if (!model.Request(2000))
            {
                throw new InvalidOperationException($"No se pudo cargar el modelo '{modelName}'");
            }

            try
            {
                Vector3 toPlayer = GTA.Game.Player.Character.Position - position;
                float heading = (float)(Math.Atan2(-toPlayer.X, toPlayer.Y) * 180.0 / Math.PI);

                Ped ped = World.CreatePed(model, position, heading);
                if (ped == null)
                {
                    throw new InvalidOperationException($"El juego no pudo crear '{modelName}' (¿límite de entidades?)");
                }
                return ped;
            }
            finally
            {
                model.MarkAsNoLongerNeeded();
            }
        }

        /// <summary>Punto al azar alrededor del jugador, entre minDist y maxDist metros, a nivel del suelo.</summary>
        public static Vector3 NearPlayer(Random rng, float minDist, float maxDist)
        {
            Vector3 origin = GTA.Game.Player.Character.Position;
            double angle = rng.NextDouble() * Math.PI * 2;
            float dist = minDist + (float)rng.NextDouble() * (maxDist - minDist);

            var pos = new Vector3(
                origin.X + (float)Math.Cos(angle) * dist,
                origin.Y + (float)Math.Sin(angle) * dist,
                origin.Z + 3f);

            // Busca el suelo desde un poco más arriba del jugador; si no lo encuentra
            // (zona no cargada), usa la altura del jugador.
            float ground = World.GetGroundHeight(pos);
            pos.Z = ground > 0f ? ground : origin.Z;
            return pos;
        }
    }
}
