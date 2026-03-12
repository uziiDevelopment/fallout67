using System;
using System.Collections.Generic;
using System.Linq;

namespace fallover_67
{
    // Returned by ExecuteCombatTurn so the UI can animate every missile
    public class StrikeResult
    {
        public List<string> Logs { get; } = new List<string>();

        // Enemy nations that will fire back at the player (name only; damage applied on landing)
        public List<string> Retaliators { get; } = new List<string>();

        // Player allies that will support the strike (pre-calculated damage applied on landing)
        public List<(string Name, long Damage)> AllySupporters { get; } = new List<(string, long)>();
    }

    public static class CombatEngine
    {
        private static Random rng = new Random();

        public static StrikeResult ExecuteCombatTurn(string targetName, int weaponIndex)
        {
            var result = new StrikeResult();
            Nation target = GameEngine.Nations[targetName];
            target.IsHostileToPlayer = true;
            target.AngerLevel = Math.Min(10, target.AngerLevel + rng.Next(2, 4));

            string weaponName = "STANDARD NUKE";
            double fraction = 0;

            if (weaponIndex == 0) { weaponName = "STANDARD NUKE";  fraction = 0.10 + rng.NextDouble() * 0.20; }
            if (weaponIndex == 1) { weaponName = "TSAR BOMBA";     fraction = 0.40 + rng.NextDouble() * 0.30; }
            if (weaponIndex == 2) { weaponName = "BIO-PLAGUE";     fraction = 0.35 + rng.NextDouble() * 0.30; }
            if (weaponIndex == 3) { weaponName = "ORBITAL LASER";  fraction = 0.15; }

            result.Logs.Add($"[STRIKE INITIATED] Payload: {weaponName} -> Destination: {target.Name.ToUpper()}");

            long casualties = (long)(target.MaxPopulation * fraction);
            if (casualties > target.Population) casualties = target.Population;
            target.Population -= casualties;
            result.Logs.Add($"[IMPACT] Confirming hits. Est. Enemy Casualties: {casualties:N0}");

            if (weaponIndex == 3)
            {
                result.Logs.Add($"[SUCCESS] Orbital Laser vaporized critical infrastructure!");
                target.Nukes = Math.Max(0, target.Nukes - 5);
                target.Money = (long)(target.Money * 0.5);
                // Orbital laser has no retaliation (no physical nuke to trace back)
                return result;
            }

            // Collect player ally support strikes (damage pre-rolled; applied when missile lands)
            foreach (string allyName in GameEngine.Player.Allies)
            {
                if (GameEngine.Nations.TryGetValue(allyName, out Nation ally) && !ally.IsDefeated)
                {
                    if (rng.NextDouble() < 0.6)
                    {
                        long allyDmg = (long)(target.MaxPopulation * (0.05 + rng.NextDouble() * 0.10));
                        result.AllySupporters.Add((ally.Name, allyDmg));
                    }
                }
            }

            if (target.Population <= 0)
            {
                target.IsDefeated = true;
                result.Logs.Add($"[VICTORY] {target.Name.ToUpper()} GOVERNMENT HAS COLLAPSED. Nation is completely annihilated.");
                CollectRetaliators(target, result);
                return result;
            }

            double healthPercent = (double)target.Population / target.MaxPopulation;
            if (healthPercent < 0.4)
            {
                double surrenderChance = (0.5 - healthPercent) - (target.Difficulty * 0.05);
                if (surrenderChance + rng.NextDouble() > 0.4)
                {
                    target.IsDefeated = true;
                    result.Logs.Add($"[SURRENDER] {target.Name.ToUpper()} IS BEGGING FOR MERCY! Unconditional surrender accepted.");
                    return result;
                }
            }

            CollectRetaliators(target, result);
            return result;
        }

        // Finds nations that will retaliate — damage is NOT applied here; the animated missile does it
        private static void CollectRetaliators(Nation primaryTarget, StrikeResult result)
        {
            if (!primaryTarget.IsDefeated && primaryTarget.Nukes > 0)
                result.Retaliators.Add(primaryTarget.Name);

            foreach (string allyName in primaryTarget.Allies)
            {
                if (GameEngine.Nations.TryGetValue(allyName, out Nation ally) && !ally.IsDefeated && ally.Nukes > 0)
                {
                    ally.IsHostileToPlayer = true;
                    result.Retaliators.Add(ally.Name);
                }
            }
        }

