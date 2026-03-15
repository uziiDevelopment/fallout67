using System;
using System.Collections.Generic;
using System.Linq;

namespace fallover_67
{
    public static class StrategicEngine
    {
        private static Random rng = new Random();

        // ═══════════════════════════════════════════════════════════════════════
        // 1. ECONOMY — Income, Resources, Trade Routes, Sanctions
        // ═══════════════════════════════════════════════════════════════════════

        public static void TickEconomy()
        {
            // Player income
            long playerIncome = CalculateIncome(
                GameEngine.Player.IndustryLevel,
                GameEngine.Player.Resources,
                GameEngine.Player.IncomeMultiplier,
                GameEngine.Player.NationName,
                GameEngine.Player.Allies);
            GameEngine.Player.Money += playerIncome;

            // AI nation income
            foreach (var n in GameEngine.Nations.Values)
            {
                if (n.IsDefeated) continue;
                long income = CalculateIncome(n.IndustryLevel, n.Resources, n.IncomeMultiplier, n.Name, n.Allies);
                n.Money += income;
            }

            // Tick sanctions
            for (int i = GameEngine.ActiveSanctions.Count - 1; i >= 0; i--)
            {
                var s = GameEngine.ActiveSanctions[i];
                s.TicksRemaining--;
                if (s.TicksRemaining <= 0)
                    GameEngine.ActiveSanctions.RemoveAt(i);
            }

            RecalcSanctionEffects();

            // Nuke restocking — nations with uranium + money build more warheads
            TickNukeRestocking();

            // Sanctioned nations may lose allies (allies distance themselves)
            TickAllianceErosion();
        }

        public static long CalculateIncome(int industryLevel, List<NaturalResource> resources, float multiplier, string nationName, List<string> allies)
        {
            long baseIncome = industryLevel * 50;

            foreach (var r in resources)
            {
                if (r.IsDestroyed) continue;
                baseIncome += r.OutputPerTick;
            }

            // Trade route bonus: +10% per active ally (capped at +40%)
            // BUT sanctioned allies don't count (trade is blocked)
            int tradePartners = 0;
            foreach (var a in allies)
            {
                if (GameEngine.Nations.TryGetValue(a, out Nation ally) && !ally.IsSanctioned)
                    tradePartners++;
                else if (a == GameEngine.Player.NationName && GameEngine.ActiveSanctions.All(s => s.Target != a))
                    tradePartners++;
            }
            float tradeBonus = Math.Min(0.4f, tradePartners * 0.10f);
            baseIncome = (long)(baseIncome * (1f + tradeBonus));

            // Nuclear winter reduces agricultural output
            if (GameEngine.NuclearWinterActive)
                baseIncome = (long)(baseIncome * (1f - GameEngine.NuclearWinterSeverity * 0.5f));

            // Apply sanctions/multiplier
            baseIncome = (long)(baseIncome * multiplier);

            return Math.Max(5, baseIncome); // minimum 5M per tick
        }

        // Nations with uranium + money build nukes over time. Arms sanctions block this entirely.
        private static void TickNukeRestocking()
        {
            foreach (var n in GameEngine.Nations.Values)
            {
                if (n.IsDefeated || n.IsHumanControlled) continue;

                // Arms embargo or full sanctions block restocking
                bool armsBlocked = GameEngine.ActiveSanctions.Any(s =>
                    s.Target == n.Name && (s.Type == SanctionType.Arms || s.Type == SanctionType.Full));
                if (armsBlocked) continue;

                // Need uranium resource and money
                bool hasUranium = n.Resources.Any(r => r.Type == ResourceType.Uranium && !r.IsDestroyed);
                if (!hasUranium) continue;
                if (n.Money < 500) continue;

                // ~15% chance per economy tick to build 1-2 nukes
                if (rng.NextDouble() < 0.15)
                {
                    int built = 1 + (n.Money > 2000 ? 1 : 0);
                    n.Nukes += built;
                    n.Money -= 300 * built;
                }
            }
        }

        // Heavily sanctioned nations lose allies over time — allies don't want to be associated
        private static void TickAllianceErosion()
        {
            foreach (var n in GameEngine.Nations.Values)
            {
                if (n.IsDefeated || !n.IsSanctioned) continue;

                int sanctionCount = GetSanctionCount(n.Name);
                if (sanctionCount < 2) continue; // only erodes with 2+ sanctions

                // 5% per tick per extra sanction above 1
                double erosionChance = (sanctionCount - 1) * 0.05;
                if (rng.NextDouble() < erosionChance && n.Allies.Count > 0)
                {
                    string droppedAlly = n.Allies[rng.Next(n.Allies.Count)];
                    n.Allies.Remove(droppedAlly);

                    // Mutual removal
                    if (GameEngine.Nations.TryGetValue(droppedAlly, out Nation ally))
                        ally.Allies.Remove(n.Name);
                    if (droppedAlly == GameEngine.Player.NationName)
                        GameEngine.Player.Allies.Remove(n.Name);
                }
            }
        }

