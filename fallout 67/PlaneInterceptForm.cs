using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace fallover_67
{
    public class PlaneInterceptForm : Form
    {
        public bool PlaneDestroyed { get; private set; } = false;

        private string _nation1, _nation2;

        // Plane state
        private float _planeX, _planeY;
        private float _planeVX, _planeVY;
        private float _planeBaseY;
        private float _jitterPhase = 0f;
        private bool _planeAlive = true;
        private float _planeExplosion = 0f;

        // AA missiles
        private const int MaxShots = 3;
        private int _shotsLeft;
        private List<AAMissile> _missiles = new();

        private class AAMissile
        {
            public float X, Y;
            public float TargetX, TargetY;
            public float Speed = 8f;
            public bool Dead;
            public bool Hit;
            public float ExpRadius;
        }

        // Layout
        private const int W = 800;
        private const int H = 500;
        private const int HitRadius = 25;

        // Timing
        private System.Windows.Forms.Timer _gameTick;
        private bool _finished = false;

        // Theme
        private Color bgColor = Color.FromArgb(5, 8, 15);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color cyanText = Color.Cyan;
        private Font hudFont = new Font("Consolas", 11F, FontStyle.Bold);
        private Font titleFont = new Font("Consolas", 14F, FontStyle.Bold);
        private Font smallFont = new Font("Consolas", 9F, FontStyle.Bold);
        private Random _rng = new Random();

        public PlaneInterceptForm(string nation1, string nation2)
        {
            _nation1 = nation1;
            _nation2 = nation2;
            _shotsLeft = MaxShots;

            // Plane starts left, flies right with slight curve
            _planeX = -30;
            _planeBaseY = 100 + (float)_rng.NextDouble() * 200;
            _planeY = _planeBaseY;
            _planeVX = 2.5f + (float)_rng.NextDouble() * 1.5f;
            _planeVY = 0;

            this.Text = "AA DEFENSE — INTERCEPT SUMMIT PLANE";
            this.ClientSize = new Size(W, H);
            this.BackColor = bgColor;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;

            this.Paint += OnPaint;
            this.MouseClick += OnClick;

            _gameTick = new System.Windows.Forms.Timer { Interval = 16 };
            _gameTick.Tick += OnTick;
            _gameTick.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_finished) return;

            // Move plane
            if (_planeAlive)
            {
                _planeX += _planeVX;
                _jitterPhase += 0.08f;
                _planeY = _planeBaseY + (float)Math.Sin(_jitterPhase) * 30f + (float)Math.Sin(_jitterPhase * 2.7f) * 15f;

                // Random speed/direction jitter
                if (_rng.NextDouble() < 0.03)
                {
                    _planeVX = 2.0f + (float)_rng.NextDouble() * 2.5f;
                    _planeBaseY += (_rng.Next(-40, 41));
                    _planeBaseY = Math.Clamp(_planeBaseY, 60, H - 150);
                }

                // Plane escaped
                if (_planeX > W + 40)
                {
                    PlaneDestroyed = false;
                    _finished = true;
                    FinishAfterDelay();
                }
            }
            else
            {
                _planeExplosion += 2f;
                if (_planeExplosion > 60)
                {
                    _finished = true;
                    FinishAfterDelay();
                }
            }

            // Move AA missiles
            for (int i = _missiles.Count - 1; i >= 0; i--)
            {
                var m = _missiles[i];
                if (m.Dead) continue;

                if (m.Hit)
                {
                    m.ExpRadius += 3f;
                    if (m.ExpRadius > 30) m.Dead = true;
                    continue;
                }

                // Move toward target
                float dx = m.TargetX - m.X;
                float dy = m.TargetY - m.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < m.Speed)
                {
                    m.X = m.TargetX;
                    m.Y = m.TargetY;

                    // Check hit
                    float pdx = m.X - _planeX;
                    float pdy = m.Y - _planeY;
                    if (_planeAlive && pdx * pdx + pdy * pdy < HitRadius * HitRadius)
                    {
                        m.Hit = true;
                        _planeAlive = false;
                        PlaneDestroyed = true;
                    }
                    else
                    {
                        // Miss explosion
                        m.Hit = true;
                    }
                }
                else
                {
                    m.X += dx / dist * m.Speed;
                    m.Y += dy / dist * m.Speed;
                }
            }

            // Out of shots and all missiles resolved?
            if (_shotsLeft <= 0 && _missiles.TrueForAll(m => m.Dead) && _planeAlive)
            {
                // Just wait for plane to escape
            }

            this.Invalidate();
        }

        private void OnClick(object sender, MouseEventArgs e)
        {
            if (_finished || !_planeAlive || _shotsLeft <= 0) return;
            if (e.Button != MouseButtons.Left) return;

            _shotsLeft--;
            _missiles.Add(new AAMissile
            {
                X = W / 2f,
                Y = H - 30,
                TargetX = e.X,
                TargetY = e.Y,
                Speed = 10f
            });
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Sky gradient
            using var skyBrush = new LinearGradientBrush(
                new Point(0, 0), new Point(0, H),
                Color.FromArgb(5, 10, 25), Color.FromArgb(15, 25, 50));
            g.FillRectangle(skyBrush, 0, 0, W, H);

            // Grid
            using var gridPen = new Pen(Color.FromArgb(12, 0, 80, 0), 1);
            for (int x = 0; x < W; x += 50) g.DrawLine(gridPen, x, 0, x, H);
            for (int y = 0; y < H; y += 50) g.DrawLine(gridPen, 0, y, W, y);

            // Ground
            using var groundBrush = new SolidBrush(Color.FromArgb(10, 20, 10));
            g.FillRectangle(groundBrush, 0, H - 50, W, 50);
            using var groundPen = new Pen(Color.FromArgb(40, greenText), 1);
            g.DrawLine(groundPen, 0, H - 50, W, H - 50);

            // AA turret
            PointF[] turret = { new(W / 2f - 15, H - 30), new(W / 2f, H - 55), new(W / 2f + 15, H - 30) };
            using var turretFill = new SolidBrush(Color.FromArgb(50, greenText));
            using var turretPen = new Pen(greenText, 2);
            g.FillPolygon(turretFill, turret);
            g.DrawPolygon(turretPen, turret);

            // Plane
            if (_planeAlive)
            {
                // Plane shape (arrow)
                var state = g.Save();
                g.TranslateTransform(_planeX, _planeY);

                PointF[] plane = { new(-15, -6), new(15, 0), new(-15, 6), new(-10, 0) };
                using var planeFill = new SolidBrush(Color.White);
                using var planeOutline = new Pen(Color.FromArgb(200, cyanText), 1.5f);
                g.FillPolygon(planeFill, plane);
                g.DrawPolygon(planeOutline, plane);

                // Engine glow
                using var glowBrush = new SolidBrush(Color.FromArgb(80, Color.Orange));
                g.FillEllipse(glowBrush, -20, -4, 8, 8);

                g.Restore(state);

                // Label
                using var labelBrush = new SolidBrush(Color.FromArgb(150, Color.White));
                g.DrawString($"SUMMIT: {_nation1} → {_nation2}", smallFont, labelBrush, _planeX + 20, _planeY - 20);
            }
            else
            {
                // Explosion
                int alpha = Math.Max(0, (int)(255 * (1f - _planeExplosion / 60f)));
                using var expFill = new SolidBrush(Color.FromArgb(alpha / 3, Color.OrangeRed));
                using var expRing = new Pen(Color.FromArgb(alpha, Color.Yellow), 2.5f);
                g.FillEllipse(expFill, _planeX - _planeExplosion, _planeY - _planeExplosion, _planeExplosion * 2, _planeExplosion * 2);
                g.DrawEllipse(expRing, _planeX - _planeExplosion, _planeY - _planeExplosion, _planeExplosion * 2, _planeExplosion * 2);

                using var hitBrush = new SolidBrush(Color.FromArgb(alpha, greenText));
                g.DrawString("TARGET DESTROYED", titleFont, hitBrush, _planeX - 100, _planeY + _planeExplosion + 10);
            }

            // AA missiles in flight
            foreach (var m in _missiles)
            {
                if (m.Dead && !m.Hit) continue;

                if (m.Hit)
                {
                    int a = Math.Max(0, (int)(200 * (1f - m.ExpRadius / 30f)));
                    using var mExpFill = new SolidBrush(Color.FromArgb(a / 3, Color.Yellow));
                    using var mExpRing = new Pen(Color.FromArgb(a, Color.Orange), 2);
                    g.FillEllipse(mExpFill, m.X - m.ExpRadius, m.Y - m.ExpRadius, m.ExpRadius * 2, m.ExpRadius * 2);
                    g.DrawEllipse(mExpRing, m.X - m.ExpRadius, m.Y - m.ExpRadius, m.ExpRadius * 2, m.ExpRadius * 2);
                    continue;
                }

                // Trail from turret
                using var trailPen = new Pen(Color.FromArgb(80, Color.Yellow), 1.5f);
                g.DrawLine(trailPen, W / 2f, H - 45, m.X, m.Y);

                // Missile dot
                using var missileBrush = new SolidBrush(Color.Yellow);
                g.FillEllipse(missileBrush, m.X - 3, m.Y - 3, 6, 6);
                using var missileGlow = new SolidBrush(Color.FromArgb(60, Color.Yellow));
                g.FillEllipse(missileGlow, m.X - 8, m.Y - 8, 16, 16);
            }

            // HUD
            using var hudBrush = new SolidBrush(amberText);
            g.DrawString("AA DEFENSE — CLICK TO FIRE AT PLANE", titleFont, hudBrush, 10, 10);

            using var shotBrush = new SolidBrush(_shotsLeft > 0 ? greenText : Color.Red);
            string shotText = $"SHOTS REMAINING: {_shotsLeft}/{MaxShots}";
            g.DrawString(shotText, hudFont, shotBrush, 10, 38);

            // Shot indicators
            for (int i = 0; i < MaxShots; i++)
            {
                Color dotColor = i < _shotsLeft ? greenText : Color.FromArgb(40, 60, 40);
                using var dotBrush = new SolidBrush(dotColor);
                g.FillEllipse(dotBrush, 250 + i * 25, 40, 15, 15);
            }

            if (!_planeAlive)
            {
                using var resultBrush = new SolidBrush(greenText);
                g.DrawString("SUMMIT PLANE DESTROYED — ALLIANCE DENIED", hudFont, resultBrush, 10, H - 75);
            }
            else if (_shotsLeft <= 0 && _missiles.TrueForAll(m => m.Dead))
            {
                using var missBrush = new SolidBrush(Color.Red);
                g.DrawString("ALL SHOTS EXPENDED — PLANE ESCAPING...", hudFont, missBrush, 10, H - 75);
            }
        }

        private void FinishAfterDelay()
        {
            var closeTimer = new System.Windows.Forms.Timer { Interval = 1200 };
            closeTimer.Tick += (s, ev) =>
            {
                closeTimer.Stop();
                closeTimer.Dispose();
                if (IsHandleCreated && !IsDisposed)
                {
                    if (InvokeRequired) Invoke((Action)(() => { DialogResult = DialogResult.OK; Close(); }));
                    else { DialogResult = DialogResult.OK; Close(); }
                }
            };
            closeTimer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _gameTick?.Stop();
            _gameTick?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
