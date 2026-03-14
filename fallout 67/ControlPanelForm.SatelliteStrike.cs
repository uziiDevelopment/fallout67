using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;

namespace fallover_67
{
    public partial class ControlPanelForm : Form
    {
        // ── Satellite Glitch State ────────────────────────────────────────────────────
        // Maps nation name → glitch seed so each glitch pattern is unique per victim
        private readonly Dictionary<string, int> _glitchSeeds = new Dictionary<string, int>();
        private readonly Random _glitchRng = new Random();

        // ── Satellite Strike ──────────────────────────────────────────────────────────
        // Called from BtnLaunch_Click / FireSalvoAsync when weaponIndex == 4
        internal async Task FireSatelliteStrikeAsync(string targetName)
        {
            if (!GameEngine.Nations.ContainsKey(targetName)) return;
            Nation target = GameEngine.Nations[targetName];

            if (GameEngine.Player.SatelliteMissiles <= 0)
            {
                MessageBox.Show("No Satellite Killers in inventory!", "OUT OF AMMO");
                return;
            }

            GameEngine.Player.SatelliteMissiles--;
            GameEngine.Player.NukesUsed++;

            PointLatLng startPt  = new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);
            PointLatLng impactPt = new PointLatLng(target.MapY, target.MapX);

            // A high, slow arc to simulate orbital trajectory
            logBox.SelectionColor = Color.Cyan;
            LogMsg($"[SAT-KILL] ANTI-SATELLITE MISSILE LAUNCHED — TARGET: {targetName.ToUpper()} ORBITAL GRID");

            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "sat_strike", target = targetName, playerNation = GameEngine.Player.NationName });

            lock (_animLock) activeMissiles.Add(new MissileAnimation
            {
                Start          = startPt,
                End            = impactPt,
                IsPlayerMissile = true,
                MissileColor   = Color.Violet,
                Speed          = 0.20f, // slower — orbital trajectory
                OnImpact       = async () => await ApplySatelliteStrikeImpact(targetName, impactPt)
            });

            RefreshData();
            await Task.CompletedTask;
        }

        internal async Task ApplySatelliteStrikeImpact(string targetName, PointLatLng impactPos)
        {
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return;

            const int blindSeconds = 90;
            target.SatelliteBlindUntil = DateTime.Now.AddSeconds(blindSeconds);

            // Assign a fixed glitch seed for this nation's visual
            lock (_glitchSeeds)
                _glitchSeeds[targetName] = _glitchRng.Next(1000, 9999);

            lock (_animLock) activeExplosions.Add(new ExplosionEffect
            {
                Center         = impactPos,
                MaxRadius      = 60f,
                DamageLines    = new[] { $"[SAT-KILL] {targetName.ToUpper()}", "SATELLITES OFFLINE — 90s" },
                IsPlayerTarget = false
            });

            logBox.SelectionColor = Color.Violet;
            LogMsg($"[SAT-KILL] {targetName.ToUpper()}'s satellite network DESTROYED! Targeting offline for {blindSeconds}s.");
            LogMsg($"[SAT-KILL] {targetName.ToUpper()} must pay $2,000M to launch replacement satellites.");

            if (targetName == GameEngine.Player.NationName)
                AddNotification("SATELLITE UPLINK LOST", "Visual telemetry offline", Color.Magenta, 7f);
            else
                AddNotification("SAT-KILL CONFIRMED", $"{targetName.ToUpper()} satellite grid down", Color.Violet, 5f);

            RefreshData();

            // After blind window expires, auto-clear the glitch seed
            await Task.Delay(blindSeconds * 1000 + 500);
            lock (_glitchSeeds) _glitchSeeds.Remove(targetName);
            RefreshData();
        }

        // ── Glitch Overlay Renderer ───────────────────────────────────────────────────
        // Called from MapPanel_Paint for any nation that is currently satellite-blind
        internal void DrawSatelliteGlitch(Graphics g, string nationName, PointF sc)
        {
            int seed;
            lock (_glitchSeeds)
            {
                if (!_glitchSeeds.TryGetValue(nationName, out seed)) return;
            }

            var rnd = new Random(seed ^ (int)(DateTime.Now.Ticks / 200_000)); // flickers ~5Hz

            // Glitch box behind the nation label — flickers between several random offsets
            for (int i = 0; i < 6; i++)
            {
                float gx = sc.X + rnd.Next(-24, 24);
                float gy = sc.Y + rnd.Next(-12, 12);
                int w2  = rnd.Next(12, 50);
                int h2  = rnd.Next(4, 14);
                int alpha = rnd.Next(80, 200);
                Color col = Color.FromArgb(alpha, rnd.Next(0, 2) == 0 ? Color.Magenta : Color.Cyan);
                using var b = new SolidBrush(col);
                g.FillRectangle(b, gx, gy, w2, h2);
            }

            // Scanline banding
            for (int i = 0; i < 3; i++)
            {
                float sy = sc.Y + rnd.Next(-20, 20);
                int la = rnd.Next(40, 120);
                using var p = new Pen(Color.FromArgb(la, Color.LimeGreen), 1f);
                g.DrawLine(p, sc.X - 30, sy, sc.X + 50, sy);
            }

            // Jittered "NO SIGNAL" text
            float tx = sc.X + rnd.Next(-3, 3);
            float ty = sc.Y - 20 + rnd.Next(-2, 2);
            using var bgB = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
            using var fgB = new SolidBrush(Color.FromArgb(220, Color.Magenta));
            using var font = new Font("Consolas", 7F, FontStyle.Bold);
            string noSig = "◈ NO SAT SIGNAL ◈";
            SizeF sz = g.MeasureString(noSig, font);
            g.FillRectangle(bgB, tx - 2, ty - 1, sz.Width + 4, sz.Height + 2);
            g.DrawString(noSig, font, fgB, tx, ty);

            // Countdown
            int secsLeft = Math.Max(0, (int)(GameEngine.Nations.TryGetValue(nationName, out Nation n)
                ? (n.SatelliteBlindUntil - DateTime.Now).TotalSeconds : 0));
            using var cdB = new SolidBrush(Color.FromArgb(200, Color.Yellow));
            using var cdFont = new Font("Consolas", 7F, FontStyle.Bold);
            string cd = $"{secsLeft}s";
            g.DrawString(cd, cdFont, cdB, tx + sz.Width / 2 - 10, ty + sz.Height + 1);
        }

        // ── Player Map Corruption Overlay ────────────────────────────────────────────
        // Called from MapPanel_Paint when the player's own sats are jammed
        internal void DrawPlayerSatelliteBlind(Graphics g, int w, int h)
        {
            var rnd = new Random((int)(DateTime.Now.Ticks / 150_000));

            // Semi-transparent static overlay
            using var staticBrush = new SolidBrush(Color.FromArgb(30, Color.Magenta));
            g.FillRectangle(staticBrush, 0, 0, w, h);

            // Horizontal glitch bands
            for (int i = 0; i < 8; i++)
            {
                int y = rnd.Next(0, h);
                int bh = rnd.Next(2, 12);
                int alpha = rnd.Next(40, 140);
                Color col = rnd.Next(0, 2) == 0 ? Color.FromArgb(alpha, Color.Magenta) : Color.FromArgb(alpha, Color.Cyan);
                using var b = new SolidBrush(col);
                g.FillRectangle(b, 0, y, w, bh);
            }

            // Vertical scanline tears
            for (int i = 0; i < 5; i++)
            {
                int x = rnd.Next(0, w);
                int bw = rnd.Next(1, 6);
                int alpha = rnd.Next(30, 100);
                using var b = new SolidBrush(Color.FromArgb(alpha, Color.White));
                g.FillRectangle(b, x, 0, bw, h);
            }

            // "SATELLITE UPLINK LOST" warning in centre
            int secsLeft = (int)(GameEngine.Player.SatelliteBlindUntil - DateTime.Now).TotalSeconds;
            string[] lines =
            {
                "◈◈ SATELLITE UPLINK LOST ◈◈",
                "TARGET LOCK DEGRADED",
                $"RESTORING IN {secsLeft}s  |  PAY $2,000M TO RESTORE NOW"
            };

            using var warnFont   = new Font("Consolas", 11F, FontStyle.Bold);
            using var warnFontSm = new Font("Consolas", 8F, FontStyle.Bold);
            using var warnBg  = new SolidBrush(Color.FromArgb(200, Color.Black));
            using var warnFg  = new SolidBrush(Color.FromArgb(230, Color.Magenta));
            using var warnFg2 = new SolidBrush(Color.FromArgb(200, Color.Yellow));

            float totalH = (lines.Length - 1) * 18f + 24f;
            float startY = h / 2f - totalH / 2f;
            for (int li = 0; li < lines.Length; li++)
            {
                var font  = li == 0 ? warnFont : warnFontSm;
                var brush = li == 0 ? warnFg : warnFg2;
                float lineH = li == 0 ? 24f : 18f;
                SizeF sz = g.MeasureString(lines[li], font);
                float lx = w / 2f - sz.Width / 2f;
                float ly = startY + li * lineH;
                g.FillRectangle(warnBg, lx - 4, ly - 2, sz.Width + 8, sz.Height + 4);
                g.DrawString(lines[li], font, brush, lx, ly);
                // Jitter last line
                if (li == lines.Length - 1 && rnd.Next(0, 3) == 0)
                    g.DrawString(lines[li], font, warnFg, lx + rnd.Next(-2, 2), ly + rnd.Next(-1, 1));
            }
        }

        // ── Restore Satellites (Player pays $2000M) ───────────────────────────────────
        // Exposed so UI can call it; currently invoked from the BUNKER STATUS panel button
        internal void TryRestorePlayerSatellites()
        {
            if (!GameEngine.Player.IsSatelliteBlind)
            {
                MessageBox.Show("Your satellites are fully operational.", "SATELLITES ONLINE");
                return;
            }

            const long cost = 2000;
            if (GameEngine.Player.Money < cost)
            {
                int secsLeft = (int)(GameEngine.Player.SatelliteBlindUntil - DateTime.Now).TotalSeconds;
                MessageBox.Show($"Insufficient funds! Need ${cost}M.\nSatellites restore automatically in {secsLeft}s.", "INSUFFICIENT FUNDS");
                return;
            }

            GameEngine.Player.Money -= cost;
            GameEngine.Player.SatelliteBlindUntil = DateTime.MinValue;
            logBox.SelectionColor = Color.Cyan;
            LogMsg("[SAT-RESTORE] Replacement satellites launched — targeting grid restored.");
            AddNotification("SATELLITE RESTORED", "Targeting grid fully online", Color.Cyan, 5f);
            RefreshData();
        }
    }
}
