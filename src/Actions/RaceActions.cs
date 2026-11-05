using System.Collections.Generic;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Modo Carrera (ver Modes/RaceMode.cs). El viewer se identifica por el nameTag; StreamTok
    /// decide qué regalo o comando dispara cada acción.
    ///   race_join   inscribirse (una sola vez, con el lobby abierto)
    ///   race_boost  turbo (solo inscritos, carrera en marcha)
    ///   race_rose   la rosa: solo acelera (para entrar está race_join)
    ///   race_missile / race_meteor / race_shield / race_jet / race_portal / race_win  poderes de viewers
    /// </summary>
    internal static class RaceActions
    {
        public static IEnumerable<ActionDef> All(string[] trackNames, bool allowRecording)
        {
            yield return new ActionDef("race_open", "Carrera: iniciar", false,
                new[]
                {
                    ParamDef.Int("laps", 3, 1, 20, 1, 2, 3, 5, 10),
                    ParamDef.Enum("track", trackNames[0], trackNames), // pistas instaladas (carpeta StreamTok.Race\\tracks)
                },
                ctx => ctx.Race.Open(ctx.Int("laps"), ctx.Enum("track")));

            yield return new ActionDef("race_start", "Carrera: empezar ya", false, null,
                ctx => ctx.Race.StartNow());

            yield return new ActionDef("race_stop", "Carrera: terminar", false, null,
                ctx => ctx.Race.Stop());

            yield return new ActionDef("race_join", "Carrera: inscribirse", true, null,
                ctx => ctx.Race.Join(ctx.NameTag));

            yield return new ActionDef("race_boost", "Carrera: turbo", true,
                new[] { ParamDef.Int("stacks", 1, 1, 10, 1, 2, 3, 5, 10) },
                ctx => ctx.Race.Boost(ctx.NameTag, ctx.Int("stacks")));

            yield return new ActionDef("race_rose", "Carrera: acelerar", true, null,
                ctx => ctx.Race.Accelerate(ctx.NameTag));

            // Poderes de los viewers (solo inscritos, con la carrera en marcha).
            yield return new ActionDef("race_missile", "Carrera: misil al de adelante", true,
                new[] { ParamDef.Int("seconds", 4, 1, 15, 2, 3, 4, 6, 10) },
                ctx => ctx.Race.Missile(ctx.NameTag, ctx.Int("seconds")));

            yield return new ActionDef("race_meteor", "Carrera: meteorito al líder", true,
                new[] { ParamDef.Int("seconds", 4, 1, 15, 2, 3, 4, 6, 10) },
                ctx => ctx.Race.Meteor(ctx.NameTag, ctx.Int("seconds")));

            yield return new ActionDef("race_shield", "Carrera: escudo (bloquea 1 golpe)", true, null,
                ctx => ctx.Race.Shield(ctx.NameTag));

            yield return new ActionDef("race_jet", "Carrera: jet (salta adelante)", true,
                new[] { ParamDef.Int("percent", 15, 1, 60, 5, 10, 15, 20, 30) },
                ctx => ctx.Race.JumpAhead(ctx.NameTag, ctx.Int("percent"), "JET"));

            yield return new ActionDef("race_portal", "Carrera: portal (teletransporte adelante)", true,
                new[] { ParamDef.Int("percent", 25, 1, 60, 10, 15, 25, 35, 50) },
                ctx => ctx.Race.JumpAhead(ctx.NameTag, ctx.Int("percent"), "PORTAL"));

            yield return new ActionDef("race_win", "Carrera: WIN (gana al instante)", true, null,
                ctx => ctx.Race.Win(ctx.NameTag));

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
