using System;
using System.Collections.Generic;
using System.Linq;

namespace fallover_67
{
    public static class DiplomacyEngine
    {
        private static Random rng = new Random();
        private const int MaxAIAllies = 5;

        // ── Acceptance Probability ──────────────────────────────────────────────
        public static float CalculateAcceptanceProbability(string nationName)
        {
            if (!GameEngine.Nations.TryGetValue(nationName, out Nation target)) return 0f;

            float prob = 0.50f;

            // Bigger nations are harder to convince
            prob -= (float)(target.Population / 1_500_000_000.0) * 0.25f;

            // Aggressive players are less trusted
            prob -= Math.Min(0.30f, GameEngine.Player.NukesUsed * 0.02f);

            // High difficulty = harder to impress
            prob -= target.Difficulty * 0.05f;

            // Existing mood toward player
            prob += target.Diplomacy.DiplomacyMood * 0.3f;

            // Hostile nations are very reluctant
            if (target.IsHostileToPlayer) prob -= 0.4f;

            // Previously betrayed by player = nearly impossible
            if (target.Diplomacy.LastBetrayedBy == GameEngine.Player.NationName) prob -= 0.6f;

            // Player is weak? Nations less interested in alliance
            float playerStrength = (float)GameEngine.Player.Population / GameEngine.Player.MaxPopulation;
            if (playerStrength < 0.3f) prob -= 0.15f;

            return Math.Clamp(prob, 0.05f, 0.85f);
        }

        public static bool TryFormAlliance(string nationName, float minigameBonus)
        {
            float prob = CalculateAcceptanceProbability(nationName) + minigameBonus;
            return rng.NextDouble() < prob;
        }

        public static void FormAlliance(string nation1, string nation2)
        {
            // nation1 can be the player's nation name
            bool n1IsPlayer = (nation1 == GameEngine.Player.NationName);
            bool n2IsPlayer = (nation2 == GameEngine.Player.NationName);

            if (n1IsPlayer)
            {
                if (!GameEngine.Player.Allies.Contains(nation2))
                    GameEngine.Player.Allies.Add(nation2);
                if (GameEngine.Nations.TryGetValue(nation2, out var n2))
                {
                    if (!n2.Allies.Contains(nation1)) n2.Allies.Add(nation1);
                    n2.Diplomacy.AllianceAge[nation1] = 0;
                    n2.IsHostileToPlayer = false;
                    n2.Diplomacy.DiplomacyMood = Math.Min(1f, n2.Diplomacy.DiplomacyMood + 0.2f);
                }
            }
            else if (n2IsPlayer)
            {
                FormAlliance(nation2, nation1); // flip and recurse
                return;
            }
            else
            {
                // AI-AI alliance
                if (GameEngine.Nations.TryGetValue(nation1, out var a) && GameEngine.Nations.TryGetValue(nation2, out var b))
                {
                    if (!a.Allies.Contains(nation2)) a.Allies.Add(nation2);
                    if (!b.Allies.Contains(nation1)) b.Allies.Add(nation1);
                    a.Diplomacy.AllianceAge[nation2] = 0;
                    b.Diplomacy.AllianceAge[nation1] = 0;
                }
            }
        }

        public static void BreakAlliance(string nation1, string nation2)
        {
            bool n1IsPlayer = (nation1 == GameEngine.Player.NationName);
            bool n2IsPlayer = (nation2 == GameEngine.Player.NationName);

            if (n1IsPlayer)
            {
                GameEngine.Player.Allies.Remove(nation2);
                if (GameEngine.Nations.TryGetValue(nation2, out var n2))
                {
                    n2.Allies.Remove(nation1);
                    n2.Diplomacy.AllianceAge.Remove(nation1);
                }
            }
            else if (n2IsPlayer)
            {
                BreakAlliance(nation2, nation1);
                return;
            }
            else
            {
                if (GameEngine.Nations.TryGetValue(nation1, out var a))
                {
                    a.Allies.Remove(nation2);
                    a.Diplomacy.AllianceAge.Remove(nation2);
                }
                if (GameEngine.Nations.TryGetValue(nation2, out var b))
                {
                    b.Allies.Remove(nation1);
                    b.Diplomacy.AllianceAge.Remove(nation1);
                }
            }
        }

