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
        // ── Cyber Ops State ──────────────────────────────────────────────────
        private bool _isHijacking = false;           // currently controlling a hacked nation
        private string _hijackedNation = null;        // which nation we're controlling
        private DateTime _hijackExpires = DateTime.MinValue;
        private System.Windows.Forms.Timer _hijackTimer;
        private Label _lblHijackBanner;

        // ── Hack Button (wired in SetupCyberOps, called from SetupUI) ────
        private Button btnHack;

        private void SetupCyberOps()
        {
            // Hijack tick timer — checks expiry + AI defense kicks
            _hijackTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _hijackTimer.Tick += HijackTimer_Tick;
            _hijackTimer.Start();
        }

        // ── HACK BUTTON CLICK ────────────────────────────────────────────────
        private void BtnHack_Click(object sender, EventArgs e)
        {
            if (GameEngine.Player.CyberOpsLevel < 2)
            {
                MessageBox.Show("Cyber Warfare Suite Level 2 required.\nUpgrade at the Black Market.", "INSUFFICIENT TECH", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrEmpty(selectedTarget) || !GameEngine.Nations.ContainsKey(selectedTarget))
            {
                MessageBox.Show("Select a target nation first.", "NO TARGET");
                return;
            }

            Nation target = GameEngine.Nations[selectedTarget];
            if (target.IsDefeated)
            {
                MessageBox.Show("Target nation is already defeated.", "INVALID TARGET");
                return;
            }

            if (target.IsHacked)
            {
                MessageBox.Show($"{target.Name.ToUpper()} is already compromised.", "ALREADY HACKED");
                return;
            }

            if (GameEngine.Player.HackCooldown > 0)
            {
                MessageBox.Show($"Cyber systems recharging. {GameEngine.Player.HackCooldown}s remaining.", "COOLDOWN");
                return;
            }

            if (GameEngine.Player.IsSatelliteBlind)
            {
                MessageBox.Show("SATELLITE UPLINK OFFLINE — Cannot establish cyber connection.", "SYSTEM ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_isHijacking)
            {
                MessageBox.Show("Already controlling a hacked nation. Wait for current hack to expire.", "BUSY");
                return;
            }

            // Launch the hacking minigame
            logBox.SelectionColor = Color.Magenta;
            LogMsg($"[CYBER] Initiating breach protocol against {target.Name.ToUpper()} defense network...");
            AddNotification("CYBER ATTACK", $"Hacking {target.Name.ToUpper()}", Color.Magenta, 4f);

            var hackGame = new HackingMinigameForm(target.Name, target.Difficulty);
            hackGame.ShowDialog(this);

            if (!hackGame.HackSuccess)
            {
                logBox.SelectionColor = Color.Magenta;
                LogMsg($"[CYBER] Intrusion FAILED — {target.Name.ToUpper()} firewall held. Systems traced.");
                AddNotification("HACK FAILED", $"{target.Name.ToUpper()} detected you", Color.Red, 5f);
                GameEngine.Player.HackCooldown = 45; // Long cooldown on failure
                target.IsHostileToPlayer = true;
                target.AngerLevel = Math.Min(10, target.AngerLevel + 3);

                // Broadcast failure (makes target hostile on all clients)
                if (_isMultiplayer && _mpClient != null)
                    _ = _mpClient.SendGameActionAsync(new { type = "hack_failed", target = target.Name, attacker = GameEngine.Player.NationName });

                RefreshData();
                return;
            }

            // SUCCESS — show choice dialog: Hijack Control or Self-Nuke
            GameEngine.Player.HackCooldown = 60; // Cooldown regardless

            logBox.SelectionColor = Color.Magenta;
            LogMsg($"[CYBER] ✓ BREACH SUCCESSFUL — {target.Name.ToUpper()} defense network compromised!");

            var choice = ShowHackChoiceDialog(target);
            if (choice == HackChoice.Hijack)
                ExecuteHijack(target);
            else if (choice == HackChoice.SelfNuke)
                ExecuteSelfNuke(target);
            else
            {
                // Cancelled — still sets cooldown but doesn't exploit
                logBox.SelectionColor = Color.Magenta;
                LogMsg($"[CYBER] Exploit abandoned. Connection severed.");
            }
        }

        // ── Choice Dialog ────────────────────────────────────────────────────
        private enum HackChoice { Hijack, SelfNuke, Cancel }

        private HackChoice ShowHackChoiceDialog(Nation target)
        {
            var dlg = new Form
            {
                Text = $"EXPLOIT — {target.Name.ToUpper()} COMPROMISED",
                Size = new Size(480, 280),
                BackColor = Color.FromArgb(8, 5, 15),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lblTitle = new Label
            {
                Text = $"ACCESS GRANTED TO {target.Name.ToUpper()}\nCHOOSE YOUR EXPLOIT:",
                Location = new Point(15, 15),
                Size = new Size(440, 45),
                ForeColor = Color.FromArgb(0, 255, 180),
                Font = new Font("Consolas", 12F, FontStyle.Bold)
            };

            var btnHijack = new Button
            {
                Text = $"⚡ HIJACK CONTROL (60s)\nBecome {target.Name} — use their nukes,\nmoney, and resources as your own",
                Location = new Point(15, 70),
                Size = new Size(440, 65),
                BackColor = Color.FromArgb(0, 40, 50),
                ForeColor = Color.FromArgb(0, 255, 200),
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };

            var btnSelfNuke = new Button
            {
                Text = $"☢ TURN THEIR NUKES ON THEMSELVES\nDetonate {target.Name}'s arsenal on their\nown soil — devastating self-strike",
                Location = new Point(15, 145),
                Size = new Size(440, 65),
                BackColor = Color.FromArgb(60, 0, 0),
                ForeColor = Color.FromArgb(255, 80, 40),
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };

            var btnCancel = new Button
            {
                Text = "[ABORT]",
                Location = new Point(380, 225),
                Size = new Size(80, 30),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Gray,
                Font = new Font("Consolas", 9F),
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel
            };

            HackChoice result = HackChoice.Cancel;
            btnHijack.Click += (s, ev) => { result = HackChoice.Hijack; dlg.DialogResult = DialogResult.OK; dlg.Close(); };
            btnSelfNuke.Click += (s, ev) => { result = HackChoice.SelfNuke; dlg.DialogResult = DialogResult.OK; dlg.Close(); };

            dlg.Controls.AddRange(new Control[] { lblTitle, btnHijack, btnSelfNuke, btnCancel });
            dlg.AcceptButton = null;
            dlg.CancelButton = btnCancel;
            dlg.ShowDialog(this);
            return result;
        }

        // ── HIJACK MODE ──────────────────────────────────────────────────────
        private void ExecuteHijack(Nation target)
        {
            _isHijacking = true;
            _hijackedNation = target.Name;
            _hijackExpires = DateTime.Now.AddSeconds(60);
            target.HackedBy = GameEngine.Player.NationName;
            target.HackedUntil = _hijackExpires;
            target.CyberDefenseStarted = DateTime.Now.AddSeconds(15); // AI starts defending after 15s

            logBox.SelectionColor = Color.Magenta;
            LogMsg($"[CYBER] ⚡ HIJACK ACTIVE — You now control {target.Name.ToUpper()} for 60 seconds!");
            LogMsg($"[CYBER] Access: {target.Nukes} nukes, ${target.Money}M treasury, {target.Population:N0} citizens");
            AddNotification("HIJACK ACTIVE", $"Controlling {target.Name.ToUpper()} — 60s", Color.Magenta, 8f);

            ShowHijackBanner(target.Name);

            // Broadcast to multiplayer
            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new
                {
                    type = "hack_hijack",
                    target = target.Name,
                    attacker = GameEngine.Player.NationName,
                    duration = 60
                });

            RefreshData();
        }

        private void ShowHijackBanner(string nationName)
        {
            if (_lblHijackBanner == null)
            {
                _lblHijackBanner = new Label
                {
                    Size = new Size(800, 30),
                    Location = new Point(10, 525),
                    Font = new Font("Consolas", 11F, FontStyle.Bold),
                    ForeColor = Color.Magenta,
                    BackColor = Color.FromArgb(30, 0, 40),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BorderStyle = BorderStyle.FixedSingle
                };
                this.Controls.Add(_lblHijackBanner);
                _lblHijackBanner.BringToFront();
            }
            _lblHijackBanner.Visible = true;
            UpdateHijackBanner();
        }

        private void UpdateHijackBanner()
        {
            if (_lblHijackBanner == null || !_isHijacking) return;
            int secsLeft = Math.Max(0, (int)(_hijackExpires - DateTime.Now).TotalSeconds);
            var nation = GameEngine.Nations.TryGetValue(_hijackedNation, out var n) ? n : null;
            string nukes = nation != null ? $"NUKES: {nation.Nukes}" : "NUKES: ?";
            string money = nation != null ? $"TREASURY: ${nation.Money}M" : "";
            _lblHijackBanner.Text = $"⚡ HIJACK: {_hijackedNation?.ToUpper()} — {secsLeft}s REMAINING | {nukes} | {money} | USE WEAPONS PANEL TO FIRE THEIR ARSENAL ⚡";
        }

        private void HijackTimer_Tick(object sender, EventArgs e)
        {
            // Tick hack cooldown
            if (GameEngine.Player.HackCooldown > 0)
                GameEngine.Player.HackCooldown--;

            if (!_isHijacking) return;

            UpdateHijackBanner();

            // Check if hijack expired
            if (DateTime.Now >= _hijackExpires)
            {
                EndHijack("TIME EXPIRED — Connection severed.");
                return;
            }

            // AI defense: after 15s, AI nations auto-boot the hacker
            if (_hijackedNation != null && GameEngine.Nations.TryGetValue(_hijackedNation, out Nation hacked))
            {
                if (!hacked.IsHumanControlled && DateTime.Now >= hacked.CyberDefenseStarted && hacked.IsHacked)
                {
                    // AI gradually fights back — 20% chance per second after defense starts
                    if (rng.NextDouble() < 0.20)
                    {
                        EndHijack($"{_hijackedNation.ToUpper()} AI countermeasures activated — Access revoked!");
                        return;
                    }
                }
            }
        }

        private void EndHijack(string reason)
        {
            if (!_isHijacking) return;

            // Clear hack state on nation
            if (_hijackedNation != null && GameEngine.Nations.TryGetValue(_hijackedNation, out Nation n))
            {
                n.HackedBy = null;
                n.HackedUntil = DateTime.MinValue;
            }

            logBox.SelectionColor = Color.Magenta;
            LogMsg($"[CYBER] {reason}");
            AddNotification("HACK ENDED", _hijackedNation?.ToUpper() ?? "Unknown", Color.Gray, 4f);

            // Broadcast end
            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "hack_end", target = _hijackedNation });

            _isHijacking = false;
            _hijackedNation = null;
            GameEngine.Player.HackedTarget = null;
            if (_lblHijackBanner != null) _lblHijackBanner.Visible = false;
            RefreshData();
        }

        // ── Fire hijacked nation's weapons ───────────────────────────────────
        public async Task FireHijackedSalvo(string targetName)
        {
            if (!_isHijacking || _hijackedNation == null) return;
            if (!GameEngine.Nations.TryGetValue(_hijackedNation, out Nation hijacked)) return;
            if (hijacked.Nukes <= 0)
            {
                LogMsg($"[CYBER] {_hijackedNation.ToUpper()} has no nukes remaining.");
                return;
            }
            if (!GameEngine.Nations.ContainsKey(targetName) && targetName != GameEngine.Player.NationName) return;

            hijacked.Nukes--;
            long damage = CombatEngine.PreCalculatePlayerDamage(targetName, 0);

            logBox.SelectionColor = Color.Magenta;
            LogMsg($"[CYBER] Launching hijacked {_hijackedNation.ToUpper()} nuke at {targetName.ToUpper()}!");
            AddNotification("HIJACK STRIKE", $"{_hijackedNation} → {targetName}", Color.Magenta, 4f);

            PointLatLng startPt = new PointLatLng(hijacked.MapY, hijacked.MapX);
            PointLatLng impactPt;

            if (GameEngine.Nations.TryGetValue(targetName, out Nation tgt))
                impactPt = new PointLatLng(tgt.MapY, tgt.MapX);
            else
                impactPt = new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);

            // Broadcast as a hijack strike
            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new
                {
                    type = "hack_strike",
                    source = _hijackedNation,
                    target = targetName,
                    attacker = GameEngine.Player.NationName,
                    damage = damage
                });

            lock (_animLock) activeMissiles.Add(new MissileAnimation
            {
                Start = startPt,
                End = impactPt,
                IsPlayerMissile = true,
                MissileColor = Color.Magenta,
                Speed = 0.4f,
                OnImpact = async () =>
                {
                    if (GameEngine.Nations.TryGetValue(targetName, out Nation target))
                    {
                        var result = CombatEngine.ExecuteCombatTurn(targetName, 0, damage);
                        lock (_animLock) activeExplosions.Add(new ExplosionEffect
                        {
                            Center = impactPt,
                            MaxRadius = 50f,
                            DamageLines = new[] { $"HIJACKED STRIKE ({_hijackedNation?.ToUpper()})", result.Logs.FirstOrDefault(l => l.Contains("[IMPACT]")) ?? "" },
                            IsPlayerTarget = false
                        });
                        foreach (var l in result.Logs)
                        {
                            logBox.SelectionColor = Color.Magenta;
                            LogMsg(l);
                            await Task.Delay(400);
                        }

                        // Sync nation defeat from hijack strike
                        if (target.IsDefeated && _isMultiplayer && _mpClient != null)
                            _ = _mpClient.SendGameActionAsync(new { type = "nation_defeated", nation = targetName, population = 0L });

                        RefreshData();
                        CheckGameOver();
                    }
                }
            });

            UpdateHijackBanner();
            RefreshData();
        }

        // ── SELF-NUKE MODE ───────────────────────────────────────────────────
        private void ExecuteSelfNuke(Nation target)
        {
            int nukesToUse = Math.Min(target.Nukes, 3); // Use up to 3 of their own nukes
            if (nukesToUse <= 0)
            {
                logBox.SelectionColor = Color.Magenta;
                LogMsg($"[CYBER] {target.Name.ToUpper()} has no nuclear arsenal to detonate.");
                return;
            }

            target.Nukes -= nukesToUse;

            // Calculate devastating self-strike damage (each nuke does 15-25% of max pop)
            long totalDamage = 0;
            for (int i = 0; i < nukesToUse; i++)
                totalDamage += (long)(target.MaxPopulation * (0.15 + rng.NextDouble() * 0.10));
            totalDamage = Math.Min(totalDamage, target.Population);

            target.Population -= totalDamage;
            // Also destroy infrastructure
            target.Money = (long)(target.Money * 0.3);

            logBox.SelectionColor = Color.Magenta;
            LogMsg($"[CYBER] ☢ TRIGGERING SELF-DESTRUCT on {target.Name.ToUpper()}!");
            LogMsg($"[CYBER] {nukesToUse} warheads detonated on home soil — {totalDamage:N0} casualties!");
            AddNotification("SELF-DESTRUCT", $"{target.Name.ToUpper()} nuked themselves!", Color.OrangeRed, 8f);

            // Explosion at target location
            PointLatLng impactPt = new PointLatLng(target.MapY, target.MapX);
            lock (_animLock)
            {
                for (int i = 0; i < nukesToUse; i++)
                {
                    float offset = (i - nukesToUse / 2f) * 0.5f;
                    activeExplosions.Add(new ExplosionEffect
                    {
                        Center = new PointLatLng(impactPt.Lat + offset * 0.2, impactPt.Lng + offset * 0.3),
                        MaxRadius = 65f,
                        DamageLines = new[] { "CYBER SELF-STRIKE", $"{totalDamage / nukesToUse:N0} per warhead" },
                        IsPlayerTarget = false
                    });
                }
            }

            // Check if defeated
            if (target.Population <= 0)
            {
                target.IsDefeated = true;
                logBox.SelectionColor = amberText;
                LogMsg($"[VICTORY] {target.Name.ToUpper()} OBLITERATED BY THEIR OWN ARSENAL!");
                AddNotification("NATION DESTROYED", $"{target.Name.ToUpper()} self-destructed", Color.Gold, 8f);
            }

            // Broadcast to multiplayer
            if (_isMultiplayer && _mpClient != null)
            {
                _ = _mpClient.SendGameActionAsync(new
                {
                    type = "hack_selfnuke",
                    target = target.Name,
                    attacker = GameEngine.Player.NationName,
                    damage = totalDamage,
                    nukesUsed = nukesToUse
                });

                if (target.IsDefeated)
                    _ = _mpClient.SendGameActionAsync(new { type = "nation_defeated", nation = target.Name, population = 0L });
            }

            GameEngine.Player.HackCooldown = 60;
            RefreshData();
            CheckGameOver();
        }

        // ── DEFENDING AGAINST INCOMING HACKS (Player is the victim) ──────────
        private void HandleIncomingHack(string attackerName, string attackerNation)
        {
            logBox.SelectionColor = Color.Red;
            LogMsg($"[CYBER ALERT] ⚠ {attackerName.ToUpper()} IS HACKING YOUR DEFENSE NETWORK! ⚠");
            AddNotification("⚠ HACKED", $"{attackerName.ToUpper()} is in your systems!", Color.Red, 8f);

            if (_minigamesEnabled)
            {
                // Player gets the cyber defense minigame
                var defenseGame = new CyberDefenseForm(attackerName, 3);
                defenseGame.ShowDialog(this);

                if (defenseGame.DefenseSuccess)
                {
                    logBox.SelectionColor = cyanText;
                    LogMsg($"[CYBER] ✓ INTRUSION REPELLED! {attackerName.ToUpper()} has been locked out.");
                    AddNotification("HACK REPELLED", "Systems secured", Color.Cyan, 5f);

                    if (_isMultiplayer && _mpClient != null)
                        _ = _mpClient.SendGameActionAsync(new { type = "hack_defended", attacker = attackerNation });
                }
                else
                {
                    logBox.SelectionColor = Color.Red;
                    LogMsg($"[CYBER] ✖ DEFENSE FAILED — {attackerName.ToUpper()} maintains access!");
                    AddNotification("DEFENSE FAILED", $"{attackerName.ToUpper()} controls your systems", Color.Red, 6f);
                }
            }
        }

        // ── Multiplayer handlers ─────────────────────────────────────────────
        private void HandleRemoteHack(string type, System.Text.Json.JsonElement action, string senderName)
        {
            string targetNation = SafeStr(action, "target");
            string attackerNation = SafeStr(action, "attacker");

            switch (type)
            {
                case "hack_hijack":
                {
                    // Someone hacked an AI or player nation
                    if (targetNation == GameEngine.Player.NationName)
                    {
                        // WE are being hacked — launch defense minigame
                        HandleIncomingHack(senderName, attackerNation);
                    }
                    else if (GameEngine.Nations.TryGetValue(targetNation, out Nation n))
                    {
                        n.HackedBy = attackerNation;
                        n.HackedUntil = DateTime.Now.AddSeconds(SafeInt(action, "duration", 60));
                        logBox.SelectionColor = Color.Magenta;
                        LogMsg($"[CYBER] {senderName.ToUpper()} has hacked {targetNation.ToUpper()}!");
                        AddNotification("CYBER ATTACK", $"{senderName} hacked {targetNation}", Color.Magenta, 5f);
                    }
                    break;
                }

                case "hack_end":
                {
                    if (GameEngine.Nations.TryGetValue(targetNation, out Nation n))
                    {
                        n.HackedBy = null;
                        n.HackedUntil = DateTime.MinValue;
                    }
                    logBox.SelectionColor = Color.Magenta;
                    LogMsg($"[CYBER] Hack on {targetNation.ToUpper()} has ended.");
                    break;
                }

                case "hack_selfnuke":
                {
                    long damage = SafeLong(action, "damage");
                    int nukesUsed = SafeInt(action, "nukesUsed", 1);
                    if (GameEngine.Nations.TryGetValue(targetNation, out Nation n))
                    {
                        n.Nukes = Math.Max(0, n.Nukes - nukesUsed);
                        long actualDmg = Math.Min(damage, n.Population);
                        n.Population -= actualDmg;
                        n.Money = (long)(n.Money * 0.3);
                        if (n.Population <= 0) n.IsDefeated = true;

                        PointLatLng pt = new PointLatLng(n.MapY, n.MapX);
                        lock (_animLock) activeExplosions.Add(new ExplosionEffect
                        {
                            Center = pt, MaxRadius = 65f,
                            DamageLines = new[] { "CYBER SELF-STRIKE", $"{actualDmg:N0} casualties" },
                            IsPlayerTarget = false
                        });

                        logBox.SelectionColor = Color.Magenta;
                        LogMsg($"[CYBER] {senderName.ToUpper()} triggered self-destruct on {targetNation.ToUpper()}! {actualDmg:N0} casualties.");
                        if (n.IsDefeated)
                        {
                            AddNotification("NATION DESTROYED", $"{targetNation.ToUpper()} self-destructed", Color.Gold, 6f);
                            CheckGameOver();
                        }
                    }
                    RefreshData();
                    break;
                }

                case "hack_strike":
                {
                    // Hijacked nation's nuke fired at someone
                    string source = SafeStr(action, "source");
                    long strikeDmg = SafeLong(action, "damage");

                    bool hitsMe = targetNation == GameEngine.Player.NationName;

                    PointLatLng startPt = GameEngine.Nations.TryGetValue(source, out Nation srcN)
                        ? new PointLatLng(srcN.MapY, srcN.MapX) : new PointLatLng(0, 0);
                    PointLatLng impactPt = hitsMe
                        ? new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX)
                        : GameEngine.Nations.TryGetValue(targetNation, out Nation tN)
                            ? new PointLatLng(tN.MapY, tN.MapX) : new PointLatLng(0, 0);

                    logBox.SelectionColor = Color.Magenta;
                    LogMsg($"[CYBER] {senderName.ToUpper()} fired hijacked {source.ToUpper()} nuke at {targetNation.ToUpper()}!");

                    if (hitsMe)
                    {
                        // Treat as inbound enemy missile
                        var missile = new MissileAnimation
                        {
                            Start = startPt, End = impactPt,
                            IsPlayerMissile = false, MissileColor = Color.Magenta, Speed = 0.4f
                        };
                        lock (_animLock)
                        {
                            _inboundMissiles[missile] = senderName;
                            _forcedDamageMap[missile] = strikeDmg;
                            activeMissiles.Add(missile);
                        }
                        missile.OnImpact = async () =>
                        {
                            bool wasIntercepted;
                            lock (_animLock)
                            {
                                wasIntercepted = _interceptedMissiles.Remove(missile);
                                _inboundMissiles.Remove(missile);
                                _forcedDamageMap.Remove(missile);
                            }
                            if (wasIntercepted)
                            {
                                lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPt, MaxRadius = 30f, DamageLines = new[] { "⚡ INTERCEPTED" }, IsPlayerTarget = false });
                                return;
                            }
                            var logs = CombatEngine.ApplyForcedEnemyStrike(strikeDmg, 0);
                            lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPt, MaxRadius = 55f, DamageLines = new[] { $"HIJACK STRIKE FROM {source.ToUpper()}" }, IsPlayerTarget = true });
                            foreach (var l in logs) { logBox.SelectionColor = redText; LogMsg(l); await Task.Delay(400); }
                            RefreshData(); CheckGameOver();
                        };
                        if (_minigamesEnabled && GameEngine.Player.IronDomeLevel > 0 && _gameState == GameState.Playing)
                            _ = Task.Run(() => SafeInvoke(() => _ = TriggerIronDomeMinigame(impactPt)));
                    }
                    else
                    {
                        // Hits AI nation
                        lock (_animLock) activeMissiles.Add(new MissileAnimation
                        {
                            Start = startPt, End = impactPt, IsPlayerMissile = false,
                            MissileColor = Color.Magenta, Speed = 0.4f,
                            OnImpact = () =>
                            {
                                if (!GameEngine.Nations.ContainsKey(targetNation)) return;
                                var (cas, def) = CombatEngine.ExecuteRemotePlayerStrike(targetNation, 0, strikeDmg);
                                lock (_animLock) activeExplosions.Add(new ExplosionEffect
                                {
                                    Center = impactPt, MaxRadius = 50f,
                                    DamageLines = new[] { $"HIJACK: {source.ToUpper()} → {targetNation.ToUpper()}", $"{cas:N0} casualties" },
                                    IsPlayerTarget = false
                                });
                                if (def) CheckGameOver();
                                RefreshData();
                            }
                        });
                    }
                    break;
                }

                case "hack_failed":
                {
                    if (GameEngine.Nations.TryGetValue(targetNation, out Nation n))
                    {
                        n.IsHostileToPlayer = true;
                        n.AngerLevel = Math.Min(10, n.AngerLevel + 3);
                    }
                    logBox.SelectionColor = Color.Magenta;
                    LogMsg($"[CYBER] {senderName.ToUpper()} failed to hack {targetNation.ToUpper()} — intrusion detected.");
                    break;
                }

                case "hack_defended":
                {
                    // The player we hacked successfully defended
                    if (_isHijacking && _hijackedNation == attackerNation)
                        EndHijack($"{attackerNation.ToUpper()} purged your intrusion — access revoked!");
                    break;
                }
            }
        }
    }
}
