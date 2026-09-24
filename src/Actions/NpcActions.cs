using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using StreamTok.GtaV.Entities;

namespace StreamTok.GtaV.Actions
{
    internal static class NpcActions
    {
        public static IEnumerable<ActionDef> All()
        {
            string[] animals = new[] { "random" }.Concat(GameData.Animals.Keys).ToArray();

            yield return new ActionDef("spawn_animal", "Spawn de animal", true,
                new[]
                {
                    ParamDef.Enum("animal", "random", animals),
                    ParamDef.Int("count", 1, 1, 20),
                    ParamDef.Bool("hostile", false),
                },
                SpawnAnimal);

            yield return new ActionDef("spawn_attackers", "Spawn de atacantes", true,
                new[]
                {
                    ParamDef.Int("count", 3, 1, 50),
                    ParamDef.Enum("weapon", "pistol", "pistol", "smg", "rifle", "mg", "rpg", "bat", "knife", "none", "random"),
                    ParamDef.Enum("model", "normal", "normal", "random", "chimp", "alien"),
                },
                SpawnAttackers);

            yield return new ActionDef("spawn_bikers", "Motorizados", true,
                new[]
                {
                    ParamDef.Int("count", 2, 1, 10),
                    ParamDef.Enum("faction", "bandits", "bandits", "police"),
                },
                SpawnBikers);

            yield return new ActionDef("attackers_remove", "Remover atacantes", false, null,
                ctx => ctx.Tracker.RemoveKind(EntityTracker.KindAttacker));
        }

        private static void SpawnAnimal(ActionContext ctx)
        {
            string key = ctx.Enum("animal");
            int count = ctx.Tracker.ClampPeds(ctx.Int("count"));
            bool hostile = ctx.Bool("hostile");

            for (int i = 0; i < count; i++)
            {
                // "random" elige un animal distinto para cada spawn.
                string model = key == "random" ? ctx.Pick(GameData.Animals.Values.ToList()) : GameData.Animals[key];

                Ped ped = Spawner.SpawnPed(model, Spawner.NearPlayer(ctx.Rng, 4f, 8f));
                if (hostile)
                {
                    ped.RelationshipGroup = ctx.Tracker.HostileGroup;
                    ped.Task.FightAgainst(GTA.Game.Player.Character);
                    ped.AlwaysKeepTask = true;
                }
                else
                {
                    ped.Task.WanderAround();
                }

                ctx.Tracker.Track(ped, ctx.NameTag, EntityTracker.KindAnimal);
            }
        }

        /// <summary>
        /// Motos con su conductor armado que persiguen y disparan al jugador.
        /// El nombre va sobre el conductor; la moto se borra junto con los atacantes.
        /// </summary>
        private static void SpawnBikers(ActionContext ctx)
        {
            int count = ctx.Tracker.ClampPeds(ctx.Int("count"));
            count = ctx.Tracker.ClampVehicles(count);
            bool police = ctx.Enum("faction") == "police";
            Ped player = GTA.Game.Player.Character;

            for (int i = 0; i < count; i++)
            {
                string bikeModel = police ? "policeb" : ctx.Pick(GameData.BanditBikes);
                string riderModel = police ? "s_m_y_cop_01" : ctx.Pick(GameData.NormalAttackers);

                Vector3 position = Spawner.NearPlayer(ctx.Rng, 35f, 50f);
                Vehicle bike = Spawner.SpawnVehicle(bikeModel, position, Spawner.HeadingToPlayer(position), placeOnGround: true);
                ctx.Tracker.Track(bike, null, EntityTracker.KindAttacker);

                Spawner.Preload(riderModel);
                Ped rider = bike.CreatePedOnSeat(VehicleSeat.Driver, new Model(riderModel));
                if (rider == null)
                {
                    throw new ActionException($"El juego no pudo crear el conductor '{riderModel}'");
                }

                rider.RelationshipGroup = ctx.Tracker.HostileGroup;
                rider.Weapons.Give(police ? WeaponHash.Pistol : WeaponHash.MicroSMG, 9999, true, true);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, rider, 2, true);  // disparar desde el vehículo
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, rider, 52, true); // atacar usando el vehículo
                rider.Task.FightAgainst(player);
                rider.AlwaysKeepTask = true;

                Blip blip = rider.AddBlip();
                blip.Color = police ? BlipColor.Blue : BlipColor.Red;
                blip.Scale = 0.7f;

                ctx.Tracker.Track(rider, ctx.NameTag, EntityTracker.KindAttacker);
            }
        }

        private static void SpawnAttackers(ActionContext ctx)
        {
            int count = ctx.Tracker.ClampPeds(ctx.Int("count"));
            string weapon = ctx.Enum("weapon");
            string model = ctx.Enum("model");
            Ped player = GTA.Game.Player.Character;
            List<string> attackerWeapons = GameData.Weapons.Keys.Where(k => k != "sniper").ToList();

            for (int i = 0; i < count; i++)
            {
                string modelName;
                switch (model)
                {
                    case "chimp": modelName = "a_c_chimp"; break;
                    case "alien": modelName = "s_m_m_movalien_01"; break;
                    case "random": modelName = ctx.Pick(GameData.RandomAttackers); break;
                    default: modelName = ctx.Pick(GameData.NormalAttackers); break;
                }

                Ped ped = Spawner.SpawnPed(modelName, Spawner.NearPlayer(ctx.Rng, 12f, 25f));
                ped.RelationshipGroup = ctx.Tracker.HostileGroup;

                // Los animales (el mono) no pueden usar armas: pelean cuerpo a cuerpo.
                if (weapon != "none" && modelName != "a_c_chimp")
                {
                    string w = weapon == "random" ? ctx.Pick(attackerWeapons) : weapon;
                    ped.Weapons.Give(GameData.Weapons[w], 9999, true, true);
                }

                ped.Task.FightAgainst(player);
                ped.AlwaysKeepTask = true;

                Blip blip = ped.AddBlip();
                blip.Color = BlipColor.Red;
                blip.Scale = 0.7f;

                ctx.Tracker.Track(ped, ctx.NameTag, EntityTracker.KindAttacker);
            }
        }
    }
}