        // ── Betrayal ────────────────────────────────────────────────────────────
        public static bool RollBetrayal(string betrayerName, string victimName)
        {
            Nation betrayer = null, victim = null;
            bool betrayerIsPlayer = betrayerName == GameEngine.Player.NationName;

            if (betrayerIsPlayer) return false; // AI betrayal only here; player betrayal is explicit

            if (!GameEngine.Nations.TryGetValue(betrayerName, out betrayer)) return false;
            if (betrayer.Diplomacy.BetrayalCooldown > 0) return false;
            if (betrayer.IsHumanControlled) return false;

            long victimPop;
            if (victimName == GameEngine.Player.NationName)
                victimPop = GameEngine.Player.Population;
            else if (GameEngine.Nations.TryGetValue(victimName, out victim))
                victimPop = victim.Population;
            else return false;

            // Minimum alliance age before betrayal is even possible (60 ticks = 60 seconds)
            int age = 0;
            betrayer.Diplomacy.AllianceAge.TryGetValue(victimName, out age);
            if (age < 60) return false; // Alliances are safe for the first 60 seconds

            float chance = 0.0008f; // Lower base chance

            // Alliance age: only starts mattering after 120 ticks
            if (age > 120)
                chance += (age - 120) * 0.000015f; // Much slower ramp

            // Power disparity: betrayer much stronger = more tempted
            float powerRatio = (float)betrayer.Population / Math.Max(1, victimPop);
            if (powerRatio > 3f) chance += (powerRatio - 3f) * 0.0005f; // Only if 3x stronger

            // Victim weakened — but less aggressive
            long victimMax = victimName == GameEngine.Player.NationName ? GameEngine.Player.MaxPopulation : (victim?.MaxPopulation ?? 1);
            if ((float)victimPop / victimMax < 0.2f) chance += 0.001f; // Only below 20%, and smaller bump

            // Smarter nations betray less randomly
            chance -= betrayer.Difficulty * 0.0003f;

            // Anger at victim increases chance (but needs high anger)
            if (betrayer.AngerLevel >= 7) chance += 0.001f;

            // Positive diplomacy mood = much less likely to betray
            chance -= betrayer.Diplomacy.DiplomacyMood * 0.001f;

            // Sanctioned nations are more desperate and backstabby
            if (betrayer.IsSanctioned) chance += 0.0005f;

            return rng.NextDouble() < Math.Max(0, Math.Min(0.01f, chance)); // Hard cap at 1%
        }

        // ── Resource Sharing ────────────────────────────────────────────────────
        public static void TickResourceSharing()
        {
            // Player receives from AI allies
            foreach (string allyName in GameEngine.Player.Allies.ToList())
            {
                if (!GameEngine.Nations.TryGetValue(allyName, out Nation ally)) continue;
                if (ally.IsDefeated) continue;

                int age = ally.Diplomacy.AllianceAge.TryGetValue(GameEngine.Player.NationName, out int a) ? a : 0;
                long share = Math.Min(100, 20 + age * 2);
                if (ally.Money >= share)
                {
                    ally.Money -= share;
                    GameEngine.Player.Money += share;
                }
            }

            // AI-AI sharing
            var processed = new HashSet<string>();
            foreach (var nation in GameEngine.Nations.Values)
            {
                if (nation.IsDefeated) continue;
                foreach (string allyName in nation.Allies)
                {
                    string key = string.Compare(nation.Name, allyName) < 0 ? $"{nation.Name}|{allyName}" : $"{allyName}|{nation.Name}";
                    if (processed.Contains(key)) continue;
                    processed.Add(key);

                    if (!GameEngine.Nations.TryGetValue(allyName, out Nation ally)) continue;
                    if (ally.IsDefeated) continue;

                    // Richer ally shares with poorer
                    if (nation.Money > ally.Money && nation.Money > 200)
                    {
                        long amount = Math.Min(50, (nation.Money - ally.Money) / 4);
                        nation.Money -= amount;
                        ally.Money += amount;
                    }
                    else if (ally.Money > nation.Money && ally.Money > 200)
                    {
                        long amount = Math.Min(50, (ally.Money - nation.Money) / 4);
                        ally.Money -= amount;
                        nation.Money += amount;
                    }
                }
            }
        }

