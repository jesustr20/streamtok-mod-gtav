# StreamTok · GTA V — Guía para streamers

Con este mod, las donaciones y comandos de tu live hacen cosas dentro de GTA V: aparecen atacantes,
tornados, autos, personajes… Todo se configura en la app **StreamTok**; en el juego no tienes que tocar nada.

> Solo funciona en **Modo Historia** de GTA V (PC, versión Legacy). No funciona en GTA Online.

## 1. Lo que necesitas

| Qué | Dónde | Para qué |
|---|---|---|
| GTA V Legacy (Steam, Epic o Rockstar) | Tu tienda | El juego |
| **ScriptHookV** | dev-c.com (botón *Download*) | Permite mods de scripts |
| **ScriptHookVDotNet Enhanced** | gta5-mods.com | Carga mods hechos en C# |
| **StreamTok.GtaV.dll** | Release de StreamTok | Este mod |
| App **StreamTok** | — | Conecta tu live con el juego |

Descarga solo de esos sitios. Si Windows o el antivirus avisa de algo raro, no lo abras.

## 2. Instalación (una sola vez)

1. Abre la carpeta del juego (donde está `GTA5.exe`). En Steam: clic derecho en GTA V →
   *Administrar → Ver archivos locales*.
2. **ScriptHookV:** del zip, carpeta `bin`, copia todo a la carpeta del juego **menos** `NativeTrainer.asi`.
3. **ScriptHookVDotNet:** copia todos los archivos sueltos del zip a la carpeta del juego.
4. Crea una carpeta llamada **`scripts`** junto a `GTA5.exe` y pon ahí `StreamTok.GtaV.dll`.
5. Abre el juego en **Modo Historia**. Arriba a la izquierda debe aparecer
   **"StreamTok vX.Y.Z cargado OK"**.

## 3. Usarlo en el live

1. Abre la app **StreamTok** y conéctala a tu live.
2. Abre GTA V. Cuando el mod se conecta aparece **"StreamTok: conectado a la app"**.
3. En StreamTok eliges qué regalo o comando dispara cada acción (ej. "Rosa → 3 atacantes").
   Las acciones del juego aparecen solas en la app, con su imagen y descripción.

**Modos especiales** (se activan desde la app): *Monte Chiliad* (reto de subir a la cima),
*Pelea de viewers* (los viewers pelean con sus personajes) y *Parkour* (torre para subir).

## 4. Si algo no funciona

| Pasa esto | Prueba esto |
|---|---|
| No aparece "StreamTok cargado OK" | Revisa que la carpeta se llame exactamente `scripts` y que el DLL esté adentro |
| El juego no abre tras una actualización de GTA | Espera la versión nueva de ScriptHookV (sale unos días después) |
| Dice "desconectado, reintentando" | Abre la app StreamTok; el mod se reconecta solo |
| Una acción no hace nada | Mira el log: `%LOCALAPPDATA%\StreamTok\logs\StreamTok.GtaV.log` (pega esa ruta en el Explorador) |

Para soporte, comparte ese archivo de log.
