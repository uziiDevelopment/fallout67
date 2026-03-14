using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;

namespace fallover_67
{
    public partial class ControlPanelForm : Form
    {
        private void SetupMultiplayer()
        {
            if (_mpClient == null) return;

            _mpClient.OnGameAction += (senderId, action) =>
            {
                try
                {
                    if (InvokeRequired)
                        Invoke(new Action(() => HandleRemoteAction(senderId, action)));
                    else
                        HandleRemoteAction(senderId, action);
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            };

            _mpClient.OnChat += (senderId, name, text) =>
            {
                SafeInvoke(() =>
                {
                    logBox.SelectionColor = cyanText;
                    LogMsg($"[COMMS] {name.ToUpper()}: {text}");
                });
            };

            _mpClient.OnReconnecting += (attempt) =>
            {
                SafeInvoke(() =>
                {
                    logBox.SelectionColor = Color.Yellow;
                    LogMsg($"[NETWORK] ⚠ Link lost. Reconnection attempt #{attempt}...");
                });
            };

            _mpClient.OnReconnected += () =>
            {
                SafeInvoke(() =>
                {
                    logBox.SelectionColor = greenText;
                    LogMsg("[NETWORK] ✔ Connection restored. Tactical data link re-established.");
                    AddNotification("RECONNECTED", "Link to command restored", Color.LimeGreen, 4f);

                    // Re-sync: broadcast current player state so others know we're alive
                    _ = BroadcastSyncStateAsync();
                });
            };

            _mpClient.OnDisconnected += () =>
            {
                SafeInvoke(() =>
                {
                    logBox.SelectionColor = redText;
                    LogMsg("[NETWORK] ✖ Final disconnect. Tactical data link offline.");
                    AddNotification("DISCONNECTED", "Lost connection to server", Color.Red, 10f);
                });
            };

            _mpClient.OnError += (msg) =>
            {
                SafeInvoke(() =>
                {
                    logBox.SelectionColor = redText;
                    LogMsg($"[NETWORK] ERROR: {msg}");
                });
            };

            _mpClient.OnPlayerDisconnected += (playerId, name) =>
            {
                SafeInvoke(() =>
                {
                    logBox.SelectionColor = Color.Yellow;
                    LogMsg($"[NETWORK] ⚠ {name.ToUpper()} lost connection. Awaiting reconnect...");
                    AddNotification("LINK LOST", $"{name} disconnected", Color.Yellow, 5f);
                });
            };

            _mpClient.OnPlayerReconnected += (playerId, name) =>
            {
                SafeInvoke(() =>
                {
                    logBox.SelectionColor = greenText;
                    LogMsg($"[NETWORK] ✔ {name.ToUpper()} has reconnected.");
                    AddNotification("LINK RESTORED", $"{name} reconnected", Color.LimeGreen, 4f);
                });
            };

            logBox.SelectionColor = cyanText;
            LogMsg("[NETWORK] Multiplayer session active. Other commanders are online.");
        }

        // ── Thread-safe UI invoke helper ────────────────────────────────────
        private void SafeInvoke(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired)
                    BeginInvoke(action);
                else
                    action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // ── Re-sync after reconnect ─────────────────────────────────────────
        private async Task BroadcastSyncStateAsync()
        {
            if (_mpClient == null || !_mpClient.IsConnected) return;

            try
            {
                // Tell other clients our vital stats so they can reconcile
                await _mpClient.SendGameActionAsync(new
                {
                    type = "sync_state",
                    nation = GameEngine.Player.NationName,
                    population = GameEngine.Player.Population,
                    allies = GameEngine.Player.Allies.ToArray(),
                    nukes = GameEngine.Player.StandardNukes
                });
            }
            catch { }
        }

        // ── Main action dispatcher ──────────────────────────────────────────
        private void HandleRemoteAction(string senderId, JsonElement action)
        {
            string type = SafeStr(action, "type");
            if (string.IsNullOrEmpty(type)) return;

            var sender = _mpPlayers.FirstOrDefault(p => p.Id == senderId);
            string senderName = sender?.Name ?? "Unknown";

            try
            {
                switch (type)
                {
                    case "strike":       HandleRemoteStrike(action, sender, senderName); break;
                    case "ai_launch":    HandleRemoteAiLaunch(action); break;
                    case "sat_strike":   HandleRemoteSatStrike(action, senderName); break;
                    case "sub_create":   HandleRemoteSubCreate(action, senderName); break;
                    case "sub_move":     HandleRemoteSubMove(action); break;
                    case "sub_fire":     HandleRemoteSubFire(action); break;
                    case "sub_destroy":  HandleRemoteSubDestroy(action, senderName); break;
                    case "sub_recover":  HandleRemoteSubRecover(action, senderName); break;
                    case "sync_state":   HandleRemoteSyncState(action, senderId, senderName); break;

                    // Diplomacy
                    case "diplomacy_summit":   HandleRemoteDiplomacySummit(action); break;
                    case "diplomacy_alliance": HandleRemoteDiplomacyAlliance(action); break;
                    case "diplomacy_betrayal": HandleRemoteDiplomacyBetrayal(action); break;

                    default:
                        // Unknown action type — ignore gracefully
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MP] Error handling '{type}': {ex.Message}");
                logBox.SelectionColor = Color.Yellow;
                LogMsg($"[NETWORK] Warning: failed to process '{type}' from {senderName}.");
            }
        }

        // ── Strike from another player ──────────────────────────────────────
        private void HandleRemoteStrike(JsonElement action, MpPlayer? sender, string senderName)
        {
            string target = SafeStr(action, "target");
            int weapon = SafeInt(action, "weapon");
            string nation = SafeStr(action, "playerNation");
            long strikeDmg = SafeLong(action, "damage");

            if (string.IsNullOrEmpty(target)) return;

            bool hitsMe = target == GameEngine.Player.NationName;
            if (!hitsMe && !GameEngine.Nations.ContainsKey(target)) return;

            string[] wNames = { "STANDARD NUKE", "TSAR BOMBA", "BIO-PLAGUE", "ORBITAL LASER" };
            Color[] wColors = { Color.OrangeRed, Color.DeepPink, Color.LimeGreen, Color.Cyan };
            float[] wRadii = { 45f, 70f, 55f, 40f };

            PointLatLng startPt = GameEngine.Nations.TryGetValue(nation, out Nation attackerNation)
                ? new PointLatLng(attackerNation.MapY, attackerNation.MapX)
                : new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);

            PointLatLng impactPt = hitsMe
                ? new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX)
                : new PointLatLng(GameEngine.Nations[target].MapY, GameEngine.Nations[target].MapX);

