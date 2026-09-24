using System.Collections.Generic;
using System.Linq;
using GTA;

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

        public static string[] Keys<T>(Dictionary<string, T> map) => map.Keys.ToArray();
    }
}