        // ── Summit Host ─────────────────────────────────────────────────────────
        public static string GetSummitHost(string nation1, string nation2)
        {
            long pop1, pop2;
            if (nation1 == GameEngine.Player.NationName)
                pop1 = GameEngine.Player.Population;
            else if (GameEngine.Nations.TryGetValue(nation1, out var n1))
                pop1 = n1.Population;
            else pop1 = 0;

            if (nation2 == GameEngine.Player.NationName)
                pop2 = GameEngine.Player.Population;
            else if (GameEngine.Nations.TryGetValue(nation2, out var n2))
                pop2 = n2.Population;
            else pop2 = 0;

            return pop1 >= pop2 ? nation1 : nation2;
        }

        // ── Plane Shootdown ─────────────────────────────────────────────────────
        public static (bool shotDown, string shooterName) RollPlaneShootdown(SummitFlight plane)
        {
            float currentLat = plane.StartLat + (plane.EndLat - plane.StartLat) * plane.Progress;
            float currentLng = plane.StartLng + (plane.EndLng - plane.StartLng) * plane.Progress;

            float baseRisk = 0.02f;

            // Check hostile nations near the flight path
            foreach (var n in GameEngine.Nations.Values)
            {
                if (n.IsDefeated || n.Nukes <= 0) continue;
                if (n.Name == plane.Nation1 || n.Name == plane.Nation2) continue;
                if (n.Allies.Contains(plane.Nation1) || n.Allies.Contains(plane.Nation2)) continue;

                float dx = n.MapX - currentLng;
                float dy = n.MapY - currentLat;
                float distSq = dx * dx + dy * dy;

                if (distSq < 400f) // within ~20 degrees
                {
                    float risk = baseRisk;
                    if (n.IsHostileToPlayer && (plane.IsPlayerPlane || plane.IsPlayerInitiated))
                        risk += 0.03f;
                    if (n.AngerLevel > 5) risk += 0.01f;

                    if (rng.NextDouble() < risk)
                        return (true, n.Name);
                }
            }

            return (false, "");
        }

        // ── AI Autonomous Diplomacy ─────────────────────────────────────────────
        public static (string initiator, string target)? AIConsiderAlliance()
        {
            var candidates = GameEngine.Nations.Values
                .Where(n => !n.IsDefeated && !n.IsHumanControlled && n.Allies.Count < MaxAIAllies && n.Diplomacy.BetrayalCooldown <= 0)
                .ToList();

            if (candidates.Count < 2) return null;
            if (rng.NextDouble() > 0.15) return null; // 15% chance per tick to even consider

            var initiator = candidates[rng.Next(candidates.Count)];

            // Find a non-allied, non-hostile nation
            var targets = candidates
                .Where(n => n.Name != initiator.Name && !initiator.Allies.Contains(n.Name) && !n.IsDefeated)
                .ToList();

            if (targets.Count == 0) return null;

            var target = targets[rng.Next(targets.Count)];

            // Simple acceptance check for AI-AI
            float prob = 0.35f;
            prob -= Math.Abs(initiator.Difficulty - target.Difficulty) * 0.05f;
            if (initiator.Allies.Intersect(target.Allies).Any()) prob += 0.15f; // shared allies
            prob += (initiator.AngerLevel + target.AngerLevel) * 0.01f; // mutual enemies = bonding

            if (rng.NextDouble() < prob)
                return (initiator.Name, target.Name);

            return null;
        }

        public static (string betrayer, string victim)? AIConsiderBetrayal()
        {
            foreach (var nation in GameEngine.Nations.Values)
            {
                if (nation.IsDefeated || nation.IsHumanControlled || nation.Allies.Count == 0) continue;
                if (nation.Diplomacy.BetrayalCooldown > 0) continue;

                foreach (string allyName in nation.Allies.ToList())
                {
                    if (RollBetrayal(nation.Name, allyName))
                        return (nation.Name, allyName);
                }
            }
            return null;
        }

        // ── Coordinates Helpers ─────────────────────────────────────────────────
        public static (float lat, float lng) GetNationCoords(string nationName)
        {
            if (nationName == GameEngine.Player.NationName)
                return (GameEngine.Player.MapY, GameEngine.Player.MapX);
            if (GameEngine.Nations.TryGetValue(nationName, out var n))
                return (n.MapY, n.MapX);
            return (0, 0);
        }
    }
}
