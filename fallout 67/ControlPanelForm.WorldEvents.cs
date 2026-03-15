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
        // ── Country vs Country Wars ──────────────────────────────────────────────────
        private void BroadcastAiLaunch(Nation attacker, string targetName, int salvo, double? lat = null, double? lng = null)
        {
            if (_isMultiplayer && _mpClient != null && !_mpClient.IsHost) return;

            long damage = 0;
            if (GameEngine.Nations.TryGetValue(targetName, out Nation target))
            {
                damage = (long)(target.MaxPopulation * (0.04 + rng.NextDouble() * 0.14));
            }

            if (_isMultiplayer && _mpClient != null)
            {
                // Await the send — if the socket is down, the message queues for
                // delivery on reconnect instead of being silently lost.
                _ = SendAiLaunchWithRetryAsync(attacker.Name, targetName, salvo, damage, lat, lng);
            }

            TriggerAiLaunchLocal(attacker.Name, targetName, salvo, damage, lat, lng);
        }

        private async Task SendAiLaunchWithRetryAsync(string attackerName, string targetName, int salvo, long damage, double? lat, double? lng)
        {
            var payload = new
            {
                type = "ai_launch",
                attacker = attackerName,
                target = targetName,
                salvo = salvo,
                damage = damage,
                lat = lat,
                lng = lng
            };

            // First attempt — goes to queue if socket is down
            await _mpClient!.SendGameActionAsync(payload);

            // If we're disconnected, the message is queued and will drain on reconnect.
            // One retry after a short delay covers the brief window where the socket
            // just dropped but hasn't been detected yet.
            if (!_mpClient.IsConnected && !_mpClient.IsReconnecting)
            {
                await Task.Delay(2000);
                if (_mpClient.IsConnected)
                    await _mpClient.SendGameActionAsync(payload);
            }
        }

        private void TriggerAiLaunchLocal(string attackerName, string targetName, int salvo, long damage, double? lat = null, double? lng = null)
        {
            if (!GameEngine.Nations.TryGetValue(attackerName, out Nation attacker)) return;

            if (targetName == GameEngine.Player.NationName)
            {
                attacker.IsHostileToPlayer = true;
                AddNotification("☢ INBOUND MISSILE", $"{attackerName.ToUpper()} has targeted you", Color.Red, 4f);
                for (int s = 0; s < salvo; s++) LaunchEnemyMissile(attacker);
            }
            else if (lat.HasValue && lng.HasValue)
            {
                // Specialized point strike (e.g. sub hunting)
                for (int s = 0; s < salvo; s++) LaunchPointStrike(attacker, new PointLatLng(lat.Value, lng.Value));
            }
            else if (GameEngine.Nations.TryGetValue(targetName, out Nation target))
            {
                for (int s = 0; s < salvo; s++) LaunchNationVsNationMissile(attacker, target, damage);
            }
        }

        private void LaunchPointStrike(Nation attacker, PointLatLng targetPos)
        {
            PointLatLng startPt = new PointLatLng(attacker.MapY, attacker.MapX);
            lock (_animLock) activeMissiles.Add(new MissileAnimation
            {
                Start = startPt,
                End = targetPos,
                IsPlayerMissile = false,
                MissileColor = Color.Yellow,
                Speed = 0.5f,
                OnImpact = async () => 
                {
                    // Collateral check for subs/assets
                    CombatEngine.CheckSubmarineCollateral((float)targetPos.Lat, (float)targetPos.Lng, 1.0f, attacker.Name);
                    lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = targetPos, MaxRadius = 35f, DamageLines = new[] { "TACTICAL STRIKE", "OCEAN SECTOR" }, IsPlayerTarget = false });
                    RefreshData();
                }
            });
        }

        private void LaunchNationVsNationMissile(Nation attacker, Nation target, long damage)
        {
            bool allyUnderAttack = GameEngine.Player.Allies.Contains(target.Name);

            logBox.SelectionColor = allyUnderAttack ? redText : amberText;
            string prefix = allyUnderAttack ? "[ALLY UNDER ATTACK]" : "[WORLD EVENT]";
            LogMsg($"{prefix} {attacker.Name.ToUpper()} has launched a missile at {target.Name.ToUpper()}!");

            PointLatLng startPt = new PointLatLng(attacker.MapY, attacker.MapX);
            PointLatLng impactPt = new PointLatLng(target.MapY, target.MapX);

            lock (_animLock) activeMissiles.Add(new MissileAnimation
            {
                Start = startPt,
                End = impactPt,
                IsPlayerMissile = false,
                MissileColor = Color.Orange,
                Speed = 0.4f,
                OnImpact = async () => await HandleNationVsNationImpact(attacker.Name, target.Name, impactPt, allyUnderAttack, damage)
            });
        }

        private async Task HandleNationVsNationImpact(string attackerName, string targetName, PointLatLng impactPos, bool allyUnderAttack, long damage)
        {
            if (!GameEngine.Nations.TryGetValue(attackerName, out Nation attacker)) return;
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return;

            damage = Math.Min(damage, target.Population);
            target.Population -= damage;
            target.AngerLevel = Math.Min(10, target.AngerLevel + 1);

            lock (_animLock) activeExplosions.Add(new ExplosionEffect
            {
                Center = impactPos,
                MaxRadius = 42f,
                DamageLines = new[] { $"{attackerName.ToUpper()} → {targetName.ToUpper()}", $"{damage:N0} casualties" },
                IsPlayerTarget = allyUnderAttack
            });

            logBox.SelectionColor = allyUnderAttack ? redText : amberText;
            LogMsg($"[WORLD EVENT] {attackerName.ToUpper()} struck {targetName.ToUpper()}! Est. {damage:N0} casualties.");

            if (target.Population <= 0)
            {
                bool wasDead = target.IsDefeated;
                target.IsDefeated = true;
                if (!wasDead)
                {
                    logBox.SelectionColor = amberText;
                    LogMsg($"[WORLD EVENT] {targetName.ToUpper()} has been annihilated by {attackerName.ToUpper()}!");
                    AddNotification("NATION ANNIHILATED", $"{targetName.ToUpper()} has been wiped out", Color.Orange, 6f);
                }
            }

            RefreshData();

            if (!_isMultiplayer || (_mpClient != null && _mpClient.IsHost))
            {
                // De-escalation: Target nation retaliation roll — gated by military readiness
                if (!target.IsDefeated && !target.IsHumanControlled && StrategicEngine.CanNationAffordLaunch(target))
                {
                    double retChance = 0.4 + (target.AngerLevel * 0.04);
                    if ((double)target.Population / target.MaxPopulation < 0.15) retChance *= 0.3;
                    retChance *= StrategicEngine.GetMilitaryReadiness(target);

                    if (rng.NextDouble() < retChance)
                    {
                        await Task.Delay(rng.Next(800, 3000));
                        if (!target.IsDefeated && !attacker.IsDefeated) BroadcastAiLaunch(target, attacker.Name, 1);
                    }
                }

                // Limited Alliance Intervention — allies need readiness too
                int maxAllyHits = 1;
                foreach (string allyName in target.Allies)
                {
                    if (maxAllyHits <= 0) break;
                    if (!GameEngine.Nations.TryGetValue(allyName, out Nation targetAlly)) continue;

                    if (targetAlly.IsDefeated || targetAlly.IsHumanControlled || !StrategicEngine.CanNationAffordLaunch(targetAlly)) continue;

                    double allyEntryChance = 0.15 + (targetAlly.AngerLevel * 0.03);
                    if (attacker.Allies.Contains(allyName)) allyEntryChance *= 0.1;
                    allyEntryChance *= StrategicEngine.GetMilitaryReadiness(targetAlly);

                    if (rng.NextDouble() < allyEntryChance)
                    {
                        await Task.Delay(rng.Next(2000, 5000));
                        if (!targetAlly.IsDefeated && !attacker.IsDefeated)
                        {
                            BroadcastAiLaunch(targetAlly, attacker.Name, 1);
                            maxAllyHits--;
                        }
                    }
                }

                foreach (string allyName in attacker.Allies)
                {
                    if (!GameEngine.Nations.TryGetValue(allyName, out Nation attackerAlly)) continue;
                    if (attackerAlly.IsDefeated || attackerAlly.IsHumanControlled || !StrategicEngine.CanNationAffordLaunch(attackerAlly)) continue;
                    if (target.Allies.Contains(allyName)) continue;
                    if (rng.NextDouble() >= 0.30 * StrategicEngine.GetMilitaryReadiness(attackerAlly)) continue;

                    await Task.Delay(700 + rng.Next(500));
                    if (!attackerAlly.IsDefeated && !target.IsDefeated)
                    {
                        logBox.SelectionColor = amberText;
                        LogMsg($"[ALLIANCE] {allyName.ToUpper()} joins {attackerName.ToUpper()}'s offensive against {targetName.ToUpper()}!");
                        BroadcastAiLaunch(attackerAlly, target.Name, 1);
                    }
                }
            }

            if (allyUnderAttack && !attacker.IsDefeated)
            {
                logBox.SelectionColor = redText;
                LogMsg($"[ALLIANCE] Your ally {targetName.ToUpper()} was attacked by {attackerName.ToUpper()}!");
            }
        }

        // ── World Event Scheduler ───────────────────────────────────────────────────
        private void TryTriggerWorldEvents()
        {
            playerAttackTick++;
            if (playerAttackTick >= 8) // Faster: 12 -> 8
            {
                playerAttackTick = 0;
                // Hostiles can target ANY human player now
                // Only nations that can AFFORD to launch will attack (economy/sanctions/ceasefire gate)
                var hostiles = GameEngine.Nations.Values.Where(n => !n.IsDefeated && n.IsHostileToPlayer && !n.IsHumanControlled && StrategicEngine.CanNationAffordLaunch(n)).ToList();
                if (hostiles.Count > 0)
                {
                    Nation a = hostiles[rng.Next(hostiles.Count)];
                    if (rng.NextDouble() < 0.15 + a.Difficulty * 0.05)
                    {
                        var playerNations = GameEngine.Nations.Values.Where(n => n.IsHumanControlled).Select(n => n.Name).ToList();
                        playerNations.Add(GameEngine.Player.NationName);
                        string randomPlayer = playerNations[rng.Next(playerNations.Count)];

                        int salvo = Math.Min(StrategicEngine.GetMaxSalvoSize(a), a.Nukes);
                        BroadcastAiLaunch(a, randomPlayer, salvo);
                    }
                }
            }

            worldWarTick++;
            if (worldWarTick >= 10) // Faster: 18 -> 10
            {
                worldWarTick = 0;

                // ONLY THE HOST DECIDES UNPROVOKED WORLD WARS IN MULTIPLAYER
                if (_isMultiplayer && _mpClient != null && !_mpClient.IsHost) return;

                if (rng.NextDouble() > 0.45) return; // More frequent: 0.3 -> 0.45

                // Only nations with military readiness can start wars
                var attackers = GameEngine.Nations.Values.Where(n => !n.IsDefeated && !n.IsHumanControlled && StrategicEngine.CanNationAffordLaunch(n)).ToList();
                if (attackers.Count == 0) return;

                Nation attacker = attackers[rng.Next(attackers.Count)];
                
                // Target pool now includes Human players for unprovoked background strikes!
                var targetPool = GameEngine.Nations.Values.Where(n => !n.IsDefeated && n.Name != attacker.Name && !attacker.Allies.Contains(n.Name)).ToList();

                // Chance to hit A player (any player)
                var allPlayers = GameEngine.Nations.Values.Where(n => n.IsHumanControlled).Select(n => n.Name).ToList();
                allPlayers.Add(GameEngine.Player.NationName);

                double playerChance = attacker.IsHostileToPlayer ? 0.35 : 0.15; // Higher chances
                bool hitPlayer = rng.NextDouble() < playerChance;

                int nvnSalvo = Math.Min(StrategicEngine.GetMaxSalvoSize(attacker), attacker.Nukes);
                
                if (hitPlayer)
                {
                    string targetPlayer = allPlayers[rng.Next(allPlayers.Count)];
                    attacker.IsHostileToPlayer = true;
                    BroadcastAiLaunch(attacker, targetPlayer, 1);
                }
                else if (targetPool.Count > 0)
                {
                    Nation nvnTarget = targetPool[rng.Next(targetPool.Count)];
                    BroadcastAiLaunch(attacker, nvnTarget.Name, 1);
                }

                // SUBMARINE HUNTER: AI nations sweep oceans if player is in 'Last Stand'
                if (GameEngine.Player.Population <= 0)
                {
                    foreach (var sub in GameEngine.Submarines.ToList())
                    {
                        if (sub.OwnerId == GameEngine.Player.NationName && !sub.IsDestroyed)
                        {
                            if (rng.NextDouble() < 0.25) // High chance to hunt
                            {
                                Nation hunter = attackers[rng.Next(attackers.Count)];
                                float sweepLat = sub.MapY + (float)(rng.NextDouble() * 10 - 5);
                                float sweepLng = sub.MapX + (float)(rng.NextDouble() * 10 - 5);
                                BroadcastAiLaunch(hunter, "OCEAN SECTOR", 1, (double)sweepLat, (double)sweepLng);
                            }
                        }
                    }
                }
            }
        }

        // ── Game Timer ──────────────────────────────────────────────────────────────
        private void GameTimer_Tick(object sender, EventArgs e)
        {
            if (_gameState != GameState.Playing) return;

            for (int i = GameEngine.ActiveMissions.Count - 1; i >= 0; i--)
            {
                var mission = GameEngine.ActiveMissions[i];
                mission.TimeRemainingSeconds--;
                if (mission.TimeRemainingSeconds <= 0)
                {
                    CombatEngine.ResolveMission(mission, (msg, success) =>
                    {
                        logBox.SelectionColor = success ? amberText : redText;
                        LogMsg(msg);
                    });
                    GameEngine.ActiveMissions.RemoveAt(i);
                    RefreshData();
                }
            }

            angerDecayTick++;
            if (angerDecayTick >= 30)
            {
                angerDecayTick = 0;
                foreach (var n in GameEngine.Nations.Values)
                    if (n.AngerLevel > 0) n.AngerLevel--;
            }

            // AI satellite restore — blind nations with enough money have a chance to restore early
            foreach (var n in GameEngine.Nations.Values)
            {
                if (n.IsSatelliteBlind && n.Money >= 2000 && rng.NextDouble() < 0.04)
                {
                    n.Money -= 2000;
                    n.SatelliteBlindUntil = DateTime.MinValue;
                    logBox.SelectionColor = amberText;
                    LogMsg($"[SAT-RESTORE] {n.Name.ToUpper()} launched replacement satellites — targeting grid restored.");
                }
            }

            // Strategic layer tick (economy, spies, UN, nuclear winter)
            TickStrategicLayer();
            RefreshData();

            TryTriggerWorldEvents();
            TickDiplomacy();

            // Tick summit meetings (InSummit phase runs on game timer, not render thread)
            foreach (var summit in GameEngine.ActiveSummits.ToList())
            {
                if (summit.Phase == SummitPhase.InSummit && !summit.ShotDown)
                    HandleSummitMeeting(summit, 1f); // 1 second per tick
            }
        }
    }
}