        // Called when any enemy missile (retaliation or random) reaches the player's bunker
        public static List<string> ExecuteEnemyStrike(string attackerName)
        {
            var log = new List<string>();
            if (!GameEngine.Nations.TryGetValue(attackerName, out Nation attacker)) return log;
            if (attacker.IsDefeated || attacker.Nukes <= 0) return log;

            attacker.Nukes--;
            log.Add($"[CATASTROPHE] ⚠ DIRECT HIT FROM {attackerName.ToUpper()} ⚠");
            TakePlayerDamage(attacker.Difficulty, attackerName, log);
            return log;
        }

        private static void TakePlayerDamage(int difficulty, string attackerName, List<string> log)
        {
            long dmg = rng.Next(1_000_000, 4_000_000) * difficulty;

            if (GameEngine.Player.IronDomeLevel > 0)
            {
                double domeBlock = 0.15 * GameEngine.Player.IronDomeLevel;
                if (domeBlock > 0.6) domeBlock = 0.6;
                long blocked = (long)(dmg * domeBlock);
                dmg -= blocked;
                log.Add($"[DEFENSE] Iron Dome intercepted a portion of the blast! (Saved {blocked:N0} citizens)");
            }

            if (GameEngine.Player.BunkerLevel > 0)
            {
                double bunkerBlock = 0.10 * GameEngine.Player.BunkerLevel;
                if (bunkerBlock > 0.5) bunkerBlock = 0.5;
                long blocked = (long)(dmg * bunkerBlock);
                dmg -= blocked;
                log.Add($"[DEFENSE] Deep Bunker Network shielded citizens! (Saved {blocked:N0} citizens)");
            }

            if (dmg > GameEngine.Player.Population) dmg = GameEngine.Player.Population;
            GameEngine.Player.Population -= dmg;

            log.Add($"[CASUALTY REPORT] We took {dmg:N0} casualties from the strike!");
        }

        // Used by multiplayer to apply another human player's strike without player-hostile side-effects
        public static (long casualties, bool defeated) ExecuteRemotePlayerStrike(string targetName, int weaponIndex)
        {
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return (0, false);

            double fraction = weaponIndex switch
            {
                0 => 0.10 + rng.NextDouble() * 0.20,
                1 => 0.40 + rng.NextDouble() * 0.30,
                2 => 0.35 + rng.NextDouble() * 0.30,
                3 => 0.15,
                _ => 0.10
            };

            long casualties = (long)(target.MaxPopulation * fraction);
            casualties = Math.Min(casualties, target.Population);
            target.Population -= casualties;
            target.AngerLevel  = Math.Min(10, target.AngerLevel + rng.Next(1, 3));

            if (target.Population <= 0) { target.IsDefeated = true; return (casualties, true); }
            return (casualties, false);
        }

        public static void ResolveMission(TroopMission mission, Action<string, bool> logCallback)
        {
            Nation target = GameEngine.Nations[mission.TargetNation];
            var hostiles = GameEngine.Nations.Values.Where(n => !n.IsDefeated && n.IsHostileToPlayer).ToList();

            bool blocked = false;
            string blockerName = "";

            foreach (var h in hostiles)
            {
                if (rng.NextDouble() < (0.05 * h.Difficulty))
                {
                    blocked = true;
                    blockerName = h.Name;
                    break;
                }
            }

            if (blocked)
            {
                // Troops are lost — TroopCount was already deducted from player pop at launch
                string troopLoss = mission.TroopCount > 0 ? $" {mission.TroopCount:N0} troops KIA." : "";
                logCallback($"[MISSION FAILED] ⚠ Troops ambushed by {blockerName.ToUpper()} en route to {target.Name}!{troopLoss}", false);
            }
            else
            {
                // Loot scales with force size: 5% → 20%, 15% → 40%, 30% → 70%, 50% → 100%
                double lootFraction = Math.Min(1.0, mission.TroopFraction * 2.0 + 0.10);
                int extractPct = (int)(lootFraction * 100);

                long moneyGained = (long)(target.Money * lootFraction);
                int nukesGained = (int)Math.Ceiling(target.Nukes * lootFraction);
                long popGained = (long)(target.Population * lootFraction);

                target.IsLooted = true;
                GameEngine.Player.Money += moneyGained;
                GameEngine.Player.StandardNukes += nukesGained;
                // Rescued citizens + troops returning home
                GameEngine.Player.Population += popGained + mission.TroopCount;

                target.Money = 0;
                target.Nukes = 0;
                target.Population = 0;

                logCallback($"[MISSION SUCCESS] Secured {target.Name.ToUpper()}! Recovered ${moneyGained}M, {nukesGained} Nukes, {popGained:N0} Citizens. ({extractPct}% extraction efficiency)", true);
            }
        }
    }
}
