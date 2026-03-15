using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
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
        public SizeF[]? CachedLineSizes { get; set; }
    }

    public class GameNotification
    {
        public string Message { get; set; } = "";
        public string SubMessage { get; set; } = "";
        public Color Color { get; set; } = Color.LimeGreen;
        public float Lifetime { get; set; } = 5f; // Seconds
        public float MaxLifetime { get; set; } = 5f;
        public float SlideProgress { get; set; } = 0f; // For entry animation
    }

    public class RadarPanel : GMapControl
    {
        public RadarPanel()
        {
            this.SetStyle(
                ControlStyles.DoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Selectable,
                true);
            this.UpdateStyles();
        }
    }

    public partial class ControlPanelForm : Form
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
        private Button btnLaunch, btnSendTroops, btnOpenShop, btnMultiStrike, btnDiplomacy, btnBetray;
        private RichTextBox logBox;
        private TrackBar sliderSalvo;
        private Label lblSalvo;

        // ── Strike limits ─────────────────────────────────────────────────────
        private const int MaxNukesPerVolley = 5;
        private const int LaunchCooldownSeconds = 15;
        private DateTime _lastLaunchTime = DateTime.MinValue;

        private System.Windows.Forms.Timer gameTimer;

        // ── Render thread ─────────────────────────────────────────────────────
        private Thread? _renderThread;
        private volatile bool _renderRunning = true;
        private readonly object _animLock = new object();
        private readonly CameraShake _cameraShake = new CameraShake();

        private float radarAngle = 0;
        private string selectedTarget = "";
        private string hoveredTarget = "";

        // Cache coordinates per frame so we aren't doing heavy map math on mouse moves
        private Dictionary<string, PointF> _currentScreenCoords = new Dictionary<string, PointF>();

        private List<MissileAnimation> activeMissiles = new List<MissileAnimation>();
        private List<ExplosionEffect> activeExplosions = new List<ExplosionEffect>();
        private List<GameNotification> activeNotifications = new List<GameNotification>();
        
        private DateTime _lastInboundTime = DateTime.MinValue;
        private float _strikeWarningAnim = 0f; // 0 to 1 for fade/scale
        private float _strikeWarningTimer = 0f; // countdown
        private float _strikeWarningRotation = 0f;
        
        private static Random rng = new Random();

        // ── Cached GDI+ objects (created once, disposed on form close) ────────
        private Pen _gridPen = new Pen(Color.FromArgb(20, 0, 255, 0), 1);
        private SolidBrush _tintBrush = new SolidBrush(Color.FromArgb(100, 5, 25, 5));
        private Pen _allyPen = new Pen(Color.FromArgb(120, Color.Cyan), 1.5f);
        private Pen _enAllyPen = new Pen(Color.FromArgb(50, Color.LimeGreen), 1f) { DashStyle = DashStyle.Dash };
        private Font _nodeFont = new Font("Consolas", 8F, FontStyle.Bold);
        private Font _explosionFont = new Font("Consolas", 8f, FontStyle.Bold);
        private Font _hintFont = new Font("Consolas", 8F);
        private SolidBrush _textBgBrush = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
        private SolidBrush _textFgBrush = new SolidBrush(Color.White);
        private SolidBrush _nodeBrush = new SolidBrush(Color.LimeGreen);
        private Pen _selectPen = new Pen(Color.Yellow, 2);
        private Pen _radarPen = new Pen(Color.FromArgb(80, 57, 255, 20), 2);
        private SolidBrush _hintBgBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
        private SolidBrush _hintFgBrush = new SolidBrush(Color.FromArgb(160, 57, 255, 20));
        private Pen _trailPen = new Pen(Color.White, 2.5f);
        private SolidBrush _glowBrush = new SolidBrush(Color.White);
        private SolidBrush _headBrush = new SolidBrush(Color.White);
        private SolidBrush _expFillBrush = new SolidBrush(Color.White);
        private Pen _expRingPen = new Pen(Color.White, 2.5f);
        private SolidBrush _expTextBgBrush = new SolidBrush(Color.Black);
        private SolidBrush _expTextFgBrush = new SolidBrush(Color.White);

        private Font _notifyFont = new Font("Consolas", 10F, FontStyle.Bold);
        private Font _notifySubFont = new Font("Consolas", 8F);
        private Font _warningFont = new Font("Consolas", 24F, FontStyle.Bold);
        private Font _warningSubFont = new Font("Consolas", 12F, FontStyle.Bold);

        // ── Plane rendering (diplomacy summits) ─────────────────────────────────
        private SolidBrush _planeBrush = new SolidBrush(Color.White);
        private Font _planeFont = new Font("Consolas", 7F, FontStyle.Bold);

        // ── Pre-allocated trail point arrays (zero-alloc per frame) ───────────
        private const int TrailSteps = 40;
        private PointF[][] _trailArrays;

        // ── Cached hint MeasureString ─────────────────────────────────────────
        private string _cachedHintText = "";
        private SizeF _cachedHintSize;

        // ── Game state machine ───────────────────────────────────────────────
        private enum GameState { Playing, IronDomeMinigame, PlaneIntercept, CyberOpsMinigame }
        private volatile GameState _gameState = GameState.Playing;

        // Tracks every enemy missile currently in flight toward the player
        // Key = missile object, Value = attacker name
        private readonly Dictionary<MissileAnimation, string> _inboundMissiles = new Dictionary<MissileAnimation, string>();
        // Set of missiles the player intercepted in the minigame — their OnImpact becomes a dud
        private readonly HashSet<MissileAnimation> _interceptedMissiles = new HashSet<MissileAnimation>();
        // Tracks pre-rolled damage for player-vs-player strikes (so Iron Dome pipeline can use them)
        private readonly Dictionary<MissileAnimation, long> _forcedDamageMap = new Dictionary<MissileAnimation, long>();
        private bool _minigamesEnabled = true;

        // ── Submarine State ──────────────────────────────────────────────────
        private Submarine? _selectedSub = null;
        private bool _isSelectingSubMoveTarget = false;
        private bool _isSelectingSubFireTarget = false;

        // ── Damage Summary Alert ──────────────────────────────────────────────
        private float _damageAlertTimer = 0f;
        private float _damageAlertAnim = 0f;
        private long _cumulativeDamageThisWave = 0;
        private long _popAtStartOfWave = 0;
        private bool _isDamageAlertActive = false;
        
        // ── Multiplayer state ────────────────────────────────────────────────
        private MultiplayerClient? _mpClient;
        private List<MpPlayer> _mpPlayers = new();
        private bool _isMultiplayer = false;
        public bool IsMultiplayer => _isMultiplayer;
        private string _serverUrl = "https://fallout67.imperiuminteractive.workers.dev";

        private int playerAttackTick = 0;
        private int worldWarTick = 0;
        private int angerDecayTick = 0;

        // ── Scoring ───────────────────────────────────────────────────────────
        private Stopwatch _gameTimer = new Stopwatch();

        public ControlPanelForm(string serverUrl = "https://fallout67.imperiuminteractive.workers.dev", bool minigamesEnabled = true)
        {
            _serverUrl       = serverUrl;
            _minigamesEnabled = minigamesEnabled;
            InitForm();
        }

        public ControlPanelForm(MultiplayerClient mpClient, List<MpPlayer> mpPlayers, string serverUrl = "https://fallout67.imperiuminteractive.workers.dev", bool minigamesEnabled = true)
        {
            _mpClient         = mpClient;
            _mpPlayers        = mpPlayers;
            _isMultiplayer    = true;
            _serverUrl        = serverUrl;
            // Iron Dome is local-only defense — damage is pre-rolled, so it won't desync
            _minigamesEnabled = minigamesEnabled;

            // Mark all human-controlled nations (both local player and remote players)
            if (GameEngine.Nations.TryGetValue(GameEngine.Player.NationName, out var localNation))
                localNation.IsHumanControlled = true;

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
            SetupCyberOps();
            SetupStrategicUI();
            RefreshData();

            gameTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            gameTimer.Tick += GameTimer_Tick;
            gameTimer.Start();
            _gameTimer.Start();

            // Pre-allocate trail point arrays (zero-alloc rendering)
            _trailArrays = new PointF[TrailSteps + 2][];
            for (int i = 0; i < _trailArrays.Length; i++)
                _trailArrays[i] = new PointF[i];

            // Dedicated render thread for ultra-smooth 60 FPS
            _renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "RenderLoop",
                Priority = ThreadPriority.AboveNormal
            };
            _renderThread.Start();
        }

        // Hardcoded real-world locations so cities spawn in the correct geographic locations
        private void CorrectCountryCoordinates()
        {
            var coords = CountryCoordinates.All;
            foreach (var nation in GameEngine.Nations.Values)
            {
                if (coords.TryGetValue(nation.Name, out PointLatLng c))
                {
                    nation.MapX = (float)c.Lng;
                    nation.MapY = (float)c.Lat;
                }
            }

            if (coords.TryGetValue(GameEngine.Player.NationName, out PointLatLng pCoords))
            {
                GameEngine.Player.MapX = (float)pCoords.Lng;
                GameEngine.Player.MapY = (float)pCoords.Lat;
            }
        }

        private void SetupUI()
        {
            this.Text = $"Mutually Assured Destruction - {GameEngine.Player.NationName}";
            this.Size = new Size(1300, 900);
            this.BackColor = bgDark;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.Shift && e.KeyCode == Keys.F1) new DevPanelForm().Show(); };

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

            GroupBox grpProfile = CreateBox("TARGET SENSORS", 820, 10, 450, 255);
            lblProfile = new Label { Location = new Point(10, 25), Size = new Size(430, 220), ForeColor = amberText, Font = stdFont };
            grpProfile.Controls.Add(lblProfile);
            this.Controls.Add(grpProfile);

            GroupBox grpOps = CreateBox("WEAPONS CONTROL", 820, 275, 450, 220);
            cmbWeapon = new ComboBox { Location = new Point(10, 30), Size = new Size(210, 30), Font = stdFont, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.Black, ForeColor = greenText };
            btnLaunch = CreateButton("EXECUTE STRIKE", 230, 25, 210, 35, Color.DarkRed, Color.White);

            lblSalvo = new Label { Location = new Point(10, 70), Size = new Size(430, 18), ForeColor = amberText, Font = new Font("Consolas", 9F, FontStyle.Bold), Text = "SALVO: 1" };
            sliderSalvo = new TrackBar
            {
                Location = new Point(10, 88),
                Size = new Size(430, 35),
                Minimum = 1, Maximum = 1, Value = 1,
                TickFrequency = 1,
                BackColor = bgDark,
            };
            sliderSalvo.ValueChanged += (s, e) => lblSalvo.Text = $"SALVO: {sliderSalvo.Value}";
            cmbWeapon.SelectedIndexChanged += (s, e) => UpdateSalvoSlider();

            btnMultiStrike = CreateButton("PLAN MULTI-TARGET STRIKE", 10, 128, 430, 35, Color.FromArgb(80, 0, 0), Color.OrangeRed);
            btnMultiStrike.Click += BtnMultiStrike_Click;

            btnSendTroops = CreateButton("DEPLOY EXTRACTION TROOPS", 10, 170, 430, 40, Color.DarkGoldenrod, Color.White);
            btnLaunch.Click += BtnLaunch_Click;
            btnSendTroops.Click += BtnSendTroops_Click;
            grpOps.Controls.Add(cmbWeapon); grpOps.Controls.Add(btnLaunch);
            grpOps.Controls.Add(lblSalvo); grpOps.Controls.Add(sliderSalvo);
            grpOps.Controls.Add(btnMultiStrike);
            grpOps.Controls.Add(btnSendTroops);
            this.Controls.Add(grpOps);

            GroupBox grpPlayer = CreateBox("BUNKER STATUS", 820, 505, 450, 165);
            lblPlayerStats = new Label { Location = new Point(10, 25), Size = new Size(300, 120), ForeColor = greenText, Font = stdFont };
            btnOpenShop = CreateButton("BLACK\nMARKET", 320, 25, 120, 40, Color.Black, cyanText);
            btnOpenShop.Click += (s, e) => { var shop = new ShopForm(); shop.ShowDialog(this); RefreshData(); };
            var btnLeaderboard = CreateButton("LEADER\nBOARD", 320, 70, 120, 40, Color.Black, amberText);
            btnLeaderboard.Click += async (s, e) =>
            {
                var lbf = new LeaderboardForm(GetServerBaseUrl());
                lbf.Show(this);
                await lbf.LoadAsync();
            };
            var btnRestoreSat = CreateButton("RESTORE\nSATS ($2B)", 320, 115, 120, 40, Color.FromArgb(30, 0, 60), Color.Violet);
            btnRestoreSat.Click += (s, e) => TryRestorePlayerSatellites();
            grpPlayer.Controls.Add(lblPlayerStats); grpPlayer.Controls.Add(btnOpenShop);
            grpPlayer.Controls.Add(btnLeaderboard); grpPlayer.Controls.Add(btnRestoreSat);
            this.Controls.Add(grpPlayer);

            // ── Diplomacy Controls ──────────────────────────────────────────
            GroupBox grpDiplomacy = CreateBox("DIPLOMACY & CYBER OPS", 10, 465, 800, 55);
            btnDiplomacy = CreateButton("PROPOSE ALLIANCE", 10, 18, 160, 30, Color.FromArgb(0, 50, 70), cyanText);
            btnDiplomacy.Click += BtnDiplomacy_Click;
            btnBetray = CreateButton("BETRAY ALLY", 180, 18, 130, 30, Color.FromArgb(70, 0, 0), Color.OrangeRed);
            btnBetray.Click += (s, e) => { if (!string.IsNullOrEmpty(selectedTarget) && GameEngine.Player.Allies.Contains(selectedTarget)) PlayerBetrayAlly(selectedTarget); else MessageBox.Show("Select an allied nation to betray.", "NO ALLY SELECTED"); };
            btnHack = CreateButton("⚡ HACK NETWORK", 330, 18, 160, 30, Color.FromArgb(40, 0, 60), Color.Magenta);
            btnHack.Click += BtnHack_Click;
            grpDiplomacy.Controls.Add(btnDiplomacy);
            grpDiplomacy.Controls.Add(btnBetray);
            grpDiplomacy.Controls.Add(btnHack);
            this.Controls.Add(grpDiplomacy);

            GroupBox grpLogs = CreateBox("🔴 LIVE TACTICAL COMMENTARY", 10, 680, 1260, 185);
            logBox = new RichTextBox { Location = new Point(10, 25), Size = new Size(1240, 150), BackColor = Color.Black, ForeColor = greenText, Font = new Font("Consolas", 14F, FontStyle.Bold), ReadOnly = true, BorderStyle = BorderStyle.None };
            grpLogs.Controls.Add(logBox);
            this.Controls.Add(grpLogs);

            LogMsg("[SYSTEM BOOT] Satellite Uplink Established.");
            LogMsg("[SYSTEM BOOT] Awaiting target coordinates from Commander.");
        }

        public void EnterSubMoveMode(Submarine sub)
        {
            _selectedSub = sub;
            _isSelectingSubMoveTarget = true;
            _isSelectingSubFireTarget = false;
            LogMsg($"[SYSTEM] Select destination on map for {sub.Name.ToUpper()}.");
        }

        public void EnterSubFireMode(Submarine sub)
        {
            _selectedSub = sub;
            _isSelectingSubFireTarget = true;
            _isSelectingSubMoveTarget = false;
            LogMsg($"[SYSTEM] SELECT TARGET COORDS FOR {sub.Name.ToUpper()} NUCLEAR STRIKE.");
        }

        public void BroadcastSubCreate(Submarine sub)
        {
            if (_mpClient != null)
            {
                _ = _mpClient.SendGameActionAsync(new
                {
                    type = "sub_create",
                    subId = sub.Id,
                    name = sub.Name,
                    x = sub.MapX,
                    y = sub.MapY
                });
            }
        }

        private void AddNotification(string msg, string subMsg, Color color, float duration = 5f)
        {
            if (msg.Contains("INBOUND"))
            {
                if ((DateTime.Now - _lastInboundTime).TotalSeconds > 30)
                {
                    _strikeWarningTimer = 6f; // Show for 6 seconds
                }
                _lastInboundTime = DateTime.Now;
            }

            lock (_animLock)
            {
                // SMART DEDUPLICATION: Don't add if an identical notification is already active and has high life
                if (activeNotifications.Any(n => n.Message == msg && n.SubMessage == subMsg && n.Lifetime > duration * 0.5f))
                    return;

                // Slide out old notifications if list is getting long
                if (activeNotifications.Count >= 4)
                {
                    // If we have to remove one, remove the oldest one that isn't already sliding out
                    var oldest = activeNotifications.OrderBy(n => n.Lifetime).FirstOrDefault();
                    if (oldest != null) activeNotifications.Remove(oldest);
                }

                activeNotifications.Add(new GameNotification
                {
                    Message = msg,
                    SubMessage = subMsg,
                    Color = color,
                    Lifetime = duration,
                    MaxLifetime = duration,
                    SlideProgress = 0f
                });
            }
        }

        private GroupBox CreateBox(string t, int x, int y, int w, int h) => new GroupBox { Text = t, ForeColor = greenText, Font = stdFont, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat };
        private Button CreateButton(string t, int x, int y, int w, int h, Color bg, Color fg) => new Button { Text = t, Location = new Point(x, y), Size = new Size(w, h), BackColor = bg, ForeColor = fg, Font = stdFont, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };

        // ── UI Logic ───────────────────────────────────────────────────────────────
        private void UpdateProfile()
        {
            if (string.IsNullOrEmpty(selectedTarget) || !GameEngine.Nations.ContainsKey(selectedTarget)) return;
            Nation target = GameEngine.Nations[selectedTarget];

            bool isBlind = GameEngine.Player.IsSatelliteBlind;

            string allies = target.Allies.Count > 0 ? string.Join(", ", target.Allies) : "None";
            string rel = "NEUTRAL";
            float acceptProb = DiplomacyEngine.CalculateAcceptanceProbability(target.Name);
            if (GameEngine.Player.Allies.Contains(target.Name)) rel = "YOUR ALLY (Will Assist)";
            else if (target.IsHostileToPlayer) rel = "HOSTILE (Will Retaliate!)";
            else if (acceptProb > 0.5f) rel = $"OPEN TO DIPLOMACY ({acceptProb * 100:F0}%)";
            else rel = $"NEUTRAL ({acceptProb * 100:F0}% acceptance)";

            string satLine = target.IsSatelliteBlind
                ? $"◈ SATELLITES OFFLINE ({(int)(target.SatelliteBlindUntil - DateTime.Now).TotalSeconds}s)"
                : "SATELLITES: ONLINE";

            if (isBlind)
            {
                lblProfile.Text =
                    $"TARGET LOCK: {target.Name.ToUpper()}\n" +
                    $"=================================\n" +
                    $"POPULATION: [DATA LOST]\n" +
                    $"NUKES DETECTED: [UNK]\n" +
                    $"EST. TREASURY: [UNK]\n" +
                    $"THREAT LEVEL: ???\n\n" +
                    $"ALLIANCE NETWORK: {allies}\n" +
                    $"DIPLOMATIC STATUS: {rel}\n" +
                    $"COMBAT STATUS: {(target.IsDefeated ? "DEFEATED" : "ACTIVE")}\n" +
                    satLine;
            }
            else
            {
                string resLine = StrategicEngine.GetResourceSummary(target.Resources);
                string sanctionLine = target.IsSanctioned ? " [SANCTIONED]" : "";
                float readiness = StrategicEngine.GetMilitaryReadiness(target);
                string readinessLabel = readiness > 0.8f ? "COMBAT READY" :
                                        readiness > 0.5f ? "DEGRADED" :
                                        readiness > 0.3f ? "WEAKENED" : "CRIPPLED";
                lblProfile.Text =
                    $"TARGET LOCK: {target.Name.ToUpper()}\n" +
                    $"=================================\n" +
                    $"POPULATION: {target.Population:N0}\n" +
                    $"NUKES DETECTED: {target.Nukes}\n" +
                    $"EST. TREASURY: ${target.Money}M{sanctionLine}\n" +
                    $"THREAT LEVEL: {new string('★', target.Difficulty)}\n" +
                    $"READINESS: {readiness:P0} ({readinessLabel})\n" +
                    $"RESOURCES: {resLine}\n" +
                    $"ALLIANCE NETWORK: {allies}\n" +
                    $"DIPLOMATIC STATUS: {rel}\n" +
                    $"COMBAT STATUS: {(target.IsDefeated ? "DEFEATED" : "ACTIVE")}\n" +
                    satLine;
            }

            bool hasPlayerMissile;
            lock (_animLock) hasPlayerMissile = activeMissiles.Any(m => m.IsPlayerMissile);
            
            // Cannot launch or send troops if blind
            btnLaunch.Enabled = !isBlind && !target.IsDefeated && !hasPlayerMissile;
            btnSendTroops.Enabled = !isBlind && target.IsDefeated && !target.IsLooted && !GameEngine.ActiveMissions.Any(m => m.TargetNation == target.Name);
            btnMultiStrike.Enabled = !isBlind;
        }

        public void RefreshData()
        {
            string pa = GameEngine.Player.Allies.Count > 0 ? string.Join(", ", GameEngine.Player.Allies) : "None";
            string satStatus = GameEngine.Player.IsSatelliteBlind
                ? $"JAMMED ({(int)(GameEngine.Player.SatelliteBlindUntil - DateTime.Now).TotalSeconds}s)"
                : "ONLINE";
            int activeSubs = GameEngine.Submarines.Count(s => s.OwnerId == GameEngine.Player.NationName && !s.IsDestroyed);
            string subLine = activeSubs > 0 ? $" | SUBS: {activeSubs}" : "";
            string cyberLine = GameEngine.Player.CyberOpsLevel > 0 ? $" | CYBER L{GameEngine.Player.CyberOpsLevel}" : "";
            string allyLine = $"({GameEngine.Player.Allies.Count}/{GameEngine.Player.MaxAllies})";
            lblPlayerStats.Text = $"YOUR NATION: {GameEngine.Player.NationName}\nALLIES {allyLine}: {pa}\nPOPULATION:  {GameEngine.Player.Population:N0}\nTREASURY:    ${GameEngine.Player.Money:N0}M\nDEFENSES:    Dome L{GameEngine.Player.IronDomeLevel} | Bunk L{GameEngine.Player.BunkerLevel} | Vac L{GameEngine.Player.VaccineLevel}{subLine}{cyberLine}\nSATELLITES:  {satStatus}";

            // Hack button state
            if (btnHack != null)
            {
                bool canHack = GameEngine.Player.CyberOpsLevel >= 2 && !_isHijacking && GameEngine.Player.HackCooldown <= 0 && !GameEngine.Player.IsSatelliteBlind;
                btnHack.Enabled = canHack;
                btnHack.Text = _isHijacking ? $"⚡ HIJACKING..." : GameEngine.Player.HackCooldown > 0 ? $"⚡ HACK ({GameEngine.Player.HackCooldown}s)" : "⚡ HACK NETWORK";
            }

            int wi = cmbWeapon.SelectedIndex;
            cmbWeapon.Items.Clear();
            cmbWeapon.Items.Add($"Standard Nuke ({GameEngine.Player.StandardNukes})");
            cmbWeapon.Items.Add($"Tsar Bomba ({GameEngine.Player.MegaNukes})");
            cmbWeapon.Items.Add($"Bio-Plague ({GameEngine.Player.BioPlagues})");
            cmbWeapon.Items.Add($"Orbital Laser ({GameEngine.Player.OrbitalLasers})");
            cmbWeapon.Items.Add($"Satellite Killer ({GameEngine.Player.SatelliteMissiles})");
            cmbWeapon.SelectedIndex = wi >= 0 ? wi : 0;
            UpdateSalvoSlider();
            UpdateProfile();
        }

        private void UpdateSalvoSlider()
        {
            if (sliderSalvo == null) return;
            int stock = cmbWeapon.SelectedIndex switch
            {
                0 => GameEngine.Player.StandardNukes,
                1 => GameEngine.Player.MegaNukes,
                2 => GameEngine.Player.BioPlagues,
                3 => GameEngine.Player.OrbitalLasers,
                4 => GameEngine.Player.SatelliteMissiles,
                _ => 1
            };
            int max = Math.Max(1, stock);
            sliderSalvo.Maximum = max;
            sliderSalvo.Value = Math.Min(sliderSalvo.Value, max);
            sliderSalvo.TickFrequency = Math.Max(1, max / 10);
            lblSalvo.Text = $"SALVO: {sliderSalvo.Value}";
        }

        private bool _gameOver = false;
        private void CheckGameOver()
        {
            if (_gameOver) return;

            // Last Stand: Game only ends if both population is zero AND no active submarines remain
            bool hasActiveSubs = GameEngine.Submarines.Any(s => s.OwnerId == GameEngine.Player.NationName && !s.IsDestroyed);
            
            if (GameEngine.Player.Population <= 0 && !hasActiveSubs)
            {
                _gameOver = true;
                _gameTimer.Stop();
                _ = HandleDefeatAsync();
                return;
            }

            // World domination: all nations defeated or surrendered
            if (GameEngine.Nations.Values.All(n => n.IsDefeated))
            {
                _gameOver = true;
                _gameTimer.Stop();
                _ = HandleVictoryAsync();
            }
        }

        private async Task HandleVictoryAsync()
        {
            int elapsedSeconds = (int)_gameTimer.Elapsed.TotalSeconds;
            int nukesUsed      = GameEngine.Player.NukesUsed;
            string nation      = GameEngine.Player.NationName;
            float countryMult  = GameEngine.Player.ScoreMultiplier;

            long baseScore   = 1_000_000;
            long timeBonus   = Math.Max(0, 500_000 - (elapsedSeconds * 100));
            long nukePenalty = nukesUsed * 500;
            long rawScore    = Math.Max(0, baseScore + timeBonus - nukePenalty);
            long finalScore  = (long)(rawScore * countryMult);
            string playerName = ProfileManager.HasProfile 
                ? ProfileManager.CurrentProfile.Username 
                : _mpClient?.Players.FirstOrDefault(p => p.Id == _mpClient.LocalPlayerId)?.Name ?? "COMMANDER";

            LogMsg($"[VICTORY] WORLD DOMINATION ACHIEVED! Final score: {finalScore:N0}");

            // Record match result in profile
            ProfileManager.RecordGameEnd(true, finalScore, elapsedSeconds, _isMultiplayer);
            _ = ProfileManager.SyncToServerAsync(GetServerBaseUrl());

            var form = new GameOverForm(
                victory: true,
                playerName, nation,
                finalScore, baseScore, timeBonus, nukePenalty,
                countryMult, elapsedSeconds, nukesUsed,
                GetServerBaseUrl());

            form.Load += async (s, e) => await form.LoadAsync();
            form.ShowDialog(this);
            this.Close();
        }

        private async Task HandleDefeatAsync()
        {
            int elapsedSeconds = (int)_gameTimer.Elapsed.TotalSeconds;
            string playerName = ProfileManager.HasProfile 
                ? ProfileManager.CurrentProfile.Username 
                : _mpClient?.Players.FirstOrDefault(p => p.Id == _mpClient.LocalPlayerId)?.Name ?? "COMMANDER";

            LogMsg("[DEFEAT] YOUR NATION HAS BEEN WIPED OUT.");

            // Record defeat in profile
            ProfileManager.RecordGameEnd(false, 0, elapsedSeconds, _isMultiplayer);
            _ = ProfileManager.SyncToServerAsync(GetServerBaseUrl());

            var form = new GameOverForm(
                victory: false,
                playerName, GameEngine.Player.NationName,
                finalScore: 0, baseScore: 0, timeBonus: 0, nukePenalty: 0,
                countryMult: GameEngine.Player.ScoreMultiplier,
                elapsedSeconds, nukesUsed: GameEngine.Player.NukesUsed,
                GetServerBaseUrl());

            form.Load += async (s, e) => await form.LoadAsync();
            form.ShowDialog(this);
            this.Close();
        }

        private string GetServerBaseUrl() => _serverUrl;

        private void LogMsg(string txt)
        {
            logBox.AppendText(txt + "\n");
            logBox.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _renderRunning = false;
            _renderThread?.Join(200);

            // Save all accumulated stats even if game wasn't finished
            ProfileManager.FlushSession();
            _ = ProfileManager.SyncToServerAsync(GetServerBaseUrl());

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderRunning = false;
                _gridPen?.Dispose();
                _tintBrush?.Dispose();
                _allyPen?.Dispose();
                _enAllyPen?.Dispose();
                _nodeFont?.Dispose();
                _explosionFont?.Dispose();
                _hintFont?.Dispose();
                _textBgBrush?.Dispose();
                _textFgBrush?.Dispose();
                _nodeBrush?.Dispose();
                _selectPen?.Dispose();
                _radarPen?.Dispose();
                _hintBgBrush?.Dispose();
                _hintFgBrush?.Dispose();
                _trailPen?.Dispose();
                _glowBrush?.Dispose();
                _headBrush?.Dispose();
                _expFillBrush?.Dispose();
                _expRingPen?.Dispose();
                _expTextBgBrush?.Dispose();
                _expTextFgBrush?.Dispose();
                _notifyFont?.Dispose();
                _notifySubFont?.Dispose();
                _planeBrush?.Dispose();
                _planeFont?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
