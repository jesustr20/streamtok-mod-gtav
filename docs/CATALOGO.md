# Catálogo de acciones · StreamTok Mod GTA V

Catálogo del mod, independiente de TikTok. Cada acción tiene un `id` estable (el que viaja en
`mod-command`) y parámetros configurables desde StreamTok. Varias acciones parecidas de la lista
original se unificaron en **una acción con parámetros**. Por ejemplo, "dar lanzacohetes", "dar
sniper" y "dar arma random" son `give_weapon` con `weapon = rpg | sniper | random`.

**Dificultad:** ⭐ 1-2 natives · ⭐⭐ lógica propia (duración, seguimiento, restaurar estado) · ⭐⭐⭐ efecto construido desde cero
**Nombre:** 🏷️ = los spawns llevan el `nameTag` del viewer encima
**Duración:** ⏱️ = efecto temporal con parámetro `seconds`; el mod lo revierte solo

## 1. NPC y atacantes

| id | Cubre de la lista original | Parámetros | Dif. | |
|---|---|---|---|---|
| `spawn_attackers` | spawn atacantes · atacantes armados · npc con RPG / MG / cuerpo a cuerpo · asesinos monos · generar alien | `count`, `weapon` (none, melee, pistol, smg, rifle, mg, rpg, random), `model` (normal, mono, alien, random) | ⭐⭐ | 🏷️ |
| `spawn_bikers` | policías motorizados · bandidos motorizados | `count`, `faction` (police, bandits) | ⭐⭐ | 🏷️ |
| `spawn_animal` | animal random · animal furioso | `animal` (random, dog, cow, boar, cougar…), `hostile` (sí/no) | ⭐ | 🏷️ |
| `spawn_companion` | compañero de caminata | `model` | ⭐⭐ | 🏷️ |
| `spawn_crazy_npc` | npc furioso divertido | — | ⭐⭐ | 🏷️ |
| `spawn_fans` | conejos fans ❓ | `count`, `animal` | ⭐⭐ | 🏷️ |
| `attackers_arm` | equipar armas en atacantes | `weapon` | ⭐ | |
| `attackers_heal` | curar atacantes | — | ⭐ | |
| `attackers_to_pigs` | atacantes convertidos en cerdos | — | ⭐⭐ | |
| `attackers_remove` | remover atacantes | — | ⭐ | |

## 2. Vehículos

| id | Cubre | Parámetros | Dif. | |
|---|---|---|---|---|
| `spawn_vehicle` | generar auto / barco / avión · vehículo random · moto random · spawn y conducir random | `type` (car, bike, boat, plane, random), `model` (opcional), `drive` (meter al jugador sí/no) | ⭐ | 🏷️ |
| `spawn_ramp` | generar rampa | — | ⭐⭐ | |
| `vehicles_remove` | remover vehículos (los spawneados) | — | ⭐ | |
| `vehicle_replace` | reemplazar vehículo de serie ❓ | `type` | ⭐ | |
| `vehicle_repair` | reparar vehículo actual | — | ⭐ | |
| `vehicle_explode` | explotar vehículo | — | ⭐ | |
| `vehicle_delete` | eliminar vehículo del jugador | — | ⭐ | |
| `vehicle_eject` | dejar vehículo | — | ⭐ | |
| `vehicle_break` | desarmar vehículo | — | ⭐ | |
| `vehicle_burst_tires` | romper ruedas | — | ⭐ | |
| `vehicle_boost` | nitro · acelerar | `power` | ⭐ | |
| `vehicle_tuning` | tuning parcial / completo random | `mode` (partial, full) | ⭐⭐ | |
| `vehicles_invisible` | vehículos invisibles | `seconds` | ⭐⭐ | ⏱️ |
| `traffic_fast` | coches rápidos | `seconds` | ⭐⭐ | ⏱️ |

## 3. Jugador

