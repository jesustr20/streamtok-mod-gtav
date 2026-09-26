# Catálogo de acciones · StreamTok Mod GTA V

Catálogo del mod, **independiente de TikTok**. Cada acción tiene un `id` estable (el que viaja
en `mod-command`) y parámetros que StreamTok configura. El mod publica este catálogo en
`mod-hello` al conectarse, así que lo que aparece aquí es lo que ve el panel de StreamTok.

**Estado:** ✅ implementada · 🚧 en esta rama · ⏳ planificada · ⏸️ pausada
**Dificultad:** ⭐ 1-2 natives · ⭐⭐ lógica propia · ⭐⭐⭐ efecto construido desde cero

## Regla de nombres

El nombre del viewer va **siempre encima del objeto**:

| Caso | Comportamiento |
|---|---|
| **Spawn aparte** (carro al lado, animales, atacantes, motorizados) | 🏷️ Único, con su nombre fijo mientras exista. Nunca se reemplaza |
| **Lo que afecta al personaje** (su vehículo, sus efectos, su transformación) | 🔁 Si otro viewer repite la misma acción, **su nombre reemplaza al anterior** |

- Los **efectos del personaje no tienen duración**: se activan o desactivan con `enabled` y quedan hasta que otra acción los cambie.
- Los **efectos del mundo** puntuales (terremoto, meteoritos, agujero negro, tornado) tienen `seconds`; si se repiten, se suma el tiempo.
- Los **efectos del mundo** persistentes (vehículos invisibles, coches rápidos, gravedad) se activan o desactivan con `enabled`.

## 1. NPC y atacantes

| id | Nombre | Parámetros | | Estado |
|---|---|---|---|---|
| `spawn_animal` | Spawn de animal | `animal`, `count` (1-20), `hostile` | 🏷️ | ✅ v0.2 |
| `spawn_attackers` | Spawn de atacantes | `count` (1-50), `weapon`, `model` (normal, random, chimp, alien) | 🏷️ | ✅ v0.2 |
| `spawn_bikers` | Motorizados | `count` (1-10), `faction` (bandits, police) | 🏷️ | 🚧 v0.4 |
| `attackers_remove` | Remover atacantes (incluye motos y NPC furiosos) | — | | ✅ v0.2 |
| `attackers_arm` | Equipar armas en atacantes | `weapon` | | 🚧 v0.5 |
| `attackers_heal` | Curar atacantes | — | | 🚧 v0.5 |
| `attackers_to_pigs` | Atacantes convertidos en cerdos (conservan su nombre) | — | 🏷️ | 🚧 v0.5 |
| `spawn_companion` | Compañero (te sigue, sube a tu vehículo y te defiende) | `type` (human, dog, random), `count`, `weapon` | 🏷️ | 🚧 v0.5 |
| `companions_remove` | Remover compañeros | — | | 🚧 v0.5 |
| `spawn_crazy_npc` | NPC furioso (estrafalario, a los puñetazos, no huye) | `count` | 🏷️ | 🚧 v0.5 |

## 2. Vehículos

| id | Nombre | Parámetros | | Estado |
|---|---|---|---|---|
| `player_vehicle` | Generar vehículo (te sube; si ya vas en uno, lo reemplaza sin perder velocidad) | `type` | 🔁 | ✅ v0.3 |
| `spawn_vehicle` | Generar carro al lado | `type` | 🏷️ | ✅ v0.3 |
| `vehicles_remove` | Remover vehículos | — | | ✅ v0.3 |
| `vehicle_repair` | Reparar vehículo | — | | ✅ v0.2 |
| `vehicle_explode` | Explotar vehículo | — | | ✅ v0.2 |
| `vehicle_delete` | Eliminar vehículo del jugador | — | | ✅ v0.3 |
| `vehicle_eject` | Sacar del vehículo | — | | ✅ v0.3 |
| `vehicle_break` | Desarmar vehículo | — | | ✅ v0.3 |
| `vehicle_burst_tires` | Romper ruedas | — | | ✅ v0.3 |
| `vehicle_boost` | Nitro | `power` | | ✅ v0.3 |
| `vehicle_tuning` | Tuning random | `mode` (partial, full) | | 🚧 v0.4 |
| `spawn_ramp` | Generar rampa (delante, mirando hacia donde vas) | `distance` | 🏷️ | 🚧 v0.5 |
| `ramps_remove` | Remover rampas | — | | 🚧 v0.5 |
| `vehicles_invisible` | Vehículos invisibles (el jugador siempre se ve) | `enabled` | | 🚧 v0.5 |
| `traffic_fast` | Coches rápidos (tráfico apurado) | `enabled` | | 🚧 v0.5 |

