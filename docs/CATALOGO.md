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
| `weapon_random` | Armas aleatorias: cada vez un arma que aún no tiene (rueda completa del modo historia, ~70); cuando la completa, cada vez suma 2 cargadores a todas (aparte de "Munición máxima") | `count` (1-10) | 🚧 v0.8.2 |
| `remove_weapons` | Quitar armas | — | ✅ v0.3 |
| `max_ammo` | Munición máxima | — | ✅ v0.3 |

## 5. Policía, dinero y mundo

| id | Nombre | Parámetros | Estado |
|---|---|---|---|
| `wanted_level` | Nivel de búsqueda | `mode` (add, remove, max, clear), `stars` | ✅ v0.2 |
| `wanted_max` | Búsqueda máxima (5 estrellas al instante) | — | | 🚧 v0.8.2 |
| `wanted_clear` | Quitar búsqueda (las estrellas desaparecen) | — | | 🚧 v0.8.2 |
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

Habilidades: `super_strength`, `tank`, `gunslinger`, `aura` (v0.7) · `energy_blast` (ki y Kamehameha), `flight`, `dodge`, `speed` (v0.8).

## 7. Modos de juego

Cada modo tiene su propia categoría en `mod-hello` (ej. `chiliad`) y su propia sección en el menú F7:

```
F7
├── Juego normal
│   ├── NPC y atacantes (10)
│   ├── Vehículos (15)
│   └── ... Jugador, Armas y dinero, Mundo, Espectáculos, Personajes
└── Monte Chiliad [ON/OFF]
    ├── Modo: APAGADO / ACTIVO   (Enter = activar/terminar · Derecha = ajustes)
    ├── GPS (minimapa) · Ruta marcada · Tiempo límite · Reaparecer donde quedó: ON/OFF   (al iniciar, todo en ON)
    ├── Chiliad: tiempo, apagar GPS, volver al inicio   (atenuadas si el modo está apagado)
    └── Accidente en el camino, quitar autos chocados, rampa, quitar rampas
```

### Modo Chiliad (v0.8.2)

Reto: llegar desde la salida hasta la **cima del Monte Chiliad** antes de que se acabe el tiempo.
Mientras está activo, todas las demás acciones siguen funcionando (los viewers ayudan o estorban).

- **Al iniciar se enciende todo:** salida desde el aeropuerto de Los Santos (o lejana al azar), GPS, cuenta regresiva y reaparición. Cada cosa
  se apaga por separado con su interruptor; el estado se ve en el HUD ("GPS ON · Ruta ON · Tiempo ON · Reaparecer ON").
- **La meta es un círculo tipo checkpoint** de ~11 m de ancho: anillo brillante en el suelo y columna
  transparente del mismo color que "respira" (amarillo afuera, verde adentro), también visible en el mapa.
  Por defecto está en la cima; con **Marcar meta aquí** se mueve a donde estés parado y queda guardada en
  `scripts\StreamTok.ChiliadGoal.txt` (con `mode = default` vuelve a la cima). Al entrar se pone
  verde y arranca un contador (`hold_seconds`, ej. 5 s): hay que quedarse adentro hasta que llegue a 0. Si lo
  sacan (una donación, una explosión…), el contador desaparece y vuelve a empezar completo cuando regrese.
  Al llegar a 0: cima lograda (récord, fuegos artificiales) y, con `repeat`, nueva vuelta desde la salida.
- **Taxi:** cada vez que sube a un taxi como pasajero aparece el cartel "[E] Viajar en taxi al Monte Chiliad
  (túnel)" (no desaparece después del primer viaje). Mientras va en taxi, el destino del mapa apunta al túnel
  (el taxi del juego también lo lleva ahí); al bajarse vuelve a marcar la meta. Con E: pantalla a negro, el
  taxi aparece en el túnel del camino de tierra del monte y el jugador se baja (el reloj se pausa durante el
  salto). La parada se marca con **Marcar parada del taxi aquí** (`scripts\StreamTok.ChiliadTaxi.txt`).
- **Libre:** a pie o en cualquier vehículo. En el mapa aparece la ruta amarilla a la cima.
- **Cuenta regresiva:** si llega a cero, se pierde el intento y vuelve a la salida con el reloj completo.
- **Si muere o lo arrestan:** reaparece en el último punto seguro (unos 2-4 s antes) y sigue; si iba
  manejando, con un vehículo del mismo modelo. El reloj se pausa mientras está muerto.
- **En pantalla:** reloj (rojo parpadeante con menos de 30 s), altura y barra de progreso, intento,
  récord y "SIN GPS". Al llegar: mensaje grande, récord y fuegos artificiales.

