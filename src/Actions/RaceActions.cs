using System.Collections.Generic;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Modo Carrera (ver Modes/RaceMode.cs). El viewer se identifica por el nameTag; StreamTok
    /// decide qué regalo o comando dispara cada acción.
    ///   race_join   inscribirse (una sola vez, con el lobby abierto)
    ///   race_boost  turbo (solo inscritos, carrera en marcha)
    ///   race_rose   la rosa: inscribe si aún no está, acelera si ya está
    /// </summary>
    internal static class RaceActions
    {
        public static IEnumerable<ActionDef> All(string[] trackNames, bool allowRecording)
        {
            yield return new ActionDef("race_open", "Carrera: iniciar", false,
                new[]
                {
                    ParamDef.Int("laps", 3, 1, 20, 1, 2, 3, 5, 10),
                    ParamDef.Int("lobby_seconds", 30, 5, 300, 10, 20, 30, 60, 120),
                    ParamDef.Enum("track", trackNames[0], trackNames), // pistas instaladas (carpeta StreamTok.Race\\tracks)
                },
                ctx => ctx.Race.Open(ctx.Int("lobby_seconds"), ctx.Int("laps"), ctx.Enum("track")));

            yield return new ActionDef("race_start", "Carrera: empezar ya", false, null,
                ctx => ctx.Race.StartNow());

            yield return new ActionDef("race_stop", "Carrera: terminar", false, null,
                ctx => ctx.Race.Stop());

            yield return new ActionDef("race_join", "Carrera: inscribirse", true, null,
                ctx => ctx.Race.Join(ctx.NameTag));

            yield return new ActionDef("race_boost", "Carrera: turbo", true,
                new[] { ParamDef.Int("stacks", 1, 1, 10, 1, 2, 3, 5, 10) },
                ctx => ctx.Race.Boost(ctx.NameTag, ctx.Int("stacks")));

            yield return new ActionDef("race_rose", "Carrera: entrar o acelerar", true, null,
                ctx => ctx.Race.Rose(ctx.NameTag));

            yield return new ActionDef("race_bots", "Carrera: agregar bots", false,
                new[] { ParamDef.Int("count", 5, 1, ParamDef.NoLimit, 1, 3, 5, 10, 20) },
                ctx => ctx.Race.AddBots(ctx.Int("count")));

            yield return new ActionDef("race_camera", "Carrera: cámara automática", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Race.SetCamera(ctx.Bool("enabled")));

            yield return new ActionDef("race_player", "Carrera: aparecer yo", false,
                new[] { ParamDef.Bool("enabled", false) },
                ctx => ctx.Race.SetPlayerRaces(ctx.Bool("enabled")));

            // Herramientas del creador: grabar y ver pistas. Ocultas por defecto para el usuario final
            // (ini: [Race] AllowRecording=true).
            if (!allowRecording)
            {
                yield break;
            }

            yield return new ActionDef("race_record_start", "Carrera: grabar pista", false, null,
                ctx => ctx.Race.RecordStart());

            yield return new ActionDef("race_record_stop", "Carrera: terminar de grabar", false, null,
                ctx => ctx.Race.RecordStop());

            yield return new ActionDef("race_import_start", "Carrera: importar pistas (IA conduce)", false, null,
                ctx => ctx.Race.ImportStart());

            yield return new ActionDef("race_import_stop", "Carrera: cancelar importación", false, null,
                ctx => ctx.Race.ImportStop());

            yield return new ActionDef("race_show_track", "Carrera: mostrar pista", false,
                new[] { ParamDef.Bool("enabled", true) },
                ctx => ctx.Race.SetShowTrack(ctx.Bool("enabled")));
        }
    }
}