            float radius = weapon < wRadii.Length ? wRadii[weapon] : 45f;
            Color mColor = weapon < wColors.Length ? wColors[weapon] : Color.OrangeRed;
            string wName = weapon < wNames.Length ? wNames[weapon] : "UNKNOWN WEAPON";

            logBox.SelectionColor = amberText;
            LogMsg($"[COMMANDER] {senderName.ToUpper()} launched {wName} at {target.ToUpper()}!");

            if (hitsMe)
            {
                logBox.SelectionColor = redText;
                LogMsg($"[WARNING] ⚠ RADAR ALERT: {senderName.ToUpper()} HAS LAUNCHED AN ICBM AT YOU! BRACE FOR IMPACT! ⚠");

                // Capture values for closure safety
                string capturedSender = senderName;
                long capturedDmg = strikeDmg;
                int capturedWeapon = weapon;

                var missile = new MissileAnimation
                {
                    Start = startPt,
                    End = impactPt,
                    IsPlayerMissile = false,
                    MissileColor = Color.Red,
                    Speed = 0.4f,
                };

                lock (_animLock)
                {
                    _inboundMissiles[missile] = capturedSender;
                    _forcedDamageMap[missile] = capturedDmg;
                    activeMissiles.Add(missile);
                }

                missile.OnImpact = async () =>
                {
                    bool wasIntercepted;
                    long dmg;
                    string sName;
                    lock (_animLock)
                    {
                        _inboundMissiles.TryGetValue(missile, out sName);
                        _forcedDamageMap.TryGetValue(missile, out dmg);
                        wasIntercepted = _interceptedMissiles.Remove(missile);
                        _inboundMissiles.Remove(missile);
                        _forcedDamageMap.Remove(missile);
                    }
                    sName ??= capturedSender;
                    if (dmg == 0) dmg = capturedDmg;

                    if (wasIntercepted)
                    {
                        lock (_animLock) activeExplosions.Add(new ExplosionEffect
                        {
                            Center = impactPt, MaxRadius = 30f,
                            DamageLines = new[] { $"⚡ INTERCEPTED — {sName.ToUpper()}" },
                            IsPlayerTarget = false
                        });
                        logBox.SelectionColor = cyanText;
                        LogMsg($"[IRON DOME] ⚡ Missile from {sName.ToUpper()} INTERCEPTED!");
                        return;
                    }

                    long popBefore = GameEngine.Player.Population;
                    var dmgLogs = CombatEngine.ApplyForcedEnemyStrike(dmg, capturedWeapon);
                    long popAfter = GameEngine.Player.Population;
                    _cumulativeDamageThisWave += (popBefore - popAfter);

                    // DEAD HAND: Automatic Submarine Retaliation
                    var remoteSender = _mpPlayers.FirstOrDefault(p => p.Name == sName);
                    if (remoteSender?.Country != null && GameEngine.Nations.TryGetValue(remoteSender.Country, out var rNation))
                    {
                        var firingSubs = CombatEngine.TriggerSubmarineRetaliation(rNation.MapY, rNation.MapX, sName);
                        foreach (var s in firingSubs)
                        {
                            LogMsg($"[DEAD-HAND] {s.Name.ToUpper()} AUTOMATIC RETALIATION AGAINST {sName.ToUpper()}!");
                            AddNotification("COUNTER-STRIKE", $"{s.Name} retaliating against {sName}", Color.OrangeRed, 6f);
                            FireSubStrikeLocally(s, new PointLatLng(rNation.MapY, rNation.MapX));
                            if (_mpClient != null)
                                _ = _mpClient.SendGameActionAsync(new { type = "sub_fire", subId = s.Id, lat = (double)rNation.MapY, lng = (double)rNation.MapX });
                        }
                    }

                    lock (_animLock) activeExplosions.Add(new ExplosionEffect
                    {
                        Center = impactPt, MaxRadius = 55f,
                        DamageLines = new[] { $"STRIKE FROM {sName.ToUpper()}", $"{dmg:N0} casualties" },
                        IsPlayerTarget = true
                    });

                    foreach (var l in dmgLogs)
                    {
                        logBox.SelectionColor = l.Contains("CATASTROPHE") || l.Contains("WARNING") ? redText : l.Contains("DEFENSE") ? cyanText : greenText;
                        LogMsg(l);
                        await Task.Delay(400);
                    }

                    RefreshData();
                    CheckGameOver();

                    // Wave summary
                    if (_inboundMissiles.Count == 0 && _cumulativeDamageThisWave > 0)
                    {
                        _isDamageAlertActive = true;
                        _damageAlertTimer = 6f;
                        _damageAlertAnim = 0f;
                    }
                    else if (_inboundMissiles.Count == 0)
                    {
                        _cumulativeDamageThisWave = 0;
                    }
                };

                if (_minigamesEnabled && GameEngine.Player.IronDomeLevel > 0 && _gameState == GameState.Playing)
                    _ = Task.Run(() => SafeInvoke(() => _ = TriggerIronDomeMinigame(impactPt)));
            }
            else
            {
                // Strike hits an AI nation — apply damage locally
                lock (_animLock) activeMissiles.Add(new MissileAnimation
                {
                    Start = startPt,
                    End = impactPt,
                    IsPlayerMissile = false,
                    MissileColor = mColor,
                    Speed = 0.4f,
                    OnImpact = () =>
                    {
                        if (!GameEngine.Nations.ContainsKey(target)) return;
                        var (cas, def) = CombatEngine.ExecuteRemotePlayerStrike(target, weapon, strikeDmg);
                        lock (_animLock) activeExplosions.Add(new ExplosionEffect
                        {
                            Center = impactPt, MaxRadius = radius,
                            DamageLines = new[] { $"[{senderName.ToUpper()}] {cas:N0} casualties{(def ? " — DEFEATED" : "")}" },
                            IsPlayerTarget = false
                        });
                        logBox.SelectionColor = amberText;
                        LogMsg($"[IMPACT] {target.ToUpper()} — {cas:N0} casualties from {senderName.ToUpper()}'s strike.{(def ? " NATION DEFEATED." : "")}");

                        if (def) AddNotification("NATION FALLEN", $"{target.ToUpper()} was defeated by {senderName.ToUpper()}", Color.Orange, 6f);
                        RefreshData();
                    }
                });
            }
        }

