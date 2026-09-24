using System;
using System.Collections.Generic;
using GTA;
using GTA.Native;

namespace StreamTok.GtaV.Actions
{
    internal static class WorldActions
    {
        public static IEnumerable<ActionDef> All()
        {
            yield return new ActionDef("wanted_level", "Nivel de búsqueda", false,
                new[]
                {
                    ParamDef.Enum("mode", "add", "add", "remove", "max", "clear"),
                    ParamDef.Int("stars", 1, 1, 5),
                },
                ctx =>
                {
                    Player player = GTA.Game.Player;
                    int current = player.WantedLevel;
                    int stars = ctx.Int("stars");
                    switch (ctx.Enum("mode"))
                    {
                        case "add": player.WantedLevel = Math.Min(5, current + stars); break;
                        case "remove": player.WantedLevel = Math.Max(0, current - stars); break;
                        case "max": player.WantedLevel = 5; break;
                        case "clear": player.WantedLevel = 0; break;
                    }
                });

            yield return new ActionDef("money", "Dinero", false,
                new[]
                {
                    ParamDef.Enum("mode", "add", "add", "set"),
                    ParamDef.Int("amount", 1000, 1, 10000000),
                },
                ctx =>
                {
                    Player player = GTA.Game.Player;
                    long amount = ctx.Int("amount");
                    long next = ctx.Enum("mode") == "add" ? player.Money + amount : amount;
                    player.Money = (int)Math.Min(next, 2000000000L);
                });

            yield return new ActionDef("set_weather", "Clima", false,
                new[] { ParamDef.Enum("weather", "rain", GameData.Keys(GameData.Weather)) },
                ctx => Function.Call(Hash.SET_WEATHER_TYPE_NOW_PERSIST, GameData.Weather[ctx.Enum("weather")]));

            yield return new ActionDef("set_time", "Hora del día", false,
                new[] { ParamDef.Int("hour", 12, 0, 23) },
                ctx => Function.Call(Hash.SET_CLOCK_TIME, ctx.Int("hour"), 0, 0));
        }
    }
}
