# Personajes custom · StreamTok Mod GTA V

Un personaje es **modelo + habilidades**. Se definen en `scripts\StreamTok.Characters.json`
(el mod lo crea con ejemplos la primera vez). Tras editarlo, pulsa **Insert** en el juego.

## Formato

```json
{
  "characters": [
    {
      "id": "guerrero",
      "name": "Guerrero de aura",
      "model": "u_m_y_imporage",
      "health": 3000,
      "weapon": "none",
      "abilities": ["super_strength", "aura"],
      "auraColor": "gold"
    }
  ]
}
```

| Campo | Qué es |
|---|---|
| `id` | Identificador único, sin espacios. Es el valor que usa StreamTok |
| `name` | Nombre visible (barra de jefe, menú) |
| `model` | **Nombre interno** del modelo: de GTA o de un add-on ped instalado |
| `health` | Vida (100 a 100000) |
| `weapon` | `none`, `pistol`, `smg`, `rifle`, `mg`, `sniper`, `rpg`, `bat`, `knife` |
| `abilities` | Lista combinable (ver abajo) |
| `auraColor` | `gold`, `blue`, `red`, `green`, `purple`, `white` o `#RRGGBB` |
| `energyColor` | Color del ki y del Kamehameha (opcional; si falta, usa `auraColor`) |
| `image` | Foto para el panel de StreamTok: ruta o URL (opcional) |

## Habilidades

| Habilidad | Qué hace | Versión |
|---|---|---|
| `super_strength` | No se cae; inmune a fuego y explosiones; cada golpe al jugador lo lanza lejos | ✅ v0.7 |
| `tank` | Blindaje, no se cae, inmune a explosiones, camina lento, **barra de vida de jefe** | ✅ v0.7 |
| `gunslinger` | Puntería casi perfecta, dispara rápido, no recarga, avanza agresivo | ✅ v0.7 |
| `aura` | Brillo de color alrededor del cuerpo | ✅ v0.7 |
| `energy_blast` | Ráfagas de ki (bolas que siguen al objetivo) y **Kamehameha**: carga 2 s con una esfera creciendo en las manos y dispara un rayo que explota donde choca | ✅ v0.8 |
| `flight` | Cada tanto se eleva, vuela en círculos sobre el objetivo lanzando ki y baja a pelear | ✅ v0.8 |
| `dodge` | Al recibir daño, a veces lo esquiva: aparece de golpe unos metros al costado, dejando una imagen residual (Ultra Instinto) | ✅ v0.8 |
| `speed` | Súper velocidad hacia el objetivo, con estela de su color | ✅ v0.8 |

## Acciones

Cada personaje del JSON genera **su propia acción** `character_<id>` (por ejemplo `character_goku`,
con el nombre visible del personaje), para enlazarla directo a una donación. Además existe:

### `spawn_character`

- `character`: el `id` del personaje (o `random`).
- `side`: `enemy` (te ataca) o `ally` (te sigue y te defiende).
- `count`: 1 a 5. Llevan el nombre del viewer encima, como todo spawn, y debajo una **barra de vida**
  (verde, amarilla, roja). Los `tank` enemigos muestran además la **barra grande de jefe** arriba de la pantalla.

## Usar personajes propios (add-on peds)

1. Descarga el personaje (mejor **add-on** que **replace**) e instálalo siguiendo su README
   (en Legacy: OpenIV con la carpeta `mods`; muchos piden un mod base de add-on peds).
2. Busca su **nombre interno**: en el README, en el nombre de sus archivos
   (`nombre.yft` / `nombre.ydd`) o en `<Name>` dentro de `peds.meta`.
3. Agrégalo al JSON con ese nombre en `model` y pulsa **Insert**.
4. Si no aparece en el menú F7, revisa `%LOCALAPPDATA%\StreamTok\logs\StreamTok.GtaV.log`: dice si el modelo no se
   encontró, si el JSON tiene un error o si alguna habilidad no existe.

Los modelos de terceros los instala cada streamer en su PC; StreamTok no los incluye ni distribuye.
