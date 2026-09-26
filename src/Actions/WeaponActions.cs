using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using StreamTok.GtaV.Entities;

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

            yield return new ActionDef("weapon_random", "Armas aleatorias", false,
                new[] { ParamDef.Int("count", 1, 1, 10) },
                RandomWeapons);

            yield return new ActionDef("remove_weapons", "Quitar armas", false, null,
                ctx => GTA.Game.Player.Character.Weapons.RemoveAll());

            yield return new ActionDef("max_ammo", "Munición máxima", false, null,
                ctx =>
                {
                    PlayerTransform.RequireHuman();
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

        /// <summary>
        /// Rueda de armas completa del modo historia. Por nombre para que un arma que no exista
        /// en esta versión de SHVDN simplemente se ignore (en vez de romper la compilación).
        /// </summary>
        private static readonly string[] WheelNames =
        {
            // cuerpo a cuerpo
            "Knife", "Nightstick", "Hammer", "Bat", "Crowbar", "GolfClub", "Bottle", "Dagger", "Hatchet",
            "KnuckleDuster", "Machete", "Flashlight", "SwitchBlade", "PoolCue", "Wrench", "BattleAxe",
            // pistolas
            "Pistol", "CombatPistol", "Pistol50", "SNSPistol", "HeavyPistol", "VintagePistol", "MarksmanPistol",
            "Revolver", "APPistol", "StunGun", "FlareGun",
            // subfusiles y ametralladoras
            "MicroSMG", "MachinePistol", "MiniSMG", "SMG", "AssaultSMG", "CombatPDW", "MG", "CombatMG", "Gusenberg",
            // rifles
            "AssaultRifle", "CarbineRifle", "AdvancedRifle", "SpecialCarbine", "BullpupRifle", "CompactRifle",
            // escopetas
            "PumpShotgun", "SawnOffShotgun", "BullpupShotgun", "AssaultShotgun", "Musket", "HeavyShotgun",
            "DoubleBarrelShotgun", "SweeperShotgun",
            // francotiradores
            "SniperRifle", "HeavySniper", "MarksmanRifle",
            // pesadas
            "GrenadeLauncher", "RPG", "Minigun", "Firework", "Railgun", "HomingLauncher", "CompactGrenadeLauncher",
            // arrojadizas y otras
            "Grenade", "StickyBomb", "ProximityMine", "SmokeGrenade", "Molotov", "PipeBomb", "Flare", "Ball",
            "PetrolCan", "FireExtinguisher",
        };

        private static readonly List<WeaponHash> Wheel = WheelNames
            .Select(n => Enum.TryParse(n, out WeaponHash h) ? (WeaponHash?)h : null)
            .Where(h => h.HasValue)
            .Select(h => h.Value)
            .Distinct()
            .ToList();

        /// <summary>
        /// Cada vez: un arma al azar que el jugador todavía NO tiene. Cuando ya tiene toda la
        /// rueda, cada vez suma munición (dos cargadores) a todas sus armas hasta el máximo.
        /// </summary>
        private static void RandomWeapons(ActionContext ctx)
        {
            PlayerTransform.RequireHuman();
            Ped p = GTA.Game.Player.Character;
            int given = 0;
            bool ammo = false;

            for (int i = 0; i < ctx.Int("count"); i++)
            {
                List<WeaponHash> missing = Wheel.Where(h => !p.Weapons.HasWeapon(h)).ToList();
                if (missing.Count > 0)
                {
                    WeaponHash hash = ctx.Pick(missing);
                    p.Weapons.Give(hash, 60, false, true);
                    Weapon w = p.Weapons[hash];
                    if (w.MaxAmmoInClip > 1)
                    {
                        w.Ammo = Math.Min(w.MaxAmmo, Math.Max(w.Ammo, w.MaxAmmoInClip * 3));
                    }
                    given++;
                    continue;
                }

                // Rueda completa: munición para todo.
                foreach (WeaponHash hash in Wheel)
                {
                    Weapon w = p.Weapons[hash];
                    if (w.MaxAmmo > 1)
                    {
                        w.Ammo = Math.Min(w.MaxAmmo, w.Ammo + Math.Max(2, w.MaxAmmoInClip * 2));
                    }
                }
                ammo = true;
            }

            int have = Wheel.Count(h => p.Weapons.HasWeapon(h));
            if (given > 0)
            {
                GTA.UI.Notification.Show($"~y~Armas~s~: +{given} nueva(s) · {have}/{Wheel.Count}");
            }
            if (ammo)
            {
                GTA.UI.Notification.Show("~y~Armas~s~: rueda completa · ~g~+munición");
            }
        }

        private static void GiveWeapon(ActionContext ctx)
        {
            PlayerTransform.RequireHuman();
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
