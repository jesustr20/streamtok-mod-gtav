using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using GTA;

namespace StreamTok.GtaV.Characters
{
    /// <summary>Un personaje custom: modelo (cómo se ve) + habilidades (cómo pelea).</summary>
    internal sealed class CharacterDef
    {
        public string Id;
        public string Name;
        public string Model;
        public int Health;
        public string Weapon;
        public HashSet<string> Abilities = new HashSet<string>();
        public Color AuraColor = Color.Gold;

        /// <summary>Color del ki / Kamehameha. Si no se indica, usa el del aura.</summary>
        public Color? EnergyColor;

        public Color Energy => EnergyColor ?? AuraColor;

        /// <summary>Imagen para el panel de StreamTok (ruta o URL). Opcional.</summary>
        public string Image;

        /// <summary>Potencia de los poderes (explosiones de ki, empujones). 1 = normal.</summary>
        public float PowerScale = 1f;

        public bool Has(string ability) => Abilities.Contains(ability);
    }

    /// <summary>
    /// Lee los personajes de scripts\StreamTok.Characters.json. Si el archivo no existe, lo crea
    /// con ejemplos (modelos normales de GTA) para que el streamer tenga un punto de partida.
    /// Cada personaje se valida: si su modelo no está instalado, no aparece y queda en el log.
    /// </summary>
    internal static class CharacterCatalog
    {
        public const string FileName = "StreamTok.Characters.json";

        /// <summary>Habilidades disponibles en esta versión.</summary>
        public static readonly string[] Supported =
        {
            "super_strength", "tank", "gunslinger", "aura",   // v0.7
            "energy_blast", "flight", "dodge", "speed",        // v0.8
        };

        /// <summary>Habilidades planeadas: se aceptan en el JSON pero aún no hacen nada.</summary>
        public static readonly string[] Planned = { };

        private static readonly Dictionary<string, Color> AuraColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["gold"] = Color.FromArgb(255, 255, 200, 40),
            ["blue"] = Color.FromArgb(255, 60, 140, 255),
            ["red"] = Color.FromArgb(255, 255, 40, 40),
            ["green"] = Color.FromArgb(255, 60, 255, 90),
            ["purple"] = Color.FromArgb(255, 170, 60, 255),
            ["white"] = Color.FromArgb(255, 240, 240, 255),
        };

        private const string DefaultJson = @"{
  ""_ayuda"": ""Personajes custom de StreamTok. model = nombre interno del modelo (de GTA o de un add-on ped instalado). abilities: super_strength, tank, gunslinger, aura, energy_blast (ki y Kamehameha), flight (volar), dodge (esquivar), speed (súper velocidad). auraColor / energyColor: gold, blue, red, green, purple, white o #RRGGBB. image: foto para el panel de StreamTok (ruta o URL, opcional). weapon: none, pistol, smg, rifle, mg, rpg, bat, knife. Tras editar, pulsa Insert en el juego."",
  ""characters"": [
    { ""id"": ""bruto"",    ""name"": ""Bruto"",          ""model"": ""u_m_y_babyd"",         ""health"": 2500, ""weapon"": ""none"",   ""abilities"": [""super_strength""] },
    { ""id"": ""jefe"",     ""name"": ""Jefe blindado"",  ""model"": ""u_m_y_juggernaut_01"", ""health"": 5000, ""weapon"": ""mg"",     ""abilities"": [""tank""] },
    { ""id"": ""sicario"",  ""name"": ""El Sicario"",     ""model"": ""s_m_m_highsec_01"",    ""health"": 800,  ""weapon"": ""pistol"", ""abilities"": [""gunslinger""] },
    { ""id"": ""guerrero"", ""name"": ""Guerrero de aura"", ""model"": ""u_m_y_imporage"",     ""health"": 3000, ""weapon"": ""none"",   ""abilities"": [""super_strength"", ""aura""], ""auraColor"": ""gold"" }
  ]
}";

        public static List<CharacterDef> Load(string directory, Action<string> log)
        {
            string path = Path.Combine(directory, FileName);
            string json;

            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, DefaultJson, new UTF8Encoding(false));
                    log($"Personajes: no existía {FileName}; se creó con ejemplos.");
                }
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                log($"Personajes: no se pudo leer/crear {FileName} ({ex.Message}); se usan los ejemplos.");
                json = DefaultJson;
            }

            List<CharacterDef> parsed;
            try
            {
                parsed = Parse(json, log);
            }
            catch (Exception ex)
            {
                log($"Personajes: {FileName} tiene un error de formato ({ex.Message}); se usan los ejemplos.");
                parsed = Parse(DefaultJson, log);
            }

            var valid = new List<CharacterDef>();
            foreach (CharacterDef c in parsed)
            {
                var model = new Model(c.Model);
                if (!model.IsInCdImage || !model.IsPed)
                {
                    log($"Personajes: '{c.Id}' omitido: el modelo '{c.Model}' no está instalado o no es un personaje.");
                    continue;
                }
                if (valid.Any(v => v.Id == c.Id))
                {
                    log($"Personajes: '{c.Id}' omitido: id repetido.");
                    continue;
                }
                valid.Add(c);
            }

            log($"Personajes: {valid.Count} disponibles ({string.Join(", ", valid.Select(v => v.Id))}).");
            return valid;
        }

        private static List<CharacterDef> Parse(string json, Action<string> log)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var result = new List<CharacterDef>();

            if (!root.TryGetValue("characters", out object list) || !(list is IEnumerable items))
            {
                throw new FormatException("falta la lista \"characters\"");
            }

            foreach (object item in items)
            {
                if (!(item is Dictionary<string, object> d))
                {
                    continue;
                }

                string id = Str(d, "id");
                string model = Str(d, "model");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(model))
                {
                    log("Personajes: se omitió uno sin 'id' o sin 'model'.");
                    continue;
                }

                var c = new CharacterDef
                {
                    Id = id.Trim(),
                    Name = Str(d, "name") ?? id,
                    Model = model.Trim(),
                    Health = Math.Max(100, Math.Min(Int(d, "health", 1000), 100000)),
                    Weapon = (Str(d, "weapon") ?? "none").ToLowerInvariant(),
                };

                if (d.TryGetValue("abilities", out object abilities) && abilities is IEnumerable list2)
                {
                    foreach (object a in list2)
                    {
                        string ability = a?.ToString().Trim().ToLowerInvariant();
                        if (Supported.Contains(ability))
                        {
                            c.Abilities.Add(ability);
                        }
                        else if (Planned.Contains(ability))
                        {
                            log($"Personajes: '{c.Id}' usa '{ability}', que todavía no está disponible; se ignora.");
                        }
                        else
                        {
                            log($"Personajes: '{c.Id}' tiene una habilidad desconocida '{ability}'; se ignora.");
                        }
                    }
                }

                c.Image = Str(d, "image");
                c.AuraColor = ParseColor(Str(d, "auraColor"), c.AuraColor);
                string energy = Str(d, "energyColor");
                if (!string.IsNullOrWhiteSpace(energy))
                {
                    c.EnergyColor = ParseColor(energy, c.AuraColor);
                }
                result.Add(c);
            }

            return result;
        }

        private static Color ParseColor(string value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }
            if (AuraColors.TryGetValue(value.Trim(), out Color named))
            {
                return named;
            }
            string hex = value.Trim().TrimStart('#');
            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
            return fallback;
        }

        private static string Str(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object v) ? v?.ToString() : null;

        private static int Int(Dictionary<string, object> d, string key, int fallback)
        {
            if (!d.TryGetValue(key, out object v) || v == null)
            {
                return fallback;
            }
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }
    }
}
