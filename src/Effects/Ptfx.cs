using System.Drawing;
using GTA;
using GTA.Math;
using GTA.Native;

namespace StreamTok.GtaV.Effects
{
    /// <summary>
    /// Partículas (humo, fuego, estelas), luces y "anclas" invisibles para efectos de espectáculo.
    /// Las partículas se piden por nombre: si un nombre no existe en esta versión del juego no
    /// se dibuja nada, pero NO se cae el juego. Por eso los efectos usan también luces de respaldo.
    /// </summary>
    internal static class Ptfx
    {
        public const string Core = "core";

        /// <summary>Modelo diminuto que sirve de ancla invisible para pegarle partículas.</summary>
        public const string AnchorModel = "prop_golf_ball";

        /// <summary>Pide cargar un paquete de partículas (no bloquea).</summary>
        public static void Request(string asset) => Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, asset);

        public static bool Ready(string asset) => Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, asset);

        /// <summary>Partícula continua pegada a una entidad. Devuelve 0 si no se pudo.</summary>
        public static int OnEntity(string asset, string effect, Entity entity, float scale)
        {
            if (entity == null || !Ready(asset))
            {
                return 0;
            }
            Function.Call(Hash.USE_PARTICLE_FX_ASSET, asset);
            return Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY, effect, entity,
                0f, 0f, 0f, 0f, 0f, 0f, scale, false, false, false);
        }

        /// <summary>Partícula de un solo disparo en un punto (humo, polvo, explosión visual).</summary>
        public static void Burst(string asset, string effect, Vector3 position, float scale)
        {
            if (!Ready(asset))
            {
                return;
            }
            Function.Call(Hash.USE_PARTICLE_FX_ASSET, asset);
            Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD, effect,
                position.X, position.Y, position.Z, 0f, 0f, 0f, scale, false, false, false);
        }

        public static void Stop(int handle)
        {
            if (handle != 0)
            {
                Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, handle, false);
            }
        }

        /// <summary>Luz puntual por este frame (hay que llamarla cada frame). Siempre funciona.</summary>
        public static void Light(Vector3 position, Color color, float range, float intensity)
        {
            Function.Call(Hash.DRAW_LIGHT_WITH_RANGE, position.X, position.Y, position.Z,
                (int)color.R, (int)color.G, (int)color.B, range, intensity);
        }

        /// <summary>Pide el modelo del ancla (no bloquea). Llamar al iniciar un efecto.</summary>
        public static void RequestAnchor() => Function.Call(Hash.REQUEST_MODEL, new Model(AnchorModel).Hash);

        /// <summary>
        /// Ancla invisible, sin colisión y sin física, que se mueve a mano con <see cref="MoveTo"/>.
        /// Devuelve null si el modelo aún no cargó (se reintenta en el próximo frame).
        /// </summary>
        public static Prop CreateAnchor(Vector3 position)
        {
            var model = new Model(AnchorModel);
            if (!model.IsLoaded)
            {
                return null;
            }

            Prop anchor = World.CreateProp(model, position, false, false);
            if (anchor == null)
            {
                return null;
            }

            anchor.IsVisible = false;
            anchor.IsCollisionEnabled = false;
            anchor.IsPositionFrozen = true;
            return anchor;
        }

        public static void MoveTo(Entity entity, Vector3 position)
        {
            Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, entity, position.X, position.Y, position.Z, false, false, false);
        }

        /// <summary>Orienta un ancla para que su estela quede detrás del movimiento.</summary>
        public static void FaceVelocity(Entity entity, Vector3 velocity)
        {
            float horizontal = new Vector2(velocity.X, velocity.Y).Length();
            float yaw = (float)(System.Math.Atan2(-velocity.X, velocity.Y) * 180.0 / System.Math.PI);
            float pitch = (float)(System.Math.Atan2(velocity.Z, horizontal) * 180.0 / System.Math.PI);
            Function.Call(Hash.SET_ENTITY_ROTATION, entity, pitch, 0f, yaw, 2, true);
        }

        public static void SafeDelete(Entity entity)
        {
            if (entity != null && entity.Exists())
            {
                entity.Delete();
            }
        }
    }

    /// <summary>Guarda clima (y opcionalmente la hora) para devolverlos al terminar un efecto.</summary>
    internal sealed class WorldMood
    {
        private Weather _weather;
        private int _hours;
        private int _minutes;
        private bool _clock;

        public static WorldMood Save(bool clock)
        {
            return new WorldMood
            {
                _weather = World.Weather,
                _clock = clock,
                _hours = Function.Call<int>(Hash.GET_CLOCK_HOURS),
                _minutes = Function.Call<int>(Hash.GET_CLOCK_MINUTES),
            };
        }

        public void Restore()
        {
            Function.Call(Hash.CLEAR_WEATHER_TYPE_PERSIST);
            World.Weather = _weather;
            if (_clock)
            {
                Function.Call(Hash.SET_CLOCK_TIME, _hours, _minutes, 0);
            }
        }
    }
}
