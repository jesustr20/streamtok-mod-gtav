using System;
using GTA;
using GTA.Math;
using StreamTok.GtaV.Actions;

namespace StreamTok.GtaV.Entities
{
    /// <summary>Ayudas para crear entidades de forma segura.</summary>
    internal static class Spawner
    {
        /// <summary>Carga un modelo por nombre (ej. "a_c_cow") y crea el ped mirando al jugador.</summary>
        public static Ped SpawnPed(string modelName, Vector3 position)
        {
            Model model = Load(modelName);
            try
            {
                Ped ped = World.CreatePed(model, position, HeadingToPlayer(position));
                if (ped == null)
                {
                    throw new ActionException($"El juego no pudo crear '{modelName}' (¿límite de entidades?)");
                }
                return ped;
            }
            finally
            {
                model.MarkAsNoLongerNeeded();
            }
        }

        public static Vehicle SpawnVehicle(string modelName, Vector3 position, float heading, bool placeOnGround)
        {
            Model model = Load(modelName);
            try
            {
                if (placeOnGround)
                {
                    float ground = World.GetGroundHeight(position + new Vector3(0f, 0f, 3f));
                    if (ground > 0f)
                    {
                        position.Z = ground;
                    }
                }

                Vehicle vehicle = World.CreateVehicle(model, position, heading);
                if (vehicle == null)
                {
                    throw new ActionException($"El juego no pudo crear '{modelName}' (¿límite de entidades?)");
                }
                if (placeOnGround)
                {
                    vehicle.PlaceOnGround();
                }
                return vehicle;
            }
            finally
            {
                model.MarkAsNoLongerNeeded();
            }
        }

        /// <summary>Rumbo (grados) para que algo en <paramref name="position"/> mire hacia el jugador.</summary>
        public static float HeadingToPlayer(Vector3 position)
        {
            Vector3 toPlayer = GTA.Game.Player.Character.Position - position;
            return (float)(Math.Atan2(-toPlayer.X, toPlayer.Y) * 180.0 / Math.PI);
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

        /// <summary>
        /// Carga el modelo en memoria SIN crear nada. Puede tardar algunos frames si el modelo
        /// no estaba cargado: hacerlo antes de tocar el estado del juego, para que el spawn
        /// posterior sea instantáneo (mismo frame).
        /// </summary>
        public static void Preload(string modelName) => Load(modelName);

        private static Model Load(string modelName)
        {
            var model = new Model(modelName);
            if (!model.IsInCdImage || !model.IsValid)
            {
                throw new ActionException($"El modelo '{modelName}' no existe en esta versión del juego");
            }
            if (!model.Request(2000))
            {
                throw new ActionException($"No se pudo cargar el modelo '{modelName}'");
            }
            return model;
        }
    }
}