        // ── AI launch relayed from host ─────────────────────────────────────
        private void HandleRemoteAiLaunch(JsonElement action)
        {
            string aiAttacker = SafeStr(action, "attacker");
            string aiTarget = SafeStr(action, "target");
            int aiSalvo = SafeInt(action, "salvo", 1);
            long aiDamage = SafeLong(action, "damage");

            if (string.IsNullOrEmpty(aiAttacker) || string.IsNullOrEmpty(aiTarget)) return;

            double? aLat = action.TryGetProperty("lat", out var latEl) && latEl.ValueKind == JsonValueKind.Number ? latEl.GetDouble() : null;
            double? aLng = action.TryGetProperty("lng", out var lngEl) && lngEl.ValueKind == JsonValueKind.Number ? lngEl.GetDouble() : null;

            TriggerAiLaunchLocal(aiAttacker, aiTarget, aiSalvo, aiDamage, aLat, aLng);
        }

        // ── Satellite strike from another player ────────────────────────────
        private void HandleRemoteSatStrike(JsonElement action, string senderName)
        {
            string satTarget = SafeStr(action, "target");
            if (string.IsNullOrEmpty(satTarget)) return;

            logBox.SelectionColor = Color.Violet;
            LogMsg($"[SAT-KILL] {senderName.ToUpper()} launched a Satellite Killer at {satTarget.ToUpper()}!");

            bool satHitsMe = satTarget == GameEngine.Player.NationName;
            PointLatLng satImpact = satHitsMe
                ? new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX)
                : GameEngine.Nations.TryGetValue(satTarget, out Nation satNation)
                    ? new PointLatLng(satNation.MapY, satNation.MapX)
                    : new PointLatLng(0, 0);

