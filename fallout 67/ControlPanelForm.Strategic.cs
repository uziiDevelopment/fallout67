using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace fallover_67
{
    public partial class ControlPanelForm : Form
    {
        // ── Strategic Layer UI ────────────────────────────────────────────────
        private Button btnSanction, btnDeploySpy, btnUNPropose, btnSendFood;
        private Label lblStrategicStatus;
        private int _economyTickCounter = 0;
        private int _stratTickCounter = 0;

        private void SetupStrategicUI()
        {
            // ── Strategic Controls — fits in the gap between diplomacy and logs ──
            GroupBox grpStrategic = CreateBox("STRATEGIC OPERATIONS", 10, 525, 800, 150);

            // Row 1: Sanctions
            btnSanction = CreateButton("IMPOSE SANCTIONS", 10, 22, 180, 30, Color.FromArgb(60, 40, 0), amberText);
            btnSanction.Click += BtnSanction_Click;

            // Row 1: Espionage
            btnDeploySpy = CreateButton("DEPLOY SPY", 200, 22, 150, 30, Color.FromArgb(0, 40, 20), Color.LimeGreen);
            btnDeploySpy.Click += BtnDeploySpy_Click;

            // Row 1: UN
            btnUNPropose = CreateButton("UN RESOLUTION", 360, 22, 170, 30, Color.FromArgb(0, 20, 60), Color.CornflowerBlue);
            btnUNPropose.Click += BtnUNPropose_Click;

            // Row 1: Food Aid
            btnSendFood = CreateButton("SEND FOOD AID", 540, 22, 150, 30, Color.FromArgb(0, 50, 0), Color.LightGreen);
            btnSendFood.Click += BtnSendFood_Click;

            // Status label — shows economy, sanctions, spies, UN, doomsday clock
            lblStrategicStatus = new Label
            {
                Location = new Point(10, 58),
                Size = new Size(780, 85),
                ForeColor = amberText,
                Font = new Font("Consolas", 8.5F, FontStyle.Bold),
                Text = ""
            };

            grpStrategic.Controls.Add(btnSanction);
            grpStrategic.Controls.Add(btnDeploySpy);
            grpStrategic.Controls.Add(btnUNPropose);
            grpStrategic.Controls.Add(btnSendFood);
            grpStrategic.Controls.Add(lblStrategicStatus);
            this.Controls.Add(grpStrategic);

            // Move log box down to make room
            var grpLogs = this.Controls.OfType<GroupBox>().FirstOrDefault(g => g.Text.Contains("TACTICAL"));
            if (grpLogs != null)
            {
                grpLogs.Location = new Point(10, 680);
            }
        }

        private void UpdateStrategicStatus()
        {
            if (lblStrategicStatus == null) return;

            var lines = new List<string>();

            // Economy line
            long income = StrategicEngine.CalculateIncome(
                GameEngine.Player.IndustryLevel,
                GameEngine.Player.Resources,
                GameEngine.Player.IncomeMultiplier,
                GameEngine.Player.NationName,
                GameEngine.Player.Allies);
            string resources = StrategicEngine.GetResourceSummary(GameEngine.Player.Resources);
            lines.Add($"ECONOMY: +${income}M/tick | Resources: {resources} | Multiplier: {GameEngine.Player.IncomeMultiplier:P0}");

            // Sanctions
            int sanctionsOnPlayer = StrategicEngine.GetSanctionCount(GameEngine.Player.NationName);
            int totalSanctions = GameEngine.ActiveSanctions.Count;
            string sanctionText = sanctionsOnPlayer > 0 ? $"SANCTIONS: {sanctionsOnPlayer} AGAINST YOU | " : "";
            if (totalSanctions > 0) sanctionText += $"{totalSanctions} active globally";
            else sanctionText += "No active sanctions";
            lines.Add(sanctionText);

            // Spies
            int activeSpies = GameEngine.Player.ActiveSpies.Count(s => s.IsActive);
            string spyCooldown = GameEngine.Player.SpyCooldown > 0 ? $" (cooldown: {GameEngine.Player.SpyCooldown}s)" : "";
            lines.Add($"SPIES: {activeSpies} deployed{spyCooldown}");

            // UN
            int activeResolutions = GameEngine.UNResolutions.Count(r => r.IsActive);
            int votingResolutions = GameEngine.UNResolutions.Count(r => r.IsVoting);
            string unCooldown = GameEngine.Player.UNCooldown > 0 ? $" (cooldown: {GameEngine.Player.UNCooldown}s)" : "";
            lines.Add($"UN COUNCIL: {activeResolutions} active | {votingResolutions} voting{unCooldown}");

            // Nuclear winter / doomsday clock
            string winterText = StrategicEngine.GetWinterStatusText();
            if (!string.IsNullOrEmpty(winterText))
                lines.Add(winterText);

            lblStrategicStatus.Text = string.Join("\n", lines);
        }

        // ── Strategic Timer Tick (called from GameTimer_Tick) ──────────────
        private void TickStrategicLayer()
        {
            _stratTickCounter++;

            // Economy: every 10 ticks (replaces old income system)
            _economyTickCounter++;
            if (_economyTickCounter >= 10)
            {
                _economyTickCounter = 0;
                StrategicEngine.TickEconomy();
            }

            // Player spies: every tick
            StrategicEngine.TickSpies();

            // Check for completed player spy missions
            var completed = StrategicEngine.GetCompletedSpies();
            foreach (var spy in completed)
            {
                var (success, report) = StrategicEngine.ResolveSpy(spy);
                logBox.SelectionColor = success ? Color.LimeGreen : redText;
                LogMsg($"[ESPIONAGE] {report}");
                GameEngine.Player.ActiveSpies.Remove(spy);

                if (success && _isMultiplayer && _mpClient != null)
                {
                    _ = _mpClient.SendGameActionAsync(new
                    {
                        type = "spy_result",
                        target = spy.TargetNation,
                        mission = spy.Mission.ToString(),
                        success = true
                    });
                }
            }

            // AI spies against the player — every 3 ticks
            if (_stratTickCounter % 3 == 0)
            {
                var aiSpyLogs = StrategicEngine.TickAISpies();
                foreach (var msg in aiSpyLogs)
                {
                    bool isAttack = msg.Contains("siphoned") || msg.Contains("saboteurs") || msg.Contains("hackers") || msg.Contains("corrupted") || msg.Contains("destroyed your");
                    logBox.SelectionColor = isAttack ? redText : amberText;
                    LogMsg(msg);
                    if (isAttack)
                        AddNotification("ESPIONAGE ATTACK", msg.Substring(msg.IndexOf(']') + 2), Color.Red, 5f);
                }
            }

            // AI strategic actions (sanctions, UN proposals) — every 15 ticks
            if (_stratTickCounter % 15 == 0)
            {
                var (aiLogs, newResolutions) = StrategicEngine.TickAIStrategicActions();
                foreach (var msg in aiLogs)
                {
                    bool isSanction = msg.Contains("SANCTIONS");
                    bool isUN = msg.Contains("UN SECURITY");
                    logBox.SelectionColor = isSanction ? amberText : (isUN ? Color.CornflowerBlue : amberText);
                    LogMsg(msg);

                    if (isSanction && msg.Contains("against you"))
                        AddNotification("SANCTIONED", msg.Substring(msg.IndexOf(']') + 2), amberText, 6f);
                    if (isUN)
                        AddNotification("UN PROPOSAL", msg.Substring(msg.IndexOf(']') + 2), Color.CornflowerBlue, 5f);
                }

                // Prompt player to vote on AI-proposed resolutions
                foreach (var res in newResolutions)
                    PromptPlayerUNVote(res);
            }

            // UN: every tick
            StrategicEngine.TickUNResolutions();

            // Check for resolutions ready to resolve
            var readyToResolve = GameEngine.UNResolutions
                .Where(r => r.IsVoting && r.VotingTicksLeft <= 0)
                .ToList();
            foreach (var res in readyToResolve)
            {
                var (passed, vetoed, summary) = StrategicEngine.ResolveVote(res);
                logBox.SelectionColor = passed ? Color.CornflowerBlue : amberText;
                LogMsg($"[UN SECURITY COUNCIL] {summary}");
                if (passed)
                    AddNotification("UN RESOLUTION PASSED", summary, Color.CornflowerBlue, 6f);
                else if (vetoed)
                    AddNotification("UN RESOLUTION VETOED", summary, Color.OrangeRed, 5f);
            }

            // Nuclear winter: every tick (only host runs simulation in MP)
            if (!_isMultiplayer || _mpClient == null || _mpClient.IsHost)
            {
                var (justStarted, playerLoss, nationLosses) = StrategicEngine.TickNuclearWinter();
                if (justStarted)
                {
                    logBox.SelectionColor = Color.DeepSkyBlue;
                    LogMsg("[GLOBAL EVENT] NUCLEAR WINTER HAS BEGUN — Temperatures plunging worldwide. Crops failing. Population declining.");
                    AddNotification("NUCLEAR WINTER", "Global temperatures plummeting", Color.DeepSkyBlue, 10f);
                }
                if (playerLoss > 0)
                {
                    logBox.SelectionColor = Color.DeepSkyBlue;
                    LogMsg($"[NUCLEAR WINTER] Your nation lost {playerLoss:N0} citizens to famine and radiation.");
                }

                // Host broadcasts nuclear winter state every 5 ticks
                if (_stratTickCounter % 5 == 0)
                    BroadcastNuclearWinterSync();
            }

            // Update strategic display every tick
            UpdateStrategicStatus();
        }

        // ── Sanctions ─────────────────────────────────────────────────────────
        private void BtnSanction_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedTarget) || !GameEngine.Nations.ContainsKey(selectedTarget))
            {
                MessageBox.Show("Select a target nation on the map first.", "NO TARGET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (GameEngine.Player.Allies.Contains(selectedTarget))
            {
                MessageBox.Show("Cannot sanction your own ally.", "INVALID TARGET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Already sanctioned by player?
            if (GameEngine.ActiveSanctions.Any(s => s.ImposedBy == GameEngine.Player.NationName && s.Target == selectedTarget))
            {
                MessageBox.Show($"You already have sanctions on {selectedTarget}.", "ALREADY SANCTIONED", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Pick sanction type
            var form = new Form
            {
                Text = "IMPOSE SANCTIONS",
                Size = new Size(350, 220),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(20, 20, 10),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label { Text = $"Choose sanction type against {selectedTarget.ToUpper()}:", Location = new Point(10, 15), Size = new Size(320, 25), ForeColor = amberText, Font = stdFont };
            var btnTrade = CreateButton("TRADE (-15% income)", 10, 50, 310, 35, Color.FromArgb(60, 40, 0), amberText);
            var btnArms = CreateButton("ARMS EMBARGO (-10% income)", 10, 90, 310, 35, Color.FromArgb(60, 20, 0), Color.OrangeRed);
            var btnFull = CreateButton("FULL SANCTIONS (-30% income)", 10, 130, 310, 35, Color.FromArgb(80, 0, 0), Color.Red);

            SanctionType? chosen = null;
            btnTrade.Click += (s, ev) => { chosen = SanctionType.Trade; form.Close(); };
            btnArms.Click += (s, ev) => { chosen = SanctionType.Arms; form.Close(); };
            btnFull.Click += (s, ev) => { chosen = SanctionType.Full; form.Close(); };

            form.Controls.AddRange(new Control[] { lbl, btnTrade, btnArms, btnFull });
            form.ShowDialog(this);

            if (chosen.HasValue)
            {
                StrategicEngine.ImposeSanction(GameEngine.Player.NationName, selectedTarget, chosen.Value);
                logBox.SelectionColor = amberText;
                LogMsg($"[SANCTIONS] Imposed {chosen.Value.ToString().ToUpper()} sanctions on {selectedTarget.ToUpper()}!");
                AddNotification("SANCTIONS IMPOSED", $"{chosen.Value} sanctions on {selectedTarget}", amberText, 4f);

                // AI anger
                if (GameEngine.Nations.TryGetValue(selectedTarget, out Nation t))
                    t.AngerLevel = Math.Min(10, t.AngerLevel + 2);

                if (_isMultiplayer && _mpClient != null)
                {
                    _ = _mpClient.SendGameActionAsync(new
                    {
                        type = "sanction",
                        imposedBy = GameEngine.Player.NationName,
                        target = selectedTarget,
                        sanctionType = chosen.Value.ToString()
                    });
                }

                RefreshData();
            }
        }

        // ── Espionage ─────────────────────────────────────────────────────────
        private void BtnDeploySpy_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedTarget) || !GameEngine.Nations.ContainsKey(selectedTarget))
            {
                MessageBox.Show("Select a target nation on the map first.", "NO TARGET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (GameEngine.Player.SpyCooldown > 0)
            {
                MessageBox.Show($"Spy network on cooldown. {GameEngine.Player.SpyCooldown} seconds remaining.", "COOLDOWN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (GameEngine.Player.ActiveSpies.Count >= 3)
            {
                MessageBox.Show("Maximum 3 spies deployed at once.", "SPY LIMIT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (GameEngine.Player.Money < 500)
            {
                MessageBox.Show("Spy deployment costs $500M.", "INSUFFICIENT FUNDS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Pick mission type
            var form = new Form
            {
                Text = "DEPLOY SPY",
                Size = new Size(400, 280),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(10, 20, 10),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label { Text = $"Choose spy mission in {selectedTarget.ToUpper()}:", Location = new Point(10, 15), Size = new Size(370, 25), ForeColor = Color.LimeGreen, Font = stdFont };
            var btnIntel = CreateButton("GATHER INTEL (reveal full stats)", 10, 50, 360, 35, Color.FromArgb(0, 40, 20), Color.LimeGreen);
            var btnSabotage = CreateButton("SABOTAGE (destroy resources/nukes)", 10, 90, 360, 35, Color.FromArgb(40, 30, 0), amberText);
            var btnSteal = CreateButton("STEAL MONEY (siphon treasury)", 10, 130, 360, 35, Color.FromArgb(0, 30, 40), cyanText);
            var btnDelay = CreateButton("DELAY LAUNCH (corrupt launch codes)", 10, 170, 360, 35, Color.FromArgb(40, 0, 40), Color.Magenta);

            SpyMissionType? chosen = null;
            btnIntel.Click += (s, ev) => { chosen = SpyMissionType.Intel; form.Close(); };
            btnSabotage.Click += (s, ev) => { chosen = SpyMissionType.Sabotage; form.Close(); };
            btnSteal.Click += (s, ev) => { chosen = SpyMissionType.StealMoney; form.Close(); };
            btnDelay.Click += (s, ev) => { chosen = SpyMissionType.DelayLaunch; form.Close(); };

            form.Controls.AddRange(new Control[] { lbl, btnIntel, btnSabotage, btnSteal, btnDelay });
            form.ShowDialog(this);

            if (chosen.HasValue)
            {
                GameEngine.Player.Money -= 500;
                GameEngine.Player.SpyCooldown = 15; // 15 second cooldown
                var spy = StrategicEngine.DeploySpy(selectedTarget, chosen.Value);

                logBox.SelectionColor = Color.LimeGreen;
                LogMsg($"[ESPIONAGE] Spy deployed to {selectedTarget.ToUpper()} — Mission: {chosen.Value}. ETA: {spy.TicksRemaining}s.");
                AddNotification("SPY DEPLOYED", $"{chosen.Value} mission in {selectedTarget}", Color.LimeGreen, 3f);

                if (_isMultiplayer && _mpClient != null)
                {
                    _ = _mpClient.SendGameActionAsync(new
                    {
                        type = "spy_deploy",
                        target = selectedTarget,
                        mission = chosen.Value.ToString()
                    });
                }

                RefreshData();
            }
        }

        // ── UN Security Council ───────────────────────────────────────────────
        private void BtnUNPropose_Click(object sender, EventArgs e)
        {
            if (GameEngine.Player.UNCooldown > 0)
            {
                MessageBox.Show($"UN cooldown: {GameEngine.Player.UNCooldown} seconds remaining.", "COOLDOWN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Check for existing voting resolution
            if (GameEngine.UNResolutions.Any(r => r.IsVoting))
            {
                MessageBox.Show("A resolution is currently being voted on. Wait for it to conclude.", "VOTE IN PROGRESS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var form = new Form
            {
                Text = "UN SECURITY COUNCIL",
                Size = new Size(420, 385),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(10, 10, 30),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label { Text = "Propose a UN Resolution:", Location = new Point(10, 15), Size = new Size(390, 25), ForeColor = Color.CornflowerBlue, Font = stdFont };

            var btnCeasefire = CreateButton("CEASEFIRE (reduce all anger by 3)", 10, 50, 380, 35, Color.FromArgb(0, 20, 60), Color.CornflowerBlue);
            var btnSanctions = CreateButton("SANCTIONS (target a nation)", 10, 90, 380, 35, Color.FromArgb(40, 20, 0), amberText);
            var btnNoFirst = CreateButton("NO FIRST STRIKE PACT", 10, 130, 380, 35, Color.FromArgb(0, 40, 40), cyanText);
            var btnFreeze = CreateButton("NUCLEAR FREEZE (ban new nukes)", 10, 170, 380, 35, Color.FromArgb(30, 0, 50), Color.Violet);
            var btnAid = CreateButton("HUMANITARIAN AID (+2% pop all)", 10, 210, 380, 35, Color.FromArgb(0, 40, 0), Color.LimeGreen);
            var btnRequestAid = CreateButton("REQUEST EMERGENCY AID ($200M, +5% pop for you)", 10, 255, 380, 35, Color.FromArgb(0, 30, 10), Color.LightGreen);

            UNResolutionType? chosen = null;
            bool requestedEmergencyAid = false;
            btnCeasefire.Click += (s, ev) => { chosen = UNResolutionType.Ceasefire; form.Close(); };
            btnSanctions.Click += (s, ev) => { chosen = UNResolutionType.Sanctions; form.Close(); };
            btnNoFirst.Click += (s, ev) => { chosen = UNResolutionType.NoFirstStrike; form.Close(); };
            btnFreeze.Click += (s, ev) => { chosen = UNResolutionType.NuclearFreeze; form.Close(); };
            btnAid.Click += (s, ev) => { chosen = UNResolutionType.HumanitarianAid; form.Close(); };
            btnRequestAid.Click += (s, ev) => { requestedEmergencyAid = true; form.Close(); };

            form.Controls.AddRange(new Control[] { lbl, btnCeasefire, btnSanctions, btnNoFirst, btnFreeze, btnAid, btnRequestAid });
            form.ShowDialog(this);

            // Handle emergency aid request (no vote needed)
            if (requestedEmergencyAid)
            {
                if (GameEngine.Player.Money < 200)
                {
                    MessageBox.Show("Emergency aid request costs $200M.", "INSUFFICIENT FUNDS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                GameEngine.Player.Money -= 200;
                GameEngine.Player.UNCooldown = 20;
                long recovered = GameEngine.Player.MaxPopulation / 20; // 5% pop recovery
                GameEngine.Player.Population = Math.Min(GameEngine.Player.MaxPopulation, GameEngine.Player.Population + recovered);

                logBox.SelectionColor = Color.LightGreen;
                LogMsg($"[UN AID] Emergency food aid received from the UN — {recovered:N0} citizens saved from famine.");
                AddNotification("UN AID RECEIVED", $"+{recovered:N0} population", Color.LightGreen, 5f);
                RefreshData();
                return;
            }

            if (chosen == null) return;

            // For sanctions, need a target
            string? sanctionTarget = null;
            if (chosen == UNResolutionType.Sanctions)
            {
                if (string.IsNullOrEmpty(selectedTarget) || !GameEngine.Nations.ContainsKey(selectedTarget))
                {
                    MessageBox.Show("Select a target nation on the map first for UN Sanctions.", "NO TARGET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                sanctionTarget = selectedTarget;
            }

            GameEngine.Player.UNCooldown = 30; // 30 second cooldown between proposals
            var res = StrategicEngine.ProposeResolution(chosen.Value, GameEngine.Player.NationName, sanctionTarget);

            // Player votes YES on their own resolution
            res.Votes[GameEngine.Player.NationName] = UNVote.Yes;

            // AI votes immediately (in real time they'd trickle in, but this is simpler)
            StrategicEngine.CastAIVotes(res);

            string targetStr = sanctionTarget != null ? $" (target: {sanctionTarget.ToUpper()})" : "";
            logBox.SelectionColor = Color.CornflowerBlue;
            LogMsg($"[UN SECURITY COUNCIL] Resolution proposed: {chosen.Value}{targetStr}. Voting period: {res.VotingTicksLeft}s.");
            AddNotification("UN VOTE CALLED", $"{chosen.Value}{targetStr}", Color.CornflowerBlue, 5f);

            if (_isMultiplayer && _mpClient != null)
            {
                _ = _mpClient.SendGameActionAsync(new
                {
                    type = "un_propose",
                    proposedBy = GameEngine.Player.NationName,
                    resolutionType = chosen.Value.ToString(),
                    target = sanctionTarget,
                    resolutionId = res.Id
                });
            }

            RefreshData();
        }

        // ── Food Aid — Combat Famine ──────────────────────────────────────────
        private void BtnSendFood_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedTarget))
            {
                // No target selected — send food to yourself
                if (GameEngine.Player.Money < 300)
                {
                    MessageBox.Show("Food aid costs $300M.", "INSUFFICIENT FUNDS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                GameEngine.Player.Money -= 300;
                long recovered = GameEngine.Player.MaxPopulation / 20; // 5% pop recovery
                GameEngine.Player.Population = Math.Min(GameEngine.Player.MaxPopulation, GameEngine.Player.Population + recovered);

                logBox.SelectionColor = Color.LightGreen;
                LogMsg($"[FOOD AID] Emergency rations distributed — {recovered:N0} citizens saved from famine.");
                AddNotification("FOOD AID", $"+{recovered:N0} population", Color.LightGreen, 3f);
                RefreshData();
                return;
            }

            // Self-targeting — send food to yourself
            if (selectedTarget == GameEngine.Player.NationName)
            {
                if (GameEngine.Player.Money < 300)
                {
                    MessageBox.Show("Food aid costs $300M.", "INSUFFICIENT FUNDS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                GameEngine.Player.Money -= 300;
                long recovered = GameEngine.Player.MaxPopulation / 20; // 5% pop recovery
                GameEngine.Player.Population = Math.Min(GameEngine.Player.MaxPopulation, GameEngine.Player.Population + recovered);

                logBox.SelectionColor = Color.LightGreen;
                LogMsg($"[FOOD AID] Emergency rations distributed — {recovered:N0} citizens saved from famine.");
                AddNotification("FOOD AID", $"+{recovered:N0} population", Color.LightGreen, 3f);
                RefreshData();
                return;
            }

            if (!GameEngine.Nations.ContainsKey(selectedTarget))
            {
                MessageBox.Show("Select a valid target nation.", "NO TARGET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (GameEngine.Player.Money < 500)
            {
                MessageBox.Show("Sending food aid to another nation costs $500M.", "INSUFFICIENT FUNDS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var target = GameEngine.Nations[selectedTarget];
            if (target.IsDefeated)
            {
                MessageBox.Show("Cannot send aid to a defeated nation.", "INVALID TARGET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            GameEngine.Player.Money -= 500;
            long targetRecovered = target.MaxPopulation / 15; // ~6.7% recovery
            target.Population = Math.Min(target.MaxPopulation, target.Population + targetRecovered);

            // Sending food improves relations
            if (target.AngerLevel > 0) target.AngerLevel = Math.Max(0, target.AngerLevel - 2);
            target.Diplomacy.DiplomacyMood = Math.Min(1f, target.Diplomacy.DiplomacyMood + 0.15f);

            logBox.SelectionColor = Color.LightGreen;
            LogMsg($"[FOOD AID] Sent emergency food to {selectedTarget.ToUpper()} — {targetRecovered:N0} citizens saved. Relations improved.");
            AddNotification("FOOD AID SENT", $"{selectedTarget}: +{targetRecovered:N0} pop", Color.LightGreen, 4f);

            if (_isMultiplayer && _mpClient != null)
            {
                _ = _mpClient.SendGameActionAsync(new
                {
                    type = "food_aid",
                    from = GameEngine.Player.NationName,
                    target = selectedTarget,
                    recovered = targetRecovered
                });
            }

            RefreshData();
        }

        // ── Multiplayer Handlers ──────────────────────────────────────────────
        private void HandleRemoteStrategicAction(JsonElement data)
        {
            string actionType = SafeStr(data, "type");

            switch (actionType)
            {
                case "sanction":
                    {
                        string imposedBy = SafeStr(data, "imposedBy");
                        string target = SafeStr(data, "target");
                        string sType = SafeStr(data, "sanctionType");
                        if (Enum.TryParse<SanctionType>(sType, out var sanctionType))
                        {
                            StrategicEngine.ImposeSanction(imposedBy, target, sanctionType);
                            logBox.SelectionColor = amberText;
                            LogMsg($"[SANCTIONS] {imposedBy.ToUpper()} imposed {sType} sanctions on {target.ToUpper()}!");
                        }
                        break;
                    }

                case "un_propose":
                    {
                        string resType = SafeStr(data, "resolutionType");
                        string proposedBy = SafeStr(data, "proposedBy", "Unknown");
                        string target = SafeStr(data, "target");
                        string resId = SafeStr(data, "resolutionId");
                        string? targetOrNull = string.IsNullOrEmpty(target) ? null : target;
                        if (Enum.TryParse<UNResolutionType>(resType, out var unType))
                        {
                            var res = StrategicEngine.ProposeResolution(unType, proposedBy, targetOrNull);
                            if (!string.IsNullOrEmpty(resId)) res.Id = resId; // sync resolution ID
                            StrategicEngine.CastAIVotes(res);
                            logBox.SelectionColor = Color.CornflowerBlue;
                            LogMsg($"[UN SECURITY COUNCIL] {proposedBy.ToUpper()} proposed: {unType}");

                            // Let the player vote!
                            PromptPlayerUNVote(res);
                        }
                        break;
                    }

                case "spy_result":
                case "spy_deploy":
                    {
                        string target = SafeStr(data, "target");
                        string mission = SafeStr(data, "mission");
                        logBox.SelectionColor = amberText;
                        LogMsg($"[INTELLIGENCE] Reports of covert {mission} operation detected in {target.ToUpper()}.");
                        break;
                    }

                case "food_aid":
                    {
                        string from = SafeStr(data, "from");
                        string target = SafeStr(data, "target");
                        long recovered = SafeLong(data, "recovered");

                        // If food was sent to a nation we track, apply it
                        if (GameEngine.Nations.TryGetValue(target, out Nation aidTarget))
                        {
                            aidTarget.Population = Math.Min(aidTarget.MaxPopulation, aidTarget.Population + recovered);
                            if (aidTarget.AngerLevel > 0) aidTarget.AngerLevel = Math.Max(0, aidTarget.AngerLevel - 2);
                        }
                        // If food was sent to us
                        if (target == GameEngine.Player.NationName)
                        {
                            long playerRecov = GameEngine.Player.MaxPopulation / 15;
                            GameEngine.Player.Population = Math.Min(GameEngine.Player.MaxPopulation, GameEngine.Player.Population + playerRecov);
                            AddNotification("FOOD AID RECEIVED", $"{from.ToUpper()} sent food! +{playerRecov:N0} pop", Color.LightGreen, 5f);
                        }

                        logBox.SelectionColor = Color.LightGreen;
                        LogMsg($"[FOOD AID] {from.ToUpper()} sent food aid to {target.ToUpper()} — {recovered:N0} saved.");
                        break;
                    }

                case "un_vote":
                    {
                        string voter = SafeStr(data, "voter");
                        string voteStr = SafeStr(data, "vote");
                        string resId = SafeStr(data, "resolutionId");

                        if (Enum.TryParse<UNVote>(voteStr, out var vote))
                        {
                            var res = GameEngine.UNResolutions.FirstOrDefault(r => r.Id == resId && r.IsVoting);
                            if (res != null)
                            {
                                res.Votes[voter] = vote;
                                logBox.SelectionColor = Color.CornflowerBlue;
                                LogMsg($"[UN VOTE] {voter.ToUpper()} voted {vote} on {res.Type}.");
                            }
                        }
                        break;
                    }

                case "nuclear_winter_sync":
                    {
                        // Host broadcasts winter state — clients sync
                        bool active = SafeInt(data, "active") == 1;
                        int tick = SafeInt(data, "tick");
                        float severity = SafeFloat(data, "severity");
                        int nukesFired = SafeInt(data, "nukesFired");

                        GameEngine.NuclearWinterActive = active;
                        GameEngine.NuclearWinterTick = tick;
                        GameEngine.NuclearWinterSeverity = severity;
                        GameEngine.GlobalNukesFired = nukesFired;

                        if (active && !GameEngine.NuclearWinterActive)
                        {
                            logBox.SelectionColor = Color.DeepSkyBlue;
                            LogMsg("[GLOBAL EVENT] NUCLEAR WINTER HAS BEGUN — Synced from host.");
                            AddNotification("NUCLEAR WINTER", "Synced from host", Color.DeepSkyBlue, 8f);
                        }
                        break;
                    }
            }

            RefreshData();
        }

        // ── Player UN Voting ──────────────────────────────────────────────────
        private void PromptPlayerUNVote(UNResolution res)
        {
            string targetStr = res.TargetNation != null ? $"\nTarget: {res.TargetNation.ToUpper()}" : "";
            string desc = res.Type switch
            {
                UNResolutionType.Ceasefire => "Reduce all anger by 3 — stop hostile attacks",
                UNResolutionType.Sanctions => $"Full sanctions on {res.TargetNation?.ToUpper()} — cripple their economy",
                UNResolutionType.NoFirstStrike => "Non-hostile nations won't attack — violators face global anger",
                UNResolutionType.NuclearFreeze => "Ban nuke production — only angry nations will fire",
                UNResolutionType.HumanitarianAid => "All nations gain +2% population recovery",
                _ => ""
            };

            var form = new Form
            {
                Text = "UN SECURITY COUNCIL — VOTE",
                Size = new Size(420, 220),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.FromArgb(10, 10, 30),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label
            {
                Text = $"Resolution: {res.Type}\nProposed by: {res.ProposedBy.ToUpper()}{targetStr}\n\n{desc}",
                Location = new Point(10, 15),
                Size = new Size(390, 80),
                ForeColor = Color.CornflowerBlue,
                Font = stdFont
            };
            var btnYes = CreateButton("VOTE YES", 10, 110, 120, 35, Color.FromArgb(0, 50, 0), Color.LimeGreen);
            var btnNo = CreateButton("VOTE NO", 140, 110, 120, 35, Color.FromArgb(50, 0, 0), Color.OrangeRed);
            var btnAbstain = CreateButton("ABSTAIN", 270, 110, 120, 35, Color.FromArgb(30, 30, 30), Color.Gray);

            UNVote? playerVote = null;
            btnYes.Click += (s, ev) => { playerVote = UNVote.Yes; form.Close(); };
            btnNo.Click += (s, ev) => { playerVote = UNVote.No; form.Close(); };
            btnAbstain.Click += (s, ev) => { playerVote = UNVote.Abstain; form.Close(); };

            form.Controls.AddRange(new Control[] { lbl, btnYes, btnNo, btnAbstain });
            form.ShowDialog(this);

            UNVote vote = playerVote ?? UNVote.Abstain;
            res.Votes[GameEngine.Player.NationName] = vote;

            logBox.SelectionColor = Color.CornflowerBlue;
            LogMsg($"[UN VOTE] You voted {vote} on {res.Type}.");

            if (_isMultiplayer && _mpClient != null)
            {
                _ = _mpClient.SendGameActionAsync(new
                {
                    type = "un_vote",
                    voter = GameEngine.Player.NationName,
                    vote = vote.ToString(),
                    resolutionId = res.Id
                });
            }
        }

        // ── Nuclear Winter Sync (host broadcasts to clients) ─────────────────
        private void BroadcastNuclearWinterSync()
        {
            if (!_isMultiplayer || _mpClient == null || !_mpClient.IsHost) return;

            _ = _mpClient.SendGameActionAsync(new
            {
                type = "nuclear_winter_sync",
                active = GameEngine.NuclearWinterActive ? 1 : 0,
                tick = GameEngine.NuclearWinterTick,
                severity = GameEngine.NuclearWinterSeverity,
                nukesFired = GameEngine.GlobalNukesFired
            });
        }

        // ── Nuclear Winter Rendering (called from MapPanel_Paint) ─────────
        public void RenderNuclearWinterOverlay(Graphics g, int width, int height)
        {
            if (!GameEngine.NuclearWinterActive) return;

            // Dark blue-gray tint over the whole map, intensity scaled by severity
            int alpha = (int)(GameEngine.NuclearWinterSeverity * 120);
            using (var winterBrush = new SolidBrush(Color.FromArgb(alpha, 20, 30, 50)))
            {
                g.FillRectangle(winterBrush, 0, 0, width, height);
            }

            // Snow particle effect (simple white dots)
            if (GameEngine.NuclearWinterSeverity > 0.2f)
            {
                int particleCount = (int)(GameEngine.NuclearWinterSeverity * 60);
                for (int i = 0; i < particleCount; i++)
                {
                    int x = rng.Next(width);
                    int y = rng.Next(height);
                    int size = rng.Next(1, 3);
                    int a = rng.Next(40, 120);
                    using (var snowBrush = new SolidBrush(Color.FromArgb(a, 200, 210, 220)))
                    {
                        g.FillEllipse(snowBrush, x, y, size, size);
                    }
                }
            }

            // Doomsday text overlay at top
            string statusText = $"NUCLEAR WINTER — SEVERITY: {(int)(GameEngine.NuclearWinterSeverity * 100)}%";
            using (var winterFont = new Font("Consolas", 10F, FontStyle.Bold))
            using (var bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            using (var fgBrush = new SolidBrush(Color.FromArgb(200, 100, 150, 220)))
            {
                SizeF sz = g.MeasureString(statusText, winterFont);
                float x = (width - sz.Width) / 2;
                g.FillRectangle(bgBrush, x - 4, 2, sz.Width + 8, sz.Height + 4);
                g.DrawString(statusText, winterFont, fgBrush, x, 4);
            }
        }
    }
}
