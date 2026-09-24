# StreamTok · Mod de GTA V

Plugin de ScriptHookVDotNet (C#) que, a partir de la Fase 2, se conecta como
**cliente** al WebSocket del sidecar de StreamTok (`ws://localhost:7331`) y
ejecuta dentro del juego los efectos configurados en *Regalos y Eventos*.

**Estado:** Fase 1 completada en GTA V **Legacy** (v0.1.0). Pendiente: probar en Enhanced.

## Roadmap del mod

1. ~~Toolchain + "hola mundo"~~ ✅ (Legacy) · | GTA V Enhanced (Steam) | 1.0.1158.16: **no soportado aún** por ScriptHookV (soporta hasta 1158.13) |
2. Conectar al WS del sidecar
3. Implementar los efectos del catálogo
4. Probar con el Simulador de eventos
5. Empaquetar para el botón "Instalar Mod"
6. Soporte de modelos custom (add-on peds)
7. Plantillas de comportamiento

## Versiones probadas

| Componente | Versión |
|---|---|
| GTA V Legacy (Steam) | 1.0.3889.0 |
| GTA V Enhanced (Steam) | 1.0.1158.13 (pendiente de probar) |
| ScriptHookV | v3889.0 / 1158.13 (15 jul 2026) |
| ScriptHookVDotNet Enhanced | 1.1.0.6 (API 3.9.0.6) |
| Visual Studio | Community 2026 (18.10) |

---

## 1. Toolchain en el PC de juego

> Solo **Modo Historia**. ScriptHookV no funciona en GTA Online.

| Pieza | Para qué | Dónde |
|---|---|---|
| **ScriptHookV** (Alexander Blade) | Hook nativo + ASI loader (`dinput8.dll`) | dev-c.com → Script Hook V → botón **Download** (no el SDK) |
| **ScriptHookVDotNet Enhanced** (Chiheb-Bacha) | Scripts .NET en el juego. Reemplazo directo de SHVDN, sirve para **Legacy y Enhanced** | gta5-mods.com → Script Hook V .NET Enhanced (o GitHub Releases) |
| .NET Framework 4.8 | Runtime de SHVDN | Viene con Windows 10/11 actualizado |
| Visual C++ Redistributable x64 (2015-2022) | Requerido por SHVDN | Microsoft |

Guarda los zips descargados **fuera del repo** (por ejemplo `..\deps\`): no se versionan.

Instalación, con el juego **cerrado**. La raíz es la carpeta donde está
`GTA5.exe` (Legacy) o `GTA5_Enhanced.exe` (Enhanced). En Steam:
clic derecho en el juego → *Administrar → Ver archivos locales*.

1. Verifica la versión: clic derecho en el `.exe` → *Propiedades → Detalles*.
   Debe coincidir con las versiones soportadas por ScriptHookV.
2. **ScriptHookV:** de la carpeta `bin` del zip copia a la raíz
   `ScriptHookV.dll` y `dinput8.dll`. No copies `NativeTrainer.asi`, que también usa F4.
3. **SHVDN Enhanced:** copia a la raíz **todos los archivos sueltos de la raíz del zip**:
   `ScriptHookVDotNet.asi`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll`,
   `ScriptHookVDotNet.ini` **y `MinHook.x64.dll`**. Sin MinHook, SHVDNE falla al
   inicializar la memoria nativa y el juego se cierra al abrir la consola (F4).
   Actualízalos siempre juntos.
4. Crea la carpeta **`scripts`** (en plural y minúsculas) junto al `.exe`.
5. Desbloquea los archivos descargados de internet. En PowerShell, dentro de la raíz:
```powershell
   Get-ChildItem -Recurse | Unblock-File
```
6. Si el launcher de Rockstar ofrece desactivar BattlEye, desactívalo.

Comprobación: en Modo Historia, **F4** abre la consola de SHVDN.

## 2. Toolchain de desarrollo

- **Visual Studio Community 2026** con la carga de trabajo **"Desarrollo de escritorio de .NET"**
  (incluye las herramientas de .NET Framework 4.8).
- Alternativa: .NET SDK + VS Code. El `.csproj` incluye
  `Microsoft.NETFramework.ReferenceAssemblies`, así que `dotnet build` compila `net48`
  sin targeting pack (útil en CI).
- La solución es `StreamTok.GtaV.slnx` (formato nuevo de VS 2026).

## 3. Compilar e iterar

```powershell
# Opcional: copia automática del DLL a <GTA>\scripts en cada build
setx GTA_DIR "D:\SteamLibrary\steamapps\common\Grand Theft Auto V"
# (reinicia Visual Studio o la terminal después de setx)

dotnet build -c Release
```

Sin `GTA_DIR`, copia a mano `bin\Release\net48\StreamTok.GtaV.dll` a `<GTA>\scripts\`.

**Iterar sin reiniciar el juego:** compila, copia el DLL y pulsa **Insert** en el juego
(`ReloadKey` por defecto; en laptops puede ser Fn + Insert). SHVDN descarga y vuelve a cargar
todos los scripts.

## 4. Definición de "hecho" de la Fase 1

- [x] F4 abre la consola de SHVDN sin cerrar el juego (Legacy)
- [x] Al cargar Modo Historia aparece **"StreamTok v0.1.0 cargado OK"** (Legacy)
- [x] `scripts\StreamTok.GtaV.log` registra "Constructor ejecutado" (Legacy)
- [x] Insert recarga el script: el log muestra "Aborted" y luego "Constructor" (Legacy)
- [ ] Mismas pruebas en **Enhanced**

Nota: al cerrar el juego no siempre se registra un "Aborted" final, porque el proceso
termina de golpe. No hay que depender de ese evento para limpieza crítica.

## Si no carga

Revisa estos logs en la raíz del juego: `asiloader.log`, `ScriptHookV.log` y
`ScriptHookVDotNet.log`.

| Síntoma | Causa probable |
|---|---|
| Al abrir el juego sale el aviso "Critical error… unknown game version" | ScriptHookV no soporta todavía tu build. Pasa tras cada update de Rockstar; espera la versión nueva |
| No existe `asiloader.log` | Falta `dinput8.dll` en la raíz |
| El juego se cierra al pulsar F4 / `DllNotFoundException: 'MinHook.x64.dll'` | Falta `MinHook.x64.dll` de SHVDNE en la raíz |
| `ScriptHookVDotNet.log` dice "directory is missing" | La carpeta debe llamarse exactamente `scripts` y estar junto al `.exe` |
| F4 no hace nada | SHVDN no cargó: falta el VC++ Redist, hay archivos bloqueados (`Unblock-File`) o mezcla de versiones de SHVDN |
| La consola funciona pero el script no aparece | El DLL no está en `scripts\`, o no se compiló como x64 |
