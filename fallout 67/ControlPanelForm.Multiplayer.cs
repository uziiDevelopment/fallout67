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
        private void SetupMultiplayer()
        {
            if (_mpClient == null) return;
            _mpClient.OnGameAction += (senderId, action) => { if (InvokeRequired) Invoke(new Action(() => HandleRemoteAction(senderId, action))); else HandleRemoteAction(senderId, action); };
            _mpClient.OnChat += (senderId, name, text) => { if (InvokeRequired) Invoke(new Action(() => { logBox.SelectionColor = cyanText; LogMsg($"[COMMS] {name.ToUpper()}: {text}"); })); };
            _mpClient.OnDisconnected += () => { if (InvokeRequired) Invoke(new Action(() => { logBox.SelectionColor = redText; LogMsg("[NETWORK] âš  Lost connection to multiplayer server."); })); };
            logBox.SelectionColor = cyanText; LogMsg("[NETWORK] Multiplayer session active. Other commanders are online.");
        }

        private void HandleRemoteAction(string senderId, System.Text.Json.JsonElement action)
        {
            if (!action.TryGetProperty("type", out var tp)) return;
            string type = tp.GetString() ?? "";

            var sender = _mpPlayers.FirstOrDefault(p => p.Id == senderId);
            string senderName = sender?.Name ?? "Unknown";

            switch (type)
            {
                case "strike":
                    string target = action.GetProperty("target").GetString() ?? "";
                    int weapon = action.GetProperty("weapon").GetInt32();
                    string nation = action.GetProperty("playerNation").GetString() ?? "";
                    long strikeDmg = action.TryGetProperty("damage", out var dg) ? dg.GetInt64() : 0;

                    bool hitsMe = target == GameEngine.Player.NationName;
                    if (!hitsMe && !GameEngine.Nations.ContainsKey(target)) break;

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

                    logBox.SelectionColor = amberText;
                    LogMsg($"[COMMANDER] {senderName.ToUpper()} launched {wNames[weapon]} at {target.ToUpper()}!");

                    if (hitsMe)
                    {
                        logBox.SelectionColor = redText;
                        LogMsg($"[WARNING] âš  RADAR ALERT: {senderName.ToUpper()} HAS LAUNCHED AN ICBM AT YOU! BRACE FOR IMPACT! âš ");

                        string capturedSender = senderName;
                        long capturedDmg = strikeDmg;

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
                            var dmgLogs = CombatEngine.ApplyForcedEnemyStrike(dmg, weapon);
                            long popAfter = GameEngine.Player.Population;
                            _cumulativeDamageThisWave += (popBefore - popAfter);

                            // DEAD HAND: Automatic Submarine Retaliation (Multiplayer)
                            var remoteSender = _mpPlayers.FirstOrDefault(p => p.Name == sName);
                            if (remoteSender != null && remoteSender.Country != null && GameEngine.Nations.TryGetValue(remoteSender.Country, out var rNation))
                            {
                                var firingSubs = CombatEngine.TriggerSubmarineRetaliation(rNation.MapY, rNation.MapX, sName);
                                foreach (var s in firingSubs)
                                {
                                    LogMsg($"[DEAD-HAND] {s.Name.ToUpper()} AUTOMATIC RETALIATION AGAINST {sName.ToUpper()}!");
                                    AddNotification("COUNTER-STRIKE", $"{s.Name} retaliating against {sName}", Color.OrangeRed, 6f);
                                    FireSubStrikeLocally(s, new PointLatLng(rNation.MapY, rNation.MapX));
                                    _ = _mpClient?.SendGameActionAsync(new { type = "sub_fire", subId = s.Id, lat = (double)rNation.MapY, lng = (double)rNation.MapX });
                                }
                            }

                            lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPt, MaxRadius = 55f, DamageLines = new[] { $"STRIKE FROM {sName.ToUpper()}", $"{dmg:N0} casualties" }, IsPlayerTarget = true });
                            foreach (var l in dmgLogs) { logBox.SelectionColor = l.Contains("CATASTROPHE") || l.Contains("WARNING") ? redText : l.Contains("DEFENSE") ? cyanText : greenText; LogMsg(l); await Task.Delay(400); }
                            
                            RefreshData();
                            CheckGameOver();

                            // Trigger Summary logic
                            if (_inboundMissiles.Count == 0 && _cumulativeDamageThisWave > 0)
                            {
                                _isDamageAlertActive = true;
                                _damageAlertTimer = 6f;
                                _damageAlertAnim = 0f;
                            }
                            else if (_inboundMissiles.Count == 0) _cumulativeDamageThisWave = 0;
                        };

                        if (_minigamesEnabled && GameEngine.Player.IronDomeLevel > 0 && _gameState == GameState.Playing)
                            _ = Task.Run(() => mapPanel.BeginInvoke(new Action(() => _ = TriggerIronDomeMinigame(impactPt))));
                    }
                    else
                    {
                        lock (_animLock) activeMissiles.Add(new MissileAnimation
                        {
                            Start = startPt,
                            End = impactPt,
                            IsPlayerMissile = false,
                            MissileColor = mColor,
                            Speed = 0.4f,
                            OnImpact = async () =>
                            {
                                var (cas, def) = CombatEngine.ExecuteRemotePlayerStrike(target, weapon, strikeDmg);
                                lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPt, MaxRadius = radius, DamageLines = new[] { $"[{senderName.ToUpper()}] {cas:N0} casualties{(def ? " — DEFEATED" : "")}" }, IsPlayerTarget = false });
                                logBox.SelectionColor = amberText; LogMsg($"[IMPACT] {target.ToUpper()} — {cas:N0} casualties from {senderName.ToUpper()}'s strike.{(def ? " NATION DEFEATED." : "")}");
                                
                                if (def) AddNotification("NATION FALLEN", $"{target.ToUpper()} was defeated by {senderName.ToUpper()}", Color.Orange, 6f);

                                RefreshData();
                            }
                        });
                    }
                    break;

                case "ai_launch":
                    string aiAttacker = action.GetProperty("attacker").GetString() ?? "";
                    string aiTarget = action.GetProperty("target").GetString() ?? "";
                    int aiSalvo = action.GetProperty("salvo").GetInt32();
                    long aiDamage = action.GetProperty("damage").GetInt64();
                    
                    double? aLat = action.TryGetProperty("lat", out var latEl) ? latEl.GetDouble() : (double?)null;
                    double? aLng = action.TryGetProperty("lng", out var lngEl) ? lngEl.GetDouble() : (double?)null;

                    TriggerAiLaunchLocal(aiAttacker, aiTarget, aiSalvo, aiDamage, aLat, aLng);
                    break;

                case "sat_strike":
                    string satTarget = action.GetProperty("target").GetString() ?? "";
                    if (!string.IsNullOrEmpty(satTarget))
                    {
                        logBox.SelectionColor = Color.Violet;
                        LogMsg($"[SAT-KILL] {senderName.ToUpper()} launched a Satellite Killer at {satTarget.ToUpper()}!");

                        bool satHitsMe = satTarget == GameEngine.Player.NationName;
                        PointLatLng satImpact = satHitsMe
                            ? new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX)
                            : GameEngine.Nations.TryGetValue(satTarget, out Nation satNation)
                                ? new PointLatLng(satNation.MapY, satNation.MapX)
                                : new PointLatLng(0, 0);

                        PointLatLng satStart = GameEngine.Nations.TryGetValue(
                            action.TryGetProperty("playerNation", out var pn) ? pn.GetString() ?? "" : "",
                            out Nation satAttNation)
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
                    break;
                case "sub_create":
                    string scId = action.GetProperty("subId").GetString();
                    string scName = action.GetProperty("name").GetString();
                    float scX = (float)action.GetProperty("x").GetDouble();
                    float scY = (float)action.GetProperty("y").GetDouble();
                    if (!GameEngine.Submarines.Any(s => s.Id == scId))
                    {
                        GameEngine.Submarines.Add(new Submarine { Id = scId, Name = scName, MapX = scX, MapY = scY, OwnerId = senderName });
                    }
                    break;

                case "sub_move":
                    string smId = action.GetProperty("subId").GetString();
                    float smX = (float)action.GetProperty("tx").GetDouble();
                    float smY = (float)action.GetProperty("ty").GetDouble();
                    var smSub = GameEngine.Submarines.FirstOrDefault(s => s.Id == smId);
                    if (smSub != null) 
                    { 
                        smSub.TargetX = smX; 
                        smSub.TargetY = smY; 
                        smSub.Waypoints = MapUtility.FindPath(smSub.MapX, smSub.MapY, smX, smY);
                        smSub.IsMoving = true; 
                    }
                    break;

                case "sub_fire":
                    string sfId = action.GetProperty("subId").GetString();
                    double sfLat = action.GetProperty("lat").GetDouble();
                    double sfLng = action.GetProperty("lng").GetDouble();
                    var sfSub = GameEngine.Submarines.FirstOrDefault(s => s.Id == sfId);
                    if (sfSub != null)
                    {
                        sfSub.RevealedUntil = DateTime.Now.AddSeconds(15);
                        FireSubStrikeLocally(sfSub, new PointLatLng(sfLat, sfLng));
                    }
                    break;

                case "sub_destroy":
                    string sdId = action.GetProperty("subId").GetString();
                    var sdSub = GameEngine.Submarines.FirstOrDefault(s => s.Id == sdId);
                    if (sdSub != null) { sdSub.Health = 0; LogMsg($"[SENSORS] {senderName.ToUpper()} reports submarine {sdSub.Name.ToUpper()} DESTROYED."); }
                    break;

                case "sub_recover":
                    string srId = action.GetProperty("subId").GetString();
                    var srSub = GameEngine.Submarines.FirstOrDefault(s => s.Id == srId);
                    if (srSub != null) { srSub.Health = 50; srSub.OwnerId = senderName; srSub.NukeCount = 0; LogMsg($"[RECOVERY] {senderName.ToUpper()} salvaged submarine {srSub.Name.ToUpper()}."); }
                    break;
            }
        }
    }
}
