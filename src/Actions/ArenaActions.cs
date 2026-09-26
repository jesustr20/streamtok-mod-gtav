using System.Collections.Generic;
using StreamTok.GtaV.Modes;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Modo "Pelea de viewers" (ver Modes/ArenaMode.cs). El viewer se identifica por el nameTag:
    /// StreamTok decide qué comando o donación dispara cada acción y con qué puntos.
    /// </summary>
    internal static class ArenaActions
    {
        public static IEnumerable<ActionDef> All(string[] characterIds)
        {
            yield return new ActionDef("arena_start", "Pelea: iniciar", false,
                new[]
                {
                    ParamDef.Int("minutes", 0, 0, 60, 0, 3, 5, 10, 15, 20, 30), // 0 = sin límite: gana el último en pie
                    ParamDef.Bool("repeat", true),                             // al haber ganador: otra ronda (la arena sigue hasta "terminar")
                    ParamDef.Enum("place", "airport", ArenaMode.Places),     // airport, sandy_shores, marked, here
                    ParamDef.Int("radius", 30, 12, 80, 15, 20, 30, 40, 60),  // tamaño de la arena (metros)
                    ParamDef.Bool("player_fights", true),                      // el jugador también pelea
                },
                ctx => ctx.Arena.Start(ctx.Int("minutes"), ctx.Bool("repeat"), ctx.Enum("place"), ctx.Int("radius"), ctx.Bool("player_fights")));

            yield return new ActionDef("arena_player", "Yo peleo", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Arena.SetPlayerFighting(ctx.Bool("enabled")));

            yield return new ActionDef("arena_set_place", "Marcar arena aquí", false, null,
                ctx => ctx.Arena.SetPlaceHere());

            yield return new ActionDef("arena_stop", "Pelea: terminar", false, null,
                ctx => ctx.Arena.Stop());

            yield return new ActionDef("arena_join", "Unirse a la pelea", true,
                new[]
                {
                    ParamDef.Enum("character", "random", characterIds),
                    ParamDef.Int("coins", 0, 0, 100000, 0, 1, 10, 100, 500, 1000), // si entra donando: vida extra
                },
                ctx => ctx.Arena.Join(ctx.NameTag, ctx.Enum("character"), ctx.Int("coins")));

            yield return new ActionDef("arena_boost", "Más vida (donación)", true,
                new[] { ParamDef.Int("coins", 10, 1, 100000, 1, 10, 100, 500, 1000) },
                ctx => ctx.Arena.Boost(ctx.NameTag, ctx.Int("coins")));

            yield return new ActionDef("arena_weapon", "Arma para el luchador", true,
                new[] { ParamDef.Enum("weapon", "random", ArenaMode.WeaponChoices) },
                ctx => ctx.Arena.GiveWeapon(ctx.NameTag, ctx.Enum("weapon")));

            yield return new ActionDef("arena_power", "Poder temporal", true,
                new[]
                {
                    ParamDef.Enum("power", "random", ArenaMode.Powers),        // random, ki, fly, strength, speed, dodge
                    ParamDef.Int("seconds", 20, 5, 120, 10, 20, 30, 60),
                },
                ctx => ctx.Arena.GivePower(ctx.NameTag, ctx.Enum("power"), ctx.Int("seconds")));

            yield return new ActionDef("arena_bots", "Pelea: agregar bots", false,
                new[] { ParamDef.Int("count", 3, 1, 10) },
                ctx => ctx.Arena.AddBots(ctx.Int("count")));
        }
    }
}
