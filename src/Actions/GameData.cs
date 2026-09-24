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

        /// <summary>Modelos por tipo de vehículo, y la altura del nombre sobre cada tipo.</summary>
        public static readonly Dictionary<string, string[]> Vehicles = new Dictionary<string, string[]>
        {
            ["car"] = new[] { "adder", "zentorno", "sultan", "elegy2", "banshee", "dominator", "kuruma", "blista" },
            ["bike"] = new[] { "bati", "akuma", "sanchez", "faggio", "hakuchou" },
            ["boat"] = new[] { "jetmax", "seashark", "speeder", "dinghy" },
            ["plane"] = new[] { "cuban800", "duster", "stunt", "velum", "luxor" },
        };

        public static readonly Dictionary<string, float> VehicleTagHeight = new Dictionary<string, float>
        {
            ["car"] = 1.6f,
            ["bike"] = 1.6f,
            ["boat"] = 2.2f,
            ["plane"] = 3.5f,
        };

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
