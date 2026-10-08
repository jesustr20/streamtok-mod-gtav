# StreamTok · Mod de GTA V

Plugin de ScriptHookVDotNet (C#) que se conecta a **StreamTok** por WebSocket y ejecuta
acciones dentro de GTA V: spawns, efectos sobre el jugador, vehículos, clima…

**El mod no conoce TikTok.** Solo publica su catálogo de acciones y ejecuta comandos ya
resueltos. Qué evento dispara qué acción ("1 rosa → 3 atacantes") lo configura el streamer
en StreamTok. Catálogo completo y plan de tandas: [`docs/CATALOGO.md`](docs/CATALOGO.md).
Personajes custom: [`docs/PERSONAJES.md`](docs/PERSONAJES.md).

## Estado

| Fase | Estado |
|---|---|
| 1. Toolchain + "hola mundo" | ✅ Legacy (v0.1.0) · ⏳ Enhanced (bloqueado: ScriptHookV aún no soporta 1.0.1158.16) |
| 2. Comunicación con StreamTok + tanda 1 (10 acciones) | ✅ v0.2.0 |
| 3. Tanda 2: vehículos, jugador, armas, dinero + regla de nombres (29 acciones) | ✅ v0.3.0 |
| 4. Tanda 3: ebrio, motorizados, tuning, terremoto, teletransporte (34 acciones; "convertir en animal" pausado) | ✅ v0.4.0 |
| 5. Tanda 4: atacantes (armas, curar, cerdos), compañero, NPC furioso, rampa, vehículos invisibles, coches rápidos, gravedad (45 acciones) | ✅ v0.5.0 |
| 6. Tanda 5: lluvia de meteoritos, agujero negro, tornado (48 acciones) | ✅ v0.6.0 |
| 7. Personajes custom: JSON, add-on peds, habilidades super_strength/tank/gunslinger/aura (49 acciones) | ✅ v0.7.0 |
| 8. Habilidades avanzadas: ki y Kamehameha, vuelo, esquivar, súper velocidad | ✅ v0.8.0 |
| 8.1 Catálogo para el panel: category, icon, description e image en cada acción | ✅ v0.8.1 |
| 8.2 Modo Chiliad: reto a la cima con cuenta regresiva, GPS apagado, accidentes (66 acciones + personajes) | ✅ v0.8.2 |
| 8.3 Modo Pelea de viewers: arena cerrada, vida por donación, armas, poderes temporales, kills y Top 5 (75 acciones + personajes) | ✅ v0.8.3 |
| 8.4 Modo Parkour: torre por semilla en lugar abierto, piezas pegadas, sin daño por caída, acciones de viewers, cargador de mapas de Menyoo (84 acciones + personajes) | ✅ v0.8.4 |
| 8.5 Parkour estilo Only Up!: torre propia con objetos del juego base, tramos temáticos, todo conectado | ⏸️ En pausa (el mapa "Only Up in GTA 5" necesita Menyoo, que cierra el juego en 1.0.3889.0) |
| 9. Producción: logs en %LOCALAPPDATA% con rotación, F7 apagado por defecto, módulos aislados, cola protegida, prueba de estrés, guía para streamers | 🚧 v0.9.0 |
| 10. Integración con la app StreamTok (v1.0) | Pendiente |

## Modos de juego

Cada modo tiene su sección en el menú F7 (interruptor ON/OFF + opciones) y su categoría en `mod-hello`.
Detalle completo en [`docs/CATALOGO.md`](docs/CATALOGO.md).

| Modo | Resumen |
|---|---|
| **Monte Chiliad** (`chiliad`) | Llegar a la meta desde la entrada del aeropuerto (1er piso). Iniciar enciende todo (GPS, ruta, tiempo límite, reaparición, taxi) y cada cosa tiene su interruptor. Tiempo con minutos sugeridos o personalizado. Meta tipo checkpoint: quedarse `hold_seconds`. Taxi con "[E]" al túnel del camino de tierra. |
| **Pelea de viewers** (`arena`) | Todos contra todos en arena cerrada (campo libre: sin policía ni tráfico). Todos empiezan con 100 de vida, sin armas ni poderes; vida por monedas, armas permanentes, poderes temporales. Gana el último en pie y empieza otra ronda. El jugador pelea o queda libre ("Yo peleo"). |
| **Parkour** (`parkour`) | Torre al azar con semilla en lugar abierto (Sandy Shores por defecto). Piezas grandes y pegadas, sin encimarse. Sin daño por caída; campo libre. Viewers: viento, tropezón, quitar el piso, súper salto, volver a lo más alto o al inicio. También carga mapas de parkour de Menyoo (`course`, ver abajo). |

**Puntos que se marcan en el juego y se guardan** (carpeta `scripts`):

| Archivo | Qué guarda | Acción |
|---|---|---|
| `StreamTok.ChiliadGoal.txt` | Meta del Chiliad | Marcar meta aquí |
| `StreamTok.ChiliadStart.txt` | Salida del Chiliad | Marcar salida aquí |
| `StreamTok.ChiliadTaxi.txt` | Parada del taxi (por defecto -508.90, 4936.64, 146.88) | Marcar parada del taxi aquí |
| `StreamTok.ArenaPlace.txt` | Lugar de la arena | Marcar arena aquí |
| `StreamTok.ParkourPlace.txt` | Inicio del parkour | Marcar parkour aquí |

**Mapas de parkour de Menyoo:** poner el `.xml` en `scripts\StreamTok.Parkour\` (ej. `OnlyUp.xml`), recargar
con Insert y elegir `course` = nombre del archivo. Se crean todas sus piezas (objetos y vehículos congelados)
con posición y giro exactos; salida = ReferenceCoords, meta = pieza más alta. No hace falta Menyoo para
cargarlos, **salvo** que el mapa use objetos de mods de props (ej. *Custom Props Add-On*): esos solo se pueden
crear con Menyoo instalado, y Menyoo 1.8.1 / latest (2023) **cierra el juego** en GTA V Legacy 1.0.3889.0.
Si faltan objetos, se avisa en pantalla y en el log. Los mapas son de sus autores: no se suben al repo
(`.gitignore`: `**/StreamTok.Parkour/*.xml`).

## Contrato WebSocket (`ws://localhost:7331`)

Todos los mensajes son `{ "channel": "...", "payload": { ... } }`.

| Canal | Dirección | Payload |
|---|---|---|
| `mod-hello` | mod → app | `{ mod, version, actions: [{ id, name, category, icon, description, image?, supportsNameTag, params: [{ name, type, default, min?, max?, options?, presets? }] }] }` |
| `mod-command` | app → mod | `{ id, action, params, nameTag?, notify? }` |
| `mod-ack` | mod → app | `{ id, ok, error? }` |

- **`nameTag`**: texto que se dibuja sobre cada entidad que cree la acción (el nombre del viewer).
- **`notify`**: texto opcional para la notificación en pantalla.
- **`presets`** (solo `int`): valores sugeridos para mostrar como botones (ej. 5, 10, 15 min); se acepta cualquier número entre `min` y `max`.
- El mod **recorta todo parámetro a sus límites** y usa el default si llega algo inválido.
- **Nombres:** todo spawn aparte (carro al costado, animales, atacantes) lleva su propio nombre
  fijo mientras exista; nunca se reemplaza. Lo que afecta al personaje sí se reemplaza:
  - **Generar vehículo** (`player_vehicle`) sube al jugador a un vehículo nuevo; si ya va en uno,
    **lo reemplaza** (mismo lugar, rumbo y velocidad) y el nombre pasa a ser el del último viewer.
  - **Generar carro al lado** (`spawn_vehicle`) crea otro vehículo aparte con su propio nombre;
    cada viewer que lo mande genera uno nuevo.
  - Los efectos del personaje (inmortal, invisible, visión nocturna, súper salto) **no tienen
    duración**: se activan o desactivan con `enabled`. El nombre va encima del personaje y el
    último viewer que repite el mismo efecto reemplaza al anterior.

## Probar sin StreamTok

- **Menú F7 dentro del juego** (apagado por defecto: activarlo con `MenuEnabled=true` en el `.ini`), por secciones: *Juego normal* (por categoría), *Monte Chiliad*,
  *Pelea de viewers* y *Parkour*. Usa el mismo camino de ejecución que los comandos reales.
  - Arriba/Abajo elegir · Enter/Derecha entrar o ejecutar · Izquierda/Retroceso volver · F7 cerrar.
  - En los parámetros: Izq/Der cambia el valor (salta entre los sugeridos); **Shift** = de 1 en 1 (personalizado) o x10.
  - Los ajustes elegidos se recuerdan hasta recargar el mod (Insert) o cerrar el juego.
  - **Prueba de estrés** (última opción del menú): encola 20-300 acciones al azar como si llegaran donaciones
    seguidas y al final muestra cuántas salieron bien, cuánto tardó y el peor FPS.
- **Simulador** (`tools/fake-sidecar`): servidor WS que imita a StreamTok.
  ```powershell
  cd tools\fake-sidecar
  npm install
  npm start
  ```
  Muestra el catálogo que publica el mod y permite enviar cualquier acción.

## Configuración opcional (`scripts\StreamTok.GtaV.ini`)

```ini
[Connection]
Url=ws://localhost:7331
[Limits]
MaxSpawnedPeds=100
MaxSpawnedVehicles=20
[Arena]
HealthTiers=1:20,10:25,100:30,500:40,1000:50   ; desde X monedas : vida por moneda
[Debug]
MenuEnabled=false   ; true = menú de pruebas F7 (apagado por defecto para streamers)
TestNameTag=Viewer de prueba
```

## Versiones probadas

| Componente | Versión |
|---|---|
| GTA V Legacy (Steam) | 1.0.3889.0 |
| GTA V Enhanced (Steam) | 1.0.1158.16: **no soportado aún** por ScriptHookV (soporta hasta 1158.13) |
| ScriptHookV | v3889.0 / 1158.13 (15 jul 2026) |
| ScriptHookVDotNet Enhanced | 1.1.0.6 (API 3.9.0.6) |
| Visual Studio | Community 2026 (18.10) |

---

Guía corta para streamers (instalar y usar, sin desarrollo): [`docs/STREAMERS.md`](docs/STREAMERS.md).

## Instalación en el PC de juego

> Solo **Modo Historia**. ScriptHookV no funciona en GTA Online.

La raíz es la carpeta donde está `GTA5.exe` (Legacy) o `GTA5_Enhanced.exe` (Enhanced).
En Steam: clic derecho en el juego → *Administrar → Ver archivos locales*.
Guarda los zips descargados **fuera del repo** (por ejemplo `..\deps\`).

1. Verifica la versión del `.exe` (*Propiedades → Detalles*): debe estar soportada por ScriptHookV.
2. **ScriptHookV** (dev-c.com → Download, no el SDK): copia a la raíz **todo el contenido de
   `bin`** (`ScriptHookV.dll`, `dinput8.dll`, `xinput1_4.dll`, `args.txt`) **excepto
   `NativeTrainer.asi`**. Enhanced usa `xinput1_4.dll` como loader; `args.txt` desactiva BattlEye.
3. **ScriptHookVDotNet Enhanced** (gta5-mods.com o GitHub Releases): copia a la raíz todos los
   archivos sueltos del zip, **incluido `MinHook.x64.dll`**. Actualízalos siempre juntos.
4. Crea la carpeta **`scripts`** (plural, minúsculas) junto al `.exe` y copia ahí
   `StreamTok.GtaV.dll` (del Release de GitHub o de tu compilación).
5. Desbloquea lo descargado: en PowerShell, dentro de la raíz, `Get-ChildItem -Recurse | Unblock-File`.

Comprobación: en Modo Historia, **F4** abre la consola de SHVDN.

## Desarrollo

- **Visual Studio Community 2026** con **"Desarrollo de escritorio de .NET"**.
- O `dotnet build -c Release` (compila `net48` sin targeting pack; así lo hace el CI en Linux).
- Copia automática del DLL al compilar: `setx GTA_DIR "<carpeta de GTA V>"` y reinicia VS.
- **Iterar sin reiniciar el juego:** compila y pulsa **Insert** en el juego para recargar.

### CI / Release

- **CI** (cada PR y push a `main`): compila y revisa el simulador.
- **Release** (al subir un tag `v*`): compila con la versión del tag y publica
  `StreamTok.GtaV-vX.Y.Z.zip` con `scripts\StreamTok.GtaV.dll`.

## Si no carga

Logs en la raíz del juego: `asiloader.log`, `ScriptHookV.log`, `ScriptHookVDotNet.log`.
Log del mod: `%LOCALAPPDATA%\StreamTok\logs\StreamTok.GtaV.log` (rota al pasar 1 MB; guarda 3 anteriores).

| Síntoma | Causa probable |
|---|---|
| Aviso "Critical error… unknown game version" o nada carga tras un update | ScriptHookV no soporta tu build todavía; espera la versión nueva |
| No existe `asiloader.log` / ningún log | Falta el loader (`dinput8.dll` en Legacy, `xinput1_4.dll` en Enhanced) o la versión del juego no es compatible |
| El juego se cierra al pulsar F4 / `DllNotFoundException: 'MinHook.x64.dll'` | Falta `MinHook.x64.dll` de SHVDNE |
| `ScriptHookVDotNet.log` dice "directory is missing" | La carpeta debe llamarse exactamente `scripts` y estar junto al `.exe` |
| F4 no hace nada | SHVDN no cargó: falta el VC++ Redist, archivos bloqueados (`Unblock-File`) o versiones mezcladas |
| No se crean logs y el juego está en `Program Files` | Windows no deja escribir ahí sin permisos; mueve el juego a otra biblioteca de Steam |
| El juego se cierra en el launcher de Rockstar y `Menyoo.log` se corta en `MODULEINFO` | Menyoo no es compatible con la versión del juego: quitar `Menyoo.asi` y `menyooStuff` |
| Un mapa de parkour tiene huecos / aviso "faltan X objetos" | El mapa usa objetos de un mod de props (ver "Mapas de parkour de Menyoo") |
