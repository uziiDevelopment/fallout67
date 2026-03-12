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
        private TrackBar sliderSalvo;
        private Label lblSalvo;

        private System.Windows.Forms.Timer gameTimer;

        // ── Render thread ─────────────────────────────────────────────────────
        private Thread? _renderThread;
        private volatile bool _renderRunning = true;
        private readonly object _animLock = new object();

        private float radarAngle = 0;
        private string selectedTarget = "";
        private string hoveredTarget = "";

        // Cache coordinates per frame so we aren't doing heavy map math on mouse moves
        private Dictionary<string, PointF> _currentScreenCoords = new Dictionary<string, PointF>();

        private List<MissileAnimation> activeMissiles = new List<MissileAnimation>();
        private List<ExplosionEffect> activeExplosions = new List<ExplosionEffect>();
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

        // ── Pre-allocated trail point arrays (zero-alloc per frame) ───────────
        private const int TrailSteps = 40;
        private PointF[][] _trailArrays;

        // ── Cached hint MeasureString ─────────────────────────────────────────
        private string _cachedHintText = "";
        private SizeF _cachedHintSize;

        // ── Game state machine ───────────────────────────────────────────────
        private enum GameState { Playing, IronDomeMinigame }
        private volatile GameState _gameState = GameState.Playing;

        // Tracks every enemy missile currently in flight toward the player
        // Key = missile object, Value = attacker name
        private readonly Dictionary<MissileAnimation, string> _inboundMissiles = new Dictionary<MissileAnimation, string>();
        // Set of missiles the player intercepted in the minigame — their OnImpact becomes a dud
        private readonly HashSet<MissileAnimation> _interceptedMissiles = new HashSet<MissileAnimation>();
        // Tracks pre-rolled damage for player-vs-player strikes (so Iron Dome pipeline can use them)
        private readonly Dictionary<MissileAnimation, long> _forcedDamageMap = new Dictionary<MissileAnimation, long>();
        private bool _minigamesEnabled = true;

        // ── Multiplayer state ────────────────────────────────────────────────
        private MultiplayerClient? _mpClient;
        private List<MpPlayer> _mpPlayers = new();
        private bool _isMultiplayer = false;
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
var accurateCoords = new Dictionary<string, PointLatLng>(StringComparer.OrdinalIgnoreCase)
            {
                // NORTH & CENTRAL AMERICA + CARIBBEAN
                { "USA", new PointLatLng(38.9, -77.0) },
                { "LITTLE SAINT JAMES", new PointLatLng(18.30, -64.82) }, // US Virgin Islands
                { "CANADA", new PointLatLng(45.4, -75.7) },
                { "MEXICO", new PointLatLng(19.4, -99.1) },
                { "CUBA", new PointLatLng(23.1, -82.3) },
                { "GUATEMALA", new PointLatLng(14.6, -90.5) },
                { "BELIZE", new PointLatLng(17.2, -88.7) },
                { "HONDURAS", new PointLatLng(14.1, -87.2) },
                { "EL SALVADOR", new PointLatLng(13.7, -89.2) },
                { "NICARAGUA", new PointLatLng(12.1, -86.2) },
                { "COSTA RICA", new PointLatLng(9.9, -84.1) },
                { "PANAMA", new PointLatLng(8.9, -79.5) },
                { "JAMAICA", new PointLatLng(18.0, -76.8) },
                { "HAITI", new PointLatLng(18.5, -72.3) },
                { "DOMINICAN REP.", new PointLatLng(18.5, -69.9) },
                { "BAHAMAS", new PointLatLng(25.0, -77.3) },
                { "TRINIDAD & TOBAGO", new PointLatLng(10.6, -61.5) },
                { "BARBADOS", new PointLatLng(13.1, -59.6) },
                { "ANTIGUA & BARBUDA", new PointLatLng(17.1, -61.8) },
                { "SAINT LUCIA", new PointLatLng(14.0, -61.0) },
                { "GRENADA", new PointLatLng(12.0, -61.7) },
                { "ST. VINCENT", new PointLatLng(13.1, -61.2) },
                { "ST. KITTS & NEVIS", new PointLatLng(17.3, -62.7) },
                { "DOMINICA", new PointLatLng(15.3, -61.4) },

                // SOUTH AMERICA
                { "BRAZIL", new PointLatLng(-15.8, -47.9) },
                { "ARGENTINA", new PointLatLng(-34.6, -58.3) },
                { "COLOMBIA", new PointLatLng(4.7, -74.0) },
                { "VENEZUELA", new PointLatLng(10.5, -66.9) },
                { "GUYANA", new PointLatLng(6.8, -58.1) },
                { "SURINAME", new PointLatLng(5.8, -55.2) },
                { "ECUADOR", new PointLatLng(-0.2, -78.5) },
                { "PERU", new PointLatLng(-12.0, -77.0) },
                { "BOLIVIA", new PointLatLng(-16.5, -68.1) },
                { "PARAGUAY", new PointLatLng(-25.3, -57.5) },
                { "CHILE", new PointLatLng(-33.4, -70.6) },
                { "URUGUAY", new PointLatLng(-34.9, -56.1) },

                // EUROPE
                { "UK", new PointLatLng(51.5, -0.1) },
                { "FRANCE", new PointLatLng(48.8, 2.3) },
                { "SPAIN", new PointLatLng(40.4, -3.7) },
                { "GERMANY", new PointLatLng(52.5, 13.4) },
                { "ITALY", new PointLatLng(41.9, 12.4) },
                { "UKRAINE", new PointLatLng(50.4, 30.5) },
                { "RUSSIA", new PointLatLng(55.7, 37.6) },
                { "PORTUGAL", new PointLatLng(38.7, -9.1) },
                { "IRELAND", new PointLatLng(53.3, -6.2) },
                { "ICELAND", new PointLatLng(64.1, -21.9) },
                { "NORWAY", new PointLatLng(59.9, 10.7) },
                { "SWEDEN", new PointLatLng(59.3, 18.0) },
                { "FINLAND", new PointLatLng(60.1, 24.9) },
                { "DENMARK", new PointLatLng(55.6, 12.5) },
                { "ESTONIA", new PointLatLng(59.4, 24.7) },
                { "LATVIA", new PointLatLng(56.9, 24.1) },
                { "LITHUANIA", new PointLatLng(54.6, 25.2) },
                { "BELARUS", new PointLatLng(53.9, 27.5) },
                { "POLAND", new PointLatLng(52.2, 21.0) },
                { "CZECHIA", new PointLatLng(50.0, 14.4) },
                { "SLOVAKIA", new PointLatLng(48.1, 17.1) },
                { "HUNGARY", new PointLatLng(47.4, 19.0) },
                { "AUSTRIA", new PointLatLng(48.2, 16.3) },
                { "SWITZERLAND", new PointLatLng(46.9, 7.4) },
                { "BELGIUM", new PointLatLng(50.8, 4.3) },
                { "NETHERLANDS", new PointLatLng(52.3, 4.8) },
                { "LUXEMBOURG", new PointLatLng(49.6, 6.1) },
                { "ROMANIA", new PointLatLng(44.4, 26.1) },
                { "MOLDOVA", new PointLatLng(47.0, 28.8) },
                { "BULGARIA", new PointLatLng(42.6, 23.3) },
                { "GREECE", new PointLatLng(37.9, 23.7) },
                { "SERBIA", new PointLatLng(44.8, 20.4) },
                { "CROATIA", new PointLatLng(45.8, 15.9) },
                { "BOSNIA & HERZ.", new PointLatLng(43.8, 18.3) },
                { "SLOVENIA", new PointLatLng(46.0, 14.5) },
                { "ALBANIA", new PointLatLng(41.3, 19.8) },
                { "NORTH MACEDONIA", new PointLatLng(41.9, 21.4) },
                { "MONTENEGRO", new PointLatLng(42.4, 19.2) },
                { "KOSOVO", new PointLatLng(42.6, 21.1) },
                { "MALTA", new PointLatLng(35.8, 14.5) },
                { "ANDORRA", new PointLatLng(42.5, 1.5) },
                { "MONACO", new PointLatLng(43.7, 7.4) },
                { "LIECHTENSTEIN", new PointLatLng(47.1, 9.5) },
                { "SAN MARINO", new PointLatLng(43.9, 12.4) },
                { "VATICAN CITY", new PointLatLng(41.9, 12.4) },

                // MIDDLE EAST & CAUCASUS
                { "TURKEY", new PointLatLng(39.9, 32.8) },
                { "ISRAEL", new PointLatLng(31.7, 35.2) },
                { "EGYPT", new PointLatLng(30.0, 31.2) },
                { "SAUDI ARABIA", new PointLatLng(24.7, 46.7) },
                { "IRAN", new PointLatLng(35.7, 51.4) },
                { "SYRIA", new PointLatLng(33.5, 36.2) },
                { "IRAQ", new PointLatLng(33.3, 44.3) },
                { "JORDAN", new PointLatLng(31.9, 35.9) },
                { "LEBANON", new PointLatLng(33.8, 35.5) },
                { "KUWAIT", new PointLatLng(29.3, 47.9) },
                { "UAE", new PointLatLng(24.4, 54.3) },
                { "QATAR", new PointLatLng(25.2, 51.5) },
                { "BAHRAIN", new PointLatLng(26.2, 50.5) },
                { "OMAN", new PointLatLng(23.6, 58.4) },
                { "YEMEN", new PointLatLng(15.3, 44.2) },
                { "CYPRUS", new PointLatLng(35.1, 33.3) },
                { "GEORGIA", new PointLatLng(41.7, 44.8) },
                { "ARMENIA", new PointLatLng(40.1, 44.5) },
                { "AZERBAIJAN", new PointLatLng(40.4, 49.8) },

                // CENTRAL & SOUTH ASIA
                { "PAKISTAN", new PointLatLng(33.7, 73.0) },
                { "INDIA", new PointLatLng(28.6, 77.2) },
                { "AFGHANISTAN", new PointLatLng(34.5, 69.1) },
                { "KAZAKHSTAN", new PointLatLng(51.1, 71.4) },
                { "UZBEKISTAN", new PointLatLng(41.3, 69.2) },
                { "TURKMENISTAN", new PointLatLng(37.9, 58.3) },
                { "KYRGYZSTAN", new PointLatLng(42.8, 74.5) },
                { "TAJIKISTAN", new PointLatLng(38.5, 68.7) },
                { "BANGLADESH", new PointLatLng(23.8, 90.4) },
                { "SRI LANKA", new PointLatLng(6.9, 79.8) },
                { "NEPAL", new PointLatLng(27.7, 85.3) },
                { "BHUTAN", new PointLatLng(27.4, 89.6) },
                { "MALDIVES", new PointLatLng(4.1, 73.5) },

                // EAST & SOUTHEAST ASIA
                { "CHINA", new PointLatLng(39.9, 116.4) },
                { "NORTH KOREA", new PointLatLng(39.0, 125.7) },
                { "SOUTH KOREA", new PointLatLng(37.5, 126.9) },
                { "JAPAN", new PointLatLng(35.6, 139.6) },
                { "INDONESIA", new PointLatLng(-6.2, 106.8) },
                { "MONGOLIA", new PointLatLng(47.9, 106.9) },
                { "TAIWAN", new PointLatLng(25.0, 121.5) },
                { "MYANMAR", new PointLatLng(19.7, 96.0) },
                { "THAILAND", new PointLatLng(13.7, 100.5) },
                { "VIETNAM", new PointLatLng(21.0, 105.8) },
                { "LAOS", new PointLatLng(17.9, 102.6) },
                { "CAMBODIA", new PointLatLng(11.5, 104.8) },
                { "MALAYSIA", new PointLatLng(3.1, 101.6) },
                { "SINGAPORE", new PointLatLng(1.2, 103.8) },
                { "PHILIPPINES", new PointLatLng(14.5, 120.9) },
                { "BRUNEI", new PointLatLng(4.9, 114.9) },
                { "TIMOR-LESTE", new PointLatLng(-8.5, 125.5) },

                // AFRICA
                { "NIGERIA", new PointLatLng(9.0, 7.5) },
                { "SOUTH AFRICA", new PointLatLng(-25.7, 28.2) },
                { "MOROCCO", new PointLatLng(34.0, -6.8) },
                { "ALGERIA", new PointLatLng(36.7, 3.0) },
                { "TUNISIA", new PointLatLng(36.8, 10.1) },
                { "LIBYA", new PointLatLng(32.8, 13.1) },
                { "SUDAN", new PointLatLng(15.5, 32.5) },
                { "SOUTH SUDAN", new PointLatLng(4.8, 31.5) },
                { "MALI", new PointLatLng(12.6, -8.0) },
                { "NIGER", new PointLatLng(13.5, 2.1) },
                { "CHAD", new PointLatLng(12.1, 15.0) },
                { "MAURITANIA", new PointLatLng(18.0, -15.9) },
                { "SENEGAL", new PointLatLng(14.7, -17.4) },
                { "GAMBIA", new PointLatLng(13.4, -16.5) },
                { "GUINEA-BISSAU", new PointLatLng(11.8, -15.5) },
                { "GUINEA", new PointLatLng(9.5, -13.7) },
                { "SIERRA LEONE", new PointLatLng(8.4, -13.2) },
                { "LIBERIA", new PointLatLng(6.3, -10.7) },
                { "IVORY COAST", new PointLatLng(6.8, -5.2) },
                { "GHANA", new PointLatLng(5.6, -0.1) },
                { "TOGO", new PointLatLng(6.1, 1.2) },
                { "BENIN", new PointLatLng(6.3, 2.6) },
                { "BURKINA FASO", new PointLatLng(12.3, -1.5) },
                { "CAPE VERDE", new PointLatLng(14.9, -23.5) },
                { "CAMEROON", new PointLatLng(3.8, 11.5) },
                { "CENTRAL AFRICAN REP.", new PointLatLng(4.3, 18.5) },
                { "EQUATORIAL GUINEA", new PointLatLng(3.7, 8.7) },
                { "GABON", new PointLatLng(0.4, 9.4) },
                { "CONGO", new PointLatLng(-4.2, 15.2) },
                { "DR CONGO", new PointLatLng(-4.3, 15.3) },
                { "SAO TOME & PRIN.", new PointLatLng(0.3, 6.7) },
                { "ETHIOPIA", new PointLatLng(9.0, 38.7) },
                { "SOMALIA", new PointLatLng(2.0, 45.3) },
                { "DJIBOUTI", new PointLatLng(11.5, 43.1) },
                { "ERITREA", new PointLatLng(15.3, 38.9) },
                { "KENYA", new PointLatLng(-1.2, 36.8) },
                { "UGANDA", new PointLatLng(0.3, 32.5) },
                { "RWANDA", new PointLatLng(-1.9, 30.0) },
                { "BURUNDI", new PointLatLng(-3.3, 29.3) },
                { "TANZANIA", new PointLatLng(-6.1, 35.7) },
                { "SEYCHELLES", new PointLatLng(-4.6, 55.4) },
                { "ANGOLA", new PointLatLng(-8.8, 13.2) },
                { "ZAMBIA", new PointLatLng(-15.3, 28.2) },
                { "ZIMBABWE", new PointLatLng(-17.8, 31.0) },
                { "MALAWI", new PointLatLng(-13.9, 33.7) },
                { "MOZAMBIQUE", new PointLatLng(-25.9, 32.5) },
                { "MADAGASCAR", new PointLatLng(-18.8, 47.5) },
                { "NAMIBIA", new PointLatLng(-22.5, 17.0) },
                { "BOTSWANA", new PointLatLng(-24.6, 25.9) },
                { "LESOTHO", new PointLatLng(-29.3, 27.4) },
                { "ESWATINI", new PointLatLng(-26.3, 31.1) },
                { "COMOROS", new PointLatLng(-11.7, 43.2) },
                { "MAURITIUS", new PointLatLng(-20.1, 57.5) },

                // OCEANIA
                { "AUSTRALIA", new PointLatLng(-35.3, 149.1) },
                { "NEW ZEALAND", new PointLatLng(-41.2, 174.7) },
                { "PAPUA NEW GUINEA", new PointLatLng(-9.4, 147.1) },
                { "FIJI", new PointLatLng(-18.1, 178.4) },
                { "SOLOMON ISLANDS", new PointLatLng(-9.4, 159.9) },
                { "VANUATU", new PointLatLng(-17.7, 168.3) },
                { "SAMOA", new PointLatLng(-13.8, -171.7) },
                { "KIRIBATI", new PointLatLng(1.3, 172.9) },
                { "MICRONESIA", new PointLatLng(6.9, 158.2) },
                { "TONGA", new PointLatLng(-21.1, -175.2) },
                { "MARSHALL ISLANDS", new PointLatLng(7.1, 171.3) },
                { "PALAU", new PointLatLng(7.4, 134.4) },
                { "TUVALU", new PointLatLng(-8.5, 179.1) },
                { "NAURU", new PointLatLng(-0.5, 166.9) }
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
            _mpClient.OnDisconnected += () => { if (InvokeRequired) Invoke(new Action(() => { logBox.SelectionColor = redText; LogMsg("[NETWORK] âš  Lost connection to multiplayer server."); })); };
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
                        LogMsg($"[WARNING] âš  RADAR ALERT: {senderName.ToUpper()} HAS LAUNCHED AN ICBM AT YOU! BRACE FOR IMPACT! âš ");

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
                                    DamageLines = new[] { $"âš¡ INTERCEPTED â€” {sName.ToUpper()}" },
                                    IsPlayerTarget = false
                                });
                                logBox.SelectionColor = cyanText;
                                LogMsg($"[IRON DOME] âš¡ Missile from {sName.ToUpper()} INTERCEPTED!");
                                return;
                            }

                            var dmgLogs = CombatEngine.ApplyForcedEnemyStrike(dmg);
                            lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPt, MaxRadius = 55f, DamageLines = new[] { $"STRIKE FROM {sName.ToUpper()}", $"{dmg:N0} casualties" }, IsPlayerTarget = true });
                            foreach (var l in dmgLogs) { logBox.SelectionColor = l.Contains("CATASTROPHE") || l.Contains("WARNING") ? redText : l.Contains("DEFENSE") ? cyanText : greenText; LogMsg(l); await Task.Delay(400); }
                            RefreshData();
                            CheckGameOver();
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
                                lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPt, MaxRadius = radius, DamageLines = new[] { $"[{senderName.ToUpper()}] {cas:N0} casualties{(def ? " â€” DEFEATED" : "")}" }, IsPlayerTarget = false });
                                logBox.SelectionColor = amberText; LogMsg($"[IMPACT] {target.ToUpper()} â€” {cas:N0} casualties from {senderName.ToUpper()}'s strike.{(def ? " NATION DEFEATED." : "")}");
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
                    TriggerAiLaunchLocal(aiAttacker, aiTarget, aiSalvo, aiDamage);
                    break;
            }
        }

        private void SetupUI()
        {
            this.Text = $"VAULT-TEC LAUNCH CONTROL V1.1.0 - {GameEngine.Player.NationName}";
            this.Size = new Size(1300, 830);
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

            GroupBox grpOps = CreateBox("WEAPONS CONTROL", 820, 260, 450, 175);
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

            btnSendTroops = CreateButton("DEPLOY EXTRACTION TROOPS", 10, 128, 430, 40, Color.DarkGoldenrod, Color.White);
            btnLaunch.Click += BtnLaunch_Click;
            btnSendTroops.Click += BtnSendTroops_Click;
            grpOps.Controls.Add(cmbWeapon); grpOps.Controls.Add(btnLaunch);
            grpOps.Controls.Add(lblSalvo); grpOps.Controls.Add(sliderSalvo);
            grpOps.Controls.Add(btnSendTroops);
            this.Controls.Add(grpOps);

            GroupBox grpPlayer = CreateBox("BUNKER STATUS", 820, 445, 450, 150);
            lblPlayerStats = new Label { Location = new Point(10, 25), Size = new Size(300, 120), ForeColor = greenText, Font = stdFont };
            btnOpenShop = CreateButton("BLACK\nMARKET", 320, 25, 120, 55, Color.Black, cyanText);
            btnOpenShop.Click += (s, e) => { new ShopForm().ShowDialog(); RefreshData(); };
            var btnLeaderboard = CreateButton("LEADER\nBOARD", 320, 85, 120, 55, Color.Black, amberText);
            btnLeaderboard.Click += async (s, e) =>
            {
                var lbf = new LeaderboardForm(GetServerBaseUrl());
                lbf.Show(this);
                await lbf.LoadAsync();
            };
            grpPlayer.Controls.Add(lblPlayerStats); grpPlayer.Controls.Add(btnOpenShop);
            grpPlayer.Controls.Add(btnLeaderboard);
            this.Controls.Add(grpPlayer);

            GroupBox grpLogs = CreateBox("ðŸ”´ LIVE TACTICAL COMMENTARY", 10, 605, 1260, 185);
            logBox = new RichTextBox { Location = new Point(10, 25), Size = new Size(1240, 150), BackColor = Color.Black, ForeColor = greenText, Font = new Font("Consolas", 14F, FontStyle.Bold), ReadOnly = true, BorderStyle = BorderStyle.None };
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

            // Freeze everything while the Iron Dome minigame is open
            if (_gameState == GameState.IronDomeMinigame) return;

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
                    exp.Progress += 1.2f * dt;
                    exp.TextProgress += 0.25f * dt;
                    if (exp.Progress >= 1.0f && exp.TextProgress >= 1.0f)
                        activeExplosions.RemoveAt(i);
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
            int w = mapPanel.Width, h = mapPanel.Height;

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

            // 3. Draw Connections
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

            float baseNodeRadius = Math.Max(3f, (float)(mapPanel.Zoom * 1.5)); // DYNAMIC SCALING

            // Player Base
            _nodeBrush.Color = cyanText;
            g.FillRectangle(_nodeBrush, pSc.X - baseNodeRadius, pSc.Y - baseNodeRadius, baseNodeRadius * 2, baseNodeRadius * 2);
            string baseLabel = $"BUNKER 67 ({GameEngine.Player.NationName})";
            SizeF bs = g.MeasureString(baseLabel, _nodeFont);
            g.FillRectangle(_textBgBrush, pSc.X + baseNodeRadius + 3, pSc.Y - 8, bs.Width, bs.Height);
            _textFgBrush.Color = cyanText;
            g.DrawString(baseLabel, _nodeFont, _textFgBrush, pSc.X + baseNodeRadius + 3, pSc.Y - 8);

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
                _nodeBrush.Color = nc;
                g.FillEllipse(_nodeBrush, sc.X - r, sc.Y - r, r * 2, r * 2);

                SizeF ts = g.MeasureString(n.Name.ToUpper(), _nodeFont);
                g.FillRectangle(_textBgBrush, sc.X + r + 3, sc.Y - 8, ts.Width, ts.Height);
                _textFgBrush.Color = nc;
                g.DrawString(n.Name.ToUpper(), _nodeFont, _textFgBrush, sc.X + r + 3, sc.Y - 8);

                if (n.Name == selectedTarget)
                    g.DrawEllipse(_selectPen, sc.X - (r * 2), sc.Y - (r * 2), r * 4, r * 4);
            }

            // ── MISSILES & EXPLOSIONS (locked for thread safety) ──────────────────
            lock (_animLock)
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

            bool hasPlayerMissile;
            lock (_animLock) hasPlayerMissile = activeMissiles.Any(m => m.IsPlayerMissile);
            btnLaunch.Enabled = !target.IsDefeated && !hasPlayerMissile;
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
                _ => 1
            };
            int max = Math.Max(1, stock);
            sliderSalvo.Maximum = max;
            sliderSalvo.Value = Math.Min(sliderSalvo.Value, max);
            sliderSalvo.TickFrequency = Math.Max(1, max / 10);
            lblSalvo.Text = $"SALVO: {sliderSalvo.Value}";
        }

        // ── Player Strike ────────────────────────────────────────────────────────────
        private void BtnLaunch_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedTarget) || cmbWeapon.SelectedItem == null) return;
            int weaponIndex = cmbWeapon.SelectedIndex;
            int salvo = sliderSalvo?.Value ?? 1;

            // Check we have enough stock
            int stock = weaponIndex switch
            {
                0 => GameEngine.Player.StandardNukes,
                1 => GameEngine.Player.MegaNukes,
                2 => GameEngine.Player.BioPlagues,
                3 => GameEngine.Player.OrbitalLasers,
                _ => 0
            };
            if (stock <= 0) { MessageBox.Show("Out of ammo!"); return; }
            salvo = Math.Min(salvo, stock);

            btnLaunch.Enabled = false;
            _ = FireSalvoAsync(selectedTarget, weaponIndex, salvo);
        }

        private async Task FireSalvoAsync(string targetName, int weaponIndex, int salvo)
        {
            if (!GameEngine.Nations.ContainsKey(targetName)) return;
            Nation target = GameEngine.Nations[targetName];
            PointLatLng startPt = new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);
            PointLatLng impactPt = new PointLatLng(target.MapY, target.MapX);

            string[] wNames = { "STANDARD NUKE", "TSAR BOMBA", "BIO-PLAGUE CANISTER", "ORBITAL LASER" };
            Color[] wColors = { Color.OrangeRed, Color.DeepPink, Color.LimeGreen, Color.Cyan };
            float[] wRadii = { 45f, 70f, 55f, 40f };
            float rc = wRadii[weaponIndex];

            logBox.SelectionColor = amberText;
            LogMsg(salvo > 1
                ? $"[LAUNCH] {salvo}× {wNames[weaponIndex]} SALVO launched toward {targetName.ToUpper()}!"
                : $"[LAUNCH] {wNames[weaponIndex]} launched toward {targetName.ToUpper()}! Tracking projectile...");

            for (int s = 0; s < salvo; s++)
            {
                // Deduct one weapon from inventory
                if (weaponIndex == 0) GameEngine.Player.StandardNukes--;
                else if (weaponIndex == 1) GameEngine.Player.MegaNukes--;
                else if (weaponIndex == 2) GameEngine.Player.BioPlagues--;
                else if (weaponIndex == 3) GameEngine.Player.OrbitalLasers--;
                GameEngine.Player.NukesUsed++;

                long preCalculatedDmg = CombatEngine.PreCalculatePlayerDamage(targetName, weaponIndex);

                if (_isMultiplayer && _mpClient != null)
                    _ = _mpClient.SendGameActionAsync(new { type = "strike", target = targetName, weapon = weaponIndex, playerNation = GameEngine.Player.NationName, damage = preCalculatedDmg });

                // Slight spread so missiles don't perfectly overlap
                float spread = salvo > 1 ? (s - salvo / 2f) * 0.3f : 0f;
                PointLatLng adjustedImpact = new PointLatLng(impactPt.Lat + spread * 0.2, impactPt.Lng + spread * 0.3);

                lock (_animLock) activeMissiles.Add(new MissileAnimation
                {
                    Start = startPt,
                    End = adjustedImpact,
                    IsPlayerMissile = true,
                    MissileColor = wColors[weaponIndex],
                    Speed = 0.4f - (s * 0.01f), // slight stagger so they don't all land simultaneously
                    OnImpact = async () => await HandlePlayerStrikeImpact(targetName, weaponIndex, adjustedImpact, rc, preCalculatedDmg)
                });

                if (s < salvo - 1)
                    await Task.Delay(180); // 180ms stagger between launches
            }

            RefreshData();
        }

        private async Task HandlePlayerStrikeImpact(string targetName, int weaponIndex, PointLatLng impactPos, float blastRadius, long calculatedDmg)
        {
            StrikeResult result = CombatEngine.ExecuteCombatTurn(targetName, weaponIndex, calculatedDmg);

            string impactLine = result.Logs.FirstOrDefault(l => l.Contains("[IMPACT]")) ?? "";
            string resultLine = result.Logs.FirstOrDefault(l => l.Contains("SURRENDER") || l.Contains("VICTORY") || l.Contains("SUCCESS")) ?? "";

            lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPos, MaxRadius = blastRadius, DamageLines = new[] { impactLine, resultLine }, IsPlayerTarget = false });

            foreach (var l in result.Logs)
            {
                logBox.SelectionColor = l.Contains("WARNING") || l.Contains("CATASTROPHE") ? redText : l.Contains("SURRENDER") || l.Contains("SUCCESS") || l.Contains("VICTORY") ? amberText : l.Contains("ALLY") ? cyanText : greenText;
                LogMsg(l); await Task.Delay(500);
            }
            RefreshData();
            CheckGameOver();

            // Broadcast the calculated results to everyone
            foreach (var (allyName, damage) in result.AllySupporters)
            {
                if (GameEngine.Nations.TryGetValue(allyName, out Nation allyNation))
                {
                    await Task.Delay(350);
                    logBox.SelectionColor = cyanText;
                    LogMsg($"[ALLY SUPPORT] {allyName.ToUpper()} has launched supporting missiles at {targetName.ToUpper()}!");
                    BroadcastAiLaunch(allyNation, targetName, 1, damage);
                }
            }

            foreach (string retaliatorName in result.Retaliators)
            {
                if (GameEngine.Nations.TryGetValue(retaliatorName, out Nation eNat))
                {
                    await Task.Delay(600);
                    BroadcastAiLaunch(eNat, GameEngine.Player.NationName, 1);
                }
            }
        }

        // ── Ally Support Missiles ────────────────────────────────────────────────────
        private void LaunchAllyMissile(Nation ally, string targetName, long damage, PointLatLng fallbackImpact)
        {
            PointLatLng startPt = new PointLatLng(ally.MapY, ally.MapX);
            PointLatLng impactPt = GameEngine.Nations.TryGetValue(targetName, out Nation tn) ? new PointLatLng(tn.MapY, tn.MapX) : fallbackImpact;

            lock (_animLock) activeMissiles.Add(new MissileAnimation
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

            lock (_animLock) activeExplosions.Add(new ExplosionEffect { Center = impactPos, MaxRadius = 35f, DamageLines = new[] { $"[ALLY IMPACT] +{actualDmg:N0} casualties" }, IsPlayerTarget = false });

            logBox.SelectionColor = cyanText;
            LogMsg($"[ALLY IMPACT] {allyName.ToUpper()} support strike caused an additional {actualDmg:N0} casualties.");
            RefreshData();
        }

        // ── Enemy Missiles (hostile → player) ───────────────────────────────────────
        private void LaunchEnemyMissile(Nation attacker)
        {
            PointLatLng startPt  = new PointLatLng(attacker.MapY, attacker.MapX);
            PointLatLng impactPt = new PointLatLng(GameEngine.Player.MapY, GameEngine.Player.MapX);

            logBox.SelectionColor = redText;
            LogMsg($"[WARNING] ⚠ RADAR ALERT: {attacker.Name.ToUpper()} HAS LAUNCHED AN ICBM! BRACE FOR IMPACT! ⚠");

            var missile = new MissileAnimation
            {
                Start           = startPt,
                End             = impactPt,
                IsPlayerMissile = false,
                MissileColor    = Color.Red,
                Speed           = 0.35f,
            };

            // Register as inbound before setting OnImpact so the minigame sees it
            lock (_animLock)
            {
                _inboundMissiles[missile] = attacker.Name;
                activeMissiles.Add(missile);
            }

            missile.OnImpact = async () => await HandleEnemyStrikeImpact(missile, impactPt);

            // First inbound missile triggers the minigame (if dome exists and minigames on)
            if (_minigamesEnabled && GameEngine.Player.IronDomeLevel > 0 && _gameState == GameState.Playing)
                _ = Task.Run(() => mapPanel.BeginInvoke(new Action(() => _ = TriggerIronDomeMinigame(impactPt))));
        }

        private async Task TriggerIronDomeMinigame(PointLatLng baseImpactPos)
        {
            // Guard: only one session at a time
            if (_gameState != GameState.Playing) return;
            _gameState = GameState.IronDomeMinigame;

            // Small delay so all missiles launched in the same salvo tick get registered
            await Task.Delay(80);

            // Count every inbound missile currently tracked
            List<MissileAnimation> wave;
            lock (_animLock)
                wave = new List<MissileAnimation>(_inboundMissiles.Keys);

            int totalMissiles = wave.Count;

            var domeGame = new IronDomeForm(totalMissiles, GameEngine.Player.IronDomeLevel);
            domeGame.ShowDialog(this);

            // Mark which missiles were intercepted (by index, best-scoring first)
            int intercepted = domeGame.InterceptedCount;
            for (int i = 0; i < wave.Count && i < intercepted; i++)
                lock (_animLock) _interceptedMissiles.Add(wave[i]);

            _gameState = GameState.Playing;
        }

        private async Task HandleEnemyStrikeImpact(MissileAnimation missile, PointLatLng impactPos)
        {
            string attackerName;
            bool   wasIntercepted;
            lock (_animLock)
            {
                _inboundMissiles.TryGetValue(missile, out attackerName);
                wasIntercepted = _interceptedMissiles.Remove(missile);
                _inboundMissiles.Remove(missile);
            }

            if (string.IsNullOrEmpty(attackerName)) return;

            if (wasIntercepted)
            {
                // Dud — show intercept explosion, no damage
                lock (_animLock) activeExplosions.Add(new ExplosionEffect
                {
                    Center = impactPos, MaxRadius = 30f,
                    DamageLines = new[] { $"⚡ INTERCEPTED — {attackerName.ToUpper()}" },
                    IsPlayerTarget = false
                });
                logBox.SelectionColor = cyanText;
                LogMsg($"[IRON DOME] ⚡ Missile from {attackerName.ToUpper()} INTERCEPTED!");
                return;
            }

            // Missile gets through — apply damage (passive dome roll since minigame already ran)
            var logs = CombatEngine.ExecuteEnemyStrike(attackerName);
            if (logs.Count == 0) return;

            string casualtyLine = logs.FirstOrDefault(l => l.Contains("CASUALTY")) ?? "";
            lock (_animLock) activeExplosions.Add(new ExplosionEffect
            {
                Center = impactPos, MaxRadius = 55f,
                DamageLines = new[] { $"STRIKE FROM {attackerName.ToUpper()}", casualtyLine },
                IsPlayerTarget = true
            });

            foreach (var l in logs)
            {
                logBox.SelectionColor = l.Contains("CATASTROPHE") || l.Contains("WARNING") ? redText
                                      : l.Contains("DEFENSE") ? cyanText : greenText;
                LogMsg(l);
                await Task.Delay(400);
            }

            RefreshData();
            CheckGameOver();
        }

        // ── Country vs Country Wars ──────────────────────────────────────────────────
        private void BroadcastAiLaunch(Nation attacker, string targetName, int salvo, long? forcedDamage = null)
        {
            long damage = 0;
            if (GameEngine.Nations.TryGetValue(targetName, out Nation target))
            {
                damage = forcedDamage ?? (long)(target.MaxPopulation * (0.04 + rng.NextDouble() * 0.14));
            }

            if (_isMultiplayer && _mpClient != null)
            {
                _ = _mpClient.SendGameActionAsync(new { 
                    type = "ai_launch", 
                    attacker = attacker.Name, 
                    target = targetName, 
                    salvo = salvo, 
                    damage = damage 
                });
            }

            TriggerAiLaunchLocal(attacker.Name, targetName, salvo, damage);
        }

        private void TriggerAiLaunchLocal(string attackerName, string targetName, int salvo, long damage)
        {
            if (!GameEngine.Nations.TryGetValue(attackerName, out Nation attacker)) return;

            if (targetName == GameEngine.Player.NationName)
            {
                attacker.IsHostileToPlayer = true;
                for (int s = 0; s < salvo; s++) LaunchEnemyMissile(attacker);
            }
            else if (GameEngine.Nations.TryGetValue(targetName, out Nation target))
            {
                for (int s = 0; s < salvo; s++) LaunchNationVsNationMissile(attacker, target, damage);
            }
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
                target.IsDefeated = true;
                logBox.SelectionColor = amberText;
                LogMsg($"[WORLD EVENT] {targetName.ToUpper()} has been annihilated by {attackerName.ToUpper()}!");
            }

            RefreshData();

            if (!_isMultiplayer || (_mpClient != null && _mpClient.IsHost))
            {
                if (!target.IsDefeated && !target.IsHumanControlled && target.Nukes > 0 && rng.NextDouble() < 0.65)
                {
                    await Task.Delay(900);
                    if (!target.IsDefeated && !attacker.IsDefeated) BroadcastAiLaunch(target, attacker.Name, 1);
                }

                foreach (string allyName in target.Allies)
                {
                    if (!GameEngine.Nations.TryGetValue(allyName, out Nation targetAlly)) continue;
                    if (targetAlly.IsDefeated || targetAlly.IsHumanControlled || targetAlly.Nukes <= 0 || rng.NextDouble() >= 0.45) continue;

                    await Task.Delay(700 + rng.Next(500));
                    if (!targetAlly.IsDefeated && !attacker.IsDefeated)
                    {
                        logBox.SelectionColor = allyUnderAttack ? redText : amberText;
                        LogMsg($"[ALLIANCE] {allyName.ToUpper()} enters the war defending {targetName.ToUpper()}!");
                        BroadcastAiLaunch(targetAlly, attacker.Name, 1);
                    }
                }

                foreach (string allyName in attacker.Allies)
                {
                    if (!GameEngine.Nations.TryGetValue(allyName, out Nation attackerAlly)) continue;
                    if (attackerAlly.IsDefeated || attackerAlly.IsHumanControlled || attackerAlly.Nukes <= 0 || rng.NextDouble() >= 0.30) continue;

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
                        BroadcastAiLaunch(a, GameEngine.Player.NationName, salvo);
                        break;
                    }
                }
            }

            worldWarTick++;
            if (worldWarTick >= 18)
            {
                worldWarTick = 0;
                
                // ONLY THE HOST DECIDES UNPROVOKED WORLD WARS IN MULTIPLAYER
                if (_isMultiplayer && _mpClient != null && !_mpClient.IsHost) return;
                
                if (rng.NextDouble() > 0.30) return;

                var attackers = GameEngine.Nations.Values.Where(n => !n.IsDefeated && n.Nukes > 0 && !n.IsHumanControlled).ToList();
                if (attackers.Count == 0) return;

                Nation attacker = attackers[rng.Next(attackers.Count)];
                var targetPool = GameEngine.Nations.Values.Where(n => !n.IsDefeated && n.Name != attacker.Name && !attacker.Allies.Contains(n.Name) && !n.IsHumanControlled).ToList();

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
                        BroadcastAiLaunch(attacker, GameEngine.Player.NationName, 1);
                    }
                    else if (nvnTarget != null)
                    {
                        BroadcastAiLaunch(attacker, nvnTarget.Name, 1);
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

            TryTriggerWorldEvents();
        }

        private bool _gameOver = false;
        private void CheckGameOver()
        {
            if (_gameOver) return;

            if (GameEngine.Player.Population <= 0)
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

            string playerName = _mpClient?.Players.FirstOrDefault(p => p.Id == _mpClient.LocalPlayerId)?.Name ?? "COMMANDER";

            LogMsg($"[VICTORY] WORLD DOMINATION ACHIEVED! Final score: {finalScore:N0}");

            var form = new GameOverForm(
                victory: true,
                playerName, nation,
                finalScore, baseScore, timeBonus, nukePenalty,
                countryMult, elapsedSeconds, nukesUsed,
                GetServerBaseUrl());

            form.Load += async (s, e) => await form.LoadAsync();
            form.ShowDialog(this);
            Application.Exit();
        }

        private async Task HandleDefeatAsync()
        {
            int elapsedSeconds = (int)_gameTimer.Elapsed.TotalSeconds;
            string playerName  = _mpClient?.Players.FirstOrDefault(p => p.Id == _mpClient.LocalPlayerId)?.Name ?? "COMMANDER";

            LogMsg("[DEFEAT] YOUR NATION HAS BEEN WIPED OUT.");

            var form = new GameOverForm(
                victory: false,
                playerName, GameEngine.Player.NationName,
                finalScore: 0, baseScore: 0, timeBonus: 0, nukePenalty: 0,
                countryMult: GameEngine.Player.ScoreMultiplier,
                elapsedSeconds, nukesUsed: GameEngine.Player.NukesUsed,
                GetServerBaseUrl());

            form.Load += async (s, e) => await form.LoadAsync();
            form.ShowDialog(this);
            Application.Exit();
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
            }
            base.Dispose(disposing);
        }
    }
}
