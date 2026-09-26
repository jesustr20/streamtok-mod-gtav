using System.Collections.Generic;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Datos de presentación de cada acción para el panel de StreamTok (se publican en mod-hello):
    ///  - category: para agruparlas (npc, vehicle, player, weapon, world, spectacle, character, chiliad).
    ///  - icon: clave de imagen; StreamTok trae su propia imagen para cada clave.
    ///  - description: texto corto bajo el nombre.
    /// Los personajes custom los completa CharacterActions (con la imagen del JSON).
    /// </summary>
    internal static class ActionMeta
    {
        public const string Npc = "npc";
        public const string Vehicle = "vehicle";
        public const string Player = "player";
        public const string Weapon = "weapon";
        public const string World = "world";
        public const string Spectacle = "spectacle";
        public const string Character = "character";
        public const string Chiliad = "chiliad";
        public const string Arena = "arena";
        public const string Other = "other";

        private static readonly Dictionary<string, string[]> Meta = new Dictionary<string, string[]>
        {
            // id                      category   icon                 description
            ["spawn_animal"]        = new[] { Npc, "animal", "Aparece un animal con el nombre del viewer; puede ser furioso" },
            ["spawn_attackers"]     = new[] { Npc, "attackers", "Atacantes armados que persiguen al jugador" },
            ["spawn_bikers"]        = new[] { Npc, "bikers", "Bandidos o policías en moto que persiguen y disparan" },
            ["attackers_remove"]    = new[] { Npc, "remove", "Borra a todos los atacantes (incluidas sus motos)" },
            ["attackers_arm"]       = new[] { Npc, "arm_attackers", "Da un arma a todos los atacantes vivos" },
            ["attackers_heal"]      = new[] { Npc, "heal_attackers", "Cura por completo a los atacantes" },
            ["attackers_to_pigs"]   = new[] { Npc, "pigs", "Convierte a los atacantes en cerdos furiosos" },
            ["spawn_companion"]     = new[] { Npc, "companion", "Un compañero que sigue y defiende al jugador" },
            ["companions_remove"]   = new[] { Npc, "remove", "Borra a los compañeros" },
            ["spawn_crazy_npc"]     = new[] { Npc, "crazy_npc", "Un personaje estrafalario que ataca a puñetazos" },

            ["player_vehicle"]      = new[] { Vehicle, "player_vehicle", "Sube al jugador a un vehículo nuevo o reemplaza el suyo sin perder velocidad" },
            ["spawn_vehicle"]       = new[] { Vehicle, "vehicle", "Aparece un vehículo al lado con el nombre del viewer" },
            ["spawn_ramp"]          = new[] { Vehicle, "ramp", "Una rampa delante del jugador" },
            ["ramps_remove"]        = new[] { Vehicle, "remove", "Borra las rampas" },
            ["vehicles_remove"]     = new[] { Vehicle, "remove", "Borra los vehículos generados" },
            ["vehicle_repair"]      = new[] { Vehicle, "repair", "Repara el vehículo del jugador" },
            ["vehicle_explode"]     = new[] { Vehicle, "explode", "Hace explotar el vehículo del jugador" },
            ["vehicle_delete"]      = new[] { Vehicle, "delete_vehicle", "Elimina el vehículo del jugador" },
            ["vehicle_eject"]       = new[] { Vehicle, "eject", "Saca al jugador del vehículo" },
            ["vehicle_break"]       = new[] { Vehicle, "dismantle", "Desmantela el vehículo: puertas, ruedas y vidrios" },
            ["vehicle_burst_tires"] = new[] { Vehicle, "tires", "Revienta las ruedas" },
            ["vehicle_tuning"]      = new[] { Vehicle, "tuning", "Tuning al azar, parcial o completo" },
            ["vehicle_boost"]       = new[] { Vehicle, "nitro", "Un empujón de nitro" },

            ["player_health"]       = new[] { Player, "health", "Suma o quita vida al jugador" },
            ["player_kill"]         = new[] { Player, "kill", "Mata al jugador" },
            ["player_invincible"]   = new[] { Player, "invincible", "Inmortalidad hasta que se desactive" },
            ["player_invisible"]    = new[] { Player, "invisible", "El jugador se vuelve invisible" },
            ["player_night_vision"] = new[] { Player, "night_vision", "Visión nocturna" },
            ["player_super_jump"]   = new[] { Player, "super_jump", "Súper salto" },
            ["player_drunk"]        = new[] { Player, "drunk", "Modo ebrio: camina tambaleando" },
            ["player_jump"]         = new[] { Player, "jump", "Lanza al jugador hacia arriba" },
            ["player_skydive"]      = new[] { Player, "skydive", "Paracaidismo desde las alturas" },
            ["player_random_outfit"] = new[] { Player, "outfit", "Ropa al azar" },
            ["teleport"]            = new[] { Player, "teleport", "Teletransporta al jugador (con su vehículo)" },
            ["wanted_level"]        = new[] { Player, "wanted", "Sube, baja o quita estrellas de búsqueda" },
            ["wanted_max"]          = new[] { Player, "wanted_max", "5 estrellas de búsqueda al instante" },
            ["wanted_clear"]        = new[] { Player, "wanted_clear", "Quita todas las estrellas de búsqueda" },
            ["money"]               = new[] { Player, "money", "Suma o fija el dinero" },

            ["give_weapon"]         = new[] { Weapon, "weapon", "Da un arma al jugador" },
            ["remove_weapons"]      = new[] { Weapon, "no_weapons", "Quita todas las armas" },
            ["max_ammo"]            = new[] { Weapon, "ammo", "Munición al máximo" },

            ["set_weather"]         = new[] { World, "weather", "Cambia el clima" },
            ["set_time"]            = new[] { World, "time", "Cambia la hora del día" },
            ["earthquake"]          = new[] { World, "earthquake", "Terremoto por unos segundos" },
            ["vehicles_invisible"]  = new[] { World, "invisible_cars", "Los autos cercanos se vuelven invisibles" },
            ["traffic_fast"]        = new[] { World, "fast_traffic", "El tráfico maneja apurado" },
            ["gravity_low"]         = new[] { World, "gravity", "Gravedad reducida" },

            ["meteor_shower"]       = new[] { Spectacle, "meteors", "Lluvia de meteoritos de noche" },
            ["black_hole"]          = new[] { Spectacle, "black_hole", "Un agujero negro se abre en el cielo y lo traga todo" },
            ["tornado"]             = new[] { Spectacle, "tornado", "Un tornado avanza hacia el jugador" },

            ["weapon_random"]       = new[] { Weapon, "weapon_random", "Un arma nueva al azar cada vez; con la rueda completa, suma munición" },
            ["arena_start"]         = new[] { Arena, "arena", "Pelea de viewers: todos contra todos alrededor del jugador, rondas por kills" },
            ["arena_stop"]          = new[] { Arena, "arena_stop", "Termina la pelea y borra a los luchadores" },
            ["arena_join"]          = new[] { Arena, "arena_join", "El viewer entra con el personaje que elija: poca vida, sin armas ni poderes" },
            ["arena_boost"]         = new[] { Arena, "arena_boost", "Donación: más vida según las monedas (y lo cura)" },
            ["arena_weapon"]        = new[] { Arena, "arena_weapon", "Un arma para su luchador; las armas se quedan" },
            ["arena_power"]         = new[] { Arena, "arena_power", "Poder por unos segundos (ki, vuelo, fuerza, velocidad, esquivar); más fuerte cuanta más vida" },
            ["arena_player"]        = new[] { Arena, "arena_player", "El jugador entra a pelear (ON) o queda libre (OFF); al morir queda fuera" },
            ["arena_set_place"]     = new[] { Arena, "arena_place", "Guarda donde está el jugador como lugar de la arena (\"marked\")" },
            ["arena_bots"]          = new[] { Arena, "arena_bots", "Agrega luchadores de prueba (Bot 1, Bot 2…)" },
            ["chiliad_start"]       = new[] { Chiliad, "chiliad", "Reto Monte Chiliad: llegar a la cima antes de que acabe el tiempo" },
            ["chiliad_stop"]        = new[] { Chiliad, "chiliad_stop", "Termina el reto Chiliad" },
            ["chiliad_set_goal"]    = new[] { Chiliad, "goal", "Pone la meta (el círculo) donde está el jugador y la guarda; default = cima" },
            ["chiliad_set_start"]   = new[] { Chiliad, "start_point", "Pone la salida principal donde está el jugador y la guarda; default = entrada del aeropuerto" },
            ["chiliad_set_taxi_stop"] = new[] { Chiliad, "taxi_stop", "Pone la parada del taxi (salida del túnel) donde está el jugador y la guarda" },
            ["chiliad_taxi"]        = new[] { Chiliad, "taxi", "En un taxi, permite viajar al instante hasta la salida del túnel del monte" },
            ["chiliad_gps"]         = new[] { Chiliad, "gps", "Enciende o apaga el minimapa (encender también cancela un apagón temporal)" },
            ["chiliad_route"]       = new[] { Chiliad, "route", "Muestra u oculta la ruta trazada hasta la cima" },
            ["chiliad_timer"]       = new[] { Chiliad, "timer", "Activa o desactiva el tiempo límite (desactivado = sin reloj, el tiempo no cuenta)" },
            ["chiliad_respawn"]     = new[] { Chiliad, "respawn", "Encendido: al morir sigue donde quedó. Apagado: vuelve a la salida" },
            ["chiliad_time"]        = new[] { Chiliad, "chiliad_time", "Suma o resta segundos al reloj del reto" },
            ["chiliad_gps_off"]     = new[] { Chiliad, "gps_off", "Apaga el minimapa y la ruta unos segundos" },
            ["chiliad_back_to_base"] = new[] { Chiliad, "back_to_base", "Manda al jugador de vuelta a la salida (el reloj sigue)" },
            ["road_accident"]       = new[] { Vehicle, "accident", "Autos chocados (y en llamas) bloquean el camino más adelante" },
            ["wrecks_remove"]       = new[] { Vehicle, "remove", "Borra los autos chocados" },

            ["spawn_character"]     = new[] { Character, "character", "Un personaje especial al azar o elegido" },
        };

        /// <summary>Completa categoría, ícono y descripción si la acción no los trae ya.</summary>
        public static void Apply(ActionDef action)
        {
            if (Meta.TryGetValue(action.Id, out string[] m))
            {
                action.Category = action.Category ?? m[0];
                action.Icon = action.Icon ?? m[1];
                action.Description = action.Description ?? m[2];
            }

            action.Category = action.Category ?? Other;
            action.Icon = action.Icon ?? action.Id;
            action.Description = action.Description ?? action.Name;
        }
    }
}