## 3. Jugador

| id | Nombre | Parámetros | | Estado |
|---|---|---|---|---|
| `player_health` | Vida del jugador | `mode` (add, remove), `amount` | | ✅ v0.2 |
| `player_kill` | Matar jugador | — | | ✅ v0.3 |
| `player_invincible` | Inmortalidad | `enabled` | 🔁 | ✅ v0.3 |
| `player_invisible` | Modo invisible | `enabled` | 🔁 | ✅ v0.3 |
| `player_night_vision` | Visión nocturna | `enabled` | 🔁 | ✅ v0.3 |
| `player_super_jump` | Súper salto | `enabled` | 🔁 | ✅ v0.3 |
| `player_drunk` | Modo ebrio | `enabled` | 🔁 | 🚧 v0.4 |
| `player_transform` | Convertir en animal — **pausado**: el cambio de modelo congela el juego (Legacy 1.0.3889.0 + SHVDNE 1.1.0.6) | `animal`, `enabled` | 🔁 | ⏸️ |
| `player_jump` | Salto | `force` | | ✅ v0.3 |
| `player_skydive` | Paracaidismo | `height` | | ✅ v0.3 |
| `player_random_outfit` | Ropa random | — | | ✅ v0.3 |
| `teleport` | Teletransporte (con el vehículo si vas en uno) | `mode` (up, random, location, next, previous), `location`, `height` | | 🚧 v0.4 |

## 4. Armas

| id | Nombre | Parámetros | Estado |
|---|---|---|---|
| `give_weapon` | Dar arma | `weapon` (específica, random, all) | ✅ v0.2 |
| `remove_weapons` | Quitar armas | — | ✅ v0.3 |
| `max_ammo` | Munición máxima | — | ✅ v0.3 |

## 5. Policía, dinero y mundo

| id | Nombre | Parámetros | Estado |
|---|---|---|---|
| `wanted_level` | Nivel de búsqueda | `mode` (add, remove, max, clear), `stars` | ✅ v0.2 |
| `money` | Dinero | `mode` (add, set), `amount` | ✅ v0.3 |
| `set_weather` | Clima | `weather` | ✅ v0.2 |
| `set_time` | Hora del día | `hour` | ✅ v0.2 |
| `earthquake` | Terremoto | `seconds`, `intensity` | 🚧 v0.4 |
| `gravity_low` | Gravedad reducida | `enabled`, `level` (low, very_low, zero) | 🚧 v0.5 |
| `meteor_shower` | Lluvia de meteoritos ⭐⭐⭐ (de noche: estrellas fugaces y bolas de fuego que impactan con explosión y fuego) | `seconds`, `density`, `night` | 🚧 v0.6 |
| `black_hole` | Agujero negro ⭐⭐⭐ (se abre en el cielo con disco de acreción y terremoto; tormenta; la gente huye aterrada; lo que llega al centro desaparece y el jugador muere) | `seconds`, `strength` | 🚧 v0.6 |
| `tornado` | Tornado ⭐⭐⭐ (embudo de humo con escombros y tormenta; avanza zigzagueando y levanta lo que atrapa) | `seconds`, `strength` | 🚧 v0.6 |

## 6. Personajes custom

Personaje = **modelo + habilidades combinables**, definidos en `scripts\StreamTok.Characters.json`.
Guía completa: [`PERSONAJES.md`](PERSONAJES.md).

| id | Nombre | Parámetros | | Estado |
|---|---|---|---|---|
| `spawn_character` | Personaje (enemigo o aliado) | `character` (ids del JSON o random), `side` (enemy, ally), `count` | 🏷️ | 🚧 v0.7 |
| `character_<id>` | Una acción por cada personaje del JSON, con su nombre (ej. "Goku") | `side`, `count` | 🏷️ | 🚧 v0.7 |

Habilidades: `super_strength`, `tank`, `gunslinger`, `aura` (v0.7) · `energy_blast`, `flight`, `dodge`, `speed` (v0.8).

## 7. Modos de juego

Reglas que corren todo el tiempo mientras están activas. Irán como `modes` en `mod-hello`.

- **Chiliad**: reto de subir el Monte Chiliad (desactivar GPS, temporizador, accidente en pista, rampa).
- **Chaos Mod**: efectos aleatorios cada X segundos (ideas: ChaosModV; revisar su licencia antes de reutilizar código).

## ❓ Por aclarar

- "Conejos fans": ¿conejos que siguen al jugador?
- "Bola naranja": ¿qué hace?
- "Súper alto": implementado como súper salto; confirmar si era eso.
