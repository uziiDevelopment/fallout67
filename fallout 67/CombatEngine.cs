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

        public static long PreCalculatePlayerDamage(string targetName, int weaponIndex)
        {
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return 0;
            double fraction = weaponIndex switch {
                0 => 0.10 + rng.NextDouble() * 0.20,
                1 => 0.40 + rng.NextDouble() * 0.30,
                2 => 0.35 + rng.NextDouble() * 0.30,
                3 => 0.15,
                _ => 0.10
            };
            long casualties = (long)(target.MaxPopulation * fraction);
            return Math.Min(casualties, target.Population);
        }

        public static StrikeResult ExecuteCombatTurn(string targetName, int weaponIndex, long forcedCasualties)
        {
            var result = new StrikeResult();
            Nation target = GameEngine.Nations[targetName];
            bool wasAlreadyDefeated = target.IsDefeated;
            
            if (!target.IsHumanControlled) target.IsHostileToPlayer = true;
            target.AngerLevel = Math.Min(10, target.AngerLevel + rng.Next(2, 4));

            string weaponName = "STANDARD NUKE";
            if (weaponIndex == 0) weaponName = "STANDARD NUKE";
            if (weaponIndex == 1) weaponName = "TSAR BOMBA";
            if (weaponIndex == 2) weaponName = "BIO-PLAGUE";
            if (weaponIndex == 3) weaponName = "ORBITAL LASER";

            result.Logs.Add($"[STRIKE INITIATED] Payload: {weaponName} -> Destination: {target.Name.ToUpper()}");

            long casualties = Math.Min(forcedCasualties, target.Population);
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
                if (allyName == targetName) continue; // ally IS the target — don't bomb yourself
                if (GameEngine.Nations.TryGetValue(allyName, out Nation ally) && !ally.IsDefeated && !ally.IsHumanControlled)
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
                if (!wasAlreadyDefeated)
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
            // Primary target retaliation check
            if (!primaryTarget.IsDefeated && !primaryTarget.IsHumanControlled && primaryTarget.Nukes > 0)
            {
                // Chance scaled by anger level (0.3 base + up to 0.7 from anger)
                double baseChance = 0.3 + (primaryTarget.AngerLevel * 0.07);
                if (rng.NextDouble() < baseChance)
                    result.Retaliators.Add(primaryTarget.Name);
            }

            // Ally interventions - capped to prevent "Nuke Rain"
            int allyCount = 0;
            foreach (string allyName in primaryTarget.Allies)
            {
                if (allyCount >= 2) break; // Maximum 2 allies will jump in per individual strike

                if (GameEngine.Nations.TryGetValue(allyName, out Nation ally) && !ally.IsDefeated && !ally.IsHumanControlled && ally.Nukes > 0)
                {
                    // Allies are more hesitant (20% base + scale by anger)
                    double allyInvolvementChance = 0.2 + (ally.AngerLevel * 0.04);
                    
                    // Survival Instinct: If population is low (<20%), AI is 80% less likely to retaliate further
                    if ((double)ally.Population / ally.MaxPopulation < 0.2) allyInvolvementChance *= 0.2;

                    if (rng.NextDouble() < allyInvolvementChance)
                    {
                        ally.IsHostileToPlayer = true;
                        result.Retaliators.Add(ally.Name);
                        allyCount++;
                    }
                }
            }
        }

        // Called when any enemy missile (retaliation or random) reaches the player's bunker.
        // domeBlockFraction: 0..1 override from the Iron Dome minigame; -1 = use passive dome roll.
        // weaponIndex: 0=Standard Nuke, 1=Tsar Bomba, 2=Bio-Plague, 3=Orbital Laser
        public static List<string> ExecuteEnemyStrike(string attackerName, double domeBlockFraction = -1, int weaponIndex = 0)
        {
            var log = new List<string>();
            if (!GameEngine.Nations.TryGetValue(attackerName, out Nation attacker)) return log;
            if (attacker.IsDefeated || attacker.Nukes <= 0) return log;

            attacker.Nukes--;
            log.Add($"[CATASTROPHE] ⚠ DIRECT HIT FROM {attackerName.ToUpper()} ⚠");
            TakePlayerDamage(attacker.Difficulty, attackerName, log, domeBlockFraction, weaponIndex);
            
            return log;
        }

        // Called when a human player (via multiplayer) strikes the local player's nation with a pre-rolled damage value.
        // weaponIndex: 0=Standard Nuke, 1=Tsar Bomba, 2=Bio-Plague, 3=Orbital Laser
        public static List<string> ApplyForcedEnemyStrike(long incomingDamage, int weaponIndex = 0)
        {
            var log = new List<string>();
            long dmg = incomingDamage;

            bool isBio = weaponIndex == 2;

            if (!isBio && GameEngine.Player.IronDomeLevel > 0)
            {
                double domeBlock = Math.Min(0.6, 0.15 * GameEngine.Player.IronDomeLevel);
                long blocked = (long)(dmg * domeBlock);
                dmg -= blocked;
                if (blocked > 0) log.Add($"[DEFENSE] Iron Dome saved {blocked:N0} citizens.");
            }

            if (!isBio && GameEngine.Player.BunkerLevel > 0)
            {
                double bunkerBlock = Math.Min(0.5, 0.10 * GameEngine.Player.BunkerLevel);
                long blocked = (long)(dmg * bunkerBlock);
                dmg -= blocked;
                if (blocked > 0) log.Add($"[DEFENSE] Deep Bunker Network shielded {blocked:N0} citizens.");
            }

            if (isBio && GameEngine.Player.VaccineLevel > 0)
            {
                double vaccineBlock = Math.Min(0.6, 0.15 * GameEngine.Player.VaccineLevel);
                long blocked = (long)(dmg * vaccineBlock);
                dmg -= blocked;
                if (blocked > 0) log.Add($"[DEFENSE] Vaccine Program neutralized {blocked:N0} infections.");
            }

            dmg = Math.Min(dmg, GameEngine.Player.Population);
            GameEngine.Player.Population -= dmg;
            log.Add($"[CATASTROPHE] ⚠ MASSIVE REMOTE STRIKE IMPACTED NATIONAL SOIL ⚠");
            log.Add($"[IMPACT] Fatalities: {dmg:N0}. Sector radiation levels critical.");

            // Automatic Submarine Retaliation — since we don't have the attacker's coordinates 
            // easily in this specific static call, we usually pass them from the network layer.
            // For now, if we can find a nation with the attacker name, we use it.
            // (In multiplayer, the network layer handles the retaliation broadcast)
            // We don't have attacker coordinates here, so we can't trigger a direct submarine retaliation.
            // This is typically handled by the network layer in multiplayer.

            return log;
        }

        public static List<Submarine> TriggerSubmarineRetaliation(float targetLat, float targetLng, string attackerName)
        {
            var firingSubs = new List<Submarine>();
            foreach (var sub in GameEngine.Submarines)
            {
                if (sub.OwnerId == GameEngine.Player.NationName && !sub.IsDestroyed && sub.NukeCount > 0)
                {
                    sub.NukeCount--;
                    firingSubs.Add(sub);
                    CheckSubmarineCollateral(targetLat, targetLng, 1.0f, sub.Name);
                }
            }
            return firingSubs;
        }

        private static void TakePlayerDamage(int difficulty, string attackerName, List<string> log, double domeBlockFraction = -1, int weaponIndex = 0)
        {
            long dmg = rng.Next(1_000_000, 4_000_000) * difficulty;
            bool isBio = weaponIndex == 2;

            if (!isBio && GameEngine.Player.IronDomeLevel > 0)
            {
                double domeBlock;
                if (domeBlockFraction >= 0)
                {
                    // Minigame result: fraction of missiles intercepted, scaled by dome level cap
                    double cap = Math.Min(0.6, 0.15 * GameEngine.Player.IronDomeLevel);
                    domeBlock = domeBlockFraction * cap;
                    log.Add($"[DEFENSE] Iron Dome intercepted {domeBlockFraction * 100:F0}% of incoming missiles!");
                }
                else
                {
                    // Passive roll (no dome level, or minigame skipped)
                    domeBlock = 0.15 * GameEngine.Player.IronDomeLevel;
                    if (domeBlock > 0.6) domeBlock = 0.6;
                    log.Add($"[DEFENSE] Iron Dome intercepted a portion of the blast!");
                }
                long blocked = (long)(dmg * domeBlock);
                dmg -= blocked;
                if (blocked > 0) log.Add($"[DEFENSE] Iron Dome saved {blocked:N0} citizens.");
            }

            if (!isBio && GameEngine.Player.BunkerLevel > 0)
            {
                double bunkerBlock = 0.10 * GameEngine.Player.BunkerLevel;
                if (bunkerBlock > 0.5) bunkerBlock = 0.5;
                long blocked = (long)(dmg * bunkerBlock);
                dmg -= blocked;
                log.Add($"[DEFENSE] Deep Bunker Network shielded citizens! (Saved {blocked:N0} citizens)");
            }

            if (isBio && GameEngine.Player.VaccineLevel > 0)
            {
                double vaccineBlock = Math.Min(0.6, 0.15 * GameEngine.Player.VaccineLevel);
                long blocked = (long)(dmg * vaccineBlock);
                dmg -= blocked;
                log.Add($"[DEFENSE] Vaccine Program neutralized {blocked:N0} infections.");
            }

            if (dmg > GameEngine.Player.Population) dmg = GameEngine.Player.Population;
            GameEngine.Player.Population -= dmg;

            log.Add($"[CASUALTY REPORT] We took {dmg:N0} casualties from the strike!");
        }

        // Used by multiplayer to apply another human player's strike without player-hostile side-effects
        public static (long casualties, bool defeated) ExecuteRemotePlayerStrike(string targetName, int weaponIndex, long forcedCasualties)
        {
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return (0, false);

            bool wasAlreadyDefeated = target.IsDefeated;
            long casualties = Math.Min(forcedCasualties, target.Population);
            target.Population -= casualties;
            target.AngerLevel = Math.Min(10, target.AngerLevel + rng.Next(1, 3));

            if (weaponIndex == 3)
            {
                target.Nukes = Math.Max(0, target.Nukes - 5);
                target.Money = (long)(target.Money * 0.5);
            }

            if (target.Population <= 0) 
            { 
                target.IsDefeated = true; 
                return (casualties, !wasAlreadyDefeated); // Only return true if it JUST happened
            }
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
                ProfileManager.RecordTroopMission(false);
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
                ProfileManager.RecordTroopMission(true);
            }
        }
        public static void CheckSubmarineCollateral(float lat, float lng, float radiusScale, string attackerName)
        {
            // Simple distance check in Lat/Lng space (not perfect but works for game logic)
            // radiusScale of 1.0 is ~5 degrees of radius
            float threshold = 5.0f * radiusScale;

            foreach (var sub in GameEngine.Submarines)
            {
                if (sub.IsDestroyed) continue;

                float dx = sub.MapX - lng;
                float dy = sub.MapY - lat;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist < threshold)
                {
                    bool wasDestroyed = sub.IsDestroyed;
                    sub.Health -= 60 + rng.Next(20, 50); // Massive damage
                    if (sub.Health <= 0)
                    {
                        sub.Health = 0;
                        if (!wasDestroyed && sub.OwnerId == GameEngine.Player.NationName)
                        {
                            // If OUR sub was just attacked directly, retaliate against the attacker coordinates!
                            // We use the coords of the strike that hit us.
                            TriggerSubmarineRetaliation(lat, lng, attackerName);
                        }
                    }
                }
            }
        }
    }
}