| id | Nombre | Parámetros | | Estado |
|---|---|---|---|---|
| `chiliad_start` | Chiliad: iniciar (reinicia si ya estaba activo) | `minutes` (sugeridos 5, 10, 15, 20, 25, 30, 45, 60 o cualquier número 1-60; por defecto 15), `timer` (sí/no: empezar con o sin tiempo límite), `hold_seconds` (segundos dentro del círculo de la cima; sugeridos 3, 5, 10, 15, 20, 30 o cualquier número 1-60; por defecto 5), `repeat` (sí/no: al lograrlo, otra vuelta desde la salida), `start` (airport = por defecto, random = lejana al azar, del_perro_pier, grove_street, vinewood_sign, sandy_shores, current), `vehicle` (keep, none, sanchez, bf400, blazer, mesa, trophy_truck, bifta, bmx) | 🏷️ | 🚧 v0.8.2 |
| `chiliad_stop` | Chiliad: terminar | — | | 🚧 v0.8.2 |
| `chiliad_set_goal` | Marcar meta aquí (o `default` = volver a la cima); se guarda para las próximas sesiones | `mode` (here, default) | | 🚧 v0.8.2 |
| `chiliad_set_start` | Marcar salida aquí: reemplaza a la salida `airport` (por defecto, la vereda de la entrada de la terminal); `default` la restaura | `mode` (here, default) | | 🚧 v0.8.2 |
| `chiliad_taxi` | Taxi al Monte Chiliad (viaje rápido a la salida del túnel) | `enabled` | | 🚧 v0.8.2 |
| `chiliad_set_taxi_stop` | Marcar parada del taxi aquí (o `default`); se guarda | `mode` (here, default) | | 🚧 v0.8.2 |
| `chiliad_gps` | GPS (minimapa). Encenderlo también cancela un "apagar GPS unos segundos" | `enabled` | | 🚧 v0.8.2 |
| `chiliad_route` | Ruta trazada hasta la cima (la línea del mapa) | `enabled` | | 🚧 v0.8.2 |
| `chiliad_timer` | Tiempo límite (desactivado = sin reloj en pantalla, el tiempo no cuenta ni hace perder; `chiliad_time` falla) | `enabled` | | 🚧 v0.8.2 |
| `chiliad_respawn` | Reaparecer donde quedó (apagado = al morir vuelve a la salida y pierde el intento) | `enabled` | | 🚧 v0.8.2 |
| `chiliad_time` | Chiliad: sumar/restar tiempo (restar puede dejarlo en cero = pierde) | `mode` (add, remove), `seconds` (5-600) | | 🚧 v0.8.2 |
| `chiliad_gps_off` | Chiliad: apagar GPS unos segundos, para viewers (minimapa y ruta; si se repite, se suma) | `seconds` (5-300) | | 🚧 v0.8.2 |
| `chiliad_back_to_base` | Chiliad: volver al inicio (el reloj sigue) | — | | 🚧 v0.8.2 |
| `road_accident` | Accidente en el camino: autos chocados atravesados más adelante, algunos en llamas (funciona también fuera del modo; categoría `vehicle`) | `cars` (1-6), `distance` (25-150), `fire` | 🏷️ | 🚧 v0.8.2 |
| `wrecks_remove` | Quitar autos chocados | — | | 🚧 v0.8.2 |

La rampa (`spawn_ramp`) sirve igual dentro del modo.

### Pelea de viewers (v0.8.3)

Todos contra todos en una **arena cerrada**. Al iniciar, el jugador va al lugar elegido y se levanta una
**pared circular** (columna naranja transparente + anillo en el suelo, y círculo en el mapa): el luchador que
la cruza vuelve adentro al instante. **La arena no se quita hasta que queda uno solo en pie** (el ganador).

- **El jugador** pelea si `player_fights` / "Yo peleo" está en ON: todos pueden atacarlo y la pared también lo
  retiene. **Si muere, queda fuera**: reaparece libre en el hospital, nadie lo ataca y la pelea sigue sin él.
  Puede volver cuando quiera con "Yo peleo" (lo lleva adentro) o quedarse fuera mirando.
- **Fin de ronda:** cuando queda **uno en pie** (habiendo peleado al menos dos) → "GANADOR: nombre" y fuegos
  artificiales. Con `minutes` > 0 hay además un límite: si se acaba, gana el de más kills.
- **Después del ganador** (también si ganas tú: "¡GANASTE!"): la arena **sigue** y empieza otra ronda; todos
  se vuelven a unir. Mientras se celebra no entra nadie. Se termina solo con "Pelea: terminar" (o `repeat` = no).
- **Recuadro de ganadores** arriba de la tabla: el último ganador (1º) y los dos anteriores, con ronda y kills.

