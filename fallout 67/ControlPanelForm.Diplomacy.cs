using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;

namespace fallover_67
{
    public partial class ControlPanelForm : Form
    {
        // ── Diplomacy State ─────────────────────────────────────────────────────
        private int _diplomacyTick = 0;
        private const int DiplomacyTickInterval = 20;  // every 20 game-timer ticks
        private int _resourceShareTick = 0;
        private const int ResourceShareInterval = 15;
        private SummitFlight _interceptableFlight = null;

        // ── Player Initiates Alliance ───────────────────────────────────────────
        private void BtnDiplomacy_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedTarget)) { MessageBox.Show("Select a target nation first.", "NO TARGET"); return; }
            if (!GameEngine.Nations.ContainsKey(selectedTarget)) return;

            var target = GameEngine.Nations[selectedTarget];
            if (target.IsDefeated) { MessageBox.Show("That nation has been defeated.", "INVALID"); return; }
            // Human players — send alliance request via multiplayer
            if (target.IsHumanControlled)
            {
                if (_mpClient == null) { MessageBox.Show("Multiplayer not connected.", "ERROR"); return; }
                _ = _mpClient.SendGameActionAsync(new
                {
                    type = "alliance_request",
                    from = GameEngine.Player.NationName,
                    target = selectedTarget
                });
                LogMsg($"[DIPLOMACY] Alliance request sent to {selectedTarget.ToUpper()}. Awaiting response...");
                AddNotification("ALLIANCE REQUEST SENT", $"Awaiting {selectedTarget}'s response", cyanText, 5f);
                GameEngine.Player.DiplomacyCooldown = 15;
                return;
            }
            if (GameEngine.Player.Allies.Contains(selectedTarget)) { MessageBox.Show($"You are already allied with {selectedTarget}.", "ALREADY ALLIED"); return; }
            if (GameEngine.Player.Allies.Count >= GameEngine.Player.MaxAllies) { MessageBox.Show($"Maximum {GameEngine.Player.MaxAllies} alliances reached.", "ALLIANCE CAP"); return; }
            if (GameEngine.Player.DiplomacyCooldown > 0) { MessageBox.Show($"Diplomacy cooldown: {GameEngine.Player.DiplomacyCooldown}s remaining.", "COOLDOWN"); return; }
            if (GameEngine.Player.IsSatelliteBlind) { MessageBox.Show("SATELLITE UPLINK OFFLINE — Diplomatic channels unavailable.", "SYSTEM ERROR"); return; }

            // Check if a summit is already in progress
            if (GameEngine.ActiveSummits.Any(s => s.IsPlayerInitiated && !s.ShotDown && s.Result == null))
            {
                MessageBox.Show("A diplomatic summit is already in progress.", "BUSY"); return;
            }

            float baseProb = DiplomacyEngine.CalculateAcceptanceProbability(selectedTarget);

            // Open negotiation minigame
            var minigame = new DiplomacyMinigameForm(selectedTarget, baseProb);
            if (minigame.ShowDialog(this) != DialogResult.OK) return;

            float bonus = minigame.NegotiationBonus;

            // Start summit plane flight
            string host = DiplomacyEngine.GetSummitHost(GameEngine.Player.NationName, selectedTarget);
            StartPlayerSummit(selectedTarget, host, bonus);
        }

        private void StartPlayerSummit(string targetNation, string hostNation, float negotiationBonus)
        {
            var (playerLat, playerLng) = DiplomacyEngine.GetNationCoords(GameEngine.Player.NationName);
            var (hostLat, hostLng) = DiplomacyEngine.GetNationCoords(hostNation);

            var summit = new SummitFlight
            {
                Nation1 = GameEngine.Player.NationName,
                Nation2 = targetNation,
                HostNation = hostNation,
                StartLat = playerLat,
                StartLng = playerLng,
                EndLat = hostLat,
                EndLng = hostLng,
                Speed = 0.12f,
                IsPlayerInitiated = true,
                IsPlayerPlane = true,
                NegotiationBonus = negotiationBonus
            };

            lock (_animLock) GameEngine.ActiveSummits.Add(summit);

            logBox.SelectionColor = cyanText;
            LogMsg($"[DIPLOMACY] Presidential plane departing for summit with {targetNation.ToUpper()} at {hostNation.ToUpper()}...");
            AddNotification("SUMMIT IN PROGRESS", $"Plane en route to {hostNation}", Color.Cyan, 6f);

            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "diplomacy_summit", nation1 = GameEngine.Player.NationName, nation2 = targetNation, host = hostNation });
        }

        // ── AI Summit ───────────────────────────────────────────────────────────
        private void StartAISummit(string initiator, string target)
        {
            string host = DiplomacyEngine.GetSummitHost(initiator, target);
            var (initLat, initLng) = DiplomacyEngine.GetNationCoords(initiator);
            var (hostLat, hostLng) = DiplomacyEngine.GetNationCoords(host);

            var summit = new SummitFlight
            {
                Nation1 = initiator,
                Nation2 = target,
                HostNation = host,
                StartLat = initLat,
                StartLng = initLng,
                EndLat = hostLat,
                EndLng = hostLng,
                Speed = 0.10f,
                IsPlayerInitiated = false,
                IsPlayerPlane = false,
                NegotiationBonus = 0.15f // AI gets a flat bonus
            };

            lock (_animLock) GameEngine.ActiveSummits.Add(summit);

            logBox.SelectionColor = amberText;
            LogMsg($"[WORLD EVENT] {initiator.ToUpper()} is sending a diplomatic plane to {target.ToUpper()} for alliance talks.");
            AddNotification("AI SUMMIT", $"{initiator} → {target}", Color.Gold, 5f);
        }

        // ── Summit Phase Completion ─────────────────────────────────────────────
        private void HandleSummitPhaseComplete(SummitFlight summit)
        {
            if (summit.Phase == SummitPhase.FlyingToSummit)
            {
                // Arrived at summit location
                summit.Phase = SummitPhase.InSummit;
                summit.SummitTimer = 0f;
                summit.Progress = 0f;

                if (summit.IsPlayerInitiated)
                {
                    logBox.SelectionColor = cyanText;
                    LogMsg($"[DIPLOMACY] Summit underway at {summit.HostNation.ToUpper()}. Negotiations in progress...");
                }
            }
            else if (summit.Phase == SummitPhase.Returning)
            {
                // Plane returned home safely — finalize result
                FinalizeSummit(summit);
                lock (_animLock) GameEngine.ActiveSummits.Remove(summit);
            }
        }

        private void HandleSummitMeeting(SummitFlight summit, float dt)
        {
            summit.SummitTimer += dt;
            if (summit.SummitTimer >= 5f) // 5 seconds at summit
            {
                // Decide outcome
                bool accepted;
                if (summit.IsPlayerInitiated)
                    accepted = DiplomacyEngine.TryFormAlliance(summit.Nation2, summit.NegotiationBonus);
                else
                    accepted = rng.NextDouble() < 0.55; // AI-AI has decent odds

                summit.Result = accepted ? SummitOutcome.Accepted : SummitOutcome.Rejected;

                // Set up return flight
                summit.Phase = SummitPhase.Returning;
                summit.StartLat = summit.EndLat;
                summit.StartLng = summit.EndLng;
                var (homeLat, homeLng) = DiplomacyEngine.GetNationCoords(summit.Nation1);
                summit.EndLat = homeLat;
                summit.EndLng = homeLng;
                summit.Progress = 0f;

                if (summit.IsPlayerInitiated)
                {
                    string resultStr = accepted ? "ALLIANCE ACCEPTED" : "ALLIANCE REJECTED";
                    logBox.SelectionColor = accepted ? greenText : amberText;
                    LogMsg($"[DIPLOMACY] {resultStr}! Plane returning home...");
                }
                else
                {
                    if (accepted)
                    {
                        logBox.SelectionColor = amberText;
                        LogMsg($"[WORLD EVENT] {summit.Nation1.ToUpper()} and {summit.Nation2.ToUpper()} have agreed to an alliance!");
                    }
                }
            }
        }

        private void FinalizeSummit(SummitFlight summit)
        {
            if (summit.Result == SummitOutcome.Accepted)
            {
                DiplomacyEngine.FormAlliance(summit.Nation1, summit.Nation2);

                if (summit.IsPlayerInitiated)
                {
                    logBox.SelectionColor = greenText;
                    LogMsg($"[DIPLOMACY] Alliance with {summit.Nation2.ToUpper()} is now ACTIVE! Resources will be shared.");
                    AddNotification("ALLIANCE FORMED", $"{summit.Nation2} is now your ally", Color.Cyan, 8f);
                    ProfileManager.RecordAllianceFormed();
                }
                else
                {
                    AddNotification("NEW AI ALLIANCE", $"{summit.Nation1} + {summit.Nation2}", Color.Gold, 5f);
                }

                if (_isMultiplayer && _mpClient != null)
                    _ = _mpClient.SendGameActionAsync(new { type = "diplomacy_alliance", nation1 = summit.Nation1, nation2 = summit.Nation2 });
            }
            else if (summit.IsPlayerInitiated)
            {
                logBox.SelectionColor = amberText;
                LogMsg($"[DIPLOMACY] {summit.Nation2.ToUpper()} has declined the alliance. Cooldown active.");
                GameEngine.Player.DiplomacyCooldown = 30;
            }

            RefreshData();
        }

        // ── Plane Shot Down ─────────────────────────────────────────────────────
        private void HandlePlaneShotDown(SummitFlight summit, string shooterName)
        {
            summit.ShotDown = true;

            // Create explosion at plane position
            float lat = summit.StartLat + (summit.EndLat - summit.StartLat) * summit.Progress;
            float lng = summit.StartLng + (summit.EndLng - summit.StartLng) * summit.Progress;
            lock (_animLock) activeExplosions.Add(new ExplosionEffect
            {
                Center = new PointLatLng(lat, lng),
                MaxRadius = 35f,
                DamageLines = new[] { "PLANE SHOT DOWN", $"by {shooterName.ToUpper()}" },
                IsPlayerTarget = summit.IsPlayerPlane
            });

            if (summit.IsPlayerPlane)
            {
                // Severe consequences for the player
                long popLoss = (long)(GameEngine.Player.Population * 0.07);
                GameEngine.Player.Population -= popLoss;
                GameEngine.Player.Money = Math.Max(0, GameEngine.Player.Money - 500);
                GameEngine.Player.DiplomacyCooldown = 60;

                logBox.SelectionColor = redText;
                LogMsg($"[CATASTROPHE] Presidential plane shot down by {shooterName.ToUpper()}! {popLoss:N0} dignitaries killed. $500M in damages.");
                AddNotification("PLANE DESTROYED", $"Shot down by {shooterName}!", Color.Red, 8f);

                // Shooter becomes hostile
                if (GameEngine.Nations.TryGetValue(shooterName, out var shooter))
                    shooter.IsHostileToPlayer = true;
            }
            else
            {
                logBox.SelectionColor = amberText;
                LogMsg($"[WORLD EVENT] Diplomatic plane from {summit.Nation1.ToUpper()} shot down by {shooterName.ToUpper()}!");

                // Both nations become hostile to shooter
                if (GameEngine.Nations.TryGetValue(summit.Nation1, out var n1) && GameEngine.Nations.TryGetValue(shooterName, out var shooter1))
                {
                    n1.IsHostileToPlayer = shooterName == GameEngine.Player.NationName;
                    n1.AngerLevel = Math.Min(10, n1.AngerLevel + 3);
                }
                if (GameEngine.Nations.TryGetValue(summit.Nation2, out var n2))
                {
                    n2.IsHostileToPlayer = shooterName == GameEngine.Player.NationName;
                    n2.AngerLevel = Math.Min(10, n2.AngerLevel + 3);
                }

                AddNotification("SUMMIT PLANE DESTROYED", $"{summit.Nation1} → {summit.Nation2}", Color.Orange, 6f);
            }

            RefreshData();
        }

        // ── Player Intercepts AI Plane ──────────────────────────────────────────
        private void TryInterceptSummitPlane(SummitFlight summit)
        {
            if (summit.ShotDown || summit.Phase != SummitPhase.FlyingToSummit) return;

            _gameState = GameState.PlaneIntercept;
            var interceptGame = new PlaneInterceptForm(summit.Nation1, summit.Nation2);
            interceptGame.ShowDialog(this);
            _gameState = GameState.Playing;

            if (interceptGame.PlaneDestroyed)
            {
                HandlePlaneShotDown(summit, GameEngine.Player.NationName);
                lock (_animLock) GameEngine.ActiveSummits.Remove(summit);
            }
        }

        // ── Player Betrays Ally ─────────────────────────────────────────────────
        private void PlayerBetrayAlly(string allyName)
        {
            if (!GameEngine.Player.Allies.Contains(allyName)) return;
            if (GameEngine.Player.BetrayalCooldown > 0) { MessageBox.Show($"Betrayal cooldown: {GameEngine.Player.BetrayalCooldown}s."); return; }

            var result = MessageBox.Show(
                $"BETRAY {allyName.ToUpper()}?\n\nThis will break the alliance and launch a surprise strike.\nAll nations will distrust you.",
                "CONFIRM BETRAYAL", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            DiplomacyEngine.BreakAlliance(GameEngine.Player.NationName, allyName);
            GameEngine.Player.BetrayalCooldown = 60;

            if (GameEngine.Nations.TryGetValue(allyName, out var victim))
            {
                victim.IsHostileToPlayer = true;
                victim.AngerLevel = 10;
                victim.Diplomacy.LastBetrayedBy = GameEngine.Player.NationName;
                victim.Diplomacy.DiplomacyMood = 0f;

                // Lower all nations' diplomacy mood toward player
                foreach (var n in GameEngine.Nations.Values)
                    n.Diplomacy.DiplomacyMood = Math.Max(0f, n.Diplomacy.DiplomacyMood - 0.15f);
            }

            logBox.SelectionColor = redText;
            LogMsg($"[BETRAYAL] You have betrayed {allyName.ToUpper()}! Alliance dissolved. Trust shattered.");
            AddNotification("BETRAYAL", $"Alliance with {allyName} broken", Color.Red, 8f);
            ProfileManager.RecordAllianceBroken();

            // Launch surprise strike
            if (GameEngine.Nations.ContainsKey(allyName))
                _ = FireSalvoAsync(allyName, 0, Math.Min(2, GameEngine.Player.StandardNukes));

            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "diplomacy_betrayal", betrayer = GameEngine.Player.NationName, victim = allyName });

            RefreshData();
        }

        // ── AI Betrayal Execution ───────────────────────────────────────────────
        private void ExecuteAIBetrayal(string betrayerName, string victimName)
        {
            DiplomacyEngine.BreakAlliance(betrayerName, victimName);

            if (GameEngine.Nations.TryGetValue(betrayerName, out var betrayer))
            {
                betrayer.Diplomacy.BetrayalCooldown = 60;

                if (GameEngine.Nations.TryGetValue(victimName, out var victim))
                {
                    victim.Diplomacy.LastBetrayedBy = betrayerName;
                    victim.AngerLevel = Math.Min(10, victim.AngerLevel + 4);
                }
            }

            bool affectsPlayer = victimName == GameEngine.Player.NationName;
            if (affectsPlayer)
            {
                logBox.SelectionColor = redText;
                LogMsg($"[BETRAYAL] {betrayerName.ToUpper()} HAS BETRAYED YOU! Alliance dissolved!");
                AddNotification("BETRAYED!", $"{betrayerName} stabbed you in the back!", Color.Red, 8f);

                if (GameEngine.Nations.TryGetValue(betrayerName, out var b))
                {
                    b.IsHostileToPlayer = true;
                    BroadcastAiLaunch(b, GameEngine.Player.NationName, 2);
                }
            }
            else
            {
                logBox.SelectionColor = amberText;
                LogMsg($"[WORLD EVENT] {betrayerName.ToUpper()} has betrayed {victimName.ToUpper()}! Surprise strike launched!");
                AddNotification("AI BETRAYAL", $"{betrayerName} betrayed {victimName}", Color.Orange, 6f);

                if (GameEngine.Nations.TryGetValue(betrayerName, out var b) && GameEngine.Nations.TryGetValue(victimName, out var v))
                    BroadcastAiLaunch(b, victimName, 2);
            }

            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "diplomacy_betrayal", betrayer = betrayerName, victim = victimName });

            RefreshData();
        }

        // ── Diplomacy Tick (called from GameTimer_Tick) ─────────────────────────
        private void TickDiplomacy()
        {
            // Decay cooldowns
            if (GameEngine.Player.DiplomacyCooldown > 0) GameEngine.Player.DiplomacyCooldown--;
            if (GameEngine.Player.BetrayalCooldown > 0) GameEngine.Player.BetrayalCooldown--;
            foreach (var n in GameEngine.Nations.Values)
                if (n.Diplomacy.BetrayalCooldown > 0) n.Diplomacy.BetrayalCooldown--;

            // Age alliances
            foreach (var n in GameEngine.Nations.Values)
                foreach (var key in n.Diplomacy.AllianceAge.Keys.ToList())
                    n.Diplomacy.AllianceAge[key]++;

            // Resource sharing
            _resourceShareTick++;
            if (_resourceShareTick >= ResourceShareInterval)
            {
                _resourceShareTick = 0;
                DiplomacyEngine.TickResourceSharing();
            }

            // AI diplomacy (only host in MP)
            if (_isMultiplayer && _mpClient != null && !_mpClient.IsHost) return;

            _diplomacyTick++;
            if (_diplomacyTick >= DiplomacyTickInterval)
            {
                _diplomacyTick = 0;

                // AI alliance attempts
                var allianceAttempt = DiplomacyEngine.AIConsiderAlliance();
                if (allianceAttempt.HasValue)
                    StartAISummit(allianceAttempt.Value.initiator, allianceAttempt.Value.target);

                // AI betrayal
                var betrayalAttempt = DiplomacyEngine.AIConsiderBetrayal();
                if (betrayalAttempt.HasValue)
                    ExecuteAIBetrayal(betrayalAttempt.Value.betrayer, betrayalAttempt.Value.victim);
            }

            // Shootdown checks for active flights (every 3 ticks)
            if (_diplomacyTick % 3 == 0)
            {
                foreach (var summit in GameEngine.ActiveSummits.ToList())
                {
                    if (summit.ShotDown || summit.Phase == SummitPhase.InSummit) continue;

                    var (shotDown, shooterName) = DiplomacyEngine.RollPlaneShootdown(summit);
                    if (shotDown)
                    {
                        HandlePlaneShotDown(summit, shooterName);
                        lock (_animLock) GameEngine.ActiveSummits.Remove(summit);
                    }
                }
            }

            // Offer intercept opportunity for AI summits near player
            foreach (var summit in GameEngine.ActiveSummits.ToList())
            {
                if (summit.ShotDown || summit.IsPlayerInitiated || summit.Phase != SummitPhase.FlyingToSummit) continue;

                float planeLat = summit.StartLat + (summit.EndLat - summit.StartLat) * summit.Progress;
                float planeLng = summit.StartLng + (summit.EndLng - summit.StartLng) * summit.Progress;
                float dx = planeLng - GameEngine.Player.MapX;
                float dy = planeLat - GameEngine.Player.MapY;

                if (dx * dx + dy * dy < 225f && _interceptableFlight != summit) // within ~15 degrees
                {
                    _interceptableFlight = summit;
                    AddNotification("INTERCEPT OPPORTUNITY", $"AI summit plane nearby — click to shoot down", Color.Yellow, 8f);
                }
            }
        }
    }
}
