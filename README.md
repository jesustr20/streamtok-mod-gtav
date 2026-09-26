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
| 7. Personajes custom: JSON, add-on peds, habilidades super_strength/tank/gunslinger/aura (49 acciones) | 🚧 v0.7.0 |
| 5+. Personajes custom, efectos ⭐⭐⭐, modos de juego, empaquetado para "Instalar Mod" | Pendiente |

## Contrato WebSocket (`ws://localhost:7331`)

Todos los mensajes son `{ "channel": "...", "payload": { ... } }`.

| Canal | Dirección | Payload |
|---|---|---|
| `mod-hello` | mod → app | `{ mod, version, actions: [{ id, name, supportsNameTag, params: [{ name, type, default, min?, max?, options? }] }] }` |
| `mod-command` | app → mod | `{ id, action, params, nameTag?, notify? }` |
| `mod-ack` | mod → app | `{ id, ok, error? }` |

- **`nameTag`**: texto que se dibuja sobre cada entidad que cree la acción (el nombre del viewer).
- **`notify`**: texto opcional para la notificación en pantalla.
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

- **Menú F7 dentro del juego:** lista todas las acciones con sus parámetros. Usa el mismo
  camino de ejecución que los comandos reales.
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
[Debug]
MenuEnabled=true
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
Log del mod: `scripts\StreamTok.GtaV.log`.

| Síntoma | Causa probable |
|---|---|
| Aviso "Critical error… unknown game version" o nada carga tras un update | ScriptHookV no soporta tu build todavía; espera la versión nueva |
| No existe `asiloader.log` / ningún log | Falta el loader (`dinput8.dll` en Legacy, `xinput1_4.dll` en Enhanced) o la versión del juego no es compatible |
| El juego se cierra al pulsar F4 / `DllNotFoundException: 'MinHook.x64.dll'` | Falta `MinHook.x64.dll` de SHVDNE |
| `ScriptHookVDotNet.log` dice "directory is missing" | La carpeta debe llamarse exactamente `scripts` y estar junto al `.exe` |
| F4 no hace nada | SHVDN no cargó: falta el VC++ Redist, archivos bloqueados (`Unblock-File`) o versiones mezcladas |
| No se crean logs y el juego está en `Program Files` | Windows no deja escribir ahí sin permisos; mueve el juego a otra biblioteca de Steam |
