using System.Collections.Generic;
using StreamTok.GtaV.Modes;

namespace StreamTok.GtaV.Actions
{
    /// <summary>Modo Parkour (ver Modes/ParkourMode.cs).</summary>
    internal static class ParkourActions
    {
        public static IEnumerable<ActionDef> All(string[] courses)
        {
            yield return new ActionDef("parkour_start", "Parkour: iniciar", false,
                new[]
                {
                    ParamDef.Int("seed", 0, 0, 99999),                                  // 0 = torre nueva al azar
                    ParamDef.Int("height", 300, 50, 1000, 100, 200, 300, 500, 800),     // metros hasta la meta
                    ParamDef.Enum("place", "sandy_shores", ParkourMode.Places),
                    ParamDef.Enum("course", "random", courses),                         // random o un mapa de Menyoo
                },
                ctx => ctx.Parkour.Start(ctx.Int("seed"), ctx.Int("height"), ctx.Enum("place"), ctx.Enum("course")));

            yield return new ActionDef("parkour_stop", "Parkour: terminar", false, null,
                ctx => ctx.Parkour.Stop());

            yield return new ActionDef("parkour_wind", "Parkour: viento", false,
                new[] { ParamDef.Int("strength", 8, 3, 25) },
                ctx => ctx.Parkour.Wind(ctx.Int("strength")));

            yield return new ActionDef("parkour_ragdoll", "Parkour: tropezón", false, null,
                ctx => ctx.Parkour.Ragdoll());

            yield return new ActionDef("parkour_remove_floor", "Parkour: quitar el piso", false,
                new[] { ParamDef.Int("seconds", 5, 2, 30) },
                ctx => ctx.Parkour.RemoveFloor(ctx.Int("seconds")));

            yield return new ActionDef("parkour_super_jump", "Parkour: súper salto", false,
                new[] { ParamDef.Int("seconds", 10, 3, 60) },
                ctx => ctx.Parkour.SuperJump(ctx.Int("seconds")));

            yield return new ActionDef("parkour_highest", "Parkour: volver a lo más alto", false, null,
                ctx => ctx.Parkour.ToHighest());

            yield return new ActionDef("parkour_back_to_start", "Parkour: volver al inicio", false, null,
                ctx => ctx.Parkour.BackToStart());

            yield return new ActionDef("parkour_set_place", "Marcar parkour aquí", false, null,
                ctx => ctx.Parkour.SetPlaceHere());
        }
    }
}
