using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Native;
using StreamTok.GtaV.Actions;
using StreamTok.GtaV.Effects;

namespace StreamTok.GtaV.Entities
{
    /// <summary>
    /// Cambia el modelo del jugador (ej. a un perro) y lo devuelve a su personaje original
    /// con la MISMA ropa y armas (GTA las borra al cambiar de modelo).
    ///
    /// Todo se hace POR PASOS en frames sucesivos (FrameScheduler.Until), sin Script.Wait ni
    /// Player.ChangeModel de SHVDN: esa combinación congelaba el juego al cambiar de modelo.
    /// </summary>
    internal static class PlayerTransform
    {
        private const int ComponentCount = 12;
        private const int PropCount = 8;

        /// <summary>Frames máximos esperando que cargue un modelo (~4 s a 60 fps).</summary>
        private const int MaxLoadFrames = 240;

        private static bool _saved;
        private static int _originalModel;
        private static int _generation; // cancela un cambio pendiente si llega otro
        private static readonly int[,] Components = new int[ComponentCount, 3];
        private static readonly int[,] Props = new int[PropCount, 2];
        private static readonly List<KeyValuePair<WeaponHash, int>> Weapons = new List<KeyValuePair<WeaponHash, int>>();

        /// <summary>Log del mod (lo asigna StreamTokMod). Cada paso se escribe al instante,
        /// así que si el juego se cae, la última línea dice en qué paso fue.</summary>
        public static Action<string> Log = _ => { };

        public static bool IsTransformed => _saved;

        /// <summary>Para acciones que un animal no puede hacer (armas, paracaídas, conducir).</summary>
        public static void RequireHuman()
        {
            if (_saved)
            {
                throw new ActionException("No disponible mientras el jugador es un animal");
            }
        }

        public static void TransformTo(string modelName, FrameScheduler scheduler)
        {
            Log($"Transform: inicio -> {modelName}");
            Ped player = GTA.Game.Player.Character;
            if (!_saved)
            {
                Save(player);
                Log($"Transform: guardado modelo original, ropa y {Weapons.Count} armas");
            }

            int hash = new Model(modelName).Hash;
            Function.Call(Hash.REQUEST_MODEL, hash);

            // Un animal no puede ir en un vehículo ni tener armas: si tiene una en la mano
            // al cambiar de modelo, el juego se cae. Las armas ya están guardadas.
            Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, player);
            Function.Call(Hash.REMOVE_ALL_PED_WEAPONS, player, true);
            Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, player, 0.0f);

            int generation = ++_generation;
            int frames = 0;
            scheduler.Until(() =>
            {
                if (generation != _generation)
                {
                    return true; // llegó otro cambio: este se descarta
                }

                if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, hash))
                {
                    if (++frames > MaxLoadFrames)
                    {
                        Log($"Transform: el modelo {modelName} no cargó a tiempo");
                        return true;
                    }
                    return false;
                }

                Log("Transform: cambiando modelo");
                SetPlayerModel(hash);
                Log("Transform: listo");
                return true;
            });
        }

        /// <summary>Vuelve al personaje original, por pasos (modelo; y al frame siguiente ropa y armas).</summary>
        public static void Restore(FrameScheduler scheduler)
        {
            if (!_saved)
            {
                return;
            }

            Log("Transform: restaurando personaje original");
            int hash = _originalModel;
            Function.Call(Hash.REQUEST_MODEL, hash);

            int generation = ++_generation;
            int frames = 0;
            bool modelSet = false;
            scheduler.Until(() =>
            {
                if (generation != _generation)
                {
                    return true;
                }

                if (!modelSet)
                {
                    if (!Function.Call<bool>(Hash.HAS_MODEL_LOADED, hash) && ++frames <= MaxLoadFrames)
                    {
                        return false;
                    }
                    SetPlayerModel(hash);
                    modelSet = true;
                    return false; // ropa y armas en el frame siguiente, sobre el ped nuevo
                }

                ApplySavedLook();
                return true;
            });
        }

        /// <summary>
        /// Restauración inmediata, sin esperar frames: solo para cuando el script se detiene
        /// (Insert / cerrar el juego) y ya no habrá frames siguientes.
        /// </summary>
        public static void RestoreNow()
        {
            if (!_saved)
            {
                return;
            }
            ++_generation;
            Function.Call(Hash.REQUEST_MODEL, _originalModel);
            SetPlayerModel(_originalModel);
            ApplySavedLook();
        }

        private static void SetPlayerModel(int hash)
        {
            Function.Call(Hash.SET_PLAYER_MODEL, GTA.Game.Player.Handle, hash);
            Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, GTA.Game.Player.Character);
            Function.Call(Hash.SET_MODEL_AS_NO_LONGER_NEEDED, hash);
        }

        private static void ApplySavedLook()
        {
            Ped player = GTA.Game.Player.Character; // ped nuevo tras el cambio de modelo
            for (int i = 0; i < ComponentCount; i++)
            {
                Function.Call(Hash.SET_PED_COMPONENT_VARIATION, player, i, Components[i, 0], Components[i, 1], Components[i, 2]);
            }
            for (int i = 0; i < PropCount; i++)
            {
                if (Props[i, 0] < 0) Function.Call(Hash.CLEAR_PED_PROP, player, i);
                else Function.Call(Hash.SET_PED_PROP_INDEX, player, i, Props[i, 0], Props[i, 1], true);
            }
            foreach (KeyValuePair<WeaponHash, int> w in Weapons)
            {
                player.Weapons.Give(w.Key, w.Value, false, true);
            }

            _saved = false;
            Log("Transform: restaurado");
        }

        private static void Save(Ped player)
        {
            _originalModel = player.Model.Hash;

            for (int i = 0; i < ComponentCount; i++)
            {
                Components[i, 0] = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, player, i);
                Components[i, 1] = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, player, i);
                Components[i, 2] = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, player, i);
            }
            for (int i = 0; i < PropCount; i++)
            {
                Props[i, 0] = Function.Call<int>(Hash.GET_PED_PROP_INDEX, player, i);
                Props[i, 1] = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, player, i);
            }

            Weapons.Clear();
            foreach (WeaponHash hash in Enum.GetValues(typeof(WeaponHash)).Cast<WeaponHash>().Distinct())
            {
                if (hash != WeaponHash.Unarmed && player.Weapons.HasWeapon(hash))
                {
                    Weapons.Add(new KeyValuePair<WeaponHash, int>(hash, player.Weapons[hash].Ammo));
                }
            }

            _saved = true;
        }
    }
}
