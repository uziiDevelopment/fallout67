using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;

namespace fallover_67
{
    public class MissileAnimation
    {
        public PointLatLng Start { get; set; }
        public PointLatLng End { get; set; }
        public float Progress { get; set; } = 0f;
        public float Speed { get; set; } = 0.5f; // Driven by delta-time (60fps)
        public bool IsPlayerMissile { get; set; }
        public Color MissileColor { get; set; } = Color.OrangeRed;
        public Action OnImpact { get; set; }
    }

    public class ExplosionEffect
    {
        public PointLatLng Center { get; set; }
        public float Progress { get; set; } = 0f;
        public float TextProgress { get; set; } = 0f;
        public float MaxRadius { get; set; } = 45f;
        public string[] DamageLines { get; set; } = Array.Empty<string>();
        public bool IsPlayerTarget { get; set; }
    }

    public class RadarPanel : GMapControl
    {
        public RadarPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.Selectable, true);
        }
    }

    public class ControlPanelForm : Form
    {
        private Color bgDark = Color.FromArgb(15, 20, 15);
        private Color radarBg = Color.FromArgb(5, 10, 5);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color redText = Color.FromArgb(255, 50, 50);
        private Color cyanText = Color.Cyan;
        private Font stdFont = new Font("Consolas", 10F, FontStyle.Bold);

        private RadarPanel mapPanel;
        private Label lblProfile, lblPlayerStats;
        private ComboBox cmbWeapon;
        private Button btnLaunch, btnSendTroops, btnOpenShop;
        private RichTextBox logBox;

        private System.Windows.Forms.Timer gameTimer;
        private Stopwatch _frameStopwatch = new Stopwatch();

        private float radarAngle = 0;
        private string selectedTarget = "";
        private string hoveredTarget = "";

        // Cache coordinates per frame so we aren't doing heavy map math on mouse moves
        private Dictionary<string, PointF> _currentScreenCoords = new Dictionary<string, PointF>();

        private List<MissileAnimation> activeMissiles = new List<MissileAnimation>();
        private List<ExplosionEffect> activeExplosions = new List<ExplosionEffect>();
        private static Random rng = new Random();

        // ── Multiplayer state ────────────────────────────────────────────────
        private MultiplayerClient? _mpClient;
        private List<MpPlayer> _mpPlayers = new();
        private bool _isMultiplayer = false;

        private int playerAttackTick = 0;
        private int worldWarTick = 0;
        private int angerDecayTick = 0;

        public ControlPanelForm()
        {
            InitForm();
        }

        public ControlPanelForm(MultiplayerClient mpClient, List<MpPlayer> mpPlayers)
        {
            _mpClient = mpClient;
            _mpPlayers = mpPlayers;
            _isMultiplayer = true;

            foreach (var p in mpPlayers)
                if (p.Country != null && p.Id != mpClient.LocalPlayerId &&
                    GameEngine.Nations.TryGetValue(p.Country, out var nation))
                    nation.IsHumanControlled = true;

            InitForm();
            SetupMultiplayer();
        }

        private void InitForm()
        {
            // Security protocol required for modern map downloads
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            GMapProvider.UserAgent = "VaultTec_LaunchControl/1.0";
            GMaps.Instance.Mode = AccessMode.ServerAndCache;

            SetupUI();
            CorrectCountryCoordinates(); // Assigns real Lat/Lng
            RefreshData();

            gameTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            gameTimer.Tick += GameTimer_Tick;
            gameTimer.Start();

            _frameStopwatch.Start();

            // Run rendering at an ultra-smooth 60 FPS
            var renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
            renderTimer.Tick += RenderTimer_Tick;
            renderTimer.Start();
        }

        // Hardcoded real-world locations so cities spawn in the correct geographic locations
        private void CorrectCountryCoordinates()
        {
            var accurateCoords = new Dictionary<string, PointLatLng>(StringComparer.OrdinalIgnoreCase)
            {
                { "USA", new PointLatLng(38.9, -77.0) },          // Washington DC
                { "CANADA", new PointLatLng(45.4, -75.7) },       // Ottawa
                { "MEXICO", new PointLatLng(19.4, -99.1) },       // Mexico City
                { "CUBA", new PointLatLng(23.1, -82.3) },         // Havana
                { "BRAZIL", new PointLatLng(-15.8, -47.9) },      // Brasilia
                { "ARGENTINA", new PointLatLng(-34.6, -58.3) },   // Buenos Aires
                { "UK", new PointLatLng(51.5, -0.1) },            // London
                { "FRANCE", new PointLatLng(48.8, 2.3) },         // Paris
                { "SPAIN", new PointLatLng(40.4, -3.7) },         // Madrid
                { "GERMANY", new PointLatLng(52.5, 13.4) },       // Berlin
                { "ITALY", new PointLatLng(41.9, 12.4) },         // Rome
                { "UKRAINE", new PointLatLng(50.4, 30.5) },       // Kyiv
                { "RUSSIA", new PointLatLng(55.7, 37.6) },        // Moscow
                { "TURKEY", new PointLatLng(39.9, 32.8) },        // Ankara
                { "ISRAEL", new PointLatLng(31.7, 35.2) },        // Jerusalem
                { "EGYPT", new PointLatLng(30.0, 31.2) },         // Cairo
                { "SAUDI ARABIA", new PointLatLng(24.7, 46.7) },  // Riyadh
                { "IRAN", new PointLatLng(35.7, 51.4) },          // Tehran
                { "PAKISTAN", new PointLatLng(33.7, 73.0) },      // Islamabad
                { "INDIA", new PointLatLng(28.6, 77.2) },         // New Delhi
                { "CHINA", new PointLatLng(39.9, 116.4) },        // Beijing
                { "NORTH KOREA", new PointLatLng(39.0, 125.7) },  // Pyongyang
                { "SOUTH KOREA", new PointLatLng(37.5, 126.9) },  // Seoul
                { "JAPAN", new PointLatLng(35.6, 139.6) },        // Tokyo
                { "INDONESIA", new PointLatLng(-6.2, 106.8) },    // Jakarta
                { "AUSTRALIA", new PointLatLng(-35.3, 149.1) },   // Canberra
                { "NIGERIA", new PointLatLng(9.0, 7.5) },         // Abuja
                { "SOUTH AFRICA", new PointLatLng(-25.7, 28.2) }  // Pretoria
            };

            foreach (var nation in GameEngine.Nations.Values)
            {
                if (accurateCoords.TryGetValue(nation.Name, out PointLatLng coords))
                {
                    nation.MapX = (float)coords.Lng;
                    nation.MapY = (float)coords.Lat;
                }
            }

            if (accurateCoords.TryGetValue(GameEngine.Player.NationName, out PointLatLng pCoords))
            {
                GameEngine.Player.MapX = (float)pCoords.Lng;
                GameEngine.Player.MapY = (float)pCoords.Lat;
            }
        }

        private void SetupMultiplayer()
        {
            if (_mpClient == null) return;
            _mpClient.OnGameAction += (senderId, action) => { if (InvokeRequired) Invoke(new Action(() => HandleRemoteAction(senderId, action))); else HandleRemoteAction(senderId, action); };
            _mpClient.OnChat += (senderId, name, text) => { if (InvokeRequired) Invoke(new Action(() => { logBox.SelectionColor = cyanText; LogMsg($"[COMMS] {name.ToUpper()}: {text}"); })); };
            _mpClient.OnDisconnected += () => { if (InvokeRequired) Invoke(new Action(() => { logBox.SelectionColor = redText; LogMsg("[NETWORK] ⚠ Lost connection to multiplayer server."); })); };
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

                    if (!GameEngine.Nations.ContainsKey(target)) break;

                    string[] wNames = { "STANDARD NUKE", "TSAR BOMBA", "BIO-PLAGUE", "ORBITAL LASER" };
                    Color[] wColors = { Color.OrangeRed, Color.DeepPink, Color.LimeGreen, Color.Cyan };
                    float[] wRadii = { 45f, 70f, 55f, 40f };

                    PointLatLng startPt = GameEngine.Nations.TryGetValue(nation, out Nation attackerNation)
                        ? new PointLatLng(attackerNation.MapY, attackerNation.MapX)
                        : new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);

                    Nation tgtNation = GameEngine.Nations[target];
                    PointLatLng impactPt = new PointLatLng(tgtNation.MapY, tgtNation.MapX);

                    float radius = weapon < wRadii.Length ? wRadii[weapon] : 45f;
                    Color mColor = weapon < wColors.Length ? wColors[weapon] : Color.OrangeRed;

                    logBox.SelectionColor = amberText;
                    LogMsg($"[COMMANDER] {senderName.ToUpper()} launched {wNames[weapon]} at {target.ToUpper()}!");

                    activeMissiles.Add(new MissileAnimation
                    {
                        Start = startPt,
                        End = impactPt,
                        IsPlayerMissile = false,
                        MissileColor = mColor,
                        Speed = 0.4f,
                        OnImpact = () => {
                            var (cas, def) = CombatEngine.ExecuteRemotePlayerStrike(target, weapon);
                            activeExplosions.Add(new ExplosionEffect { Center = impactPt, MaxRadius = radius, DamageLines = new[] { $"[{senderName.ToUpper()}] {cas:N0} casualties{(def ? " — DEFEATED" : "")}" }, IsPlayerTarget = false });
                            logBox.SelectionColor = amberText; LogMsg($"[IMPACT] {target.ToUpper()} — {cas:N0} casualties from {senderName.ToUpper()}'s strike.{(def ? " NATION DEFEATED." : "")}");
                            RefreshData();
                        }
                    });
                    break;
            }
        }

        private void SetupUI()
        {
            this.Text = $"VAULT-TEC LAUNCH CONTROL - {GameEngine.Player.NationName}";
            this.Size = new Size(1300, 850);
            this.BackColor = bgDark;
            this.StartPosition = FormStartPosition.CenterScreen;

            GroupBox grpMap = CreateBox("GLOBAL TARGETING SATELLITE", 10, 10, 800, 450);

            mapPanel = new RadarPanel
            {
                Location = new Point(10, 25),
                Size = new Size(780, 415),
                BackColor = radarBg,
                Cursor = Cursors.Cross,
                MapProvider = GMapProviders.OpenStreetMap,
                Position = new PointLatLng(20, 0),
                MinZoom = 2,           // <--- CLAMPED MIN ZOOM
                MaxZoom = 4,           // <--- CLAMPED MAX ZOOM
                Zoom = 2,
                ShowCenter = false,
                DragButton = MouseButtons.Right,
                GrayScaleMode = true,
                NegativeMode = true,
                ShowTileGridLines = false
            };

            mapPanel.Paint += MapPanel_Paint;
            mapPanel.MouseClick += MapPanel_MouseClick;
            mapPanel.MouseMove += MapPanel_MouseMove;
            mapPanel.DoubleClick += (s, e) => ResetView();
            mapPanel.MouseEnter += (s, e) => mapPanel.Focus();
            grpMap.Controls.Add(mapPanel);
            this.Controls.Add(grpMap);

            GroupBox grpProfile = CreateBox("TARGET SENSORS", 820, 10, 450, 240);
            lblProfile = new Label { Location = new Point(10, 25), Size = new Size(430, 200), ForeColor = amberText, Font = stdFont };
            grpProfile.Controls.Add(lblProfile);
            this.Controls.Add(grpProfile);

            GroupBox grpOps = CreateBox("WEAPONS CONTROL", 820, 260, 450, 130);
            cmbWeapon = new ComboBox { Location = new Point(10, 30), Size = new Size(210, 30), Font = stdFont, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.Black, ForeColor = greenText };
            btnLaunch = CreateButton("EXECUTE STRIKE", 230, 25, 210, 35, Color.DarkRed, Color.White);
            btnSendTroops = CreateButton("DEPLOY EXTRACTION TROOPS", 10, 75, 430, 40, Color.DarkGoldenrod, Color.White);
            btnLaunch.Click += BtnLaunch_Click;
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

        private GroupBox CreateBox(string t, int x, int y, int w, int h) => new GroupBox { Text = t, ForeColor = greenText, Font = stdFont, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat };
        private Button CreateButton(string t, int x, int y, int w, int h, Color bg, Color fg) => new Button { Text = t, Location = new Point(x, y), Size = new Size(w, h), BackColor = bg, ForeColor = fg, Font = stdFont, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };

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

        // ── Ultra-Smooth Render Loop ────────────────────────────────────────────────
        private void RenderTimer_Tick(object sender, EventArgs e)
        {
            float dt = (float)_frameStopwatch.Elapsed.TotalSeconds;
            _frameStopwatch.Restart();

            radarAngle = (radarAngle + (120f * dt)) % 360;

            for (int i = activeMissiles.Count - 1; i >= 0; i--)
            {
                var m = activeMissiles[i];
                m.Progress += m.Speed * dt;
                if (m.Progress >= 1.0f)
                {
                    m.Progress = 1.0f;
                    Action impact = m.OnImpact;
                    activeMissiles.RemoveAt(i);
                    impact?.Invoke();
                }
            }

            for (int i = activeExplosions.Count - 1; i >= 0; i--)
            {
                var exp = activeExplosions[i];
                exp.Progress += 1.2f * dt;
                exp.TextProgress += 0.25f * dt;
                if (exp.Progress >= 1.0f && exp.TextProgress >= 1.0f)
                    activeExplosions.RemoveAt(i);
            }

            mapPanel.Invalidate();
        }

        private void MapPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = mapPanel.Width, h = mapPanel.Height;

            // 1. Terminal Green Tint
            using (var tintBrush = new SolidBrush(Color.FromArgb(100, 5, 25, 5)))
            {
                g.FillRectangle(tintBrush, 0, 0, w, h);
            }

            // 2. STATIC GRID (Lat/Lng mapped so it sticks and zooms with the world map)
            using (var gridPen = new Pen(Color.FromArgb(20, 0, 255, 0), 1))
            {
                // Draw Latitude lines (Horizontal)
                for (int lat = -85; lat <= 85; lat += 10)
                {
                    PointF left = ToScreenPoint(new PointLatLng(lat, -360));
                    PointF right = ToScreenPoint(new PointLatLng(lat, 360));
                    g.DrawLine(gridPen, left, right);
                }

                // Draw Longitude lines (Vertical)
                for (int lng = -360; lng <= 360; lng += 10)
                {
                    PointF top = ToScreenPoint(new PointLatLng(85, lng));
                    PointF bottom = ToScreenPoint(new PointLatLng(-85, lng));
                    g.DrawLine(gridPen, top, bottom);
                }
            }

            // Subtly Cache the coordinates so we don't recalculate math later
            _currentScreenCoords.Clear();
            foreach (var n in GameEngine.Nations.Values)
                _currentScreenCoords[n.Name] = ToScreenPoint(new PointLatLng(n.MapY, n.MapX));

            PointF pSc = ToScreenPoint(new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX));

            // 3. Draw Connections
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var allyPen = new Pen(Color.FromArgb(120, cyanText), 1.5f) { DashStyle = DashStyle.Solid })
            using (var enAllyPen = new Pen(Color.FromArgb(50, Color.LimeGreen), 1f) { DashStyle = DashStyle.Dash })
            {
                foreach (string a in GameEngine.Player.Allies)
                    if (_currentScreenCoords.TryGetValue(a, out PointF anSc))
                        g.DrawLine(allyPen, pSc, anSc);

                foreach (var n in GameEngine.Nations.Values)
                {
                    PointF nSc = _currentScreenCoords[n.Name];
                    foreach (var a in n.Allies)
                        if (_currentScreenCoords.TryGetValue(a, out PointF aaSc))
                            g.DrawLine(enAllyPen, nSc, aaSc);
                }
            }

            Font nodeFont = new Font("Consolas", 8F, FontStyle.Bold);
            float baseNodeRadius = Math.Max(3f, (float)(mapPanel.Zoom * 1.5)); // DYNAMIC SCALING

            using (SolidBrush textBg = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
            using (SolidBrush textFg = new SolidBrush(Color.White))
            using (SolidBrush nodeBrush = new SolidBrush(Color.LimeGreen))
            using (Pen selectPen = new Pen(Color.Yellow, 2))
            {
                // Player Base
                nodeBrush.Color = cyanText;
                g.FillRectangle(nodeBrush, pSc.X - baseNodeRadius, pSc.Y - baseNodeRadius, baseNodeRadius * 2, baseNodeRadius * 2);
                string baseLabel = $"BUNKER 67 ({GameEngine.Player.NationName})";
                SizeF bs = g.MeasureString(baseLabel, nodeFont);
                g.FillRectangle(textBg, pSc.X + baseNodeRadius + 3, pSc.Y - 8, bs.Width, bs.Height);
                textFg.Color = cyanText;
                g.DrawString(baseLabel, nodeFont, textFg, pSc.X + baseNodeRadius + 3, pSc.Y - 8);

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
                    nodeBrush.Color = mc;
                    g.FillPolygon(nodeBrush, diamond);
                    string mpLabel = $"[{mp.Name.ToUpper()}]";
                    SizeF ms2 = g.MeasureString(mpLabel, nodeFont);
                    g.FillRectangle(textBg, mpSc.X + baseNodeRadius + 5, mpSc.Y - 8, ms2.Width, ms2.Height);
                    textFg.Color = mc;
                    g.DrawString(mpLabel, nodeFont, textFg, mpSc.X + baseNodeRadius + 5, mpSc.Y - 8);
                }

                // AI Nodes - Labels ALWAYS show now
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
                    nodeBrush.Color = nc;
                    g.FillEllipse(nodeBrush, sc.X - r, sc.Y - r, r * 2, r * 2);

                    SizeF ts = g.MeasureString(n.Name.ToUpper(), nodeFont);
                    g.FillRectangle(textBg, sc.X + r + 3, sc.Y - 8, ts.Width, ts.Height);
                    textFg.Color = nc;
                    g.DrawString(n.Name.ToUpper(), nodeFont, textFg, sc.X + r + 3, sc.Y - 8);

                    if (n.Name == selectedTarget)
                    {
                        g.DrawEllipse(selectPen, sc.X - (r * 2), sc.Y - (r * 2), r * 4, r * 4);
                    }
                }
            }

            // ── MISSILES (Optimized Continuous Line) ──────────────────────────────
            foreach (var m in activeMissiles)
            {
                PointF startSc = ToScreenPoint(m.Start);
                PointF endSc = ToScreenPoint(m.End);
                PointF ctrl = GetArcControl(startSc, endSc);

                int steps = 25;
                int headStep = Math.Max(1, (int)(m.Progress * steps));

                var trailPts = new List<PointF>();
                for (int i = 0; i <= headStep; i++)
                    trailPts.Add(BezierPoint(startSc, ctrl, endSc, (float)i / steps));

                if (trailPts.Count > 1)
                {
                    using (var trailPen = new Pen(Color.FromArgb(180, m.MissileColor), 2.5f))
                        g.DrawLines(trailPen, trailPts.ToArray());
                }

                PointF head = BezierPoint(startSc, ctrl, endSc, m.Progress);
                using (var gb = new SolidBrush(Color.FromArgb(150, m.MissileColor)))
                    g.FillEllipse(gb, head.X - 6, head.Y - 6, 12, 12);
                using (var hb = new SolidBrush(Color.White))
                    g.FillEllipse(hb, head.X - 2, head.Y - 2, 4, 4);
            }

            // ── EXPLOSIONS ─────────────────────────────────────────────────────────
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
                    using (var fb = new SolidBrush(Color.FromArgb(alpha / 3, inner)))
                        g.FillEllipse(fb, expSc.X - radius, expSc.Y - radius, radius * 2, radius * 2);
                    using (var rp = new Pen(Color.FromArgb(alpha, outer), 2.5f))
                        g.DrawEllipse(rp, expSc.X - radius, expSc.Y - radius, radius * 2, radius * 2);
                }

                if (exp.DamageLines != null && exp.TextProgress < 1.0f)
                {
                    float tt = exp.TextProgress;
                    int ta = tt < 0.15f ? (int)(tt / 0.15f * 235) : tt < 0.80f ? 235 : (int)((1f - tt) / 0.20f * 235);
                    ta = Math.Clamp(ta, 0, 235);

                    float tx2 = expSc.X + exp.MaxRadius + 8, ty2 = expSc.Y - 16;
                    using (var font = new Font("Consolas", 8f, FontStyle.Bold))
                    using (var txtBg = new SolidBrush(Color.FromArgb(Math.Min(ta + 40, 210), 0, 0, 0)))
                    using (var txtFg = new SolidBrush(exp.IsPlayerTarget ? Color.FromArgb(ta, redText) : Color.FromArgb(ta, amberText)))
                    {
                        foreach (string line in exp.DamageLines.Where(l => !string.IsNullOrWhiteSpace(l)))
                        {
                            SizeF sz = g.MeasureString(line, font);
                            g.FillRectangle(txtBg, tx2 - 2, ty2 - 1, sz.Width + 4, sz.Height + 2);
                            g.DrawString(line, font, txtFg, tx2, ty2);
                            ty2 += sz.Height + 3;
                        }
                    }
                }
            }

            // Radar sweep
            float cx = w / 2f, cy = h / 2f, rr = Math.Max(w, h);
            float ex = cx + (float)(Math.Cos(radarAngle * Math.PI / 180) * rr);
            float ey = cy + (float)(Math.Sin(radarAngle * Math.PI / 180) * rr);
            using (var rp = new Pen(Color.FromArgb(80, 57, 255, 20), 2))
                g.DrawLine(rp, cx, cy, ex, ey);

            // Hint overlay 
            string hint = $"Zoom: {mapPanel.Zoom}x  │  Right-drag: pan  │  Double-click: reset";
            using var hf = new Font("Consolas", 8F);
            SizeF hs = g.MeasureString(hint, hf);
            g.FillRectangle(new SolidBrush(Color.FromArgb(140, 0, 0, 0)), 4, mapPanel.Height - hs.Height - 4, hs.Width + 4, hs.Height + 2);
            g.DrawString(hint, hf, new SolidBrush(Color.FromArgb(160, 57, 255, 20)), 6, mapPanel.Height - hs.Height - 3);
        }

        // ── Controls ─────────────────────────────────────────────────────────────────
        private void MapPanel_MouseMove(object sender, MouseEventArgs e)
        {
            float hitRadius = 16f;
            hoveredTarget = "";
            foreach (var kvp in _currentScreenCoords)
            {
                float dx = kvp.Value.X - e.X, dy = kvp.Value.Y - e.Y;
                if (dx * dx + dy * dy < hitRadius * hitRadius) { hoveredTarget = kvp.Key; break; }
            }
        }

        private void MapPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && hoveredTarget != "")
            { selectedTarget = hoveredTarget; UpdateProfile(); }
        }

        private void ResetView()
        {
            mapPanel.Position = new PointLatLng(20, 0);
            mapPanel.Zoom = 2; // Fixed to respect the newly clamped MinZoom
        }

        // ── UI Logic ───────────────────────────────────────────────────────────────
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

            btnLaunch.Enabled = !target.IsDefeated && !activeMissiles.Any(m => m.IsPlayerMissile);
            btnSendTroops.Enabled = target.IsDefeated && !target.IsLooted && !GameEngine.ActiveMissions.Any(m => m.TargetNation == target.Name);
        }

        private void RefreshData()
        {
            string pa = GameEngine.Player.Allies.Count > 0 ? string.Join(", ", GameEngine.Player.Allies) : "None";
            lblPlayerStats.Text = $"YOUR NATION: {GameEngine.Player.NationName}\nYOUR ALLIES: {pa}\nPOPULATION:  {GameEngine.Player.Population:N0}\nTREASURY:    ${GameEngine.Player.Money:N0}M\nDEFENSES:    Dome L{GameEngine.Player.IronDomeLevel} | Bunk L{GameEngine.Player.BunkerLevel} | Vac L{GameEngine.Player.VaccineLevel}";

            int wi = cmbWeapon.SelectedIndex;
            cmbWeapon.Items.Clear();
            cmbWeapon.Items.Add($"Standard Nuke ({GameEngine.Player.StandardNukes})");
            cmbWeapon.Items.Add($"Tsar Bomba ({GameEngine.Player.MegaNukes})");
            cmbWeapon.Items.Add($"Bio-Plague ({GameEngine.Player.BioPlagues})");
            cmbWeapon.Items.Add($"Orbital Laser ({GameEngine.Player.OrbitalLasers})");
            cmbWeapon.SelectedIndex = wi >= 0 ? wi : 0;
            UpdateProfile();
        }

        // ── Player Strike ────────────────────────────────────────────────────────────
        private void BtnLaunch_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedTarget) || cmbWeapon.SelectedItem == null) return;
            int weaponIndex = cmbWeapon.SelectedIndex;

            if (weaponIndex == 0 && GameEngine.Player.StandardNukes <= 0) { MessageBox.Show("Out of Standard Nukes!"); return; }
            if (weaponIndex == 1 && GameEngine.Player.MegaNukes <= 0) { MessageBox.Show("Out of Tsar Bombas!"); return; }
            if (weaponIndex == 2 && GameEngine.Player.BioPlagues <= 0) { MessageBox.Show("Out of Bio-Plagues!"); return; }
            if (weaponIndex == 3 && GameEngine.Player.OrbitalLasers <= 0) { MessageBox.Show("Out of Orbital Lasers!"); return; }

            if (weaponIndex == 0) GameEngine.Player.StandardNukes--;
            if (weaponIndex == 1) GameEngine.Player.MegaNukes--;
            if (weaponIndex == 2) GameEngine.Player.BioPlagues--;
            if (weaponIndex == 3) GameEngine.Player.OrbitalLasers--;

            btnLaunch.Enabled = false;

            Nation target = GameEngine.Nations[selectedTarget];
            PointLatLng startPt = new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);
            PointLatLng impactPt = new PointLatLng(target.MapY, target.MapX);

            string[] wNames = { "STANDARD NUKE", "TSAR BOMBA", "BIO-PLAGUE CANISTER", "ORBITAL LASER" };
            Color[] wColors = { Color.OrangeRed, Color.DeepPink, Color.LimeGreen, Color.Cyan };
            float[] wRadii = { 45f, 70f, 55f, 40f };

            logBox.SelectionColor = amberText;
            LogMsg($"[LAUNCH] {wNames[weaponIndex]} launched toward {selectedTarget.ToUpper()}! Tracking projectile...");

            string tc = selectedTarget; int wc = weaponIndex; float rc = wRadii[weaponIndex];

            if (_isMultiplayer && _mpClient != null)
                _ = _mpClient.SendGameActionAsync(new { type = "strike", target = tc, weapon = wc, playerNation = GameEngine.Player.NationName });

            activeMissiles.Add(new MissileAnimation
            {
                Start = startPt,
                End = impactPt,
                IsPlayerMissile = true,
                MissileColor = wColors[weaponIndex],
                Speed = 0.4f,
                OnImpact = async () => await HandlePlayerStrikeImpact(tc, wc, impactPt, rc)
            });
        }

        private async Task HandlePlayerStrikeImpact(string targetName, int weaponIndex, PointLatLng impactPos, float blastRadius)
        {
            StrikeResult result = CombatEngine.ExecuteCombatTurn(targetName, weaponIndex);

            string impactLine = result.Logs.FirstOrDefault(l => l.Contains("[IMPACT]")) ?? "";
            string resultLine = result.Logs.FirstOrDefault(l => l.Contains("SURRENDER") || l.Contains("VICTORY") || l.Contains("SUCCESS")) ?? "";

            activeExplosions.Add(new ExplosionEffect { Center = impactPos, MaxRadius = blastRadius, DamageLines = new[] { impactLine, resultLine }, IsPlayerTarget = false });

            foreach (var l in result.Logs)
            {
                logBox.SelectionColor = l.Contains("WARNING") || l.Contains("CATASTROPHE") ? redText : l.Contains("SURRENDER") || l.Contains("SUCCESS") || l.Contains("VICTORY") ? amberText : l.Contains("ALLY") ? cyanText : greenText;
                LogMsg(l); await Task.Delay(500);
            }
            RefreshData();

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

            foreach (string retaliatorName in result.Retaliators)
            {
                if (GameEngine.Nations.TryGetValue(retaliatorName, out Nation eNat))
                {
                    await Task.Delay(600);
                    LaunchEnemyMissile(eNat);
                }
            }
        }

        // ── Ally Support Missiles ────────────────────────────────────────────────────
        private void LaunchAllyMissile(Nation ally, string targetName, long damage, PointLatLng fallbackImpact)
        {
            PointLatLng startPt = new PointLatLng(ally.MapY, ally.MapX);
            PointLatLng impactPt = GameEngine.Nations.TryGetValue(targetName, out Nation tn) ? new PointLatLng(tn.MapY, tn.MapX) : fallbackImpact;

            activeMissiles.Add(new MissileAnimation
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

            activeExplosions.Add(new ExplosionEffect { Center = impactPos, MaxRadius = 35f, DamageLines = new[] { $"[ALLY IMPACT] +{actualDmg:N0} casualties" }, IsPlayerTarget = false });

            logBox.SelectionColor = cyanText;
            LogMsg($"[ALLY IMPACT] {allyName.ToUpper()} support strike caused an additional {actualDmg:N0} casualties.");
            RefreshData();
        }

        // ── Enemy Missiles (hostile → player) ───────────────────────────────────────
        private void LaunchEnemyMissile(Nation attacker)
        {
            PointLatLng startPt = new PointLatLng(attacker.MapY, attacker.MapX);
            PointLatLng impactPt = new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);

            logBox.SelectionColor = redText;
            LogMsg($"[WARNING] ⚠ RADAR ALERT: {attacker.Name.ToUpper()} HAS LAUNCHED AN ICBM! BRACE FOR IMPACT! ⚠");

            activeMissiles.Add(new MissileAnimation
            {
                Start = startPt,
                End = impactPt,
                IsPlayerMissile = false,
                MissileColor = Color.Red,
                Speed = 0.35f,
                OnImpact = async () => await HandleEnemyStrikeImpact(attacker.Name, impactPt)
            });
        }

        private async Task HandleEnemyStrikeImpact(string attackerName, PointLatLng impactPos)
        {
            var logs = CombatEngine.ExecuteEnemyStrike(attackerName);
            if (logs.Count == 0) return;

            string casualtyLine = logs.FirstOrDefault(l => l.Contains("CASUALTY")) ?? "";
            activeExplosions.Add(new ExplosionEffect { Center = impactPos, MaxRadius = 55f, DamageLines = new[] { $"STRIKE FROM {attackerName.ToUpper()}", casualtyLine }, IsPlayerTarget = true });

            foreach (var l in logs)
            {
                logBox.SelectionColor = l.Contains("CATASTROPHE") || l.Contains("WARNING") ? redText : l.Contains("DEFENSE") ? cyanText : greenText;
                LogMsg(l); await Task.Delay(400);
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

            PointLatLng startPt = new PointLatLng(attacker.MapY, attacker.MapX);
            PointLatLng impactPt = new PointLatLng(target.MapY, target.MapX);

            activeMissiles.Add(new MissileAnimation
            {
                Start = startPt,
                End = impactPt,
                IsPlayerMissile = false,
                MissileColor = Color.Orange,
                Speed = 0.4f,
                OnImpact = async () => await HandleNationVsNationImpact(attacker.Name, target.Name, impactPt, allyUnderAttack)
            });
        }

        private async Task HandleNationVsNationImpact(string attackerName, string targetName, PointLatLng impactPos, bool allyUnderAttack)
        {
            if (!GameEngine.Nations.TryGetValue(attackerName, out Nation attacker)) return;
            if (!GameEngine.Nations.TryGetValue(targetName, out Nation target)) return;

            long damage = (long)(target.MaxPopulation * (0.04 + rng.NextDouble() * 0.14));
            damage = Math.Min(damage, target.Population);
            target.Population -= damage;
            target.AngerLevel = Math.Min(10, target.AngerLevel + 1);

            activeExplosions.Add(new ExplosionEffect
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
                target.IsDefeated = true;
                logBox.SelectionColor = amberText;
                LogMsg($"[WORLD EVENT] {targetName.ToUpper()} has been annihilated by {attackerName.ToUpper()}!");
            }

            RefreshData();

            if (!target.IsDefeated && target.Nukes > 0 && rng.NextDouble() < 0.65)
            {
                await Task.Delay(900);
                if (!target.IsDefeated && !attacker.IsDefeated) LaunchNationVsNationMissile(target, attacker);
            }

            foreach (string allyName in target.Allies)
            {
                if (!GameEngine.Nations.TryGetValue(allyName, out Nation targetAlly)) continue;
                if (targetAlly.IsDefeated || targetAlly.Nukes <= 0 || rng.NextDouble() >= 0.45) continue;

                await Task.Delay(700 + rng.Next(500));
                if (!targetAlly.IsDefeated && !attacker.IsDefeated)
                {
                    logBox.SelectionColor = allyUnderAttack ? redText : amberText;
                    LogMsg($"[ALLIANCE] {allyName.ToUpper()} enters the war defending {targetName.ToUpper()}!");
                    LaunchNationVsNationMissile(targetAlly, attacker);
                }
            }

            foreach (string allyName in attacker.Allies)
            {
                if (!GameEngine.Nations.TryGetValue(allyName, out Nation attackerAlly)) continue;
                if (attackerAlly.IsDefeated || attackerAlly.Nukes <= 0 || rng.NextDouble() >= 0.30) continue;

                await Task.Delay(700 + rng.Next(500));
                if (!attackerAlly.IsDefeated && !target.IsDefeated)
                {
                    logBox.SelectionColor = amberText;
                    LogMsg($"[ALLIANCE] {allyName.ToUpper()} joins {attackerName.ToUpper()}'s offensive against {targetName.ToUpper()}!");
                    LaunchNationVsNationMissile(attackerAlly, target);
                }
            }

            if (allyUnderAttack && !attacker.IsDefeated)
            {
                var choice = MessageBox.Show(
                    $"YOUR ALLY {targetName.ToUpper()} WAS ATTACKED BY {attackerName.ToUpper()}!\n\nDo you want to declare war and retaliate against {attackerName.ToUpper()}?",
                    "⚠ ALLIANCE TRIGGERED — DECISION REQUIRED", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

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
            playerAttackTick++;
            if (playerAttackTick >= 12)
            {
                playerAttackTick = 0;
                var hostiles = GameEngine.Nations.Values.Where(n => !n.IsDefeated && n.IsHostileToPlayer && n.Nukes > 0 && !n.IsHumanControlled).ToList();
                foreach (var a in hostiles)
                {
                    if (rng.NextDouble() < 0.12 + a.Difficulty * 0.04)
                    {
                        int salvo = Math.Min(1 + rng.Next(0, a.AngerLevel / 3 + 1), a.Nukes);
                        for (int s = 0; s < salvo; s++) LaunchEnemyMissile(a);
                        break;
                    }
                }
            }

            worldWarTick++;
            if (worldWarTick >= 18)
            {
                worldWarTick = 0;
                if (rng.NextDouble() > 0.30) return;

                var attackers = GameEngine.Nations.Values.Where(n => !n.IsDefeated && n.Nukes > 0 && !n.IsHumanControlled).ToList();
                if (attackers.Count == 0) return;

                Nation attacker = attackers[rng.Next(attackers.Count)];
                var targetPool = GameEngine.Nations.Values.Where(n => !n.IsDefeated && n.Name != attacker.Name && !attacker.Allies.Contains(n.Name)).ToList();

                bool canHitPlayer = !GameEngine.Player.Allies.Contains(attacker.Name);
                if (targetPool.Count == 0 && !canHitPlayer) return;

                double playerChance = attacker.IsHostileToPlayer ? 0.30 : 0.08;
                bool hitPlayer = canHitPlayer && rng.NextDouble() < playerChance;

                int nvnSalvo = Math.Min(1 + rng.Next(0, attacker.AngerLevel / 4 + 1), attacker.Nukes);
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
            GameEngine.ActiveMissions.Add(new TroopMission { TargetNation = selectedTarget, TroopFraction = fraction, TroopCount = troopCount, TimeRemainingSeconds = etaSeconds });

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

            angerDecayTick++;
            if (angerDecayTick >= 30)
            {
                angerDecayTick = 0;
                foreach (var n in GameEngine.Nations.Values)
                    if (n.AngerLevel > 0) n.AngerLevel--;
            }

            TryTriggerWorldEvents();
        }

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