            string attackerNation = SafeStr(action, "playerNation");
            PointLatLng satStart = GameEngine.Nations.TryGetValue(attackerNation, out Nation satAttNation)
                ? new PointLatLng(satAttNation.MapY, satAttNation.MapX)
                : new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);

            lock (_animLock) activeMissiles.Add(new MissileAnimation
            {
                Start = satStart,
                End = satImpact,
                IsPlayerMissile = false,
                MissileColor = Color.Violet,
                Speed = 0.20f,
                OnImpact = async () =>
                {
                    if (satHitsMe)
                    {
                        GameEngine.Player.SatelliteBlindUntil = DateTime.Now.AddSeconds(90);
                        logBox.SelectionColor = Color.Violet;
                        LogMsg($"[SAT-KILL] {senderName.ToUpper()} destroyed YOUR satellites! Targeting offline for 90s.");
                        RefreshData();
                    }
                    else
                    {
                        await ApplySatelliteStrikeImpact(satTarget, satImpact);
                    }
                }
            });
        }

        // ── Submarine commands ──────────────────────────────────────────────
        private void HandleRemoteSubCreate(JsonElement action, string senderName)
        {
            string scId = SafeStr(action, "subId");
            string scName = SafeStr(action, "name");
            float scX = SafeFloat(action, "x");
            float scY = SafeFloat(action, "y");
            if (string.IsNullOrEmpty(scId)) return;
            if (!GameEngine.Submarines.Any(s => s.Id == scId))
                GameEngine.Submarines.Add(new Submarine { Id = scId, Name = scName, MapX = scX, MapY = scY, OwnerId = senderName });
        }

        private void HandleRemoteSubMove(JsonElement action)
        {
            string smId = SafeStr(action, "subId");
            float smX = SafeFloat(action, "tx");
            float smY = SafeFloat(action, "ty");
            var smSub = GameEngine.Submarines.FirstOrDefault(s => s.Id == smId);
            if (smSub != null)
            {
                smSub.TargetX = smX;
                smSub.TargetY = smY;
                smSub.Waypoints = MapUtility.FindPath(smSub.MapX, smSub.MapY, smX, smY);
                smSub.IsMoving = true;
            }
        }

        private void HandleRemoteSubFire(JsonElement action)
        {
            string sfId = SafeStr(action, "subId");
            double sfLat = SafeDouble(action, "lat");
            double sfLng = SafeDouble(action, "lng");
            var sfSub = GameEngine.Submarines.FirstOrDefault(s => s.Id == sfId);
            if (sfSub != null)
            {
                sfSub.RevealedUntil = DateTime.Now.AddSeconds(15);
                FireSubStrikeLocally(sfSub, new PointLatLng(sfLat, sfLng));
            }
        }

        private void HandleRemoteSubDestroy(JsonElement action, string senderName)
        {
            string sdId = SafeStr(action, "subId");
            var sdSub = GameEngine.Submarines.FirstOrDefault(s => s.Id == sdId);
            if (sdSub != null)
            {
                sdSub.Health = 0;
                LogMsg($"[SENSORS] {senderName.ToUpper()} reports submarine {sdSub.Name.ToUpper()} DESTROYED.");
            }
        }

        private void HandleRemoteSubRecover(JsonElement action, string senderName)
        {
            string srId = SafeStr(action, "subId");
            var srSub = GameEngine.Submarines.FirstOrDefault(s => s.Id == srId);
            if (srSub != null)
            {
                srSub.Health = 50;
                srSub.OwnerId = senderName;
                srSub.NukeCount = 0;
                LogMsg($"[RECOVERY] {senderName.ToUpper()} salvaged submarine {srSub.Name.ToUpper()}.");
            }
        }

        // ── Sync state from reconnected player ──────────────────────────────
        private void HandleRemoteSyncState(JsonElement action, string senderId, string senderName)
        {
            string nationName = SafeStr(action, "nation");
            long population = SafeLong(action, "population");

            if (string.IsNullOrEmpty(nationName)) return;

            // Update the remote player's nation state if we track it
            if (GameEngine.Nations.TryGetValue(nationName, out var nation))
            {
                // Only reconcile if the remote value diverged significantly (>5%)
                if (nation.IsHumanControlled && population > 0)
                {
                    long diff = Math.Abs(nation.Population - population);
                    if (diff > nation.MaxPopulation * 0.05)
                    {
                        nation.Population = population;
                        logBox.SelectionColor = Color.Yellow;
                        LogMsg($"[SYNC] Reconciled {nationName.ToUpper()} population with {senderName.ToUpper()}'s state.");
                    }
                }
            }

            // Reconcile allies from the remote player's perspective
            if (action.TryGetProperty("allies", out var alliesEl) && alliesEl.ValueKind == JsonValueKind.Array)
            {
                var remoteAllies = new List<string>();
                foreach (var a in alliesEl.EnumerateArray())
                {
                    string allyName = a.GetString() ?? "";
                    if (!string.IsNullOrEmpty(allyName)) remoteAllies.Add(allyName);
                }

                // If the remote player has allies we don't know about, form them
                if (GameEngine.Nations.TryGetValue(nationName, out var syncNation))
                {
                    foreach (string ally in remoteAllies)
                    {
                        if (!syncNation.Allies.Contains(ally))
                        {
                            DiplomacyEngine.FormAlliance(nationName, ally);
                            logBox.SelectionColor = Color.Yellow;
                            LogMsg($"[SYNC] Discovered alliance: {nationName.ToUpper()} + {ally.ToUpper()}.");
                        }
                    }
                }
            }

            RefreshData();
        }

        // ── Diplomacy messages ──────────────────────────────────────────────
        private void HandleRemoteDiplomacySummit(JsonElement action)
        {
            string dn1 = SafeStr(action, "nation1");
            string dn2 = SafeStr(action, "nation2");
            if (string.IsNullOrEmpty(dn1) || string.IsNullOrEmpty(dn2)) return;

            logBox.SelectionColor = amberText;
            LogMsg($"[DIPLOMACY] {dn1.ToUpper()} is sending a diplomatic plane to {dn2.ToUpper()}.");
            AddNotification("REMOTE SUMMIT", $"{dn1} → {dn2}", Color.Gold, 5f);
        }

        private void HandleRemoteDiplomacyAlliance(JsonElement action)
        {
            string an1 = SafeStr(action, "nation1");
            string an2 = SafeStr(action, "nation2");
            if (string.IsNullOrEmpty(an1) || string.IsNullOrEmpty(an2)) return;

            DiplomacyEngine.FormAlliance(an1, an2);
            logBox.SelectionColor = cyanText;
            LogMsg($"[DIPLOMACY] {an1.ToUpper()} and {an2.ToUpper()} have formed an alliance.");
            AddNotification("NEW ALLIANCE", $"{an1} + {an2}", Color.Cyan, 5f);
            RefreshData();
        }

        private void HandleRemoteDiplomacyBetrayal(JsonElement action)
        {
            string db = SafeStr(action, "betrayer");
            string dv = SafeStr(action, "victim");
            if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(dv)) return;

            DiplomacyEngine.BreakAlliance(db, dv);
            logBox.SelectionColor = redText;
            LogMsg($"[BETRAYAL] {db.ToUpper()} has betrayed {dv.ToUpper()}!");
            AddNotification("BETRAYAL", $"{db} → {dv}", Color.Red, 6f);
            RefreshData();
        }

        // ── Safe JSON accessors (match MultiplayerClient pattern) ───────────
        private static string SafeStr(JsonElement el, string key, string fallback = "")
        {
            if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? fallback;
            return fallback;
        }

        private static int SafeInt(JsonElement el, string key, int fallback = 0)
        {
            if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
            return fallback;
        }

        private static long SafeLong(JsonElement el, string key, long fallback = 0)
        {
            if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt64();
            return fallback;
        }

        private static float SafeFloat(JsonElement el, string key, float fallback = 0)
        {
            if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                return (float)v.GetDouble();
            return fallback;
        }

        private static double SafeDouble(JsonElement el, string key, double fallback = 0)
        {
            if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetDouble();
            return fallback;
        }
    }
}
