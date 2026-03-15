using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;

namespace fallover_67
{
    public partial class ControlPanelForm : Form
    {
        // ── Player Strike ────────────────────────────────────────────────────────────
        private void BtnLaunch_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedTarget) || cmbWeapon.SelectedItem == null) return;

            if (GameEngine.Player.IsSatelliteBlind)
            {
                MessageBox.Show("SATELLITE UPLINK OFFLINE — Target lock cannot be established.", "SYSTEM ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            double cooldownLeft = (DateTime.Now - _lastLaunchTime).TotalSeconds;
            if (cooldownLeft < LaunchCooldownSeconds)
            {
                int remaining = (int)Math.Ceiling(LaunchCooldownSeconds - cooldownLeft);
                MessageBox.Show($"LAUNCH COOLDOWN ACTIVE — {remaining}s remaining.", "SYSTEMS RECHARGING");
                return;
            }

            int weaponIndex = cmbWeapon.SelectedIndex;

            // Satellite Killer — single shot, special pipeline
            if (weaponIndex == 4)
            {
                if (GameEngine.Player.SatelliteMissiles <= 0) { MessageBox.Show("Out of ammo!"); return; }
                _lastLaunchTime = DateTime.Now;
                btnLaunch.Enabled = false;
                _ = Task.Run(async () =>
                {
                    await FireSatelliteStrikeAsync(selectedTarget);
                    try { Invoke(new Action(() => btnLaunch.Enabled = true)); } catch { }
                });
                return;
            }

            int salvo = sliderSalvo?.Value ?? 1;

            int stock = weaponIndex switch
            {
                0 => GameEngine.Player.StandardNukes,
                1 => GameEngine.Player.MegaNukes,
                2 => GameEngine.Player.BioPlagues,
                3 => GameEngine.Player.OrbitalLasers,
                _ => 0
            };
            if (stock <= 0) { MessageBox.Show("Out of ammo!"); return; }
            salvo = Math.Min(salvo, Math.Min(stock, MaxNukesPerVolley));

            _lastLaunchTime = DateTime.Now;
            btnLaunch.Enabled = false;
            _ = FireSalvoAsync(selectedTarget, weaponIndex, salvo);
        }

        // ── Multi-Target Strike Planner ──────────────────────────────────────────────
        private void BtnMultiStrike_Click(object sender, EventArgs e)
        {
            if (GameEngine.Player.IsSatelliteBlind)
            {
                MessageBox.Show("SATELLITE UPLINK OFFLINE — Multi-targeting system requires orbital assist.", "SYSTEM ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            double cooldownLeft = (DateTime.Now - _lastLaunchTime).TotalSeconds;
            if (cooldownLeft < LaunchCooldownSeconds)
            {
                int remaining = (int)Math.Ceiling(LaunchCooldownSeconds - cooldownLeft);
                MessageBox.Show($"LAUNCH COOLDOWN ACTIVE — {remaining}s remaining.", "SYSTEMS RECHARGING");
                return;
            }

            int weaponIndex = cmbWeapon.SelectedIndex;
            // Satellite Killer can't be used in multi-strike (it's a single precision shot)
            if (weaponIndex == 4) { MessageBox.Show("Satellite Killer cannot be used in multi-target mode — use single strike.", "INVALID"); return; }

            int stock = weaponIndex switch
            {
                0 => GameEngine.Player.StandardNukes,
                1 => GameEngine.Player.MegaNukes,
                2 => GameEngine.Player.BioPlagues,
                3 => GameEngine.Player.OrbitalLasers,
                _ => 0
            };
            if (stock <= 0) { MessageBox.Show("Out of ammo!"); return; }

            string[] wNames = { "STANDARD NUKE", "TSAR BOMBA", "BIO-PLAGUE", "ORBITAL LASER" };
            var activeNations = GameEngine.Nations.Keys.Where(n => !GameEngine.Nations[n].IsDefeated).OrderBy(n => n).ToList();

            var plan = ShowMultiStrikePlanner(activeNations, wNames[weaponIndex], stock);
            if (plan == null || plan.Count == 0) return;

            int totalMissiles = plan.Sum(p => p.count);
            if (totalMissiles > MaxNukesPerVolley)
            {
                MessageBox.Show($"Total missiles ({totalMissiles}) exceeds the {MaxNukesPerVolley}-nuke volley cap.", "OVERLOAD");
                return;
            }

            _lastLaunchTime = DateTime.Now;
            btnLaunch.Enabled = false;
            btnMultiStrike.Enabled = false;
            _ = ExecuteMultiStrikeAsync(plan, weaponIndex, () =>
            {
                btnLaunch.Enabled = true;
                btnMultiStrike.Enabled = true;
            });
        }

        private List<(string target, int count)>? ShowMultiStrikePlanner(List<string> nations, string weaponName, int stock)
        {
            using (var planner = new VisualStrikePlannerForm(nations, weaponName, stock, MaxNukesPerVolley))
            {
                if (planner.ShowDialog(this) == DialogResult.OK)
                {
                    return planner.FinalPlan;
                }
            }
            return null;
        }

        private async Task ExecuteMultiStrikeAsync(List<(string target, int count)> plan, int weaponIndex, Action onComplete)
        {
            foreach (var (targetName, count) in plan)
            {
                if (!GameEngine.Nations.ContainsKey(targetName)) continue;
                int available = weaponIndex switch
                {
                    0 => GameEngine.Player.StandardNukes,
                    1 => GameEngine.Player.MegaNukes,
                    2 => GameEngine.Player.BioPlagues,
                    3 => GameEngine.Player.OrbitalLasers,
                    4 => GameEngine.Player.SatelliteMissiles,
                    _ => 0
                };
                int toFire = Math.Min(count, available);
                if (toFire <= 0) continue;
                await FireSalvoAsync(targetName, weaponIndex, toFire);
                await Task.Delay(300); // brief gap between targets
            }
            onComplete();
        }

        private async Task FireSalvoAsync(string targetName, int weaponIndex, int salvo)
        {
            if (!GameEngine.Nations.ContainsKey(targetName)) return;
            Nation target = GameEngine.Nations[targetName];
            PointLatLng startPt = new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);
            PointLatLng impactPt = new PointLatLng(target.MapY, target.MapX);

            string[] wNames = { "STANDARD NUKE", "TSAR BOMBA", "BIO-PLAGUE CANISTER", "ORBITAL LASER", "SATELLITE KILLER" };
            Color[] wColors = { Color.OrangeRed, Color.DeepPink, Color.LimeGreen, Color.Cyan, Color.Violet };
            float[] wRadii = { 45f, 70f, 55f, 40f, 50f };
            float rc = wRadii[weaponIndex];

            logBox.SelectionColor = amberText;
            LogMsg(salvo > 1
                ? $"[LAUNCH] {salvo}× {wNames[weaponIndex]} SALVO launched toward {targetName.ToUpper()}!"
                : $"[LAUNCH] {wNames[weaponIndex]} launched toward {targetName.ToUpper()}! Tracking projectile...");

            for (int s = 0; s < salvo; s++)
            {
                // Deduct one weapon from inventory
                if (weaponIndex == 0) GameEngine.Player.StandardNukes--;
                else if (weaponIndex == 1) GameEngine.Player.MegaNukes--;
                else if (weaponIndex == 2) GameEngine.Player.BioPlagues--;
                else if (weaponIndex == 3) GameEngine.Player.OrbitalLasers--;
                // weaponIndex 4 (Satellite Killer) is handled exclusively through FireSatelliteStrikeAsync
                GameEngine.Player.NukesUsed++;
                ProfileManager.RecordNukeLaunch(weaponIndex);

                long preCalculatedDmg = CombatEngine.PreCalculatePlayerDamage(targetName, weaponIndex);

                if (_isMultiplayer && _mpClient != null)
                    _ = _mpClient.SendGameActionAsync(new { type = "strike", target = targetName, weapon = weaponIndex, playerNation = GameEngine.Player.NationName, damage = preCalculatedDmg });

                // Slight spread so missiles don't perfectly overlap
                float spread = salvo > 1 ? (s - salvo / 2f) * 0.3f : 0f;
                PointLatLng adjustedImpact = new PointLatLng(impactPt.Lat + spread * 0.2, impactPt.Lng + spread * 0.3);

                lock (_animLock) activeMissiles.Add(new MissileAnimation
                {
                    Start = startPt,
                    End = adjustedImpact,
                    IsPlayerMissile = true,
                    MissileColor = wColors[weaponIndex],
                    Speed = 0.4f - (s * 0.01f), // slight stagger so they don't all land simultaneously
                    OnImpact = async () => await HandlePlayerStrikeImpact(targetName, weaponIndex, adjustedImpact, rc, preCalculatedDmg)
                });

                if (s < salvo - 1)
                    await Task.Delay(180); // 180ms stagger between launches
            }

            RefreshData();
        }

        private async Task HandlePlayerStrikeImpact(string targetName, int weaponIndex, PointLatLng impactPos, float blastRadius, long calculatedDmg)
        {
            StrikeResult result = CombatEngine.ExecuteCombatTurn(targetName, weaponIndex, calculatedDmg);

            string impactLine = result.Logs.FirstOrDefault(l => l.Contains("[IMPACT]")) ?? "";
            string resultLine = result.Logs.FirstOrDefault(l => l.Contains("SURRENDER") || l.Contains("VICTORY") || l.Contains("SUCCESS")) ?? "";

            lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPos, MaxRadius = blastRadius, DamageLines = new[] { impactLine, resultLine }, IsPlayerTarget = false });

            foreach (var l in result.Logs)
            {
                logBox.SelectionColor = l.Contains("WARNING") || l.Contains("CATASTROPHE") ? redText : l.Contains("SURRENDER") || l.Contains("SUCCESS") || l.Contains("VICTORY") ? amberText : l.Contains("ALLY") ? cyanText : greenText;
                LogMsg(l); await Task.Delay(500);
            }

            if (result.Logs.Any(l => l.Contains("SURRENDER") || l.Contains("VICTORY") || l.Contains("COLLAPSED")))
            {
                AddNotification("NATION SECURED", $"{targetName.ToUpper()} has surrendered", Color.Gold);
                bool surrendered = result.Logs.Any(l => l.Contains("SURRENDER"));
                ProfileManager.RecordNationConquered(surrendered);
            }

            // Track kills from this strike
            if (GameEngine.Nations.TryGetValue(targetName, out var strikeTarget))
            {
                ProfileManager.RecordKills(calculatedDmg);
            }

            RefreshData();
            CheckGameOver();

            // Broadcast the calculated results to everyone
            foreach (var (allyName, damage) in result.AllySupporters)
            {
                if (GameEngine.Nations.TryGetValue(allyName, out Nation allyNation))
                {
                    await Task.Delay(350);
                    logBox.SelectionColor = cyanText;
                    LogMsg($"[ALLY SUPPORT] {allyName.ToUpper()} has launched supporting missiles at {targetName.ToUpper()}!");
                    BroadcastAiLaunch(allyNation, targetName, 1, damage);
                }
            }

            foreach (string retaliatorName in result.Retaliators)
            {
                if (GameEngine.Nations.TryGetValue(retaliatorName, out Nation eNat))
                {
                    await Task.Delay(600);
                    BroadcastAiLaunch(eNat, GameEngine.Player.NationName, 1);
                }
            }
        }

        // ── Ally Support Missiles ────────────────────────────────────────────────────
        private void LaunchAllyMissile(Nation ally, string targetName, long damage, PointLatLng fallbackImpact)
        {
            PointLatLng startPt = new PointLatLng(ally.MapY, ally.MapX);
            PointLatLng impactPt = GameEngine.Nations.TryGetValue(targetName, out Nation tn) ? new PointLatLng(tn.MapY, tn.MapX) : fallbackImpact;

            lock (_animLock) activeMissiles.Add(new MissileAnimation
            {
                Start = startPt,
                End = impactPt,
                IsPlayerMissile = false,
                MissileColor = Color.Cyan,
                Speed = 0.45f,
                OnImpact = () => HandleAllyStrikeImpact(ally.Name, targetName, damage, impactPt)
            });
        }

        private void HandleAllyStrikeImpact(string allyName, string targetName, long damage, PointLatLng impactPos)
        {
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return;

            long actualDmg = Math.Min(damage, target.Population);
            target.Population -= actualDmg;

            lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPos, MaxRadius = 35f, DamageLines = new[] { $"[ALLY IMPACT] +{actualDmg:N0} casualties" }, IsPlayerTarget = false });

            logBox.SelectionColor = cyanText;
            LogMsg($"[ALLY IMPACT] {allyName.ToUpper()} support strike caused an additional {actualDmg:N0} casualties.");
            RefreshData();
        }

        // ── Enemy Missiles (hostile → player) ───────────────────────────────────────
        private void LaunchEnemyMissile(Nation attacker)
        {
            PointLatLng startPt  = new PointLatLng(attacker.MapY, attacker.MapX);
            PointLatLng impactPt = new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);

            logBox.SelectionColor = redText;
            LogMsg($"[WARNING] ⚠ RADAR ALERT: {attacker.Name.ToUpper()} HAS LAUNCHED AN ICBM! BRACE FOR IMPACT! ⚠");

            var missile = new MissileAnimation
            {
                Start           = startPt,
                End             = impactPt,
                IsPlayerMissile = false,
                MissileColor    = Color.Red,
                Speed           = 0.35f,
            };

            // Register as inbound before setting OnImpact so the minigame sees it
            lock (_animLock)
            {
                // Reset tracking if this is the start of a new wave
                if (_inboundMissiles.Count == 0 && !_isDamageAlertActive)
                {
                    _cumulativeDamageThisWave = 0;
                }

                _inboundMissiles[missile] = attacker.Name;
                activeMissiles.Add(missile);
            }

            missile.OnImpact = async () => await HandleEnemyStrikeImpact(missile, impactPt);

            // First inbound missile triggers the minigame (if dome exists and minigames on)
            if (_minigamesEnabled && GameEngine.Player.IronDomeLevel > 0 && _gameState == GameState.Playing)
                _ = Task.Run(() => mapPanel.BeginInvoke(new Action(() => _ = TriggerIronDomeMinigame(impactPt))));
        }

        private async Task TriggerIronDomeMinigame(PointLatLng baseImpactPos)
        {
            // Guard: only one session at a time
            if (_gameState != GameState.Playing) return;
            _gameState = GameState.IronDomeMinigame;

            // Small delay so all missiles launched in the same salvo tick get registered
            await Task.Delay(80);

            // Count every inbound missile currently tracked
            List<MissileAnimation> wave;
            lock (_animLock)
                wave = new List<MissileAnimation>(_inboundMissiles.Keys);

            int totalMissiles = wave.Count;

            var domeGame = new IronDomeForm(totalMissiles, GameEngine.Player.IronDomeLevel);
            domeGame.ShowDialog(this);

            // Mark which missiles were intercepted (by index, best-scoring first)
            int intercepted = domeGame.InterceptedCount;
            for (int i = 0; i < wave.Count && i < intercepted; i++)
                lock (_animLock) _interceptedMissiles.Add(wave[i]);

            _gameState = GameState.Playing;
        }

        private async Task HandleEnemyStrikeImpact(MissileAnimation missile, PointLatLng impactPos)
        {
            string attackerName;
            bool   wasIntercepted;
            lock (_animLock)
            {
                _inboundMissiles.TryGetValue(missile, out attackerName);
                wasIntercepted = _interceptedMissiles.Remove(missile);
                _inboundMissiles.Remove(missile);
            }

            if (string.IsNullOrEmpty(attackerName)) return;

            if (wasIntercepted)
            {
                // Dud — show intercept explosion, no damage
                lock (_animLock) activeExplosions.Add(new ExplosionEffect
                {
                    Center = impactPos, MaxRadius = 30f,
                    DamageLines = new[] { $"⚡ INTERCEPTED — {attackerName.ToUpper()}" },
                    IsPlayerTarget = false
                });
                logBox.SelectionColor = cyanText;
                LogMsg($"[IRON DOME] ⚡ Missile from {attackerName.ToUpper()} INTERCEPTED!");
                AddNotification("INTERCEPT SUCCESS", $"Dome neutralized {attackerName.ToUpper()} warhead", Color.Cyan, 4f);
                ProfileManager.RecordMissileIntercepted();
                return;
            }

            // Missile gets through — apply damage (passive dome roll since minigame already ran)
            long popBefore = GameEngine.Player.Population;
            var logs = CombatEngine.ExecuteEnemyStrike(attackerName);
            long popAfter = GameEngine.Player.Population;
            long damageTaken = popBefore - popAfter;
            _cumulativeDamageThisWave += damageTaken;
            ProfileManager.RecordDamageAbsorbed(damageTaken);
            
            // DEAD HAND: Automatic Submarine Retaliation
            if (GameEngine.Nations.TryGetValue(attackerName, out Nation attacker))
            {
                var firingSubs = CombatEngine.TriggerSubmarineRetaliation(attacker.MapY, attacker.MapX, attackerName);
                foreach (var s in firingSubs)
                {
                    LogMsg($"[DEAD-HAND] {s.Name.ToUpper()} AUTOMATIC RETALIATION AGAINST {attackerName.ToUpper()}!");
                    AddNotification("COUNTER-STRIKE", $"{s.Name} launching Dead Hand payload", Color.OrangeRed, 5f);
                    
                    // Spawn animation
                    FireSubStrikeLocally(s, new PointLatLng(attacker.MapY, attacker.MapX));
                    
                    // Sync to MP
                    if (_isMultiplayer) _mpClient?.SendGameActionAsync(new { type = "sub_fire", subId = s.Id, lat = (double)attacker.MapY, lng = (double)attacker.MapX });
                }
            }

            if (logs.Count == 0) return;

            string casualtyLine = logs.FirstOrDefault(l => l.Contains("CASUALTY")) ?? "";
            lock (_animLock) activeExplosions.Add(new ExplosionEffect
            {
                Center = impactPos, MaxRadius = 55f,
                DamageLines = new[] { $"STRIKE FROM {attackerName.ToUpper()}", casualtyLine },
                IsPlayerTarget = true
            });

            foreach (var l in logs)
            {
                logBox.SelectionColor = l.Contains("CATASTROPHE") || l.Contains("WARNING") ? redText
                                      : l.Contains("DEFENSE") ? cyanText : greenText;
                LogMsg(l);
                await Task.Delay(400);
            }

            RefreshData();
            CheckGameOver();

            // All missiles in this wave have hit? Show the summary screen
            if (_inboundMissiles.Count == 0 && _cumulativeDamageThisWave > 0)
            {
                _isDamageAlertActive = true;
                _damageAlertTimer = 6f; // Show for 6 seconds
                _damageAlertAnim = 0f;
                // Wait briefly for explosions to settle
            }
            else if (_inboundMissiles.Count == 0)
            {
                // No damage this wave (all intercepted or 0 pop loss)
                _cumulativeDamageThisWave = 0;
            }
        }

        // ── Troop Deployment Dialog ──────────────────────────────────────────────────
        private void BtnSendTroops_Click(object sender, EventArgs e)
        {
            if (GameEngine.Player.IsSatelliteBlind)
            {
                MessageBox.Show("SATELLITE UPLINK OFFLINE — Extraction coordination requires orbital telemetry.", "SYSTEM ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var (fraction, confirmed) = ShowTroopDeploymentDialog(selectedTarget, GameEngine.Player.Population);
            if (!confirmed) return;

            long troopCount = (long)(GameEngine.Player.Population * fraction);
            if (troopCount < 50_000) { MessageBox.Show("Not enough population to form a deployment force!"); return; }

            int etaSeconds = Math.Max(20, (int)(12.0 / fraction));
            string etaStr = etaSeconds >= 60 ? $"{etaSeconds / 60}m {etaSeconds % 60}s" : $"{etaSeconds}s";

            GameEngine.Player.Population -= troopCount;
            GameEngine.ActiveMissions.Add(new TroopMission { TargetNation = selectedTarget, TroopFraction = fraction, TroopCount = troopCount, TimeRemainingSeconds = etaSeconds });

            int lootPct = (int)(Math.Min(1.0, fraction * 2.0 + 0.10) * 100);
            logBox.SelectionColor = amberText;
            LogMsg($"[COMMAND] {troopCount:N0} troops deployed to {selectedTarget.ToUpper()}. ETA: {etaStr}. Est. extraction efficiency: {lootPct}%.");
            ProfileManager.CurrentProfile.TroopMissionsLaunched++;

            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "deploy", target = selectedTarget, fraction });

            RefreshData();
        }

        private (double fraction, bool confirmed) ShowTroopDeploymentDialog(string targetName, long playerPop)
        {
            double[] fractions = { 0.05, 0.15, 0.30, 0.50 };
            int[] etas = { 240, 80, 40, 24 };
            int[] lootPcts = { 20, 40, 70, 100 };
            string[] labels = { "RECON TEAM", "STRIKE FORCE", "FULL ASSAULT", "OVERWHELMING FORCE" };

            var dlg = new Form { Text = $"DEPLOY TROOPS — {targetName.ToUpper()}", Size = new Size(520, 340), BackColor = bgDark, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };

            var headerLabel = new Label { Text = $"Select deployment force for {targetName.ToUpper()}:\n(Current Population: {playerPop:N0})", Location = new Point(15, 10), Size = new Size(480, 40), ForeColor = amberText, Font = stdFont, BackColor = Color.Transparent };
            dlg.Controls.Add(headerLabel);

            var radios = new RadioButton[4];
            for (int i = 0; i < fractions.Length; i++)
            {
                long troops = (long)(playerPop * fractions[i]);
                string etaStr = etas[i] >= 60 ? $"{etas[i] / 60}m {etas[i] % 60}s" : $"{etas[i]}s";
                string line = $"{labels[i]} ({(int)(fractions[i] * 100)}%)  |  Troops: {troops:N0}  |  ETA: {etaStr}  |  Loot: ~{lootPcts[i]}%";
                var rb = new RadioButton { Text = line, Location = new Point(15, 55 + i * 42), Size = new Size(480, 35), ForeColor = greenText, Font = new Font("Consolas", 9F, FontStyle.Bold), BackColor = Color.Transparent, Checked = (i == 1) };
                radios[i] = rb;
                dlg.Controls.Add(rb);
            }

            var btnDeploy = new Button { Text = "DEPLOY", Location = new Point(280, 268), Size = new Size(100, 35), BackColor = Color.DarkGoldenrod, ForeColor = Color.White, Font = stdFont, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "CANCEL", Location = new Point(390, 268), Size = new Size(100, 35), BackColor = Color.DarkRed, ForeColor = Color.White, Font = stdFont, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
            dlg.Controls.Add(btnDeploy); dlg.Controls.Add(btnCancel);
            dlg.AcceptButton = btnDeploy; dlg.CancelButton = btnCancel;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                for (int i = 0; i < radios.Length; i++)
                    if (radios[i].Checked) return (fractions[i], true);
                return (fractions[1], true);
            }
            return (0, false);
        }

        public void FireSubStrikeLocally(Submarine sub, PointLatLng target)
        {
            PointLatLng startPt = new PointLatLng(sub.MapY, sub.MapX);
            AddNotification("SUB LAUNCH", $"{sub.Name.ToUpper()} has fired!", Color.Cyan);

            lock (_animLock) activeMissiles.Add(new MissileAnimation
            {
                Start = startPt,
                End = target,
                IsPlayerMissile = true,
                MissileColor = Color.Cyan,
                Speed = 0.5f,
                OnImpact = () => {
                    lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = target, MaxRadius = 50f, DamageLines = new[] { $"SUB STRIKE", "Surgical Hit" }, IsPlayerTarget = false });
                    // Handle generic damage at this spot
                    ResolveAreaDamage(target, 150_000, 50);
                }
            });
        }

        private void ResolveAreaDamage(PointLatLng pos, long baseDmg, float radius)
        {
            // Find nations or subs near this lat/lng
            foreach (var nation in GameEngine.Nations.Values.ToList())
            {
                float dx = (float)(nation.MapX - pos.Lng);
                float dy = (float)(nation.MapY - pos.Lat);
                if (dx * dx + dy * dy < 1.0f) // roughly near
                {
                    CombatProxy(nation.Name, 0, 1); // trigger a hit via combat engine logic
                }
            }
            
            CheckSubmarineCasualties(pos, radius);
        }

        private void CheckSubmarineCasualties(PointLatLng pos, float radius)
        {
            foreach (var sub in GameEngine.Submarines)
            {
                if (sub.IsDestroyed) continue;
                float dx = (float)(sub.MapX - pos.Lng);
                float dy = (float)(sub.MapY - pos.Lat);
                if (dx * dx + dy * dy < 0.5f) // Near hit
                {
                    sub.Health = 0;
                    LogMsg($"[SENSORS] Submarine {sub.Name.ToUpper()} has been DESTROYED.");
                    AddNotification("SUB DESTROYED", $"{sub.Name.ToUpper()} lost at sea", Color.Gray, 6f);
                    if (_isMultiplayer) _mpClient?.SendGameActionAsync(new { type = "sub_destroy", subId = sub.Id });
                }
            }
        }

        private void TrySalvageWreck(Submarine sub)
        {
            if (!sub.IsDestroyed) return;
            LogMsg($"[RECOVERY] Dispatching salvage team to {sub.Name.ToUpper()} wreckage...");
            
            _ = Task.Run(async () => {
                await Task.Delay(5000); // 5 sec salvage time
                sub.Health = 50;
                sub.OwnerId = GameEngine.Player.NationName;
                sub.NukeCount = 0;
                LogMsg($"[RECOVERY] {sub.Name.ToUpper()} has been salvaged and added to your fleet!");
                AddNotification("RECOVERY SUCCESS", $"{sub.Name.ToUpper()} salvaged", Color.Cyan);
                if (_isMultiplayer) _mpClient?.SendGameActionAsync(new { type = "sub_recover", subId = sub.Id });
            });
        }
        
        private void CombatProxy(string target, int weapon, int count)
        {
             _ = FireSalvoAsync(target, weapon, count);
        }
    }
}
