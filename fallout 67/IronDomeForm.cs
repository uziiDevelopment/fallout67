using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace fallover_67
{
    /// <summary>
    /// Iron Dome intercept minigame.
    /// Missiles fall from the top toward the base at the bottom.
    /// Player clicks dots to destroy them before they reach the base.
    /// InterceptedCount / MissileCount = fraction blocked (replaces the passive dome roll).
    /// </summary>
    public class IronDomeForm : Form
    {
        // ── Result ────────────────────────────────────────────────────────────
        public int InterceptedCount { get; private set; } = 0;
        public int TotalMissiles    { get; private set; }

        // ── Game objects ──────────────────────────────────────────────────────
        private class Missile
        {
            public float X, Y;          // current position
            public float TargetX;       // x-coord of base target point
            public float Speed;         // pixels per tick
            public bool  Dead;          // clicked / off-screen
            public bool  Reached;       // hit the base
            public float TrailAlpha = 1f;

            // Pre-computed direction
            public float DX, DY;

            // Explosion animation
            public bool  Exploding;
            public float ExpRadius = 0f;
        }

        private class BaseHit
        {
            public float X;
            public float Radius = 0f;
            public float Alpha  = 1f;
        }

        private List<Missile>  _missiles  = new();
        private List<BaseHit>  _baseHits  = new();
        private List<PointF>[] _trails;        // ring-buffer trail per missile slot

        // ── Layout ───────────────────────────────────────────────────────────
        private const int W            = 800;
        private const int H            = 600;
        private const int BaseY        = 555;   // y-coord of the base line
        private const int BaseCenterX  = W / 2;
        private const int MissileR     = 7;     // click/hit radius (small — makes it harder)

        // ── Timing ───────────────────────────────────────────────────────────
        private System.Windows.Forms.Timer _gameTick;
        private System.Windows.Forms.Timer _spawnTick;
        private int   _spawnedCount  = 0;
        private int   _spawnInterval = 900;     // ms between spawns (gets shorter)
        private float _baseSpeed;

        // ── Appearance ───────────────────────────────────────────────────────
        private Color bgColor      = Color.FromArgb(8,  12, 8);
        private Color gridColor    = Color.FromArgb(20, 50, 20);
        private Color missileColor = Color.FromArgb(255, 60, 40);
        private Color trailColor   = Color.FromArgb(180, 40, 20);
        private Color explodeColor = Color.FromArgb(57,  255, 20);
        private Color baseColor    = Color.FromArgb(57,  200, 20);
        private Color hitColor     = Color.FromArgb(255, 80, 40);
        private Color textColor    = Color.FromArgb(57,  255, 20);
        private Color amberColor   = Color.FromArgb(255, 176, 0);

        private Font _hudFont  = new Font("Consolas", 11F, FontStyle.Bold);
        private Font _bigFont  = new Font("Consolas", 16F, FontStyle.Bold);

        private Random _rng = new Random();

        // ── Trail buffer ──────────────────────────────────────────────────────
        private const int TrailLen = 12;

        public IronDomeForm(int missileCount, int domeLevel)
        {
            TotalMissiles = Math.Max(1, missileCount);

            // Base speed: 2.5 px/tick at dome L1, scaling up with missile count
            // More missiles = faster (overwhelming) — cap at 6.5 px/tick
            _baseSpeed = Math.Min(6.5f, 2.5f + (missileCount - 1) * 0.18f);

            // Spawn interval: starts tight, gets tighter with more missiles
            // 10 missiles → ~300ms apart; 20 → ~200ms; never below 150ms
            _spawnInterval = Math.Max(150, 600 - (missileCount * 22));

            // Pre-alloc trail buffers
            _trails = new List<PointF>[TotalMissiles];
            for (int i = 0; i < TotalMissiles; i++)
                _trails[i] = new List<PointF>(TrailLen + 1);

            BuildForm();
        }

        private void BuildForm()
        {
            this.Text            = "⚡ IRON DOME — INTERCEPT INCOMING MISSILES ⚡";
            this.ClientSize      = new Size(W, H);
            this.BackColor       = bgColor;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.DoubleBuffered  = true;
            this.Cursor          = Cursors.Cross;

            this.Paint      += OnPaint;
            this.MouseClick += OnClick;

            // Game logic tick — 16 ms ≈ 60 fps
            _gameTick = new System.Windows.Forms.Timer { Interval = 16 };
            _gameTick.Tick += OnGameTick;
            _gameTick.Start();

            // Missile spawner
            _spawnTick = new System.Windows.Forms.Timer { Interval = _spawnInterval };
            _spawnTick.Tick += OnSpawnTick;
            _spawnTick.Start();
        }

        // ── Spawn ─────────────────────────────────────────────────────────────
        private void OnSpawnTick(object sender, EventArgs e)
        {
            if (_spawnedCount >= TotalMissiles)
            {
                _spawnTick.Stop();
                return;
            }

            int idx = _spawnedCount++;

            float startX  = 20 + (float)_rng.NextDouble() * (W - 40);
            float targetX = BaseCenterX + (_rng.Next(-180, 181));
            float speed   = _baseSpeed + (float)_rng.NextDouble() * (_baseSpeed * 0.35f);

            // Direction vector toward base
            float dx = targetX - startX;
            float dy = BaseY - 10f;   // from y=10 to BaseY
            float len = MathF.Sqrt(dx * dx + dy * dy);

            var m = new Missile
            {
                X       = startX,
                Y       = 10f,
                TargetX = targetX,
                Speed   = speed,
                DX      = dx / len * speed,
                DY      = dy / len * speed
            };

            _missiles.Add(m);
        }

        // ── Game tick ─────────────────────────────────────────────────────────
        private void OnGameTick(object sender, EventArgs e)
        {
            bool allDone = _spawnedCount >= TotalMissiles;

            foreach (var m in _missiles)
            {
                if (m.Dead) continue;

                if (m.Exploding)
                {
                    m.ExpRadius += 3f;
                    if (m.ExpRadius > 38f) m.Dead = true;
                    continue;
                }

                m.X += m.DX;
                m.Y += m.DY;

                // Update trail
                var trail = _trails[_missiles.IndexOf(m)];
                trail.Add(new PointF(m.X, m.Y));
                if (trail.Count > TrailLen) trail.RemoveAt(0);

                // Reached the base?
                if (m.Y >= BaseY - MissileR)
                {
                    m.Dead    = true;
                    m.Reached = true;
                    _baseHits.Add(new BaseHit { X = m.X });
                }

                if (!m.Dead) allDone = false;
            }

            // Animate base hits
            for (int i = _baseHits.Count - 1; i >= 0; i--)
            {
                _baseHits[i].Radius += 2.5f;
                _baseHits[i].Alpha  -= 0.04f;
                if (_baseHits[i].Alpha <= 0) _baseHits.RemoveAt(i);
            }

            // Check if all spawned and all dead
            if (_spawnedCount >= TotalMissiles && _missiles.TrueForAll(m => m.Dead))
            {
                _gameTick.Stop();
                // Short delay so player sees the last explosion, then close
                var closeTimer = new System.Windows.Forms.Timer { Interval = 900 };
                closeTimer.Tick += (s, ev) => { closeTimer.Stop(); FinishGame(); };
                closeTimer.Start();
            }

            this.Invalidate();
        }

        // ── Click handler ─────────────────────────────────────────────────────
        private void OnClick(object sender, MouseEventArgs e)
        {
            foreach (var m in _missiles)
            {
                if (m.Dead || m.Exploding) continue;

                float dx = e.X - m.X;
                float dy = e.Y - m.Y;
                if (dx * dx + dy * dy <= (MissileR + 5) * (MissileR + 5))
                {
                    m.Exploding = true;
                    InterceptedCount++;
                    return;  // only kill one per click
                }
            }
        }

        // ── Painting ──────────────────────────────────────────────────────────
        private void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DrawGrid(g);
            DrawBase(g);
            DrawMissiles(g);
            DrawBaseHits(g);
            DrawHUD(g);
        }

        private void DrawGrid(Graphics g)
        {
            using var pen = new Pen(gridColor, 1);
            for (int x = 0; x < W; x += 50) g.DrawLine(pen, x, 0, x, H);
            for (int y = 0; y < H; y += 50) g.DrawLine(pen, 0, y, W, y);

            // Radar circle from base
            for (int r = 80; r <= 500; r += 80)
            {
                using var rpen = new Pen(Color.FromArgb(15, 60, 15), 1);
                g.DrawEllipse(rpen, BaseCenterX - r, BaseY - r, r * 2, r * 2);
            }
        }

        private void DrawBase(Graphics g)
        {
            // Base line
            using var basePen = new Pen(baseColor, 2);
            g.DrawLine(basePen, 0, BaseY, W, BaseY);

            // Base structure — triangle turret
            var pts = new PointF[]
            {
                new(BaseCenterX - 30, BaseY),
                new(BaseCenterX,      BaseY - 30),
                new(BaseCenterX + 30, BaseY)
            };
            using var fill  = new SolidBrush(Color.FromArgb(40, baseColor));
            using var bpen  = new Pen(baseColor, 2);
            g.FillPolygon(fill, pts);
            g.DrawPolygon(bpen, pts);

            // Label
            using var lbr = new SolidBrush(baseColor);
            g.DrawString("BUNKER 67", _hudFont, lbr, BaseCenterX - 46, BaseY + 4);
        }

        private void DrawMissiles(Graphics g)
        {
            for (int mi = 0; mi < _missiles.Count; mi++)
            {
                var m = _missiles[mi];
                if (m.Dead && !m.Exploding) continue;

                if (m.Exploding)
                {
                    DrawExplosion(g, m.X, m.Y, m.ExpRadius);
                    continue;
                }

                // Trail
                var trail = _trails[mi];
                for (int t = 1; t < trail.Count; t++)
                {
                    float alpha = (float)t / trail.Count * 180f;
                    using var tp = new Pen(Color.FromArgb((int)alpha, trailColor), 2f);
                    g.DrawLine(tp, trail[t - 1], trail[t]);
                }

                // Glow
                using var glowBr = new SolidBrush(Color.FromArgb(40, missileColor));
                g.FillEllipse(glowBr, m.X - MissileR * 2, m.Y - MissileR * 2, MissileR * 4, MissileR * 4);

                // Dot
                using var dotBr = new SolidBrush(missileColor);
                g.FillEllipse(dotBr, m.X - MissileR, m.Y - MissileR, MissileR * 2, MissileR * 2);

                // Ring
                using var ringPen = new Pen(Color.FromArgb(200, 255, 100, 60), 1.5f);
                g.DrawEllipse(ringPen, m.X - MissileR, m.Y - MissileR, MissileR * 2, MissileR * 2);
            }
        }

        private void DrawExplosion(Graphics g, float x, float y, float r)
        {
            int alpha = Math.Max(0, (int)(255 * (1f - r / 38f)));
            using var fill = new SolidBrush(Color.FromArgb(Math.Min(alpha, 80), explodeColor));
            using var ring = new Pen(Color.FromArgb(alpha, explodeColor), 2.5f);
            g.FillEllipse(fill, x - r, y - r, r * 2, r * 2);
            g.DrawEllipse(ring, x - r, y - r, r * 2, r * 2);

            // Inner bright core
            float cr = r * 0.4f;
            using var core = new SolidBrush(Color.FromArgb(Math.Min(alpha + 60, 255), explodeColor));
            g.FillEllipse(core, x - cr, y - cr, cr * 2, cr * 2);
        }

        private void DrawBaseHits(Graphics g)
        {
            foreach (var bh in _baseHits)
            {
                int a = Math.Max(0, (int)(bh.Alpha * 200));
                using var fill = new SolidBrush(Color.FromArgb(Math.Min(a, 80), hitColor));
                using var pen  = new Pen(Color.FromArgb(a, hitColor), 2f);
                g.FillEllipse(fill, bh.X - bh.Radius, BaseY - bh.Radius, bh.Radius * 2, bh.Radius * 2);
                g.DrawEllipse(pen,  bh.X - bh.Radius, BaseY - bh.Radius, bh.Radius * 2, bh.Radius * 2);
            }
        }

        private void DrawHUD(Graphics g)
        {
            int missed      = _missiles.Count(m => m.Reached);
            int destroyed   = InterceptedCount;
            int inFlight    = _missiles.Count(m => !m.Dead);
            int remaining   = TotalMissiles - _spawnedCount;

            using var br = new SolidBrush(textColor);
            using var ab = new SolidBrush(amberColor);
            using var rb = new SolidBrush(Color.FromArgb(255, 80, 60));

            // Top-left panel
            g.DrawString($"⚡ IRON DOME INTERCEPT", _bigFont, ab, 10, 10);
            g.DrawString($"CLICK MISSILES TO DESTROY THEM", _hudFont, br, 10, 38);

            // Stats top-right
            string stats = $"INTERCEPTED: {destroyed}   MISSED: {missed}   INCOMING: {inFlight + remaining}";
            var sz = g.MeasureString(stats, _hudFont);
            g.DrawString(stats, _hudFont, br, W - sz.Width - 10, 10);

            // Bottom bar
            float pct = TotalMissiles > 0 ? (float)destroyed / TotalMissiles : 0f;
            using var barBg  = new SolidBrush(Color.FromArgb(30, 50, 30));
            using var barFill = new SolidBrush(pct >= 0.5f ? explodeColor : Color.FromArgb(255, 120, 40));
            g.FillRectangle(barBg,  10, H - 30, W - 20, 18);
            g.FillRectangle(barFill, 10, H - 30, (int)((W - 20) * pct), 18);
            using var barPen = new Pen(Color.FromArgb(80, textColor), 1);
            g.DrawRectangle(barPen, 10, H - 30, W - 20, 18);
            g.DrawString($"INTERCEPT RATE: {pct * 100:F0}%", _hudFont, ab, 14, H - 30);
        }

        // ── Finish ────────────────────────────────────────────────────────────
        private void FinishGame()
        {
            if (IsHandleCreated && !IsDisposed)
            {
                if (InvokeRequired) Invoke((Action)FinishGame);
                else { this.DialogResult = DialogResult.OK; this.Close(); }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _gameTick.Stop();
            _spawnTick.Stop();
            base.OnFormClosing(e);
        }
    }
}
