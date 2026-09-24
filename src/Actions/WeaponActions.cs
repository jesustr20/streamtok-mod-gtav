using System;
using System.Collections.Generic;
using System.Linq;
using GTA;

namespace StreamTok.GtaV.Actions
{
    internal static class WeaponActions
    {
        public static IEnumerable<ActionDef> All()
        {
            string[] options = GameData.Weapons.Keys.Concat(new[] { "random", "all" }).ToArray();

            yield return new ActionDef("give_weapon", "Dar arma", false,
                new[] { ParamDef.Enum("weapon", "pistol", options) },
                GiveWeapon);

            yield return new ActionDef("remove_weapons", "Quitar armas", false, null,
                ctx => GTA.Game.Player.Character.Weapons.RemoveAll());

            yield return new ActionDef("max_ammo", "Munición máxima", false, null,
                ctx =>
                {
                    Ped p = GTA.Game.Player.Character;
                    foreach (WeaponHash hash in Enum.GetValues(typeof(WeaponHash)).Cast<WeaponHash>().Distinct())
                    {
                        if (hash == WeaponHash.Unarmed || !p.Weapons.HasWeapon(hash))
                        {
                            continue;
                        }
                        Weapon w = p.Weapons[hash];
                        w.Ammo = w.MaxAmmo;
                    }
                });
        }

        private static void GiveWeapon(ActionContext ctx)
        {
            Ped player = GTA.Game.Player.Character;
            string weapon = ctx.Enum("weapon");

            IEnumerable<string> toGive;
            if (weapon == "all") toGive = GameData.Weapons.Keys;
            else if (weapon == "random") toGive = new[] { ctx.Pick(GameData.Weapons.Keys.ToList()) };
            else toGive = new[] { weapon };

            foreach (string w in toGive)
            {
                player.Weapons.Give(GameData.Weapons[w], w == "rpg" ? 20 : 250, true, true);
            }
        }
    }
}