        public static void ImposeSanction(string imposedBy, string target, SanctionType type, int duration = 30)
        {
            if (GameEngine.ActiveSanctions.Any(s => s.ImposedBy == imposedBy && s.Target == target))
                return;

            GameEngine.ActiveSanctions.Add(new Sanction
            {
                ImposedBy = imposedBy,
                Target = target,
                Type = type,
                TicksRemaining = duration
            });

            RecalcSanctionEffects();
        }

        public static void LiftSanction(string imposedBy, string target)
        {
            GameEngine.ActiveSanctions.RemoveAll(s => s.ImposedBy == imposedBy && s.Target == target);
            RecalcSanctionEffects();
        }

        private static void RecalcSanctionEffects()
        {
            GameEngine.Player.IncomeMultiplier = 1.0f;
            foreach (var n in GameEngine.Nations.Values)
            {
                n.IncomeMultiplier = 1.0f;
                n.IsSanctioned = false;
            }

            foreach (var s in GameEngine.ActiveSanctions)
            {
                float penalty = s.Type switch
                {
                    SanctionType.Trade => 0.15f,
                    SanctionType.Arms => 0.10f,
                    SanctionType.Full => 0.30f,
                    _ => 0.10f
                };

                if (s.Target == GameEngine.Player.NationName)
                {
                    GameEngine.Player.IncomeMultiplier -= penalty;
                }
                else if (GameEngine.Nations.TryGetValue(s.Target, out Nation n))
                {
                    n.IncomeMultiplier -= penalty;
                    n.IsSanctioned = true;
                }
            }

            GameEngine.Player.IncomeMultiplier = Math.Max(0.2f, GameEngine.Player.IncomeMultiplier);
            foreach (var n in GameEngine.Nations.Values)
                n.IncomeMultiplier = Math.Max(0.2f, n.IncomeMultiplier);
        }

        public static int GetSanctionCount(string nationName)
        {
            return GameEngine.ActiveSanctions.Count(s => s.Target == nationName);
        }

