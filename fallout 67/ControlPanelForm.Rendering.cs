using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace fallover_67
{
    public partial class ControlPanelForm : Form
    {
        // ── Drawing Math ────────────────────────────────────────────────────────────
        private PointF ToScreenPoint(PointLatLng p)
        {
            GPoint gp = mapPanel.FromLatLngToLocal(p);
            return new PointF((float)gp.X, (float)gp.Y);
        }

        private PointF BezierPoint(PointF p0, PointF p1, PointF p2, float t)
        {
            float u = 1 - t;
            return new PointF(u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X, u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y);
        }

        private PointF GetArcControl(PointF start, PointF end)
        {
            float dist = (float)Math.Sqrt((end.X - start.X) * (end.X - start.X) + (end.Y - start.Y) * (end.Y - start.Y));
            return new PointF((start.X + end.X) / 2f, (start.Y + end.Y) / 2f - dist * 0.5f);
        }

        // ── Dedicated Render Thread (replaces WinForms Timer for smooth 60 FPS) ──
        private void RenderLoop()
        {
            var sw = new Stopwatch();
            sw.Start();
            const double targetMs = 1000.0 / 60.0; // 16.67ms

            while (_renderRunning)
            {
                double elapsed = sw.Elapsed.TotalMilliseconds;
                if (elapsed < targetMs)
                {
                    double remaining = targetMs - elapsed;
                    if (remaining > 2.0)
                        Thread.Sleep((int)(remaining - 2.0));
                    while (sw.Elapsed.TotalMilliseconds < targetMs)
                        Thread.SpinWait(10);
                }

                float dt = (float)sw.Elapsed.TotalSeconds;
                sw.Restart();

                UpdateAnimations(dt);
                _cameraShake.Update(dt);

                try
                {
                    if (!_renderRunning) break;
                    mapPanel.BeginInvoke(new Action(() => mapPanel.Invalidate(false)));
                }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }
            }
        }

        private void UpdateAnimations(float dt)
        {
            radarAngle = (radarAngle + (120f * dt)) % 360;

            // Freeze everything while a minigame is open
            if (_gameState != GameState.Playing) return;

            lock (_animLock)
            {
                for (int i = activeMissiles.Count - 1; i >= 0; i--)
                {
                    var m = activeMissiles[i];
                    m.Progress += m.Speed * dt;
                    if (m.Progress >= 1.0f)
                    {
                        m.Progress = 1.0f;
                        Action impact = m.OnImpact;
                        activeMissiles.RemoveAt(i);
                        try { mapPanel.BeginInvoke(new Action(() => impact?.Invoke())); }
                        catch { }
                    }
                }

                for (int i = activeExplosions.Count - 1; i >= 0; i--)
                {
                    var exp = activeExplosions[i];
                    // Trigger camera shake on first frame of a new explosion
                    if (exp.Progress == 0f)
                        _cameraShake.AddTrauma(exp.MaxRadius);
                    exp.Progress += 1.2f * dt;
                    exp.TextProgress += 0.25f * dt;
                    if (exp.Progress >= 1.0f && exp.TextProgress >= 1.0f)
                        activeExplosions.RemoveAt(i);
                }

                for (int i = activeNotifications.Count - 1; i >= 0; i--)
                {
                    var n = activeNotifications[i];
                    n.Lifetime -= dt;
                    
                    // Animate sliding in/out
                    if (n.Lifetime > 0.5f) n.SlideProgress = Math.Min(1.0f, n.SlideProgress + dt * 4f);
                    else n.SlideProgress = Math.Max(0.0f, n.SlideProgress - dt * 4f);

                    if (n.Lifetime <= 0)
                        activeNotifications.RemoveAt(i);
                }

                // --- SUBMARINE MOVEMENT ---
                foreach (var sub in GameEngine.Submarines)
                {
                    if (sub.IsMoving && !sub.IsDestroyed && sub.Waypoints != null && sub.Waypoints.Count > 0)
                    {
                        var wp = sub.Waypoints[0];
                        float dx = wp.X - sub.MapX;
                        float dy = wp.Y - sub.MapY;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        float moveSpeed = 4.5f * dt; // Much faster! (was 0.5)

                        if (dist < moveSpeed)
                        {
                            sub.MapX = wp.X;
                            sub.MapY = wp.Y;
                            sub.Waypoints.RemoveAt(0);
                            if (sub.Waypoints.Count == 0) sub.IsMoving = false;
                        }
                        else
                        {
                            sub.MapX += (dx / dist) * moveSpeed;
                            sub.MapY += (dy / dist) * moveSpeed;
                        }
                    }
                }

                // --- SUMMIT PLANE MOVEMENT ---
                for (int i = GameEngine.ActiveSummits.Count - 1; i >= 0; i--)
                {
                    var flight = GameEngine.ActiveSummits[i];
                    if (flight.ShotDown) { GameEngine.ActiveSummits.RemoveAt(i); continue; }

                    if (flight.Phase == SummitPhase.InSummit)
                    {
                        // Handled on game timer thread via HandleSummitMeeting
                        continue;
                    }

                    flight.Progress += flight.Speed * dt;
                    if (flight.Progress >= 1.0f)
                    {
                        flight.Progress = 1.0f;
                        try { mapPanel.BeginInvoke(new Action(() => HandleSummitPhaseComplete(flight))); }
                        catch { }
                    }
                }

                if (_strikeWarningTimer > 0)
                {
                    _strikeWarningTimer -= dt;
                    _strikeWarningAnim = Math.Min(1.0f, _strikeWarningAnim + dt * 2f);
                    _strikeWarningRotation += dt * 90f;
                }
                else
                {
                    _strikeWarningAnim = Math.Max(0.0f, _strikeWarningAnim - dt * 2f);
                }

                if (_isDamageAlertActive)
                {
                    _damageAlertTimer -= dt;
                    _damageAlertAnim = Math.Min(1.0f, _damageAlertAnim + dt * 1.5f);
                    if (_damageAlertTimer <= 0)
                    {
                        _isDamageAlertActive = false;
                    }
                }
                else
                {
                    _damageAlertAnim = Math.Max(0.0f, _damageAlertAnim - dt * 2.0f);
                    if (_damageAlertAnim <= 0) 
                    {
                        _cumulativeDamageThisWave = 0;
                    }
                }
            }
        }

        private void MapPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.Low;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = mapPanel.Width, h = mapPanel.Height;

            // ── Camera shake offset ──────────────────────────────────────────
            if (_cameraShake.IsActive)
                g.TranslateTransform(_cameraShake.OffsetX, _cameraShake.OffsetY);

            // 1. Terminal Green Tint
            g.FillRectangle(_tintBrush, 0, 0, w, h);

            // 2. STATIC GRID (Lat/Lng mapped so it sticks and zooms with the world map)
            for (int lat = -85; lat <= 85; lat += 10)
            {
                PointF left = ToScreenPoint(new PointLatLng(lat, -360));
                PointF right = ToScreenPoint(new PointLatLng(lat, 360));
                g.DrawLine(_gridPen, left, right);
            }
            for (int lng = -360; lng <= 360; lng += 10)
            {
                PointF top = ToScreenPoint(new PointLatLng(85, lng));
                PointF bottom = ToScreenPoint(new PointLatLng(-85, lng));
                g.DrawLine(_gridPen, top, bottom);
            }

            // Subtly Cache the coordinates so we don't recalculate math later
            _currentScreenCoords.Clear();
            foreach (var n in GameEngine.Nations.Values)
                _currentScreenCoords[n.Name] = ToScreenPoint(new PointLatLng(n.MapY, n.MapX));

            PointF pSc = ToScreenPoint(new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX));

            bool isBlind = GameEngine.Player.IsSatelliteBlind;

            // 3. Draw Connections
            if (!isBlind)
            {
                foreach (string a in GameEngine.Player.Allies)
                    if (_currentScreenCoords.TryGetValue(a, out PointF anSc))
                        g.DrawLine(_allyPen, pSc, anSc);

                foreach (var n in GameEngine.Nations.Values)
                {
                    PointF nSc = _currentScreenCoords[n.Name];
                    foreach (var a in n.Allies)
                        if (_currentScreenCoords.TryGetValue(a, out PointF aaSc))
                            g.DrawLine(_enAllyPen, nSc, aaSc);
                }
            }

            float baseNodeRadius = Math.Max(3f, (float)(mapPanel.Zoom * 1.5)); // DYNAMIC SCALING

            // Player Base - Always visible to the player
            _nodeBrush.Color = cyanText;
            g.FillRectangle(_nodeBrush, pSc.X - baseNodeRadius, pSc.Y - baseNodeRadius, baseNodeRadius * 2, baseNodeRadius * 2);
            string baseLabel = $"BUNKER 67 ({GameEngine.Player.NationName})";
            SizeF bs = g.MeasureString(baseLabel, _nodeFont);
            g.FillRectangle(_textBgBrush, pSc.X + baseNodeRadius + 3, pSc.Y - 8, bs.Width, bs.Height);
            _textFgBrush.Color = cyanText;
            g.DrawString(baseLabel, _nodeFont, _textFgBrush, pSc.X + baseNodeRadius + 3, pSc.Y - 8);

            if (!isBlind)
            {
                // Multiplayer Bases
                foreach (var mp in _mpPlayers)
                {
                    if (mp.Id == _mpClient?.LocalPlayerId || mp.Country == null) continue;
                    if (!GameEngine.Nations.TryGetValue(mp.Country, out Nation mpNation)) continue;

                    PointF mpSc = _currentScreenCoords[mpNation.Name];
                    Color mc = ColorTranslator.FromHtml(mp.Color);

                    var diamond = new PointF[] {
                        new PointF(mpSc.X, mpSc.Y - (baseNodeRadius + 3)), new PointF(mpSc.X + (baseNodeRadius + 3), mpSc.Y),
                        new PointF(mpSc.X, mpSc.Y + (baseNodeRadius + 3)), new PointF(mpSc.X - (baseNodeRadius + 3), mpSc.Y)
                    };
                    _nodeBrush.Color = mc;
                    g.FillPolygon(_nodeBrush, diamond);
                    string mpLabel = $"[{mp.Name.ToUpper()}]";
                    SizeF ms2 = g.MeasureString(mpLabel, _nodeFont);
                    g.FillRectangle(_textBgBrush, mpSc.X + baseNodeRadius + 5, mpSc.Y - 8, ms2.Width, ms2.Height);
                    _textFgBrush.Color = mc;
                    g.DrawString(mpLabel, _nodeFont, _textFgBrush, mpSc.X + baseNodeRadius + 5, mpSc.Y - 8);
                }

                // AI Nodes
                foreach (var kvp in GameEngine.Nations)
                {
                    Nation n = kvp.Value;
                    PointF sc = _currentScreenCoords[n.Name];

                    Color nc = Color.LimeGreen;
                    if (n.IsDefeated) nc = Color.Gray;
                    else if (GameEngine.Player.Allies.Contains(n.Name)) nc = cyanText;
                    else if (n.IsHostileToPlayer) nc = redText;
                    if (n.Name == hoveredTarget || n.Name == selectedTarget) nc = Color.White;

                    float r = (n.Name == hoveredTarget || n.Name == selectedTarget) ? baseNodeRadius * 1.5f : baseNodeRadius;
                    _nodeBrush.Color = nc;
                    g.FillEllipse(_nodeBrush, sc.X - r, sc.Y - r, r * 2, r * 2);

                    SizeF ts = g.MeasureString(n.Name.ToUpper(), _nodeFont);
                    g.FillRectangle(_textBgBrush, sc.X + r + 3, sc.Y - 8, ts.Width, ts.Height);
                    _textFgBrush.Color = nc;
                    g.DrawString(n.Name.ToUpper(), _nodeFont, _textFgBrush, sc.X + r + 3, sc.Y - 8);

                    if (n.Name == selectedTarget)
                        g.DrawEllipse(_selectPen, sc.X - (r * 2), sc.Y - (r * 2), r * 4, r * 4);

                    // Glitch overlay for satellite-blind nations
                    if (n.IsSatelliteBlind) DrawSatelliteGlitch(g, n.Name, sc);
                }
            }

            // Player satellite-blind overlay (covers entire map)
            if (isBlind) DrawPlayerSatelliteBlind(g, w, h);

            // ── MISSILES & EXPLOSIONS (locked for thread safety) ──────────────────
            lock (_animLock)
            {
                if (!isBlind)
                {
                    // Fade trails when many missiles are in flight to reduce visual noise
                    int missileCount = activeMissiles.Count;
                    int baseTrailAlpha = missileCount > 8 ? 80 : missileCount > 4 ? 130 : 180;

                    foreach (var m in activeMissiles)
                    {
                        PointF startSc = ToScreenPoint(m.Start);
                        PointF endSc = ToScreenPoint(m.End);
                        PointF ctrl = GetArcControl(startSc, endSc);

                        int headStep = Math.Max(1, (int)(m.Progress * TrailSteps));
                        int pointCount = Math.Min(headStep + 1, _trailArrays.Length - 1);

                        var pts = _trailArrays[pointCount];
                        for (int i = 0; i < pointCount; i++)
                            pts[i] = BezierPoint(startSc, ctrl, endSc, (float)i / TrailSteps);

                        if (pointCount > 1)
                        {
                            _trailPen.Color = Color.FromArgb(baseTrailAlpha, m.MissileColor);
                            g.DrawLines(_trailPen, pts);
                        }

                        PointF head = BezierPoint(startSc, ctrl, endSc, m.Progress);
                        _glowBrush.Color = Color.FromArgb(150, m.MissileColor);
                        g.FillEllipse(_glowBrush, head.X - 6, head.Y - 6, 12, 12);
                        g.FillEllipse(_headBrush, head.X - 2, head.Y - 2, 4, 4);
                    }
                }

                // Only show damage text on the 3 newest explosions to avoid screen clutter
                var textExplosions = activeExplosions
                    .Where(ex => ex.DamageLines != null && ex.TextProgress < 1.0f)
                    .OrderBy(ex => ex.TextProgress)  // lowest TextProgress = most recently started
                    .Take(3)
                    .ToHashSet();

                foreach (var exp in activeExplosions)
                {
                    PointF expSc = ToScreenPoint(exp.Center);
                    float t = exp.Progress;
                    float radius = exp.MaxRadius * (float)Math.Sin(t * Math.PI) * (float)mapPanel.Zoom * 0.5f;
                    int alpha = t < 0.3f ? (int)(t / 0.3f * 220) : (int)((1f - t) / 0.7f * 220);
                    alpha = Math.Clamp(alpha, 0, 220);

                    Color inner = exp.IsPlayerTarget ? Color.Crimson : Color.OrangeRed;
                    Color outer = exp.IsPlayerTarget ? Color.Red : Color.Yellow;

                    if (radius > 0.5f)
                    {
                        _expFillBrush.Color = Color.FromArgb(alpha / 3, inner);
                        g.FillEllipse(_expFillBrush, expSc.X - radius, expSc.Y - radius, radius * 2, radius * 2);
                        _expRingPen.Color = Color.FromArgb(alpha, outer);
                        g.DrawEllipse(_expRingPen, expSc.X - radius, expSc.Y - radius, radius * 2, radius * 2);
                    }

                    if (!textExplosions.Contains(exp)) continue;

                    float tt = exp.TextProgress;
                    int ta = tt < 0.15f ? (int)(tt / 0.15f * 235) : tt < 0.80f ? 235 : (int)((1f - tt) / 0.20f * 235);
                    ta = Math.Clamp(ta, 0, 235);

                    // Cache MeasureString results on first use
                    var nonEmpty = exp.DamageLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                    if (exp.CachedLineSizes == null)
                    {
                        exp.CachedLineSizes = new SizeF[nonEmpty.Length];
                        for (int li = 0; li < nonEmpty.Length; li++)
                            exp.CachedLineSizes[li] = g.MeasureString(nonEmpty[li], _explosionFont);
                    }

                    float tx2 = expSc.X + exp.MaxRadius + 8, ty2 = expSc.Y - 16;
                    _expTextBgBrush.Color = Color.FromArgb(Math.Min(ta + 40, 210), 0, 0, 0);
                    _expTextFgBrush.Color = exp.IsPlayerTarget ? Color.FromArgb(ta, redText) : Color.FromArgb(ta, amberText);
                    for (int li = 0; li < nonEmpty.Length && li < exp.CachedLineSizes.Length; li++)
                    {
                        SizeF sz = exp.CachedLineSizes[li];
                        g.FillRectangle(_expTextBgBrush, tx2 - 2, ty2 - 1, sz.Width + 4, sz.Height + 2);
                        g.DrawString(nonEmpty[li], _explosionFont, _expTextFgBrush, tx2, ty2);
                        ty2 += sz.Height + 3;
                    }
                }

                // --- SUBMARINES ---
                foreach (var sub in GameEngine.Submarines)
                {
                    bool isOwner = sub.OwnerId == GameEngine.Player.NationName;
                    bool isRevealed = sub.IsRevealed;
                    bool isWreck = sub.IsDestroyed;

                    if (!isOwner && !isRevealed && !isWreck) continue;

                    PointF pos = ToScreenPoint(new PointLatLng(sub.MapY, sub.MapX));
                    Color subColor = isOwner ? Color.Cyan : (isWreck ? Color.Gray : Color.OrangeRed);
                    using var subPen = new Pen(subColor, 2);
                    using var subBrush = new SolidBrush(Color.FromArgb(120, subColor));

                    // Draw sub hull
                    g.FillRectangle(subBrush, pos.X - 6, pos.Y - 3, 12, 6);
                    g.DrawRectangle(subPen, pos.X - 6, pos.Y - 3, 12, 6);
                    g.FillRectangle(subBrush, pos.X - 2, pos.Y - 6, 4, 3); // Sail
                    
                    if (isOwner || isRevealed || isWreck)
                    {
                        string nameText = sub.Name.ToUpper();
                        if (isWreck) nameText += " [WRECK]";
                        g.DrawString(nameText, _nodeFont, Brushes.White, pos.X + 10, pos.Y - 6);
                    }
                }
            }

            // --- SUMMIT PLANES ---
            lock (_animLock)
            {
                foreach (var flight in GameEngine.ActiveSummits)
                {
                    if (flight.ShotDown) continue;

                    float planeLat, planeLng;
                    if (flight.Phase == SummitPhase.InSummit)
                    {
                        planeLat = flight.EndLat;
                        planeLng = flight.EndLng;
                    }
                    else
                    {
                        planeLat = flight.StartLat + (flight.EndLat - flight.StartLat) * flight.Progress;
                        planeLng = flight.StartLng + (flight.EndLng - flight.StartLng) * flight.Progress;
                    }

                    PointF planeSc = ToScreenPoint(new PointLatLng(planeLat, planeLng));

                    // Dashed flight path
                    PointF startSc = ToScreenPoint(new PointLatLng(flight.StartLat, flight.StartLng));
                    PointF endSc = ToScreenPoint(new PointLatLng(flight.EndLat, flight.EndLng));
                    Color planeColor = flight.IsPlayerPlane ? Color.Cyan :
                                       flight.IsPlayerInitiated ? Color.Gold : Color.White;
                    using var pathPen = new Pen(Color.FromArgb(40, planeColor), 1f) { DashStyle = DashStyle.Dot };
                    g.DrawLine(pathPen, startSc, endSc);

                    // Plane triangle pointing in direction of travel
                    float angle = (float)Math.Atan2(endSc.Y - startSc.Y, endSc.X - startSc.X);
                    var state = g.Save();
                    g.TranslateTransform(planeSc.X, planeSc.Y);
                    g.RotateTransform(angle * 180f / (float)Math.PI);

                    _planeBrush.Color = planeColor;
                    PointF[] planeShape = { new PointF(-10, -5), new PointF(10, 0), new PointF(-10, 5), new PointF(-6, 0) };
                    g.FillPolygon(_planeBrush, planeShape);

                    // Engine glow
                    _planeBrush.Color = Color.FromArgb(80, Color.Orange);
                    g.FillEllipse(_planeBrush, -14, -3, 6, 6);

                    g.Restore(state);

                    // Label
                    string phaseText = flight.Phase == SummitPhase.InSummit ? "IN SESSION" :
                                       flight.Phase == SummitPhase.Returning ? "RETURNING" : "EN ROUTE";
                    string label = $"✈ {flight.Nation1} → {flight.Nation2} [{phaseText}]";
                    _planeBrush.Color = Color.FromArgb(180, planeColor);
                    g.DrawString(label, _planeFont, _planeBrush, planeSc.X + 14, planeSc.Y - 8);

                    // Pulsing ring when at summit
                    if (flight.Phase == SummitPhase.InSummit)
                    {
                        float pulseR = 12f + (float)Math.Sin(flight.SummitTimer * 3) * 4f;
                        using var pulsePen = new Pen(Color.FromArgb(100, planeColor), 1.5f);
                        g.DrawEllipse(pulsePen, planeSc.X - pulseR, planeSc.Y - pulseR, pulseR * 2, pulseR * 2);
                    }
                }
            }

            // Radar sweep
            float cx = w / 2f, cy = h / 2f, rr = Math.Max(w, h);
            float ex = cx + (float)(Math.Cos(radarAngle * Math.PI / 180) * rr);
            float ey = cy + (float)(Math.Sin(radarAngle * Math.PI / 180) * rr);
            g.DrawLine(_radarPen, cx, cy, ex, ey);

            // Hint overlay
            string hint = $"Zoom: {mapPanel.Zoom}x  │  Right-drag: pan  │  Double-click: reset";
            if (hint != _cachedHintText)
            {
                _cachedHintText = hint;
                _cachedHintSize = g.MeasureString(hint, _hintFont);
            }
            g.FillRectangle(_hintBgBrush, 4, h - _cachedHintSize.Height - 4, _cachedHintSize.Width + 4, _cachedHintSize.Height + 2);
            g.DrawString(hint, _hintFont, _hintFgBrush, 6, h - _cachedHintSize.Height - 3);

            // ── NOTIFICATIONS (Toasts) ──────────────────────────
            lock (_animLock)
            {
                float toastY = 10;
                foreach (var n in activeNotifications)
                {
                    float slideX = 300 * (1.0f - n.SlideProgress);
                    float tx = w - 260 + slideX;
                    
                    RectangleF toastRect = new RectangleF(tx, toastY, 250, 45);
                    
                    // Glassmorphism background
                    using var bgB = new SolidBrush(Color.FromArgb((int)(200 * n.SlideProgress), 10, 15, 10));
                    using var borderP = new Pen(Color.FromArgb((int)(255 * n.SlideProgress), n.Color), 1f);
                    
                    g.FillRectangle(bgB, toastRect);
                    g.DrawRectangle(borderP, toastRect.X, toastRect.Y, toastRect.Width, toastRect.Height);
                    
                    // Accent bar
                    using var accentB = new SolidBrush(n.Color);
                    g.FillRectangle(accentB, toastRect.X, toastRect.Y, 4, toastRect.Height);
                    
                    using var textB = new SolidBrush(Color.FromArgb((int)(255 * n.SlideProgress), Color.White));
                    using var subTextB = new SolidBrush(Color.FromArgb((int)(200 * n.SlideProgress), n.Color));
                    
                    g.DrawString(n.Message, _notifyFont, textB, toastRect.X + 10, toastY + 5);
                    g.DrawString(n.SubMessage, _notifySubFont, subTextB, toastRect.X + 10, toastY + 24);
                    
                    toastY += 55;
                }
            }

            // ── LARGE STRIKE WARNING (Center Screen) ───────────
            if (_strikeWarningAnim > 0.01f)
            {
                float opacity = _strikeWarningAnim;
                int centerX = w / 2;
                int centerY = h / 2;

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // 1. Pulsing Background Glow
                float pulse = (float)(Math.Sin(DateTime.Now.Ticks / 2000000.0) * 0.2 + 0.8);
                using (var glowB = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    glowB.AddEllipse(centerX - 150, centerY - 150, 300, 300);
                    using (var pgb = new System.Drawing.Drawing2D.PathGradientBrush(glowB))
                    {
                        pgb.CenterColor = Color.FromArgb((int)(180 * opacity * pulse), Color.Red);
                        pgb.SurroundColors = new[] { Color.Transparent };
                        g.FillPath(pgb, glowB);
                    }
                }

                // 2. Rotating Danger Ring
                using (Pen warningPen = new Pen(Color.FromArgb((int)(255 * opacity), Color.Red), 3f))
                {
                    warningPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    var state = g.Save();
                    g.TranslateTransform(centerX, centerY);
                    g.RotateTransform(_strikeWarningRotation);
                    g.DrawEllipse(warningPen, -120, -120, 240, 240);
                    g.Restore(state);
                }

                // 3. Main Text
                string warnText = "☢ RADAR LOCKED ☢";
                string subText = "INCOMING THERMONUCLEAR STRIKE";
                
                SizeF mainSize = g.MeasureString(warnText, _warningFont);
                SizeF subSize = g.MeasureString(subText, _warningSubFont);

                using var textBrush = new SolidBrush(Color.FromArgb((int)(255 * opacity), Color.White));
                using var subBrush = new SolidBrush(Color.FromArgb((int)(255 * opacity), Color.Red));

                g.DrawString(warnText, _warningFont, textBrush, centerX - mainSize.Width / 2, centerY - 40);
                g.DrawString(subText, _warningSubFont, subBrush, centerX - subSize.Width / 2, centerY + 10);

                // 4. Corner Accents
                using (Pen accentPen = new Pen(Color.FromArgb((int)(255 * opacity), Color.Red), 2f))
                {
                    int arm = 40;
                    int dist = 140;
                    // Top Left
                    g.DrawLine(accentPen, centerX - dist, centerY - dist, centerX - dist + arm, centerY - dist);
                    g.DrawLine(accentPen, centerX - dist, centerY - dist, centerX - dist, centerY - dist + arm);
                    // Top Right
                    g.DrawLine(accentPen, centerX + dist, centerY - dist, centerX + dist - arm, centerY - dist);
                    g.DrawLine(accentPen, centerX + dist, centerY - dist, centerX + dist, centerY - dist + arm);
                    // Bottom Left
                    g.DrawLine(accentPen, centerX - dist, centerY + dist, centerX - dist + arm, centerY + dist);
                    g.DrawLine(accentPen, centerX - dist, centerY + dist, centerX - dist, centerY + dist - arm);
                    // Bottom Right
                    g.DrawLine(accentPen, centerX + dist, centerY + dist, centerX + dist - arm, centerY + dist);
                    g.DrawLine(accentPen, centerX + dist, centerY + dist, centerX + dist, centerY + dist - arm);
                }
            }

            DrawDamageAlert(g, w, h);
        }

        private void DrawDamageAlert(Graphics g, int w, int h)
        {
            if (_damageAlertAnim <= 0.01f) return;

            int alpha = (int)(_damageAlertAnim * 220);
            
            // Glitchy red overlay
            using (var b = new SolidBrush(Color.FromArgb(alpha / 2, 180, 0, 0)))
                g.FillRectangle(b, 0, 0, w, h);

            // Scan lines/glitch effect
            if (alpha > 40)
            {
                for (int i = 0; i < h; i += 6)
                {
                    if (rng.Next(10) > 6) continue;
                    using (var p = new Pen(Color.FromArgb(alpha / 5, 255, 50, 50), 1))
                        g.DrawLine(p, 0, i, w, i);
                }
            }

            // Big text
            string mainText = "!!! IMPACT DETECTED !!!";
            string subText = $"- {(_cumulativeDamageThisWave):N0} CITIZENS KILLED -";
            double percent = GameEngine.Player.MaxPopulation > 0 ? (double)GameEngine.Player.Population / GameEngine.Player.MaxPopulation * 100.0 : 0;
            string lifeText = $"REMAINING POPULATION: {percent:F1}%";

            // Glitchy offset for text
            float offset = (float)(Math.Sin(DateTime.Now.Ticks / 500000.0) * 4.0 * _damageAlertAnim);
            
            using (var fontLarge = new Font("Consolas", 42f, FontStyle.Bold))
            using (var fontSmall = new Font("Consolas", 18f, FontStyle.Bold))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                
                // Shadow / Glitch layer
                using (var shadowB = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                    g.DrawString(mainText, fontLarge, shadowB, w/2 + 4 + offset, h/2 - 60 + 4, sf);
                
                using (var redB = new SolidBrush(Color.FromArgb(alpha, 255, 0, 0)))
                {
                    g.DrawString(mainText, fontLarge, redB, w/2 + offset, h/2 - 60, sf);
                    g.DrawString(lifeText, fontSmall, redB, w/2 + 2, h/2 + 50 + 2, sf);
                }

                using (var whiteB = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                {
                    g.DrawString(subText, fontSmall, whiteB, w/2, h/2 + 10, sf);
                    g.DrawString(lifeText, fontSmall, whiteB, w/2, h/2 + 50, sf);
                }
            }
        }

        // ── Controls ─────────────────────────────────────────────────────────────────
        private void MapPanel_MouseMove(object sender, MouseEventArgs e)
        {
            float hitRadius = 16f;
            hoveredTarget = "";

            if (GameEngine.Player.IsSatelliteBlind) return;

            foreach (var kvp in _currentScreenCoords)
            {
                float dx = kvp.Value.X - e.X, dy = kvp.Value.Y - e.Y;
                if (dx * dx + dy * dy < hitRadius * hitRadius) { hoveredTarget = kvp.Key; break; }
            }
        }

        private void MapPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var clickedLatLng = mapPanel.FromLocalToLatLng(e.X, e.Y);

                if (_isSelectingSubMoveTarget && _selectedSub != null)
                {
                    float tx = (float)clickedLatLng.Lng;
                    float ty = (float)clickedLatLng.Lat;

                    if (!MapUtility.IsWater(tx, ty))
                    {
                        MessageBox.Show("INVALID OPERATION: Target location is over land.", "NAV-COM ERROR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Smart Pathfinding
                    var path = MapUtility.FindPath(_selectedSub.MapX, _selectedSub.MapY, tx, ty);
                    if (path == null || path.Count == 0)
                    {
                        MessageBox.Show("INVALID OPERATION: No clear water route to destination.", "NAV-COM ERROR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    _selectedSub.Waypoints = path;
                    _selectedSub.TargetX = tx;
                    _selectedSub.TargetY = ty;
                    _selectedSub.IsMoving = true;
                    _isSelectingSubMoveTarget = false;
                    
                    LogMsg($"[SUB-COM] {_selectedSub.Name.ToUpper()} course set. Route plotted ({path.Count} waypoints).");
                    if (_isMultiplayer) _mpClient?.SendGameActionAsync(new { type = "sub_move", subId = _selectedSub.Id, tx = _selectedSub.TargetX, ty = _selectedSub.TargetY });
                    return;
                }

                if (_isSelectingSubFireTarget && _selectedSub != null)
                {
                    _selectedSub.NukeCount--;
                    _isSelectingSubFireTarget = false;
                    _selectedSub.RevealedUntil = DateTime.Now.AddSeconds(15);
                    LogMsg($"[SUB-COM] {_selectedSub.Name.ToUpper()} LAUNCHED NUCLEAR PAYLOAD.");
                    
                    // Trigger a strike at this location
                    FireSubStrikeLocally(_selectedSub, clickedLatLng);

                    if (_isMultiplayer) _mpClient?.SendGameActionAsync(new { type = "sub_fire", subId = _selectedSub.Id, lat = clickedLatLng.Lat, lng = clickedLatLng.Lng });
                    return;
                }

                // Check if we clicked a submarine
                foreach (var sub in GameEngine.Submarines)
                {
                    PointF subSc = ToScreenPoint(new PointLatLng(sub.MapY, sub.MapX));
                    float ddx = subSc.X - e.X, ddy = subSc.Y - e.Y;
                    if (ddx * ddx + ddy * ddy < 100) // 10px radius
                    {
                        if (sub.OwnerId == GameEngine.Player.NationName || sub.IsRevealed || sub.IsDestroyed)
                        {
                            if (sub.OwnerId == GameEngine.Player.NationName && !sub.IsDestroyed)
                            {
                                new SubmarineControlForm(sub, this).ShowDialog(this);
                            }
                            else if (sub.IsDestroyed)
                            {
                                // Salvage logic
                                TrySalvageWreck(sub);
                            }
                            return;
                        }
                    }
                }

                // Check if we clicked a summit plane (for intercept)
                lock (_animLock)
                {
                    foreach (var flight in GameEngine.ActiveSummits)
                    {
                        if (flight.ShotDown || flight.IsPlayerInitiated || flight.IsPlayerPlane) continue;
                        if (flight.Phase != SummitPhase.FlyingToSummit && flight.Phase != SummitPhase.Returning) continue;

                        float planeLat = flight.StartLat + (flight.EndLat - flight.StartLat) * flight.Progress;
                        float planeLng = flight.StartLng + (flight.EndLng - flight.StartLng) * flight.Progress;
                        PointF planeSc = ToScreenPoint(new PointLatLng(planeLat, planeLng));
                        float pdx = planeSc.X - e.X, pdy = planeSc.Y - e.Y;
                        if (pdx * pdx + pdy * pdy < 400) // 20px radius
                        {
                            TryInterceptSummitPlane(flight);
                            return;
                        }
                    }
                }

                if (hoveredTarget != "")
                {
                    selectedTarget = hoveredTarget;
                    UpdateProfile();
                }
            }
        }

        private void ResetView()
        {
            mapPanel.Position = new PointLatLng(20, 0);
            mapPanel.Zoom = 2; // Fixed to respect the newly clamped MinZoom
        }
    }
}
