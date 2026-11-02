using System;
using GTA;
using GTA.Math;
using GTA.Native;
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

                // Si aparece encima de un helicóptero en movimiento, arranca con su velocidad: así cae sobre las
                // hélices y no se queda atrás en el aire.
                Vehicle heli = PlayerHelicopter();
                if (heli != null && position.Z > heli.Position.Z + 1f)
                {
                    ped.Velocity = heli.Velocity;
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

        /// <summary>Crea un objeto fijo (ej. una rampa) apoyado en el suelo, con el rumbo indicado.</summary>
        public static Prop SpawnProp(string modelName, Vector3 position, float heading)
        {
            Model model = Load(modelName);
            try
            {
                Prop prop = World.CreateProp(model, position, new Vector3(0f, 0f, heading), false, true);
                if (prop == null)
                {
                    throw new ActionException($"El juego no pudo crear '{modelName}'");
                }
                prop.IsPositionFrozen = true; // que no se mueva al chocarla
                return prop;
            }
            finally
            {
                model.MarkAsNoLongerNeeded();
            }
        }

        /// <summary>Pieza suelta con física (rueda que sale rodando, etc.): a diferencia de SpawnProp, NO queda congelada.</summary>
        public static Prop SpawnLoosePart(string modelName, Vector3 position, Vector3 velocity)
        {
            Model model = Load(modelName);
            try
            {
                Prop prop = World.CreateProp(model, position, Vector3.Zero, true, false);
                if (prop == null)
                {
                    throw new ActionException($"El juego no pudo crear '{modelName}'");
                }
                prop.IsPositionFrozen = false;
                prop.Velocity = velocity;
                return prop;
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

        /// <summary>Radio (m) dentro del que caen los NPC sobre el rotor principal de un helicóptero.</summary>
        private const float RotorRadius = 2.5f;

        /// <summary>El helicóptero en el que va el jugador (conductor o pasajero), o null si no va en uno.</summary>
        public static Vehicle PlayerHelicopter()
        {
            Ped player = GTA.Game.Player.Character;
            if (!player.IsInVehicle())
            {
                return null;
            }
            Vehicle v = player.CurrentVehicle;
            if (v == null || !v.Exists())
            {
                return null;
            }
            return Function.Call<int>(Hash.GET_VEHICLE_CLASS, v) == 15 ? v : null; // 15 = helicópteros
        }

        /// <summary>Altura (m) de la parte más alta del vehículo (con el rotor) sobre su centro. Si no se puede medir, 3 m.</summary>
        private static float TopOf(Vehicle v)
        {
            Hash dimHash;
            if (Enum.TryParse("GET_MODEL_DIMENSIONS", out dimHash) && Enum.IsDefined(typeof(Hash), dimHash))
            {
                var outMin = new OutputArgument();
                var outMax = new OutputArgument();
                Function.Call(dimHash, v.Model.Hash, outMin, outMax);
                float top = outMax.GetResult<Vector3>().Z;
                if (top > 0.5f && top < 15f)
                {
                    return top;
                }
            }
            return 3f;
        }

        /// <summary>Altura sobre el suelo (m) desde la que se considera que el jugador está "en el aire".</summary>
        private const float AirborneHeight = 4f;

        /// <summary>
        /// ¿El jugador está en el aire (cayendo, en paracaídas, en un avión o helicóptero, saltando de un edificio)?
        /// Un salto normal o estar en un techo NO cuenta: se mide contra lo que tiene justo debajo.
        /// </summary>
        public static bool IsPlayerAirborne()
        {
            Ped player = GTA.Game.Player.Character;
            return Function.Call<float>(Hash.GET_ENTITY_HEIGHT_ABOVE_GROUND, player) > AirborneHeight;
        }

        /// <summary>
        /// Punto al azar alrededor del jugador, entre minDist y maxDist metros. A nivel del suelo si el jugador
        /// está en tierra; a SU altura si está en el aire (así los espectadores ven salir a los NPC donde él está).
        /// Si va en un helicóptero (<paramref name="onRotors"/>), aparecen encima de las hélices.
        /// </summary>
        public static Vector3 NearPlayer(Random rng, float minDist, float maxDist, bool onRotors = true)
        {
            // En un helicóptero: encima de las hélices (sobre el rotor principal), apenas arriba para que caigan sobre ellas.
            Vehicle heli = onRotors ? PlayerHelicopter() : null;
            if (heli != null)
            {
                double a = rng.NextDouble() * 2.0 * Math.PI;
                float r = (float)Math.Sqrt(rng.NextDouble()) * RotorRadius;
                Vector3 hp = heli.Position;
                return new Vector3(
                    hp.X + (float)Math.Cos(a) * r,
                    hp.Y + (float)Math.Sin(a) * r,
                    hp.Z + TopOf(heli) + 0.6f + (float)rng.NextDouble() * 0.8f);
            }

            Vector3 origin = GTA.Game.Player.Character.Position;
            // Aparecen DENTRO del campo de visión de la cámara (±70° de hacia donde mira): así quien dona los ve salir.
            Vector3 camDir = GTA.GameplayCamera.Direction;
            double angle = Math.Atan2(camDir.Y, camDir.X) + (rng.NextDouble() - 0.5) * (140.0 * Math.PI / 180.0);
            float dist = minDist + (float)rng.NextDouble() * (maxDist - minDist);

            var pos = new Vector3(
                origin.X + (float)Math.Cos(angle) * dist,
                origin.Y + (float)Math.Sin(angle) * dist,
                origin.Z + 3f);

            if (IsPlayerAirborne())
            {
                // En el aire: a la altura del jugador (con un poco de variación para que no salgan todos en fila).
                pos.Z = origin.Z + (float)(rng.NextDouble() * 2.0 - 0.5);
                return pos;
            }

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