        public static string GetResourceSummary(List<NaturalResource> resources)
        {
            if (resources.Count == 0) return "None";
            return string.Join(", ", resources.Where(r => !r.IsDestroyed).Select(r =>
            {
                string icon = r.Type switch
                {
                    ResourceType.Oil => "OIL",
                    ResourceType.Uranium => "URANIUM",
                    ResourceType.RareEarth => "TECH",
                    ResourceType.Agriculture => "AGRI",
                    _ => "???"
                };
                return $"{icon}(+{r.OutputPerTick})";
            }));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MILITARY READINESS — Economy affects combat capability
        // ═══════════════════════════════════════════════════════════════════════

        /// Returns 0.0 to 1.0 — how capable a nation is of launching attacks.
        /// Low money, heavy sanctions, destroyed resources = can't fight effectively.
        public static float GetMilitaryReadiness(Nation n)
        {
            float readiness = 1.0f;

            // Broke nations can barely fight (below $200M = degraded, below $50M = crippled)
            if (n.Money < 50) readiness *= 0.2f;
            else if (n.Money < 200) readiness *= 0.5f;
            else if (n.Money < 500) readiness *= 0.75f;

            // Sanctions degrade readiness
            if (n.IsSanctioned)
            {
                int count = GetSanctionCount(n.Name);
                readiness *= Math.Max(0.2f, 1f - count * 0.2f); // each sanction = -20% readiness
            }

            // No oil = logistics nightmare (30% penalty)
            bool hasOil = n.Resources.Any(r => r.Type == ResourceType.Oil && !r.IsDestroyed);
            if (!hasOil && n.Resources.Any(r => r.Type == ResourceType.Oil))
                readiness *= 0.7f; // had oil, lost it — worse than never having it

            // Nuclear winter makes everything harder
            if (GameEngine.NuclearWinterActive)
                readiness *= Math.Max(0.4f, 1f - GameEngine.NuclearWinterSeverity * 0.4f);

            return Math.Max(0.1f, readiness);
        }

        /// Returns true if a nation can afford to launch an attack right now.
        /// Used by TryTriggerWorldEvents and CollectRetaliators.
        public static bool CanNationAffordLaunch(Nation n)
        {
            if (n.Nukes <= 0) return false;
            if (n.IsDefeated) return false;

            float readiness = GetMilitaryReadiness(n);

            // Readiness acts as a probability gate — crippled nations rarely attack
            if (rng.NextDouble() > readiness) return false;

            // Ceasefire makes AI nations respect the peace (mostly)
            if (IsResolutionActive(UNResolutionType.Ceasefire))
            {
                // Only super-angry nations (8+) will violate ceasefire
                if (n.AngerLevel < 8) return false;
            }

            // Nuclear freeze: nations with low anger won't fire
            if (IsResolutionActive(UNResolutionType.NuclearFreeze))
            {
                if (n.AngerLevel < 6) return false;
            }

            return true;
        }

        /// Salvo size is limited by readiness — broke/sanctioned nations fire fewer nukes
        public static int GetMaxSalvoSize(Nation n)
        {
            float readiness = GetMilitaryReadiness(n);
            int maxFromReadiness = Math.Max(1, (int)(5 * readiness)); // 1-5 based on readiness
            return Math.Min(maxFromReadiness, n.Nukes);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // COLLATERAL DAMAGE — Strikes destroy resources
        // ═══════════════════════════════════════════════════════════════════════

        /// Called when a nation is struck. Each strike has a chance to destroy a resource.
        /// Bigger weapons = higher chance. This makes nuking oil-rich nations economically devastating.
        public static string? OnNationStruck(string targetName, int weaponIndex)
        {
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return null;

            // Chance to destroy a resource: Standard=15%, Tsar=35%, Bio=10%, Orbital=25%
            double destroyChance = weaponIndex switch
            {
                0 => 0.15,
                1 => 0.35,
                2 => 0.10,
                3 => 0.25,
                _ => 0.10
            };

            var intact = target.Resources.Where(r => !r.IsDestroyed).ToList();
            if (intact.Count == 0) return null;

            if (rng.NextDouble() < destroyChance)
            {
                var destroyed = intact[rng.Next(intact.Count)];
                destroyed.IsDestroyed = true;
                return $"[COLLATERAL] {target.Name.ToUpper()}'s {destroyed.Type} production facility destroyed in the blast!";
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // 2. ESPIONAGE — Spies, Intel, Sabotage (Player + AI)
        // ═══════════════════════════════════════════════════════════════════════

        public static Spy DeploySpy(string targetNation, SpyMissionType mission)
        {
            int duration = mission switch
            {
                SpyMissionType.Intel => 10 + rng.Next(5),
                SpyMissionType.Sabotage => 15 + rng.Next(10),
                SpyMissionType.StealMoney => 12 + rng.Next(8),
                SpyMissionType.DelayLaunch => 8 + rng.Next(5),
                _ => 10
            };

            var spy = new Spy
            {
                TargetNation = targetNation,
                Mission = mission,
                TicksRemaining = duration
            };
            GameEngine.Player.ActiveSpies.Add(spy);
            return spy;
        }

        public static (bool success, string report) ResolveSpy(Spy spy)
        {
            if (!GameEngine.Nations.TryGetValue(spy.TargetNation, out Nation target))
                return (false, "Target nation no longer exists.");

            double detectChance = 0.15 + target.Difficulty * 0.08;
            if (target.IsSanctioned) detectChance -= 0.05;
            bool detected = rng.NextDouble() < detectChance;

            if (detected)
            {
                spy.IsRevealed = true;
                target.IsHostileToPlayer = true;
                target.AngerLevel = Math.Min(10, target.AngerLevel + 2);
                return (false, $"SPY CAUGHT in {target.Name.ToUpper()}! Agent executed. Relations deteriorated.");
            }

            switch (spy.Mission)
            {
                case SpyMissionType.Intel:
                    return (true, $"INTEL: {target.Name.ToUpper()} — Pop: {target.Population:N0}, Nukes: {target.Nukes}, Money: ${target.Money}M, " +
                           $"Anger: {target.AngerLevel}/10, Allies: [{string.Join(", ", target.Allies)}], " +
                           $"Resources: {GetResourceSummary(target.Resources)}, Readiness: {GetMilitaryReadiness(target):P0}");

                case SpyMissionType.Sabotage:
                    var activeResources = target.Resources.Where(r => !r.IsDestroyed).ToList();
                    if (activeResources.Count > 0 && rng.NextDouble() < 0.6)
                    {
                        var res = activeResources[rng.Next(activeResources.Count)];
                        res.IsDestroyed = true;
                        return (true, $"SABOTAGE: Destroyed {target.Name.ToUpper()}'s {res.Type} production facility! Their economy takes a hit.");
                    }
                    else
                    {
                        int nukesDestroyed = Math.Min(3, target.Nukes);
                        target.Nukes -= nukesDestroyed;
                        return (true, $"SABOTAGE: Infiltrated {target.Name.ToUpper()}'s arsenal — {nukesDestroyed} warheads disabled!");
                    }

                case SpyMissionType.StealMoney:
                    long stolen = Math.Min(target.Money / 4, 500 + rng.Next(1500));
                    target.Money -= stolen;
                    GameEngine.Player.Money += stolen;
                    return (true, $"THEFT: Siphoned ${stolen}M from {target.Name.ToUpper()}'s treasury! Their military readiness drops to {GetMilitaryReadiness(target):P0}.");

                case SpyMissionType.DelayLaunch:
                    target.Nukes = Math.Max(0, target.Nukes - 2);
                    target.AngerLevel = Math.Max(0, target.AngerLevel - 3);
                    return (true, $"SABOTAGE: Corrupted {target.Name.ToUpper()}'s launch codes — 2 warheads disabled, anger reduced.");

                default:
                    return (false, "Unknown mission type.");
            }
        }

        public static void TickSpies()
        {
            if (GameEngine.Player.SpyCooldown > 0)
                GameEngine.Player.SpyCooldown--;

            for (int i = GameEngine.Player.ActiveSpies.Count - 1; i >= 0; i--)
            {
                var spy = GameEngine.Player.ActiveSpies[i];
                if (!spy.IsActive) continue;
                spy.TicksRemaining--;
                if (spy.TicksRemaining <= 0)
                    spy.IsActive = false;
            }
        }

        public static List<Spy> GetCompletedSpies()
        {
            return GameEngine.Player.ActiveSpies
                .Where(s => !s.IsActive && !s.IsRevealed && s.TicksRemaining <= 0)
                .ToList();
        }

        // ── AI Spy Operations Against the Player ──────────────────────────────
        /// Returns log messages for any AI spy actions that resolve this tick.
        public static List<string> TickAISpies()
        {
            var logs = new List<string>();

            foreach (var n in GameEngine.Nations.Values)
            {
                if (n.IsDefeated || n.IsHumanControlled) continue;
                if (!n.IsHostileToPlayer) continue;
                if (n.Money < 400) continue; // too broke to spy

                // Higher difficulty = more spy-savvy. Chance per tick: 1-3%
                double spyChance = 0.005 + n.Difficulty * 0.005;

                // Tech nations (RareEarth) are better at espionage
                if (n.Resources.Any(r => r.Type == ResourceType.RareEarth && !r.IsDestroyed))
                    spyChance *= 1.5;

                if (rng.NextDouble() >= spyChance) continue;

                n.Money -= 300; // AI pays for espionage too

                // Pick mission weighted by what would hurt the player most
                SpyMissionType mission;
                double roll = rng.NextDouble();
                if (GameEngine.Player.Money > 3000 && roll < 0.3)
                    mission = SpyMissionType.StealMoney;
                else if (roll < 0.55)
                    mission = SpyMissionType.Sabotage;
                else if (roll < 0.75)
                    mission = SpyMissionType.DelayLaunch;
                else
                    mission = SpyMissionType.Intel; // AI uses intel to decide strategy

                // Detection by player (inverse of AI detection — player's tech level helps)
                double playerDetectChance = 0.25 + GameEngine.Player.CyberOpsLevel * 0.10;
                if (rng.NextDouble() < playerDetectChance)
                {
                    logs.Add($"[COUNTER-INTEL] Enemy spy from {n.Name.ToUpper()} intercepted! Mission: {mission} — NEUTRALIZED.");
                    n.AngerLevel = Math.Min(10, n.AngerLevel + 1);
                    continue;
                }

                // Spy succeeds — apply damage to player
                string result = ResolveAISpyOnPlayer(n, mission);
                logs.Add(result);
            }

            return logs;
        }

        private static string ResolveAISpyOnPlayer(Nation attacker, SpyMissionType mission)
        {
            switch (mission)
            {
                case SpyMissionType.StealMoney:
                    long stolen = Math.Min(GameEngine.Player.Money / 5, 200 + rng.Next(800));
                    GameEngine.Player.Money = Math.Max(0, GameEngine.Player.Money - stolen);
                    attacker.Money += stolen;
                    return $"[ESPIONAGE] {attacker.Name.ToUpper()} agents siphoned ${stolen}M from your treasury!";

                case SpyMissionType.Sabotage:
                    var playerRes = GameEngine.Player.Resources.Where(r => !r.IsDestroyed).ToList();
                    if (playerRes.Count > 0 && rng.NextDouble() < 0.5)
                    {
                        var target = playerRes[rng.Next(playerRes.Count)];
                        target.IsDestroyed = true;
                        return $"[ESPIONAGE] {attacker.Name.ToUpper()} saboteurs destroyed your {target.Type} production facility!";
                    }
                    else
                    {
                        // Degrade a random defense by 1 level
                        int defType = rng.Next(3);
                        if (defType == 0 && GameEngine.Player.IronDomeLevel > 0)
                        {
                            GameEngine.Player.IronDomeLevel--;
                            return $"[ESPIONAGE] {attacker.Name.ToUpper()} saboteurs damaged your Iron Dome systems! Level reduced.";
                        }
                        else if (defType == 1 && GameEngine.Player.BunkerLevel > 0)
                        {
                            GameEngine.Player.BunkerLevel--;
                            return $"[ESPIONAGE] {attacker.Name.ToUpper()} saboteurs compromised your Bunker Network! Level reduced.";
                        }
                        else if (GameEngine.Player.VaccineLevel > 0)
                        {
                            GameEngine.Player.VaccineLevel--;
                            return $"[ESPIONAGE] {attacker.Name.ToUpper()} saboteurs contaminated your Vaccine stockpile! Level reduced.";
                        }
                        int nukesLost = Math.Min(2, GameEngine.Player.StandardNukes);
                        GameEngine.Player.StandardNukes -= nukesLost;
                        return $"[ESPIONAGE] {attacker.Name.ToUpper()} saboteurs disabled {nukesLost} of your warheads!";
                    }

                case SpyMissionType.DelayLaunch:
                    // Temporarily jam player's satellites
                    if (!GameEngine.Player.IsSatelliteBlind)
                    {
                        GameEngine.Player.SatelliteBlindUntil = DateTime.Now.AddSeconds(15);
                        return $"[ESPIONAGE] {attacker.Name.ToUpper()} hackers jammed your satellite uplink for 15 seconds!";
                    }
                    int nukesDisabled = Math.Min(3, GameEngine.Player.StandardNukes);
                    GameEngine.Player.StandardNukes -= nukesDisabled;
                    return $"[ESPIONAGE] {attacker.Name.ToUpper()} agents corrupted {nukesDisabled} launch codes!";

                default: // Intel — AI just gathers info, no player-visible effect
                    return $"[COUNTER-INTEL] Detected surveillance activity from {attacker.Name.ToUpper()}. No damage.";
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // AI STRATEGIC BEHAVIOR — AI uses sanctions, spies, UN proposals
        // ═══════════════════════════════════════════════════════════════════════

        /// Called every ~15 ticks. Returns log messages and any new UN resolutions for player voting.
        public static (List<string> logs, List<UNResolution> newResolutions) TickAIStrategicActions()
        {
            var logs = new List<string>();
            var newResolutions = new List<UNResolution>();

            // Already a vote in progress? Skip AI proposals.
            bool voteInProgress = GameEngine.UNResolutions.Any(r => r.IsVoting);

            foreach (var n in GameEngine.Nations.Values)
            {
                if (n.IsDefeated || n.IsHumanControlled) continue;

                // ── AI Sanctions ──
                // Hostile nations with money will sanction the player
                if (n.IsHostileToPlayer && n.Money > 800 && n.AngerLevel >= 4)
                {
                    bool alreadySanctioning = GameEngine.ActiveSanctions.Any(s => s.ImposedBy == n.Name && s.Target == GameEngine.Player.NationName);
                    if (!alreadySanctioning && rng.NextDouble() < 0.08 + n.Difficulty * 0.02)
                    {
                        SanctionType sType = n.AngerLevel >= 7 ? SanctionType.Full :
                                             n.AngerLevel >= 5 ? SanctionType.Arms : SanctionType.Trade;
                        ImposeSanction(n.Name, GameEngine.Player.NationName, sType, 25 + rng.Next(20));
                        logs.Add($"[SANCTIONS] {n.Name.ToUpper()} has imposed {sType.ToString().ToUpper()} sanctions against you!");
                    }
                }

                // Nations also sanction each other's enemies
                if (rng.NextDouble() < 0.02)
                {
                    var enemies = GameEngine.Nations.Values
                        .Where(e => !e.IsDefeated && e.Name != n.Name && !n.Allies.Contains(e.Name) && e.AngerLevel > 3)
                        .ToList();
                    if (enemies.Count > 0)
                    {
                        var enemy = enemies[rng.Next(enemies.Count)];
                        if (!GameEngine.ActiveSanctions.Any(s => s.ImposedBy == n.Name && s.Target == enemy.Name))
                        {
                            ImposeSanction(n.Name, enemy.Name, SanctionType.Trade, 20 + rng.Next(15));
                            logs.Add($"[WORLD EVENT] {n.Name.ToUpper()} imposed trade sanctions on {enemy.Name.ToUpper()}.");
                        }
                    }
                }

                // ── AI UN Proposals ──
                if (!voteInProgress && rng.NextDouble() < 0.015 + n.Difficulty * 0.005)
                {
                    float health = (float)n.Population / n.MaxPopulation;

                    UNResolutionType? proposal = null;
                    string? target = null;

                    // Weak nations propose ceasefires
                    if (health < 0.3f && rng.NextDouble() < 0.5)
                    {
                        proposal = UNResolutionType.Ceasefire;
                    }
                    // Angry nations propose sanctions against player
                    else if (n.IsHostileToPlayer && n.AngerLevel >= 5 && rng.NextDouble() < 0.4)
                    {
                        proposal = UNResolutionType.Sanctions;
                        target = GameEngine.Player.NationName;
                    }
                    // Nations with few nukes propose nuclear freeze
                    else if (n.Nukes < 10 && rng.NextDouble() < 0.3)
                    {
                        proposal = UNResolutionType.NuclearFreeze;
                    }
                    // Damaged nations propose humanitarian aid
                    else if (health < 0.5f && rng.NextDouble() < 0.3)
                    {
                        proposal = UNResolutionType.HumanitarianAid;
                    }

                    if (proposal.HasValue)
                    {
                        var res = ProposeResolution(proposal.Value, n.Name, target);
                        res.Votes[n.Name] = UNVote.Yes; // proposer votes yes
                        CastAIVotes(res);
                        voteInProgress = true; // prevent multiple proposals per tick
                        newResolutions.Add(res); // let UI prompt player to vote

                        string targetStr = target != null ? $" against {target.ToUpper()}" : "";
                        logs.Add($"[UN SECURITY COUNCIL] {n.Name.ToUpper()} proposes {proposal.Value}{targetStr}.");
                    }
                }
            }

            return (logs, newResolutions);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // 3. UN SECURITY COUNCIL — Resolutions, Votes, Vetoes
        // ═══════════════════════════════════════════════════════════════════════

        public static UNResolution ProposeResolution(UNResolutionType type, string proposedBy, string? targetNation = null)
        {
            var res = new UNResolution
            {
                Type = type,
                ProposedBy = proposedBy,
                TargetNation = targetNation,
                IsVoting = true,
                VotingTicksLeft = UNConstants.VotingDuration
            };
            GameEngine.UNResolutions.Add(res);
            return res;
        }

        public static void CastAIVotes(UNResolution res)
        {
            foreach (var n in GameEngine.Nations.Values)
            {
                if (n.IsDefeated || n.IsHumanControlled) continue;
                if (res.Votes.ContainsKey(n.Name)) continue;

                UNVote vote = DecideAIVote(n, res);
                res.Votes[n.Name] = vote;
            }
        }

        private static UNVote DecideAIVote(Nation nation, UNResolution res)
        {
            float yesChance = 0.4f;

            if (res.Type == UNResolutionType.Sanctions && res.TargetNation != null)
            {
                if (nation.IsHostileToPlayer && res.TargetNation == GameEngine.Player.NationName)
                    yesChance += 0.3f;
                if (nation.Allies.Contains(res.TargetNation))
                    yesChance -= 0.4f;
                if (res.TargetNation == nation.Name)
                    yesChance = 0.0f;
                // Nations already sanctioning the target love this
                if (GameEngine.ActiveSanctions.Any(s => s.ImposedBy == nation.Name && s.Target == res.TargetNation))
                    yesChance += 0.25f;
            }

            if (res.Type == UNResolutionType.Ceasefire)
            {
                float health = (float)nation.Population / nation.MaxPopulation;
                yesChance += (1f - health) * 0.3f;
                if (nation.AngerLevel > 6) yesChance -= 0.2f;
                // Sanctioned nations want peace (they can't fight anyway)
                if (nation.IsSanctioned) yesChance += 0.2f;
            }

            if (res.Type == UNResolutionType.NuclearFreeze)
            {
                if (nation.Nukes < 15) yesChance += 0.2f;
                if (nation.Nukes > 25) yesChance -= 0.3f;
                // Nations with uranium oppose it (they want to build more)
                if (nation.Resources.Any(r => r.Type == ResourceType.Uranium && !r.IsDestroyed))
                    yesChance -= 0.15f;
            }

            if (res.Type == UNResolutionType.HumanitarianAid)
            {
                yesChance += 0.3f;
                float health = (float)nation.Population / nation.MaxPopulation;
                if (health < 0.5f) yesChance += 0.2f; // damaged nations really want this
            }

            if (res.Type == UNResolutionType.NoFirstStrike)
            {
                yesChance += 0.1f;
                if (nation.AngerLevel > 5) yesChance -= 0.3f;
            }

            yesChance += nation.Diplomacy.DiplomacyMood * 0.15f;

            bool isP5 = UNConstants.P5Members.Contains(nation.Name);
            if (isP5 && yesChance < 0.25f && res.TargetNation != null)
            {
                if (nation.Allies.Contains(res.TargetNation) || res.TargetNation == nation.Name)
                    return UNVote.Veto;
            }

            if (rng.NextDouble() < yesChance)
                return UNVote.Yes;
            if (rng.NextDouble() < 0.15)
                return UNVote.Abstain;
            return UNVote.No;
        }

        public static (bool passed, bool vetoed, string summary) ResolveVote(UNResolution res)
        {
            int yes = res.Votes.Values.Count(v => v == UNVote.Yes);
            int no = res.Votes.Values.Count(v => v == UNVote.No);
            bool vetoed = res.Votes.Any(v => v.Value == UNVote.Veto);

            string typeName = res.Type switch
            {
                UNResolutionType.Ceasefire => "CEASEFIRE",
                UNResolutionType.Sanctions => $"SANCTIONS ON {res.TargetNation?.ToUpper()}",
                UNResolutionType.NoFirstStrike => "NO FIRST STRIKE PACT",
                UNResolutionType.NuclearFreeze => "NUCLEAR FREEZE",
                UNResolutionType.HumanitarianAid => "HUMANITARIAN AID",
                _ => "RESOLUTION"
            };

            if (vetoed)
            {
                string vetoer = res.Votes.First(v => v.Value == UNVote.Veto).Key;
                res.IsVoting = false;
                return (false, true, $"UN {typeName} — VETOED by {vetoer.ToUpper()} (Yes: {yes}, No: {no})");
            }

            bool passed = yes > no;
            res.IsVoting = false;

            if (passed)
            {
                res.IsActive = true;
                res.EffectTicksLeft = UNConstants.ResolutionDuration;
                ApplyResolutionEffect(res);
                return (true, false, $"UN {typeName} — PASSED (Yes: {yes}, No: {no})");
            }

            return (false, false, $"UN {typeName} — REJECTED (Yes: {yes}, No: {no})");
        }

        private static void ApplyResolutionEffect(UNResolution res)
        {
            switch (res.Type)
            {
                case UNResolutionType.Sanctions:
                    if (res.TargetNation != null)
                        ImposeSanction("UN", res.TargetNation, SanctionType.Full, UNConstants.ResolutionDuration);
                    break;

                case UNResolutionType.HumanitarianAid:
                    foreach (var n in GameEngine.Nations.Values)
                        if (!n.IsDefeated) n.Population = Math.Min(n.MaxPopulation, n.Population + n.MaxPopulation / 50);
                    GameEngine.Player.Population = Math.Min(GameEngine.Player.MaxPopulation, GameEngine.Player.Population + GameEngine.Player.MaxPopulation / 50);
                    break;

                case UNResolutionType.Ceasefire:
                    // Reduce anger globally — ceasefire calms things down
                    foreach (var n in GameEngine.Nations.Values)
                        n.AngerLevel = Math.Max(0, n.AngerLevel - 3);
                    break;

                case UNResolutionType.NoFirstStrike:
                    // Reduce hostility globally
                    foreach (var n in GameEngine.Nations.Values)
                        if (n.AngerLevel < 5) n.IsHostileToPlayer = false;
                    break;
            }
        }

        public static void TickUNResolutions()
        {
            if (GameEngine.Player.UNCooldown > 0)
                GameEngine.Player.UNCooldown--;

            for (int i = GameEngine.UNResolutions.Count - 1; i >= 0; i--)
            {
                var res = GameEngine.UNResolutions[i];

                if (res.IsVoting)
                {
                    res.VotingTicksLeft--;
                    if (res.VotingTicksLeft <= 0)
                    {
                        CastAIVotes(res);
                    }
                }
                else if (res.IsActive)
                {
                    res.EffectTicksLeft--;
                    if (res.EffectTicksLeft <= 0)
                    {
                        res.IsActive = false;
                        if (res.Type == UNResolutionType.Sanctions && res.TargetNation != null)
                            LiftSanction("UN", res.TargetNation);
                    }
                }

                if (!res.IsVoting && !res.IsActive)
                    GameEngine.UNResolutions.RemoveAt(i);
            }
        }

        public static bool IsResolutionActive(UNResolutionType type)
        {
            return GameEngine.UNResolutions.Any(r => r.IsActive && r.Type == type);
        }

        public static int GetAngerPenaltyForViolation(UNResolutionType type)
        {
            return type switch
            {
                UNResolutionType.Ceasefire => 4,
                UNResolutionType.NoFirstStrike => 3,
                UNResolutionType.NuclearFreeze => 2,
                _ => 1
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // 4. NUCLEAR WINTER — Global extinction event
        // ═══════════════════════════════════════════════════════════════════════

        public static (bool justStarted, long playerLoss, List<(string name, long loss)> nationLosses) TickNuclearWinter()
        {
            bool justStarted = false;
            long playerLoss = 0;
            var nationLosses = new List<(string, long)>();

            if (!GameEngine.NuclearWinterActive && GameEngine.GlobalNukesFired >= GameEngine.NuclearWinterThreshold)
            {
                GameEngine.NuclearWinterActive = true;
                GameEngine.NuclearWinterTick = 0;
                GameEngine.NuclearWinterSeverity = 0.1f;
                justStarted = true;
            }

            if (!GameEngine.NuclearWinterActive)
                return (false, 0, nationLosses);

            GameEngine.NuclearWinterTick++;
            GameEngine.NuclearWinterSeverity = Math.Min(0.8f, 0.1f + GameEngine.NuclearWinterTick * 0.008f);

            // Every 5 ticks, all nations lose population
            if (GameEngine.NuclearWinterTick % 5 == 0)
            {
                float lossFraction = GameEngine.NuclearWinterSeverity * 0.02f;

                long pLoss = (long)(GameEngine.Player.Population * lossFraction);
                if (pLoss > 0)
                {
                    GameEngine.Player.Population = Math.Max(0, GameEngine.Player.Population - pLoss);
                    playerLoss = pLoss;
                }

                foreach (var n in GameEngine.Nations.Values)
                {
                    if (n.IsDefeated) continue;
                    long nLoss = (long)(n.Population * lossFraction);
                    if (nLoss > 0)
                    {
                        n.Population = Math.Max(0, n.Population - nLoss);
                        nationLosses.Add((n.Name, nLoss));
                        // Don't mark human-controlled nations as defeated — their own client handles that
                        if (n.Population <= 0 && !n.IsHumanControlled) n.IsDefeated = true;
                    }
                }

                // Nuclear winter also destroys agriculture
                if (GameEngine.NuclearWinterSeverity > 0.4f && rng.NextDouble() < 0.1)
                {
                    foreach (var n in GameEngine.Nations.Values)
                    {
                        var agri = n.Resources.FirstOrDefault(r => r.Type == ResourceType.Agriculture && !r.IsDestroyed);
                        if (agri != null && rng.NextDouble() < 0.15)
                            agri.IsDestroyed = true;
                    }
                    var playerAgri = GameEngine.Player.Resources.FirstOrDefault(r => r.Type == ResourceType.Agriculture && !r.IsDestroyed);
                    if (playerAgri != null && rng.NextDouble() < 0.15)
                        playerAgri.IsDestroyed = true;
                }
            }

            return (justStarted, playerLoss, nationLosses);
        }

        public static string GetWinterStatusText()
        {
            if (!GameEngine.NuclearWinterActive)
            {
                int remaining = GameEngine.NuclearWinterThreshold - GameEngine.GlobalNukesFired;
                if (remaining <= 10)
                    return $"DOOMSDAY CLOCK: {remaining} nukes until nuclear winter";
                return "";
            }

            int severity = (int)(GameEngine.NuclearWinterSeverity * 100);
            return $"NUCLEAR WINTER — Severity: {severity}% — All nations losing population";
        }
    }
}