| id | Cubre | Parámetros | Dif. | |
|---|---|---|---|---|
| `player_health` | aumentar vida · quitar vida | `mode` (add, remove), `amount` | ⭐ | |
| `player_kill` | matar jugador | — | ⭐ | |
| `player_invincible` | inmortalidad | `seconds` | ⭐ | ⏱️ |
| `player_invisible` | modo invisible | `seconds` | ⭐ | ⏱️ |
| `player_drunk` | modo ebrio | `seconds` | ⭐⭐ | ⏱️ |
| `player_night_vision` | visión nocturna | `seconds` | ⭐ | ⏱️ |
| `player_jump` | salto | `force` | ⭐ | |
| `player_super_jump` | super alto ❓ | `seconds` | ⭐ | ⏱️ |
| `player_skydive` | paracaidismo | `height` | ⭐ | |
| `player_transform` | convertir en perro · convertir en paloma | `animal` (dog, pigeon…), `seconds` | ⭐⭐ | ⏱️ |
| `player_random_outfit` | ropa random | — | ⭐ | |

`player_transform` guarda el modelo y la ropa originales y los restaura al terminar.

## 4. Armas

| id | Cubre | Parámetros | Dif. |
|---|---|---|---|
| `give_weapon` | añadir armas · dar armas · lanzacohetes · rifle sniper · arma random | `weapon` (específica, rpg, sniper, random, all) | ⭐ |
| `remove_weapons` | quitar armas | — | ⭐ |
| `max_ammo` | munición máxima | — | ⭐ |

## 5. Policía y dinero

| id | Cubre | Parámetros | Dif. |
|---|---|---|---|
| `wanted_level` | aumentar · reducir · búsqueda máxima | `mode` (add, remove, max, clear), `stars` | ⭐ |
| `money` | añadir dinero · establecer dinero | `mode` (add, set), `amount` | ⭐ |

## 6. Teletransporte

| id | Cubre | Parámetros | Dif. |
|---|---|---|---|
| `teleport` | arriba · random · a ubicación · punto siguiente · punto anterior | `mode` (up, random, location, next, previous), `location` | ⭐⭐ |

`next` y `previous` recorren una lista de puntos que el mod trae (y que más adelante podría editarse desde StreamTok).

## 7. Mundo y efectos

| id | Cubre | Parámetros | Dif. | |
|---|---|---|---|---|
| `set_time` | establecer hora | `hour` | ⭐ | |
| `set_weather` | establecer clima | `weather` (clear, rain, thunder, snow, fog…) | ⭐ | |
| `gravity_low` | gravedad reducida | `seconds` | ⭐ | ⏱️ |
| `earthquake` | terremoto | `seconds`, `intensity` | ⭐⭐ | ⏱️ |
| `meteor_shower` | lluvia de meteoritos | `seconds`, `density` | ⭐⭐⭐ | ⏱️ |
| `black_hole` | agujero negro | `seconds`, `strength` | ⭐⭐⭐ | ⏱️ |
| `tornado` | (propuesto) | `seconds`, `strength` | ⭐⭐⭐ | ⏱️ |
| `orange_ball` | bola naranja ❓ | — | ❓ | |

## 8. Modos de juego (fase aparte)

No son acciones sueltas: son **reglas que corren todo el tiempo** mientras están activas.
En el contrato irían como una lista `modes` en `mod-hello`, aparte de `actions`.

- **Chiliad** (interpretación a confirmar): reto de subir el Monte Chiliad. Sus acciones
  propias serían desactivar GPS, temporizador, accidente en pista y rampa.
- **Chaos Mod**: efectos aleatorios cada X segundos. Existe **ChaosModV**, un mod de código
  abierto con cientos de efectos; sirve de fuente de ideas. Revisar su licencia antes de
  reutilizar código.

## ❓ Por aclarar

- `spawn_fans`: ¿"conejos fans" son conejos que siguen al jugador?
- `vehicle_replace`: ¿"reemplazar vehículo de serie" cambia el vehículo actual por otro al azar?
- `player_super_jump`: ¿"super alto" es súper salto temporal?
- `orange_ball`: ¿qué hace la "bola naranja"?

## Tandas de implementación

1. **Base (todo ⭐, valida el sistema):** `spawn_animal`, `spawn_attackers`, `attackers_remove`, `give_weapon`, `player_health`, `wanted_level`, `set_weather`, `set_time`, `vehicle_repair`, `vehicle_explode`
2. **Vehículos y jugador:** el resto de ⭐ de las secciones 2 a 6
3. **Temporales ⏱️ y ⭐⭐:** drunk, transform, earthquake, bikers, tuning, teleport…
4. **Espectáculo ⭐⭐⭐:** meteoritos, agujero negro, tornado
5. **Modos:** Chiliad, Chaos