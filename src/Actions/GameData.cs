using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Modelos y valores del juego usados por las acciones. Las claves (dog, rifle, rain…)
    /// son las opciones que ve StreamTok; los valores son los nombres internos de GTA.
    /// Referencia completa: github.com/DurtyFree/gta-v-data-dumps
    /// </summary>
    internal static class GameData
    {
        public static readonly Dictionary<string, string> Animals = new Dictionary<string, string>
        {
            ["dog"] = "a_c_shepherd",
            ["rottweiler"] = "a_c_rottweiler",
            ["husky"] = "a_c_husky",
            ["cow"] = "a_c_cow",
            ["pig"] = "a_c_pig",
            ["boar"] = "a_c_boar",
            ["deer"] = "a_c_deer",
            ["coyote"] = "a_c_coyote",
            ["cougar"] = "a_c_mtlion",
            ["chimp"] = "a_c_chimp",
            ["rabbit"] = "a_c_rabbit_01",
        };

        public static readonly string[] NormalAttackers =
        {
            "g_m_y_lost_01", "g_m_y_lost_02", "g_m_y_mexgoon_01", "g_m_y_ballasout_01",
        };

        public static readonly string[] RandomAttackers =
        {
            "g_m_y_lost_01", "g_m_y_mexgoon_01", "g_m_y_ballasout_01", "s_m_y_clown_01",
            "u_m_y_zombie_01", "s_m_m_movalien_01", "a_m_m_beach_01",
        };

        public static readonly Dictionary<string, WeaponHash> Weapons = new Dictionary<string, WeaponHash>
        {
            ["pistol"] = WeaponHash.Pistol,
            ["smg"] = WeaponHash.SMG,
            ["rifle"] = WeaponHash.CarbineRifle,
            ["mg"] = WeaponHash.CombatMG,
            ["sniper"] = WeaponHash.SniperRifle,
            ["rpg"] = WeaponHash.RPG,
            ["bat"] = WeaponHash.Bat,
            ["knife"] = WeaponHash.Knife,
        };

        public static readonly Dictionary<string, string> Weather = new Dictionary<string, string>
        {
            ["extrasunny"] = "EXTRASUNNY",
            ["clear"] = "CLEAR",
            ["clouds"] = "CLOUDS",
            ["overcast"] = "OVERCAST",
            ["rain"] = "RAIN",
            ["thunder"] = "THUNDER",
            ["foggy"] = "FOGGY",
            ["snow"] = "SNOW",
            ["blizzard"] = "BLIZZARD",
            ["xmas"] = "XMAS",
        };

        /// <summary>
        /// Modelos por tipo de vehículo. NO es una lista escrita a mano: se arma una sola vez
        /// recorriendo TODO el enum GTA.VehicleHash (todos los vehículos que el juego/SHVDN
        /// conoce, incluyendo los de los DLC) y clasificando cada uno con Model.IsCar / IsBike /
        /// IsBoat / IsPlane / IsHelicopter — son datos del propio juego, no inventados aquí.
        /// Así crece sola con cada actualización de GTA V o de SHVDN, sin tocar este archivo.
        /// </summary>
        public static Dictionary<string, string[]> Vehicles
        {
            get { return _vehicles ?? (_vehicles = BuildVehicleCatalog()); }
        }
        private static Dictionary<string, string[]> _vehicles;

        public static readonly Dictionary<string, float> VehicleTagHeight = new Dictionary<string, float>
        {
            ["car"] = 1.6f,
            ["bike"] = 1.6f,
            ["boat"] = 2.2f,
            ["plane"] = 3.5f,
            ["helicopter"] = 3.8f,
        };

        /// <summary>
        /// Recorre GTA.VehicleHash una sola vez (se llama la primera vez que algo pide
        /// GameData.Vehicles, ya con el juego corriendo) y agrupa cada modelo válido según lo
        /// que el propio juego dice que es. Se guarda en texto en minúscula porque
        /// Spawner.SpawnVehicle recibe el nombre, no el hash.
        /// </summary>
        private static Dictionary<string, string[]> BuildVehicleCatalog()
        {
            var byType = new Dictionary<string, List<string>>
            {
                ["car"] = new List<string>(),
                ["bike"] = new List<string>(),
                ["boat"] = new List<string>(),
                ["plane"] = new List<string>(),
                ["helicopter"] = new List<string>(),
            };

            foreach (VehicleHash hash in System.Enum.GetValues(typeof(VehicleHash)))
            {
                Model model = hash;
                if (!model.IsValid)
                {
                    continue; // está en el enum de SHVDN pero esta versión del juego no lo tiene
                }

                string type;
                if (model.IsBike || model.IsBicycle) type = "bike"; // motos y bicis: dos ruedas
                else if (model.IsBoat || model.IsJetSki) type = "boat";
                else if (model.IsHelicopter) type = "helicopter";
                else if (model.IsPlane) type = "plane";
                else if (model.IsCar || model.IsQuadBike || model.IsAmphibiousCar || model.IsAmphibiousQuadBike) type = "car";
                else continue; // tren, remolque, submarino, blimp: no sirven para "generar vehículo"

                byType[type].Add(hash.ToString().ToLowerInvariant());
            }

            var result = new Dictionary<string, string[]>();
            foreach (KeyValuePair<string, List<string>> entry in byType)
            {
                // Si por algo quedara vacío (versión rara de SHVDN), al menos un modelo seguro.
                result[entry.Key] = entry.Value.Count > 0 ? entry.Value.ToArray() : new[] { FallbackVehicle(entry.Key) };
            }
            return result;
        }

        private static string FallbackVehicle(string type)
        {
            switch (type)
            {
                case "bike": return "bati";
                case "boat": return "dinghy";
                case "plane": return "duster";
                case "helicopter": return "maverick";
                default: return "blista";
            }
        }

        /// <summary>Animales en los que se puede convertir el jugador.</summary>
        public static readonly Dictionary<string, string> TransformAnimals = new Dictionary<string, string>
        {
            ["dog"] = "a_c_shepherd",
            ["pug"] = "a_c_pug",
            ["cat"] = "a_c_cat_01",
            ["pigeon"] = "a_c_pigeon",
            ["chicken"] = "a_c_hen",
            ["pig"] = "a_c_pig",
            ["cow"] = "a_c_cow",
            ["chimp"] = "a_c_chimp",
            ["cougar"] = "a_c_mtlion",
            ["boar"] = "a_c_boar",
        };

        /// <summary>Personajes "divertidos" para el NPC furioso.</summary>
        public static readonly string[] CrazyNpcs =
        {
            "s_m_y_clown_01", "s_m_m_movspace_01", "u_m_y_zombie_01", "u_m_y_mani", "s_m_m_strperf_01",
        };

        public static readonly string[] CompanionHumans =
        {
            "s_m_y_marine_01", "s_m_m_security_01", "g_m_y_famca_01", "s_m_y_swat_01",
        };

        public static readonly string[] BanditBikes = { "daemon", "hexer", "zombiea" };

        /// <summary>
        /// Lugares para teletransportar (coordenadas aproximadas, a nivel del suelo o techo).
        /// El orden importa: es el que recorren "siguiente" y "anterior".
        /// </summary>
        public static readonly Dictionary<string, Vector3> Locations = new Dictionary<string, Vector3>
        {
            ["maze_bank"] = new Vector3(-75.2f, -818.9f, 326.2f),      // techo del Maze Bank
            ["vinewood_sign"] = new Vector3(711.4f, 1198.1f, 348.5f),  // letrero de Vinewood
            ["chiliad"] = new Vector3(501.8f, 5604.4f, 797.9f),        // cima del Monte Chiliad
            ["paleto_bay"] = new Vector3(-379.5f, 6118.3f, 31.5f),
            ["sandy_shores"] = new Vector3(1747.0f, 3273.7f, 41.1f),   // aeródromo
            ["fort_zancudo"] = new Vector3(-2047.4f, 3132.1f, 32.8f),  // base militar: ¡disparan!
            ["del_perro_pier"] = new Vector3(-1850.1f, -1231.8f, 13.0f),
            ["airport"] = new Vector3(-1336.6f, -3044.0f, 13.9f),      // aeropuerto de Los Santos
            ["grove_street"] = new Vector3(105.8f, -1941.7f, 20.8f),
        };

        public static string[] Keys<T>(Dictionary<string, T> map) => map.Keys.ToArray();
    }
}