Lugares: `airport` (pista del aeropuerto), `sandy_shores` (aeródromo), `marked` (el guardado con
**Marcar arena aquí**, en `scripts\StreamTok.ArenaPlace.txt`) o `here` (donde esté el jugador).
**Campo libre** mientras dura la pelea: sin policía ni estrellas (tampoco por entrar a la zona restringida
del aeropuerto), sin tráfico ni peatones nuevos, y la arena se limpia de gente y autos del juego cada 2 s
(lo que crea el mod y el vehículo del jugador se respetan). Al terminar, todo vuelve a la normalidad.
Lugares: `airport` (pista del aeropuerto), `sandy_shores` (aeródromo), `marked` (el guardado con
**Marcar arena aquí**, en `scripts\StreamTok.ArenaPlace.txt`) o `here` (donde esté el jugador). Ronda **continua**: se puede entrar en
cualquier momento y el que muere puede volver. Al acabar el tiempo de la ronda gana **quien tenga más kills**
(cartel de GANADOR y fuegos artificiales); con `repeat`, empieza otra ronda (las kills vuelven a 0).

- **Un luchador por viewer** (su `nameTag` es la clave), con el personaje que elija (los del JSON o los base:
  brawler, biker, soldier, gangster, clown, boxer). **Todos empiezan igual**: 100 de vida, sin armas y sin
  poderes, sea cual sea el personaje (Goku incluido: solo se usa su modelo).
- **Vida por donación** (`coins` = monedas donadas; también cura esa cantidad). Cada moneda vale según el tamaño
  de la donación, así una donación grande rinde más que muchas chicas:

  | Donación | Vida por moneda | Ejemplos |
  |---|---|---|
  | 1-9 monedas | 20 | 1 → +20 · 5 → +100 |
  | 10-99 | 25 | 10 → +250 · 50 → +1 250 |
  | 100-499 | 30 | 100 → +3 000 |
  | 500-999 | 40 | 500 → +20 000 |
  | 1000+ | 50 | 1000 → +50 000 |

  Tope 200 000. Se cambia en `StreamTok.GtaV.ini`: `[Arena] HealthTiers=1:20,10:25,100:30,500:40,1000:50`.
- **Armas** (`arena_weapon`): se quedan para siempre, también al revivir.
- **Poderes** (`arena_power`): temporales (X segundos; si se repite, se suma el tiempo) y se pierden al morir.
  Su potencia (explosiones de ki, empujones) crece con la vida: 100 → x1 · 1 000 → x1,5 · 10 000 → x2 · 100 000 → x2,5.
  `ki` (ráfagas y Kamehameha) · `fly` (vuela) · `strength` (cada golpe lanza lejos) · `speed` · `dodge` · `random`.
- Unirse otra vez estando vivo = donación; si murió, vuelve a entrar con el mismo personaje y sus armas
  (y 100 de vida más lo que done). Donar sin haberse unido lo mete con personaje al azar.
- **En pantalla:** nombre y barra de vida sobre cada luchador; tabla **Top 5** (kills, vida, "+P" si tiene
  un poder activo; el jugador aparece como "TÚ") y el reloj de la ronda.

| id | Nombre | Parámetros | | Estado |
|---|---|---|---|---|
| `arena_start` | Pelea: iniciar | `minutes` (0 = sin límite, por defecto; o 3, 5, 10… hasta 60), `repeat` (por defecto sí), `player_fights` (por defecto sí), `place` (airport, sandy_shores, marked, here), `radius` (sugeridos 15, 20, 30, 40, 60 o 12-80 m) | | 🚧 v0.8.3 |
| `arena_player` | Yo peleo (ON = entra a la arena; OFF = queda libre) | `enabled` | | 🚧 v0.8.3 |
| `arena_set_place` | Marcar arena aquí (se guarda; luego `place = marked`) | — | | 🚧 v0.8.3 |
| `arena_stop` | Pelea: terminar (borra a los luchadores) | — | | 🚧 v0.8.3 |
| `arena_join` | Unirse a la pelea | `character` (random, ids del JSON, luchadores base), `coins` (0-100000) | 🏷️ | 🚧 v0.8.3 |
| `arena_boost` | Más vida (donación) | `coins` (sugeridos 1, 10, 100, 500, 1000 o 1-100000) | 🏷️ | 🚧 v0.8.3 |
| `arena_weapon` | Arma para el luchador (se queda) | `weapon` (random, pistol, smg, rifle, mg, sniper, rpg, bat, knife) | 🏷️ | 🚧 v0.8.3 |
| `arena_power` | Poder temporal | `power` (random, ki, fly, strength, speed, dodge), `seconds` (sugeridos 10, 20, 30, 60 o 5-120) | 🏷️ | 🚧 v0.8.3 |
| `arena_bots` | Pelea: agregar bots de prueba | `count` (1-10) | | 🚧 v0.8.3 |

### Chaos Mod (pendiente)

Efectos aleatorios cada X segundos (ideas: ChaosModV; revisar su licencia antes de reutilizar código).

## ❓ Por aclarar

- "Conejos fans": ¿conejos que siguen al jugador?
- "Bola naranja": ¿qué hace?
- "Súper alto": implementado como súper salto; confirmar si era eso.
