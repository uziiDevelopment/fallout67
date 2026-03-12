using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace fallover_67
{
    public class MissileAnimation
    {
        public PointF Start { get; set; }
        public PointF End { get; set; }
        public float Progress { get; set; } = 0f;
        public float Speed { get; set; } = 0.007f;
        public bool IsPlayerMissile { get; set; }
        public Color MissileColor { get; set; } = Color.OrangeRed;
        public Action OnImpact { get; set; }
    }

    public class ExplosionEffect
    {
        public PointF Center { get; set; }
        public float Progress { get; set; } = 0f;      // drives the blast ring animation (fast)
        public float TextProgress { get; set; } = 0f;  // drives the damage text fade (slow, ~4 s)
        public float MaxRadius { get; set; } = 45f;
        public string[] DamageLines { get; set; } = Array.Empty<string>();
        public bool IsPlayerTarget { get; set; }
    }

    public class RadarPanel : Panel
    {
        public RadarPanel() { this.DoubleBuffered = true; this.SetStyle(ControlStyles.Selectable, true); }
    }

    public class ControlPanelForm : Form
    {
        private Color bgDark    = Color.FromArgb(15, 20, 15);
        private Color radarBg   = Color.FromArgb(5, 10, 5);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color redText   = Color.FromArgb(255, 50, 50);
        private Color cyanText  = Color.Cyan;
        private Font stdFont = new Font("Consolas", 10F, FontStyle.Bold);

        private RadarPanel mapPanel;
        private Label lblProfile, lblPlayerStats;
        private ComboBox cmbWeapon;
        private Button btnLaunch, btnSendTroops, btnOpenShop;
        private RichTextBox logBox;

        private System.Windows.Forms.Timer gameTimer;
        private System.Windows.Forms.Timer radarTimer;
        private System.Windows.Forms.Timer animTimer;

        private Image worldMapImage;
        private float radarAngle = 0;
        private string selectedTarget = "";
        private string hoveredTarget = "";

        private List<MissileAnimation> activeMissiles = new List<MissileAnimation>();
        private List<ExplosionEffect>  activeExplosions = new List<ExplosionEffect>();
        private static Random rng = new Random();

        // ── Multiplayer state ────────────────────────────────────────────────
        private MultiplayerClient? _mpClient;
        private List<MpPlayer>     _mpPlayers = new();
        private bool               _isMultiplayer = false;

        // ── Map zoom / pan ───────────────────────────────────────────────────
        private float  _zoom             = 1.0f;
        private PointF _panOffset        = PointF.Empty;
        private bool   _isPanning        = false;
        private Point  _panStart;
        private PointF _panOffsetAtStart;

        // Separate counters for two world-event categories
        private int playerAttackTick = 0;   // hostile nations → player
        private int worldWarTick     = 0;   // any nation → any nation
        private int angerDecayTick   = 0;   // nation anger cooldown

        public ControlPanelForm()
        {
            InitForm();
        }

        // Multiplayer constructor
        public ControlPanelForm(MultiplayerClient mpClient, List<MpPlayer> mpPlayers)
        {
            _mpClient      = mpClient;
            _mpPlayers     = mpPlayers;
            _isMultiplayer = true;

            // Flag other players' nations as human-controlled so AI skips them
            foreach (var p in mpPlayers)
                if (p.Country != null && p.Id != mpClient.LocalPlayerId &&
                    GameEngine.Nations.TryGetValue(p.Country, out var nation))
                    nation.IsHumanControlled = true;

            InitForm();
            SetupMultiplayer();
        }

        private void InitForm()
        {
            SetupUI();
            LoadCustomMap();
            RefreshData();

            gameTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            gameTimer.Tick += GameTimer_Tick;
            gameTimer.Start();

            radarTimer = new System.Windows.Forms.Timer { Interval = 30 };
            radarTimer.Tick += (s, e) => { radarAngle = (radarAngle + 3) % 360; mapPanel.Invalidate(); };
            radarTimer.Start();

            animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            animTimer.Tick += AnimTimer_Tick;
            animTimer.Start();
        }

        // ── Multiplayer wiring ───────────────────────────────────────────────
        private void SetupMultiplayer()
        {
            if (_mpClient == null) return;

            _mpClient.OnGameAction += (senderId, action) =>
            {
                if (InvokeRequired) Invoke(new Action(() => HandleRemoteAction(senderId, action)));
                else HandleRemoteAction(senderId, action);
            };

            _mpClient.OnChat += (senderId, name, text) =>
            {
                if (InvokeRequired) Invoke(new Action(() =>
                {
                    logBox.SelectionColor = cyanText;
                    LogMsg($"[COMMS] {name.ToUpper()}: {text}");
                }));
            };

            _mpClient.OnDisconnected += () =>
            {
                if (InvokeRequired) Invoke(new Action(() =>
                {
                    logBox.SelectionColor = redText;
                    LogMsg("[NETWORK] ⚠ Lost connection to multiplayer server.");
                }));
            };

            logBox.SelectionColor = cyanText;
            LogMsg("[NETWORK] Multiplayer session active. Other commanders are online.");
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
                {
                    string target   = action.GetProperty("target").GetString() ?? "";
                    int    weapon   = action.GetProperty("weapon").GetInt32();
                    string nation   = action.GetProperty("playerNation").GetString() ?? "";

                    if (!GameEngine.Nations.ContainsKey(target)) break;

                    string[] wNames  = { "STANDARD NUKE", "TSAR BOMBA", "BIO-PLAGUE", "ORBITAL LASER" };
                    Color[]  wColors = { Color.OrangeRed, Color.DeepPink, Color.LimeGreen, Color.Cyan };
                    float[]  wRadii  = { 45f, 70f, 55f, 40f };

                    int mw = mapPanel.Width, mh = mapPanel.Height;

                    // Find attacker's map position
                    PointF startPt = GameEngine.Nations.TryGetValue(nation, out Nation attackerNation)
                        ? new PointF(mw * attackerNation.MapX, mh * attackerNation.MapY)
                        : new PointF(mw * GameEngine.Player.MapX, mh * GameEngine.Player.MapY);

                    Nation tgtNation = GameEngine.Nations[target];
                    PointF impactPt  = new PointF(mw * tgtNation.MapX, mh * tgtNation.MapY);
                    float  radius    = weapon < wRadii.Length ? wRadii[weapon] : 45f;
                    Color  mColor    = weapon < wColors.Length ? wColors[weapon] : Color.OrangeRed;
                    string wName     = weapon < wNames.Length  ? wNames[weapon]  : "NUKE";

                    logBox.SelectionColor = amberText;
                    LogMsg($"[COMMANDER] {senderName.ToUpper()} launched {wName} at {target.ToUpper()}!");

                    activeMissiles.Add(new MissileAnimation
                    {
                        Start = startPt, End = impactPt, IsPlayerMissile = false,
                        MissileColor = mColor, Speed = 0.007f,
                        OnImpact = () =>
                        {
                            var (cas, def) = CombatEngine.ExecuteRemotePlayerStrike(target, weapon);
                            activeExplosions.Add(new ExplosionEffect
                            {
                                Center = impactPt, MaxRadius = radius,
                                DamageLines = new[] { $"[{senderName.ToUpper()}] {cas:N0} casualties{(def ? " — DEFEATED" : "")}" },
                                IsPlayerTarget = false
                            });
                            logBox.SelectionColor = amberText;
                            LogMsg($"[IMPACT] {target.ToUpper()} — {cas:N0} casualties from {senderName.ToUpper()}'s strike.{(def ? " NATION DEFEATED." : "")}");
                            RefreshData();
                        }
                    });
                    break;
                }

                case "deploy":
                {
                    string target = action.GetProperty("target").GetString() ?? "";
                    logBox.SelectionColor = cyanText;
                    LogMsg($"[COMMANDER] {senderName.ToUpper()} deployed troops to {target.ToUpper()}.");
                    break;
                }
            }
        }

        // ── Map Download ────────────────────────────────────────────────────────────
        private void LoadCustomMap()
        {
            Task.Run(() =>
            {
                try
                {
                    using (var wc = new System.Net.WebClient())
                    {
                        byte[] imageBytes = wc.DownloadData("https://i.postimg.cc/tJxj6Kr9/map.png");
                        using (var ms = new System.IO.MemoryStream(imageBytes))
                        {
                            // new Bitmap() forces all pixel data into memory immediately,
                            // so the stream can be safely disposed without breaking GDI+ transforms.
                            worldMapImage = new Bitmap(ms);
                        }
                        if (mapPanel.IsHandleCreated)
                            mapPanel.Invoke(new Action(() => mapPanel.Invalidate()));
                    }
                }
                catch
                {
                    Invoke(new Action(() => LogMsg("[SYSTEM WARNING] Failed to download satellite imagery.")));
                }
            });
        }

        // ── UI Setup ────────────────────────────────────────────────────────────────
        private void SetupUI()
        {
            this.Text = $"VAULT-TEC LAUNCH CONTROL - {GameEngine.Player.NationName}";
            this.Size = new Size(1300, 850);
            this.BackColor = bgDark;
            this.StartPosition = FormStartPosition.CenterScreen;

            GroupBox grpMap = CreateBox("GLOBAL TARGETING SATELLITE", 10, 10, 800, 450);
            mapPanel = new RadarPanel { Location = new Point(10, 25), Size = new Size(780, 415), BackColor = radarBg, Cursor = Cursors.Cross };
            mapPanel.Paint        += MapPanel_Paint;
            mapPanel.MouseClick   += MapPanel_MouseClick;
            mapPanel.MouseMove    += MapPanel_MouseMove;
            mapPanel.MouseDown    += MapPanel_MouseDown;
            mapPanel.MouseUp      += MapPanel_MouseUp;
            mapPanel.MouseWheel   += MapPanel_MouseWheel;
            mapPanel.DoubleClick  += (s, e) => ResetView();
            mapPanel.MouseEnter   += (s, e) => mapPanel.Focus(); // needed for scroll wheel to fire
            grpMap.Controls.Add(mapPanel);
            this.Controls.Add(grpMap);

            GroupBox grpProfile = CreateBox("TARGET SENSORS", 820, 10, 450, 240);
            lblProfile = new Label { Location = new Point(10, 25), Size = new Size(430, 200), ForeColor = amberText, Font = stdFont };
            grpProfile.Controls.Add(lblProfile);
            this.Controls.Add(grpProfile);

            GroupBox grpOps = CreateBox("WEAPONS CONTROL", 820, 260, 450, 130);
            cmbWeapon = new ComboBox { Location = new Point(10, 30), Size = new Size(210, 30), Font = stdFont, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.Black, ForeColor = greenText };
            btnLaunch     = CreateButton("EXECUTE STRIKE",            230, 25,  210, 35,  Color.DarkRed,       Color.White);
            btnSendTroops = CreateButton("DEPLOY EXTRACTION TROOPS",  10,  75,  430, 40,  Color.DarkGoldenrod, Color.White);
            btnLaunch.Click     += BtnLaunch_Click;
            btnSendTroops.Click += BtnSendTroops_Click;
            grpOps.Controls.Add(cmbWeapon); grpOps.Controls.Add(btnLaunch); grpOps.Controls.Add(btnSendTroops);
            this.Controls.Add(grpOps);

            GroupBox grpPlayer = CreateBox("BUNKER STATUS", 820, 400, 450, 150);
            lblPlayerStats = new Label { Location = new Point(10, 25), Size = new Size(300, 120), ForeColor = greenText, Font = stdFont };
            btnOpenShop = CreateButton("BLACK\nMARKET", 320, 25, 120, 115, Color.Black, cyanText);
            btnOpenShop.Click += (s, e) => { new ShopForm().ShowDialog(); RefreshData(); };
            grpPlayer.Controls.Add(lblPlayerStats); grpPlayer.Controls.Add(btnOpenShop);
            this.Controls.Add(grpPlayer);

            GroupBox grpLogs = CreateBox("🔴 LIVE TACTICAL COMMENTARY", 10, 470, 1260, 320);
            logBox = new RichTextBox { Location = new Point(10, 25), Size = new Size(1240, 285), BackColor = Color.Black, ForeColor = greenText, Font = new Font("Consolas", 14F, FontStyle.Bold), ReadOnly = true, BorderStyle = BorderStyle.None };
            grpLogs.Controls.Add(logBox);
            this.Controls.Add(grpLogs);

            LogMsg("[SYSTEM BOOT] Satellite Uplink Established.");
            LogMsg("[SYSTEM BOOT] Awaiting target coordinates from Commander.");
        }

        private GroupBox CreateBox(string t, int x, int y, int w, int h) =>
            new GroupBox { Text = t, ForeColor = greenText, Font = stdFont, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat };

        private Button CreateButton(string t, int x, int y, int w, int h, Color bg, Color fg) =>
            new Button { Text = t, Location = new Point(x, y), Size = new Size(w, h), BackColor = bg, ForeColor = fg, Font = stdFont, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };

        // ── Bezier helpers ──────────────────────────────────────────────────────────
        private PointF BezierPoint(PointF p0, PointF p1, PointF p2, float t)
        {
            float u = 1 - t;
            return new PointF(
                u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X,
                u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y);
        }

        private PointF GetArcControl(PointF start, PointF end)
        {
            float dist = (float)Math.Sqrt(
                (end.X - start.X) * (end.X - start.X) +
                (end.Y - start.Y) * (end.Y - start.Y));
            return new PointF((start.X + end.X) / 2f, (start.Y + end.Y) / 2f - dist * 0.5f);
        }

        // ── Map Rendering ───────────────────────────────────────────────────────────
        private void MapPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = mapPanel.Width, h = mapPanel.Height;

            // Apply zoom + pan — all world-space drawing below is automatically transformed
            g.TranslateTransform(_panOffset.X, _panOffset.Y);
            g.ScaleTransform(_zoom, _zoom);

            if (worldMapImage != null) g.DrawImage(worldMapImage, 0, 0, w, h);

            // Alliance lines
            var allyPen  = new Pen(Color.FromArgb(180, cyanText), 2) { DashStyle = DashStyle.Solid };
            var enAllyPen = new Pen(Color.FromArgb(60, Color.LimeGreen), 1) { DashStyle = DashStyle.Dash };
            float px = w * GameEngine.Player.MapX, py = h * GameEngine.Player.MapY;

            foreach (string a in GameEngine.Player.Allies)
                if (GameEngine.Nations.TryGetValue(a, out Nation an))
                    g.DrawLine(allyPen, px, py, w * an.MapX, h * an.MapY);

            foreach (var n in GameEngine.Nations.Values)
                foreach (var a in n.Allies)
                    if (GameEngine.Nations.TryGetValue(a, out Nation aa))
                        g.DrawLine(enAllyPen, w * n.MapX, h * n.MapY, w * aa.MapX, h * aa.MapY);

            Font nodeFont = new Font("Consolas", 8F, FontStyle.Bold);

            // Player base
            g.FillRectangle(new SolidBrush(cyanText), px - 7, py - 7, 14, 14);
            string baseLabel = $"BUNKER 67 ({GameEngine.Player.NationName})";
            SizeF bs = g.MeasureString(baseLabel, nodeFont);
            g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), px + 10, py - 8, bs.Width, bs.Height);
            g.DrawString(baseLabel, nodeFont, new SolidBrush(cyanText), px + 10, py - 8);

            // Other human players' bases
            foreach (var mp in _mpPlayers)
            {
                if (mp.Id == _mpClient?.LocalPlayerId || mp.Country == null) continue;
                if (!GameEngine.Nations.TryGetValue(mp.Country, out Nation mpNation)) continue;
                float mx = w * mpNation.MapX, my = h * mpNation.MapY;
                Color mc = ColorTranslator.FromHtml(mp.Color);
                // Diamond shape to distinguish from AI nodes
                var diamond = new PointF[] {
                    new PointF(mx, my - 9), new PointF(mx + 9, my),
                    new PointF(mx, my + 9), new PointF(mx - 9, my)
                };
                g.FillPolygon(new SolidBrush(mc), diamond);
                string mpLabel = $"[{mp.Name.ToUpper()}]";
                SizeF ms2 = g.MeasureString(mpLabel, nodeFont);
                g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), mx + 12, my - 8, ms2.Width, ms2.Height);
                g.DrawString(mpLabel, nodeFont, new SolidBrush(mc), mx + 12, my - 8);
            }

            // Nation nodes — labels only when zoomed in enough to read them
            bool showLabels = _zoom >= 1.1f;
            foreach (var kvp in GameEngine.Nations)
            {
                Nation n = kvp.Value;
                float x = w * n.MapX, y = h * n.MapY;

                Color nc = Color.LimeGreen;
                if (n.IsDefeated)                                   nc = Color.Gray;
                else if (GameEngine.Player.Allies.Contains(n.Name)) nc = cyanText;
                else if (n.IsHostileToPlayer)                       nc = redText;
                if (n.Name == hoveredTarget || n.Name == selectedTarget) nc = Color.White;

                // Node dot — slightly larger for hovered/selected
                float r = (n.Name == hoveredTarget || n.Name == selectedTarget) ? 8f : 6f;
                g.FillEllipse(new SolidBrush(nc), x - r, y - r, r * 2, r * 2);

                if (showLabels || n.Name == hoveredTarget || n.Name == selectedTarget)
                {
                    SizeF ts = g.MeasureString(n.Name.ToUpper(), nodeFont);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), x + 9, y - 8, ts.Width, ts.Height);
                    g.DrawString(n.Name.ToUpper(), nodeFont, new SolidBrush(nc), x + 9, y - 8);
                }

                if (n.Name == selectedTarget)
                {
                    var sp = new Pen(Color.Yellow, 2);
                    g.DrawEllipse(sp, x - 15, y - 15, 30, 30);
                    g.DrawLine(sp, x - 20, y, x + 20, y);
                    g.DrawLine(sp, x, y - 20, x, y + 20);
                }
            }

            // ── MISSILES ───────────────────────────────────────────────────────────
            foreach (var missile in activeMissiles)
            {
                PointF ctrl = GetArcControl(missile.Start, missile.End);
                int steps = 50;
                int headStep = Math.Max(1, (int)(missile.Progress * steps));

                for (int i = 1; i <= headStep; i++)
                {
                    PointF pt0 = BezierPoint(missile.Start, ctrl, missile.End, (float)(i - 1) / steps);
                    PointF pt1 = BezierPoint(missile.Start, ctrl, missile.End, (float)i / steps);
                    int alpha = (int)(255f * i / headStep);
                    using (var tp = new Pen(Color.FromArgb(Math.Clamp(alpha, 0, 255), missile.MissileColor), 1.5f))
                        g.DrawLine(tp, pt0, pt1);
                }

                PointF head = BezierPoint(missile.Start, ctrl, missile.End, missile.Progress);
                using (var gb = new SolidBrush(Color.FromArgb(80, missile.MissileColor)))
                    g.FillEllipse(gb, head.X - 8, head.Y - 8, 16, 16);
                using (var hb = new SolidBrush(Color.White))
                    g.FillEllipse(hb, head.X - 3, head.Y - 3, 6, 6);
            }

            // ── EXPLOSIONS ─────────────────────────────────────────────────────────
            foreach (var exp in activeExplosions)
            {
                float t = exp.Progress;
                float radius = exp.MaxRadius * (float)Math.Sin(t * Math.PI);
                int alpha = t < 0.3f ? (int)(t / 0.3f * 220) : (int)((1f - t) / 0.7f * 220);
                alpha = Math.Clamp(alpha, 0, 220);

                Color inner = exp.IsPlayerTarget ? Color.Crimson   : Color.OrangeRed;
                Color outer = exp.IsPlayerTarget ? Color.Red       : Color.Yellow;

                if (radius > 0.5f)
                {
                    using (var fb = new SolidBrush(Color.FromArgb(alpha / 3, inner)))
                        g.FillEllipse(fb, exp.Center.X - radius, exp.Center.Y - radius, radius * 2, radius * 2);
                    using (var rp = new Pen(Color.FromArgb(alpha, outer), 2.5f))
                        g.DrawEllipse(rp, exp.Center.X - radius, exp.Center.Y - radius, radius * 2, radius * 2);

                    if (t < 0.35f)
                    {
                        float cr = radius * 0.5f * (1 - t / 0.35f);
                        int ca = (int)((1 - t / 0.35f) * 255);
                        using (var cb = new SolidBrush(Color.FromArgb(ca, Color.White)))
                            g.FillEllipse(cb, exp.Center.X - cr, exp.Center.Y - cr, cr * 2, cr * 2);
                    }
                }

                if (exp.DamageLines != null && exp.TextProgress < 1.0f)
                {
                    float tt = exp.TextProgress;
                    // Fade in over first 15%, hold full, fade out over last 20%
                    int ta = tt < 0.15f ? (int)(tt / 0.15f * 235) :
                             tt < 0.80f ? 235 :
                             (int)((1f - tt) / 0.20f * 235);
                    ta = Math.Clamp(ta, 0, 235);

                    using (var font = new Font("Consolas", 8f, FontStyle.Bold))
                    {
                        float tx2 = exp.Center.X + exp.MaxRadius + 8, ty2 = exp.Center.Y - 16;
                        foreach (string line in exp.DamageLines.Where(l => !string.IsNullOrWhiteSpace(l)))
                        {
                            SizeF sz = g.MeasureString(line, font);
                            g.FillRectangle(new SolidBrush(Color.FromArgb(Math.Min(ta + 40, 210), 0, 0, 0)), tx2 - 2, ty2 - 1, sz.Width + 4, sz.Height + 2);
                            Color tc2 = exp.IsPlayerTarget ? Color.FromArgb(ta, redText) : Color.FromArgb(ta, amberText);
                            g.DrawString(line, font, new SolidBrush(tc2), tx2, ty2);
                            ty2 += sz.Height + 3;
                        }
                    }
                }
            }

            // Radar sweep
            float cx = w / 2f, cy = h / 2f, rr = Math.Max(w, h);
            float ex = cx + (float)(Math.Cos(radarAngle * Math.PI / 180) * rr);
            float ey = cy + (float)(Math.Sin(radarAngle * Math.PI / 180) * rr);
            g.DrawLine(new Pen(Color.FromArgb(120, 57, 255, 20), 2), cx, cy, ex, ey);

            // Hint overlay — drawn in screen space so it stays fixed regardless of zoom/pan
            g.ResetTransform();
            string hint = $"Scroll: zoom ({_zoom:F1}x)  │  Right-drag: pan  │  Double-click: reset";
            using var hf = new Font("Consolas", 8F);
            SizeF hs = g.MeasureString(hint, hf);
            g.FillRectangle(new SolidBrush(Color.FromArgb(140, 0, 0, 0)), 4, mapPanel.Height - hs.Height - 4, hs.Width + 4, hs.Height + 2);
            g.DrawString(hint, hf, new SolidBrush(Color.FromArgb(160, 57, 255, 20)), 6, mapPanel.Height - hs.Height - 3);
        }

        // ── Animation Timer ─────────────────────────────────────────────────────────
        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            bool dirty = false;

            for (int i = activeMissiles.Count - 1; i >= 0; i--)
            {
                var m = activeMissiles[i];
                m.Progress += m.Speed;
                if (m.Progress >= 1.0f)
                {
                    m.Progress = 1.0f;
                    Action impact = m.OnImpact;
                    activeMissiles.RemoveAt(i);
                    impact?.Invoke();
                }
                dirty = true;
            }

            for (int i = activeExplosions.Count - 1; i >= 0; i--)
            {
                var exp = activeExplosions[i];
                exp.Progress     += 0.014f;  // blast ring: ~1 second
                exp.TextProgress += 0.004f;  // damage text: ~4 seconds
                if (exp.Progress >= 1.0f && exp.TextProgress >= 1.0f)
                    activeExplosions.RemoveAt(i);
                dirty = true;
            }

            if (dirty) mapPanel.Invalidate();
        }

        // ── Mouse Events ────────────────────────────────────────────────────────────
        private PointF ScreenToWorld(float sx, float sy) =>
            new PointF((sx - _panOffset.X) / _zoom, (sy - _panOffset.Y) / _zoom);

        private void MapPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _panOffset = new PointF(
                    _panOffsetAtStart.X + (e.X - _panStart.X),
                    _panOffsetAtStart.Y + (e.Y - _panStart.Y));
                mapPanel.Invalidate();
                return;
            }

            int w = mapPanel.Width, h = mapPanel.Height;
            PointF world = ScreenToWorld(e.X, e.Y);
            // Hit radius scales with zoom so small targets stay clickable when zoomed out
            float hitRadius = 14f / _zoom;
            hoveredTarget = "";
            foreach (var n in GameEngine.Nations.Values)
            {
                float dx = world.X - w * n.MapX, dy = world.Y - h * n.MapY;
                if (dx * dx + dy * dy < hitRadius * hitRadius) { hoveredTarget = n.Name; break; }
            }
        }

        private void MapPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && hoveredTarget != "")
            { selectedTarget = hoveredTarget; UpdateProfile(); }
        }

        private void MapPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                _isPanning        = true;
                _panStart         = e.Location;
                _panOffsetAtStart = _panOffset;
                mapPanel.Cursor   = Cursors.SizeAll;
            }
        }

        private void MapPanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isPanning) { _isPanning = false; mapPanel.Cursor = Cursors.Cross; }
        }

        private void MapPanel_MouseWheel(object sender, MouseEventArgs e)
        {
            float factor  = e.Delta > 0 ? 1.18f : 1f / 1.18f;
            float newZoom = Math.Clamp(_zoom * factor, 0.4f, 6.0f);
            // Keep the point under the cursor stationary
            _panOffset = new PointF(
                e.X - (e.X - _panOffset.X) * (newZoom / _zoom),
                e.Y - (e.Y - _panOffset.Y) * (newZoom / _zoom));
            _zoom = newZoom;
            mapPanel.Invalidate();
        }

        private void ResetView()
        {
            _zoom      = 1.0f;
            _panOffset = PointF.Empty;
            mapPanel.Invalidate();
        }

        // ── UI Update ───────────────────────────────────────────────────────────────
        private void UpdateProfile()
        {
            if (string.IsNullOrEmpty(selectedTarget) || !GameEngine.Nations.ContainsKey(selectedTarget)) return;
            Nation target = GameEngine.Nations[selectedTarget];

            string allies = target.Allies.Count > 0 ? string.Join(", ", target.Allies) : "None";
            string rel = "NEUTRAL";
            if (GameEngine.Player.Allies.Contains(target.Name)) rel = "YOUR ALLY (Will Assist)";
            else if (target.IsHostileToPlayer) rel = "HOSTILE (Will Retaliate!)";

            lblProfile.Text =
                $"TARGET LOCK: {target.Name.ToUpper()}\n" +
                $"=================================\n" +
                $"POPULATION: {target.Population:N0}\n" +
                $"NUKES DETECTED: {target.Nukes}\n" +
                $"EST. TREASURY: ${target.Money}M\n" +
                $"THREAT LEVEL: {new string('★', target.Difficulty)}\n\n" +
                $"ALLIANCE NETWORK: {allies}\n" +
                $"DIPLOMATIC STATUS: {rel}\n" +
                $"COMBAT STATUS: {(target.IsDefeated ? "DEFEATED" : "ACTIVE")}";

            btnLaunch.Enabled     = !target.IsDefeated && !activeMissiles.Any(m => m.IsPlayerMissile);
            btnSendTroops.Enabled = target.IsDefeated && !target.IsLooted &&
                                    !GameEngine.ActiveMissions.Any(m => m.TargetNation == target.Name);
        }

        private void RefreshData()
        {
            string pa = GameEngine.Player.Allies.Count > 0 ? string.Join(", ", GameEngine.Player.Allies) : "None";
            lblPlayerStats.Text =
                $"YOUR NATION: {GameEngine.Player.NationName}\n" +
                $"YOUR ALLIES: {pa}\n" +
                $"POPULATION:  {GameEngine.Player.Population:N0}\n" +
                $"TREASURY:    ${GameEngine.Player.Money:N0}M\n" +
                $"DEFENSES:    Dome L{GameEngine.Player.IronDomeLevel} | Bunk L{GameEngine.Player.BunkerLevel} | Vac L{GameEngine.Player.VaccineLevel}";

            int wi = cmbWeapon.SelectedIndex;
            cmbWeapon.Items.Clear();
            cmbWeapon.Items.Add($"Standard Nuke ({GameEngine.Player.StandardNukes})");
            cmbWeapon.Items.Add($"Tsar Bomba ({GameEngine.Player.MegaNukes})");
            cmbWeapon.Items.Add($"Bio-Plague ({GameEngine.Player.BioPlagues})");
            cmbWeapon.Items.Add($"Orbital Laser ({GameEngine.Player.OrbitalLasers})");
            cmbWeapon.SelectedIndex = wi >= 0 ? wi : 0;

            UpdateProfile();
            mapPanel.Invalidate();
        }

        // ── Player Strike ────────────────────────────────────────────────────────────
        private void BtnLaunch_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedTarget) || cmbWeapon.SelectedItem == null) return;
            int weaponIndex = cmbWeapon.SelectedIndex;

            if (weaponIndex == 0 && GameEngine.Player.StandardNukes <= 0) { MessageBox.Show("Out of Standard Nukes!"); return; }
            if (weaponIndex == 1 && GameEngine.Player.MegaNukes    <= 0) { MessageBox.Show("Out of Tsar Bombas!");    return; }
            if (weaponIndex == 2 && GameEngine.Player.BioPlagues   <= 0) { MessageBox.Show("Out of Bio-Plagues!");    return; }
            if (weaponIndex == 3 && GameEngine.Player.OrbitalLasers <= 0){ MessageBox.Show("Out of Orbital Lasers!"); return; }

            if (weaponIndex == 0) GameEngine.Player.StandardNukes--;
            if (weaponIndex == 1) GameEngine.Player.MegaNukes--;
            if (weaponIndex == 2) GameEngine.Player.BioPlagues--;
            if (weaponIndex == 3) GameEngine.Player.OrbitalLasers--;

            btnLaunch.Enabled = false;

            int mw = mapPanel.Width, mh = mapPanel.Height;
            Nation target = GameEngine.Nations[selectedTarget];
            PointF startPt  = new PointF(mw * GameEngine.Player.MapX, mh * GameEngine.Player.MapY);
            PointF impactPt = new PointF(mw * target.MapX, mh * target.MapY);

            string[] wNames  = { "STANDARD NUKE", "TSAR BOMBA", "BIO-PLAGUE CANISTER", "ORBITAL LASER" };
            Color[]  wColors = { Color.OrangeRed, Color.DeepPink, Color.LimeGreen, Color.Cyan };
            float[]  wRadii  = { 45f, 70f, 55f, 40f };

            logBox.SelectionColor = amberText;
            LogMsg($"[LAUNCH] {wNames[weaponIndex]} launched toward {selectedTarget.ToUpper()}! Tracking projectile...");

            string tc = selectedTarget; int wc = weaponIndex; float rc = wRadii[weaponIndex];

            // Broadcast to other players in multiplayer
            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "strike", target = tc, weapon = wc, playerNation = GameEngine.Player.NationName });

            activeMissiles.Add(new MissileAnimation
            {
                Start = startPt, End = impactPt, IsPlayerMissile = true,
                MissileColor = wColors[weaponIndex], Speed = 0.007f,
                OnImpact = async () => await HandlePlayerStrikeImpact(tc, wc, impactPt, rc)
            });
        }

        private async Task HandlePlayerStrikeImpact(string targetName, int weaponIndex, PointF impactPos, float blastRadius)
        {
            StrikeResult result = CombatEngine.ExecuteCombatTurn(targetName, weaponIndex);

            string impactLine = result.Logs.FirstOrDefault(l => l.Contains("[IMPACT]")) ?? "";
            string resultLine = result.Logs.FirstOrDefault(l =>
                l.Contains("SURRENDER") || l.Contains("VICTORY") || l.Contains("SUCCESS")) ?? "";

            activeExplosions.Add(new ExplosionEffect
            {
                Center = impactPos, MaxRadius = blastRadius,
                DamageLines = new[] { impactLine, resultLine }, IsPlayerTarget = false
            });

            foreach (var l in result.Logs)
            {
                logBox.SelectionColor =
                    l.Contains("WARNING") || l.Contains("CATASTROPHE") ? redText :
                    l.Contains("SURRENDER") || l.Contains("SUCCESS") || l.Contains("VICTORY") ? amberText :
                    l.Contains("ALLY") ? cyanText : greenText;
                LogMsg(l);
                await Task.Delay(500);
            }

            RefreshData();

            // Animated ally support strikes toward target
            foreach (var (allyName, damage) in result.AllySupporters)
            {
                if (GameEngine.Nations.TryGetValue(allyName, out Nation allyNation))
                {
                    await Task.Delay(350);
                    logBox.SelectionColor = cyanText;
                    LogMsg($"[ALLY SUPPORT] {allyName.ToUpper()} has launched supporting missiles at {targetName.ToUpper()}!");
                    LaunchAllyMissile(allyNation, targetName, damage, impactPos);
                }
            }

            // Animated retaliation missiles toward player (staggered)
            foreach (string retaliatorName in result.Retaliators)
            {
                if (GameEngine.Nations.TryGetValue(retaliatorName, out _))
                {
                    await Task.Delay(600);
                    LaunchEnemyMissile(GameEngine.Nations[retaliatorName]);
                }
            }
        }

        // ── Ally Support Missiles ────────────────────────────────────────────────────
        private void LaunchAllyMissile(Nation ally, string targetName, long damage, PointF fallbackImpact)
        {
            int w = mapPanel.Width, h = mapPanel.Height;
            PointF startPt  = new PointF(w * ally.MapX, h * ally.MapY);
            PointF impactPt = GameEngine.Nations.TryGetValue(targetName, out Nation tn)
                ? new PointF(w * tn.MapX, h * tn.MapY) : fallbackImpact;

            string ac = ally.Name, tgtC = targetName;
            long dc = damage;

            activeMissiles.Add(new MissileAnimation
            {
                Start = startPt, End = impactPt, IsPlayerMissile = false,
                MissileColor = Color.Cyan, Speed = 0.008f,
                OnImpact = () => HandleAllyStrikeImpact(ac, tgtC, dc, impactPt)
            });
        }

        private void HandleAllyStrikeImpact(string allyName, string targetName, long damage, PointF impactPos)
        {
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return;

            long actualDmg = Math.Min(damage, target.Population);
            target.Population -= actualDmg;

            activeExplosions.Add(new ExplosionEffect
            {
                Center = impactPos, MaxRadius = 35f,
                DamageLines = new[] { $"[ALLY IMPACT] +{actualDmg:N0} casualties" },
                IsPlayerTarget = false
            });

            logBox.SelectionColor = cyanText;
            LogMsg($"[ALLY IMPACT] {allyName.ToUpper()} support strike caused an additional {actualDmg:N0} casualties.");
            RefreshData();
        }

        // ── Enemy Missiles (hostile → player) ───────────────────────────────────────
        private void LaunchEnemyMissile(Nation attacker)
        {
            int w = mapPanel.Width, h = mapPanel.Height;
            PointF startPt  = new PointF(w * attacker.MapX, h * attacker.MapY);
            PointF impactPt = new PointF(w * GameEngine.Player.MapX, h * GameEngine.Player.MapY);

            logBox.SelectionColor = redText;
            LogMsg($"[WARNING] ⚠ RADAR ALERT: {attacker.Name.ToUpper()} HAS LAUNCHED AN ICBM! BRACE FOR IMPACT! ⚠");

            string ac = attacker.Name;

            activeMissiles.Add(new MissileAnimation
            {
                Start = startPt, End = impactPt, IsPlayerMissile = false,
                MissileColor = Color.Red, Speed = 0.005f,
                OnImpact = async () => await HandleEnemyStrikeImpact(ac, impactPt)
            });
        }

        private async Task HandleEnemyStrikeImpact(string attackerName, PointF impactPos)
        {
            var logs = CombatEngine.ExecuteEnemyStrike(attackerName);
            if (logs.Count == 0) return;

            string casualtyLine = logs.FirstOrDefault(l => l.Contains("CASUALTY")) ?? "";

            activeExplosions.Add(new ExplosionEffect
            {
                Center = impactPos, MaxRadius = 55f,
                DamageLines = new[] { $"STRIKE FROM {attackerName.ToUpper()}", casualtyLine },
                IsPlayerTarget = true
            });

            foreach (var l in logs)
            {
                logBox.SelectionColor =
                    l.Contains("CATASTROPHE") || l.Contains("WARNING") ? redText :
                    l.Contains("DEFENSE") ? cyanText : greenText;
                LogMsg(l);
                await Task.Delay(400);
            }

            RefreshData();
            CheckGameOver();
        }

        // ── Country vs Country Wars ──────────────────────────────────────────────────
        private void LaunchNationVsNationMissile(Nation attacker, Nation target)
        {
            bool allyUnderAttack = GameEngine.Player.Allies.Contains(target.Name);

            logBox.SelectionColor = allyUnderAttack ? redText : amberText;
            string prefix = allyUnderAttack ? "[ALLY UNDER ATTACK]" : "[WORLD EVENT]";
            LogMsg($"{prefix} {attacker.Name.ToUpper()} has launched a missile at {target.Name.ToUpper()}!");

            int w = mapPanel.Width, h = mapPanel.Height;
            PointF startPt  = new PointF(w * attacker.MapX, h * attacker.MapY);
            PointF impactPt = new PointF(w * target.MapX,   h * target.MapY);

            string ac = attacker.Name, tc = target.Name;

            activeMissiles.Add(new MissileAnimation
            {
                Start = startPt, End = impactPt, IsPlayerMissile = false,
                MissileColor = Color.Orange, Speed = 0.006f,
                OnImpact = async () => await HandleNationVsNationImpact(ac, tc, impactPt, allyUnderAttack)
            });
        }

        private async Task HandleNationVsNationImpact(string attackerName, string targetName, PointF impactPos, bool allyUnderAttack)
        {
            if (!GameEngine.Nations.TryGetValue(attackerName, out Nation attacker)) return;
            if (!GameEngine.Nations.TryGetValue(targetName,   out Nation target))   return;

            // Apply damage — being struck makes nations angrier
            long damage = (long)(target.MaxPopulation * (0.04 + rng.NextDouble() * 0.14));
            damage = Math.Min(damage, target.Population);
            target.Population -= damage;
            target.AngerLevel = Math.Min(10, target.AngerLevel + 1);

            activeExplosions.Add(new ExplosionEffect
            {
                Center = impactPos, MaxRadius = 42f,
                DamageLines = new[] { $"{attackerName.ToUpper()} → {targetName.ToUpper()}", $"{damage:N0} casualties" },
                IsPlayerTarget = allyUnderAttack
            });

            logBox.SelectionColor = allyUnderAttack ? redText : amberText;
            LogMsg($"[WORLD EVENT] {attackerName.ToUpper()} struck {targetName.ToUpper()}! Est. {damage:N0} casualties.");

            if (target.Population <= 0)
            {
                target.IsDefeated = true;
                logBox.SelectionColor = amberText;
                LogMsg($"[WORLD EVENT] {targetName.ToUpper()} has been annihilated by {attackerName.ToUpper()}!");
            }

            RefreshData();

            // Target retaliates against attacker if able
            if (!target.IsDefeated && target.Nukes > 0 && rng.NextDouble() < 0.65)
            {
                await Task.Delay(900);
                if (!target.IsDefeated && !attacker.IsDefeated)
                    LaunchNationVsNationMissile(target, attacker);
            }

            // Target's allies retaliate against the attacker
            foreach (string allyName in target.Allies)
            {
                if (!GameEngine.Nations.TryGetValue(allyName, out Nation targetAlly)) continue;
                if (targetAlly.IsDefeated || targetAlly.Nukes <= 0) continue;
                if (rng.NextDouble() >= 0.45) continue;

                await Task.Delay(700 + rng.Next(500));
                if (!targetAlly.IsDefeated && !attacker.IsDefeated)
                {
                    logBox.SelectionColor = allyUnderAttack ? redText : amberText;
                    LogMsg($"[ALLIANCE] {allyName.ToUpper()} enters the war defending {targetName.ToUpper()}!");
                    LaunchNationVsNationMissile(targetAlly, attacker);
                }
            }

            // Attacker's allies join the offensive
            foreach (string allyName in attacker.Allies)
            {
                if (!GameEngine.Nations.TryGetValue(allyName, out Nation attackerAlly)) continue;
                if (attackerAlly.IsDefeated || attackerAlly.Nukes <= 0) continue;
                if (rng.NextDouble() >= 0.30) continue;

                await Task.Delay(700 + rng.Next(500));
                if (!attackerAlly.IsDefeated && !target.IsDefeated)
                {
                    logBox.SelectionColor = amberText;
                    LogMsg($"[ALLIANCE] {allyName.ToUpper()} joins {attackerName.ToUpper()}'s offensive against {targetName.ToUpper()}!");
                    LaunchNationVsNationMissile(attackerAlly, target);
                }
            }

            // Prompt player to get involved if an ally was attacked
            if (allyUnderAttack && !attacker.IsDefeated)
            {
                var choice = MessageBox.Show(
                    $"YOUR ALLY {targetName.ToUpper()} WAS ATTACKED BY {attackerName.ToUpper()}!\n\n" +
                    $"Do you want to declare war and retaliate against {attackerName.ToUpper()}?",
                    "⚠ ALLIANCE TRIGGERED — DECISION REQUIRED",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (choice == DialogResult.Yes)
                {
                    attacker.IsHostileToPlayer = true;
                    logBox.SelectionColor = amberText;
                    LogMsg($"[DECLARATION] You have declared war on {attackerName.ToUpper()} in defense of {targetName.ToUpper()}!");
                    RefreshData();
                }
                else
                {
                    logBox.SelectionColor = greenText;
                    LogMsg($"[INTEL] You chose to stay neutral in the {attackerName.ToUpper()} / {targetName.ToUpper()} conflict.");
                }
            }
        }

        // ── World Event Scheduler ───────────────────────────────────────────────────
        private void TryTriggerWorldEvents()
        {
            // 1) Hostile nations specifically targeting the player
            playerAttackTick++;
            if (playerAttackTick >= 12)
            {
                playerAttackTick = 0;
                var hostiles = GameEngine.Nations.Values
                    .Where(n => !n.IsDefeated && n.IsHostileToPlayer && n.Nukes > 0 && !n.IsHumanControlled)
                    .ToList();
                foreach (var a in hostiles)
                {
                    if (rng.NextDouble() < 0.12 + a.Difficulty * 0.04)
                    {
                        // Angry nations fire a salvo; anger 0 = 1 missile, anger 9+ = up to 4
                        int salvo = 1 + rng.Next(0, a.AngerLevel / 3 + 1);
                        salvo = Math.Min(salvo, a.Nukes);
                        for (int s = 0; s < salvo; s++)
                            LaunchEnemyMissile(a);
                        break; // one nation fires per tick
                    }
                }
            }

            // 2) Any nation attacks any other nation (or player)
            worldWarTick++;
            if (worldWarTick >= 18)
            {
                worldWarTick = 0;

                // 30% chance a war event fires this cycle
                if (rng.NextDouble() > 0.30) return;

                var attackers = GameEngine.Nations.Values
                    .Where(n => !n.IsDefeated && n.Nukes > 0 && !n.IsHumanControlled)
                    .ToList();
                if (attackers.Count == 0) return;

                Nation attacker = attackers[rng.Next(attackers.Count)];

                // Valid non-player targets: not allied with attacker, not defeated
                var targetPool = GameEngine.Nations.Values
                    .Where(n => !n.IsDefeated && n.Name != attacker.Name && !attacker.Allies.Contains(n.Name))
                    .ToList();

                // Can attacker also target the player?
                bool canHitPlayer = !GameEngine.Player.Allies.Contains(attacker.Name);

                if (targetPool.Count == 0 && !canHitPlayer) return;

                // Hostile nations lean toward hitting the player (30 %), others rarely do (8 %)
                double playerChance = attacker.IsHostileToPlayer ? 0.30 : 0.08;
                bool hitPlayer = canHitPlayer && rng.NextDouble() < playerChance;

                // Anger drives salvo size: anger 0 = 1 missile, anger 8+ = up to 3
                int nvnSalvo = 1 + rng.Next(0, attacker.AngerLevel / 4 + 1);
                nvnSalvo = Math.Min(nvnSalvo, attacker.Nukes);

                Nation nvnTarget = targetPool.Count > 0 ? targetPool[rng.Next(targetPool.Count)] : null;

                for (int s = 0; s < nvnSalvo; s++)
                {
                    if (hitPlayer)
                    {
                        attacker.IsHostileToPlayer = true;
                        LaunchEnemyMissile(attacker);
                    }
                    else if (nvnTarget != null)
                    {
                        LaunchNationVsNationMissile(attacker, nvnTarget);
                    }
                }
            }
        }

        // ── Troop Deployment Dialog ──────────────────────────────────────────────────
        private void BtnSendTroops_Click(object sender, EventArgs e)
        {
            var (fraction, confirmed) = ShowTroopDeploymentDialog(selectedTarget, GameEngine.Player.Population);
            if (!confirmed) return;

            long troopCount = (long)(GameEngine.Player.Population * fraction);
            if (troopCount < 50_000) { MessageBox.Show("Not enough population to form a deployment force!"); return; }

            int etaSeconds = Math.Max(20, (int)(12.0 / fraction));
            string etaStr = etaSeconds >= 60 ? $"{etaSeconds / 60}m {etaSeconds % 60}s" : $"{etaSeconds}s";

            GameEngine.Player.Population -= troopCount;
            GameEngine.ActiveMissions.Add(new TroopMission
            {
                TargetNation = selectedTarget,
                TroopFraction = fraction,
                TroopCount = troopCount,
                TimeRemainingSeconds = etaSeconds
            });

            int lootPct = (int)(Math.Min(1.0, fraction * 2.0 + 0.10) * 100);
            logBox.SelectionColor = amberText;
            LogMsg($"[COMMAND] {troopCount:N0} troops deployed to {selectedTarget.ToUpper()}. ETA: {etaStr}. Est. extraction efficiency: {lootPct}%.");

            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "deploy", target = selectedTarget, fraction });

            RefreshData();
        }

        private (double fraction, bool confirmed) ShowTroopDeploymentDialog(string targetName, long playerPop)
        {
            double[] fractions = { 0.05, 0.15, 0.30, 0.50 };
            int[]    etas      = { 240,  80,   40,   24  };
            int[]    lootPcts  = { 20,   40,   70,   100 };
            string[] labels    = { "RECON TEAM", "STRIKE FORCE", "FULL ASSAULT", "OVERWHELMING FORCE" };

            var dlg = new Form
            {
                Text = $"DEPLOY TROOPS — {targetName.ToUpper()}",
                Size = new Size(520, 340),
                BackColor = bgDark,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false
            };

            var headerLabel = new Label
            {
                Text = $"Select deployment force for {targetName.ToUpper()}:\n(Current Population: {playerPop:N0})",
                Location = new Point(15, 10), Size = new Size(480, 40),
                ForeColor = amberText, Font = stdFont, BackColor = Color.Transparent
            };
            dlg.Controls.Add(headerLabel);

            var radios = new RadioButton[4];
            for (int i = 0; i < fractions.Length; i++)
            {
                long troops = (long)(playerPop * fractions[i]);
                string etaStr = etas[i] >= 60 ? $"{etas[i] / 60}m {etas[i] % 60}s" : $"{etas[i]}s";
                string line = $"{labels[i]} ({(int)(fractions[i]*100)}%)  |  Troops: {troops:N0}  |  ETA: {etaStr}  |  Loot: ~{lootPcts[i]}%";
                var rb = new RadioButton
                {
                    Text = line,
                    Location = new Point(15, 55 + i * 42),
                    Size = new Size(480, 35),
                    ForeColor = greenText, Font = new Font("Consolas", 9F, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    Checked = (i == 1)
                };
                radios[i] = rb;
                dlg.Controls.Add(rb);
            }

            var btnDeploy = new Button
            {
                Text = "DEPLOY", Location = new Point(280, 268), Size = new Size(100, 35),
                BackColor = Color.DarkGoldenrod, ForeColor = Color.White,
                Font = stdFont, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK
            };
            var btnCancel = new Button
            {
                Text = "CANCEL", Location = new Point(390, 268), Size = new Size(100, 35),
                BackColor = Color.DarkRed, ForeColor = Color.White,
                Font = stdFont, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel
            };
            dlg.Controls.Add(btnDeploy);
            dlg.Controls.Add(btnCancel);
            dlg.AcceptButton = btnDeploy;
            dlg.CancelButton = btnCancel;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                for (int i = 0; i < radios.Length; i++)
                    if (radios[i].Checked) return (fractions[i], true);
                return (fractions[1], true);
            }
            return (0, false);
        }

        // ── Game Timer ──────────────────────────────────────────────────────────────

        private void GameTimer_Tick(object sender, EventArgs e)
        {
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

            // Cool down nation anger over time (–1 every 30 seconds)
            angerDecayTick++;
            if (angerDecayTick >= 30)
            {
                angerDecayTick = 0;
                foreach (var n in GameEngine.Nations.Values)
                    if (n.AngerLevel > 0) n.AngerLevel--;
            }

            TryTriggerWorldEvents();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────
        private void CheckGameOver()
        {
            if (GameEngine.Player.Population <= 0)
            {
                MessageBox.Show("YOUR NATION WAS WIPED OUT. GAME OVER.");
                Application.Exit();
            }
        }

        private void LogMsg(string txt)
        {
            logBox.AppendText(txt + "\n");
            logBox.ScrollToCaret();
        }
    }
}
