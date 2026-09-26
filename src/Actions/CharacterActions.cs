using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Native;
using StreamTok.GtaV.Characters;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Actions
{
    /// <summary>
    /// Personajes custom definidos en scripts\StreamTok.Characters.json. La lista de personajes
    /// sale del archivo, así que el catálogo (mod-hello) muestra también los que agregue el streamer.
    /// </summary>
    internal static class CharacterActions
    {
        public static IEnumerable<ActionDef> All(IReadOnlyList<CharacterDef> characters)
        {
            if (characters.Count == 0)
            {
                yield break; // sin personajes válidos no se publica la acción
            }

            string[] ids = characters.Select(c => c.Id).ToArray();
            string[] options = ids.Length > 1 ? new[] { "random" }.Concat(ids).ToArray() : ids;

            yield return new ActionDef("spawn_character", "Personaje", true,
                new[]
                {
                    ParamDef.Enum("character", ids[0], options),
                    ParamDef.Enum("side", "enemy", "enemy", "ally"),
                    ParamDef.Int("count", 1, 1, 5),
                },
                ctx => Spawn(ctx, characters, ctx.Enum("character")));

            // Además, UNA ACCIÓN POR PERSONAJE ("Goku", "John Wick"…): así cada uno aparece en la
            // lista como cualquier otra acción y se puede enlazar directo a una donación.
            foreach (CharacterDef def in characters)
            {
                string id = def.Id;
                yield return new ActionDef("character_" + id, def.Name, true,
                    new[]
                    {
                        ParamDef.Enum("side", "enemy", "enemy", "ally"),
                        ParamDef.Int("count", 1, 1, 5),
                    },
                    ctx => Spawn(ctx, characters, id))
                {
                    Category = ActionMeta.Character,
                    Icon = "character",
                    Description = Describe(def),
                    Image = def.Image,
                };
            }
        }

        /// <summary>Descripción automática a partir de sus habilidades, ej. "Súper fuerza · Aura · Ki".</summary>
        private static string Describe(CharacterDef def)
        {
            var names = new Dictionary<string, string>
            {
                ["super_strength"] = "Súper fuerza",
                ["tank"] = "Tanque",
                ["gunslinger"] = "Pistolero",
                ["aura"] = "Aura",
                ["energy_blast"] = "Ki y Kamehameha",
                ["flight"] = "Vuela",
                ["dodge"] = "Esquiva",
                ["speed"] = "Súper velocidad",
            };
            var parts = def.Abilities.Where(names.ContainsKey).Select(a => names[a]).ToList();
            return parts.Count > 0 ? string.Join(" · ", parts) : "Personaje especial";
        }

        private static void Spawn(ActionContext ctx, IReadOnlyList<CharacterDef> characters, string characterId)
        {
            int count = ctx.Tracker.ClampPeds(ctx.Int("count"));
            bool hostile = ctx.Enum("side") == "enemy";
            Ped player = GTA.Game.Player.Character;
            int group = Function.Call<int>(Hash.GET_PED_GROUP_INDEX, player);

            for (int i = 0; i < count; i++)
            {
                CharacterDef def = characterId == "random" ? ctx.Pick(characters.ToList()) : characters.First(c => c.Id == characterId);

                Ped ped = Spawner.SpawnPed(def.Model, Spawner.NearPlayer(ctx.Rng, hostile ? 12f : 3f, hostile ? 20f : 6f));

                if (GameData.Weapons.TryGetValue(def.Weapon, out WeaponHash weapon))
                {
                    ped.Weapons.Give(weapon, 9999, true, true);
                }

                Blip blip = ped.AddBlip();
                blip.Scale = 0.8f;

                if (hostile)
                {
                    ped.RelationshipGroup = ctx.Tracker.HostileGroup;
                    ped.Task.FightAgainst(player);
                    ped.AlwaysKeepTask = true;
                    blip.Color = BlipColor.Red;
                }
                else
                {
                    ped.RelationshipGroup = player.RelationshipGroup;
                    Function.Call(Hash.SET_PED_AS_GROUP_MEMBER, ped, group);
                    Function.Call(Hash.SET_PED_NEVER_LEAVES_GROUP, ped, true);
                    blip.Color = BlipColor.Blue;
                }

                ctx.Characters.Setup(ped, def, hostile, ctx.NameTag);
                ctx.Tracker.Track(ped, ctx.NameTag, hostile ? EntityTracker.KindAttacker : EntityTracker.KindCompanion);
            }
        }
    }
}
