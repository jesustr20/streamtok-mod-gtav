# StreamTok · Mod de GTA V

Plugin de ScriptHookVDotNet (C#) que, a partir de la Fase 2, se conecta como
**cliente** al WebSocket del sidecar de StreamTok (`ws://localhost:7331`) y
ejecuta dentro del juego los efectos configurados en *Regalos y Eventos*.

**Estado: Fase 1.** Solo confirma que el script carga. No tiene lógica real.

## Roadmap del mod

1. **Toolchain + "hola mundo"** ← estamos aquí
2. Conectar al WS del sidecar
3. Implementar los efectos del catálogo
4. Probar con el Simulador de eventos
5. Empaquetar para el botón "Instalar Mod"
6. Soporte de modelos custom (add-on peds)
7. Plantillas de comportamiento

---

## 1. Toolchain en tu PC (lo que corre el juego)

> Juega siempre en **Modo Historia**. ScriptHookV no funciona en GTA Online.

| Pieza | Para qué | Dónde |
|---|---|---|
| **ScriptHookV** (Alexander Blade) | Hook nativo en C++ + ASI loader (`dinput8.dll`) | dev-c.com → ScriptHookV. Revisa que la versión soporte **tu build y tu edición** del juego |
| **ScriptHookVDotNet** | Permite correr scripts .NET dentro del juego | **Legacy:** `scripthookvdotnet/scripthookvdotnet` (Releases o Nightly). **Enhanced:** `Chiheb-Bacha/ScriptHookVDotNetEnhanced` (reemplazo directo, sirve para ambas ediciones) |
| .NET Framework 4.8 (runtime) | SHVDN corre sobre él | Ya viene en Windows 10/11 actualizado |
| Visual C++ Redistributable x64 (2015-2022) | Lo requiere SHVDN | Microsoft |

Instalación, con el juego **cerrado**:

1. Copia `ScriptHookV.dll` y `dinput8.dll` a la carpeta raíz del juego (donde está `GTA5.exe`, o `GTA5_Enhanced.exe` en Enhanced).
2. Copia los archivos de la **raíz** del zip de SHVDN a esa misma carpeta (`ScriptHookVDotNet.asi`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll`, `.ini`). Actualízalos siempre juntos, nunca por separado.
3. Crea la carpeta `scripts\` en esa misma raíz si no existe.
4. Solo en Legacy con Rockstar Launcher: desactiva BattlEye en los ajustes del juego.

Comprobación antes de tocar código: abre el juego, carga Modo Historia y
pulsa **F4**. Si se abre la consola de SHVDN, el toolchain funciona.

## 2. Toolchain de desarrollo

- **Visual Studio 2022 Community** con la carga de trabajo **".NET desktop development"**.
- Alternativa: .NET SDK + VS Code. El `.csproj` incluye
  `Microsoft.NETFramework.ReferenceAssemblies`, así que `dotnet build`
  compila `net48` sin instalar el targeting pack. Esto también sirve para CI.

## 3. Compilar

```powershell
# Opcional: copia automática del DLL a <GTA>\scripts en cada build
setx GTA_DIR "E:\SteamLibrary\steamapps\common\Grand Theft Auto V"
# (abre una terminal nueva después de setx)

dotnet build -c Release
```

Si no defines `GTA_DIR`, copia a mano `bin\Release\net48\StreamTok.GtaV.dll`
a `<GTA>\scripts\`.

**Iterar sin reiniciar el juego:** vuelve a compilar y pulsa **Insert**
dentro del juego para recargar los scripts. Es la `ReloadKey` por defecto
de `ScriptHookVDotNet.ini`.

## 4. Definición de "hecho" de la Fase 1

- [ ] F4 abre la consola de SHVDN
- [ ] Al cargar Modo Historia aparece la notificación **"StreamTok v0.1.0 cargado OK"**
- [ ] Existe `<GTA>\scripts\StreamTok.GtaV.log` con la línea "Constructor ejecutado"
- [ ] En `<GTA>\ScriptHookVDotNet.log` aparece `StreamTok.GtaV.dll` cargado, sin excepciones
- [ ] Pulsar Insert recarga el script: el log muestra "Aborted" y luego "Constructor" de nuevo

## Si no carga

Revisa estos logs en la raíz del juego: `asiloader.log`, `ScriptHookV.log`
y `ScriptHookVDotNet.log`.

| Síntoma | Causa probable |
|---|---|
| Al abrir el juego sale el aviso "Critical error… unknown game version" | ScriptHookV no soporta todavía tu build. Pasa tras cada update de Rockstar; espera la versión nueva |
| F4 no hace nada | SHVDN no cargó: falta el VC++ Redist o hay un `.asi` o `.dll` de SHVDN de otra versión |
| La consola funciona pero el script no aparece | El DLL no está en `scripts\`, o se compiló como x86 o AnyCPU de 32 bits |
