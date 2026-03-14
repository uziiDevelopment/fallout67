using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Velopack;
using Velopack.Sources;

namespace fallover_67
{
    public class LobbyForm : Form
    {
        // ── Result data read by Program.cs ───────────────────────────────────
        public bool IsMultiplayer          { get; private set; }
        public string SelectedCountry      { get; private set; } = "";
        public MultiplayerClient? MpClient { get; private set; }
        public List<MpPlayer>? MpPlayers   { get; private set; }
        public int GameSeed                { get; private set; }
        public string ServerUrl            { get; private set; } = "https://fallout67.imperiuminteractive.workers.dev";
        public bool MinigamesEnabled       { get; private set; } = true;

        // ── Theme ────────────────────────────────────────────────────────────
        private readonly Color bgDark       = Color.FromArgb(8, 12, 8);
        private readonly Color gridLine     = Color.FromArgb(15, 30, 15);
        private readonly Color termBorder   = Color.FromArgb(0, 120, 0);
        private readonly Color termText     = Color.FromArgb(80, 220, 80);
        private readonly Color termHighlight = Color.FromArgb(0, 255, 200);
        private readonly Color termWarning  = Color.FromArgb(220, 100, 50);
        private readonly Color termMoney    = Color.FromArgb(200, 200, 50);
        private readonly Color redGlow      = Color.FromArgb(255, 40, 40);
        private readonly Color amberText    = Color.FromArgb(255, 176, 0);
        private readonly Color dimText      = Color.FromArgb(50, 80, 50);

        private readonly Font fontTitle   = new Font("Consolas", 32F, FontStyle.Bold);
        private readonly Font fontSubtitle = new Font("Consolas", 11F, FontStyle.Bold);
        private readonly Font fontLarge   = new Font("Consolas", 14F, FontStyle.Bold);
        private readonly Font fontStd     = new Font("Consolas", 10F, FontStyle.Bold);
        private readonly Font fontSmall   = new Font("Consolas", 9F, FontStyle.Regular);
        private readonly Font fontTech    = new Font("Consolas", 7F, FontStyle.Regular);

        // ── State ────────────────────────────────────────────────────────────
        private enum MenuState { Main, Singleplayer, MultiplayerSetup, MultiplayerLobby }
        private MenuState _state = MenuState.Main;

        // ── SP widgets ───────────────────────────────────────────────────────
        private Panel pnlSP;
        private ListBox lstCountries;
        private CheckBox chkHardMode;

        // ── MP widgets ───────────────────────────────────────────────────────
        private Panel   pnlMPSetup;
        private Panel   pnlMPLobby;
        private TextBox txtName, txtServer, txtJoinCode;
        private Label   lblRoomCode, lblLobbyCode, lblStatus;
        private ListBox lstPlayers;
        private ComboBox cmbCountry;
        private Button   btnStartGame;
        private CheckBox chkMpMinigames;
        private CheckBox chkMpHardMode;
        private MultiplayerClient? _mp;

        // ── Main menu ────────────────────────────────────────────────────────
        private Panel pnlMain;

        // ── Title animation ──────────────────────────────────────────────────
        private System.Windows.Forms.Timer _animTimer;
        private float _titleGlow = 0f;
        private int _glowDir = 1;
        private float _scanY = 0f;
        private string _versionText = "";

        // Dragging (borderless form)
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();

        public LobbyForm()
        {
            this.Text            = "M.A.D. — MUTUALLY ASSURED DESTRUCTION";
            this.ClientSize      = new Size(960, 660);
            this.BackColor       = bgDark;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.DoubleBuffered  = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            BuildUI();

            _animTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _animTimer.Tick += (s, e) =>
            {
                _titleGlow += 0.03f * _glowDir;
                if (_titleGlow >= 1f) { _titleGlow = 1f; _glowDir = -1; }
                else if (_titleGlow <= 0.3f) { _titleGlow = 0.3f; _glowDir = 1; }
                _scanY += 1.5f;
                if (_scanY > this.Height) _scanY = 0;
                this.Invalidate(new Rectangle(0, 0, this.Width, 200)); // title region
                this.Invalidate(new Rectangle(0, (int)_scanY - 2, this.Width, 4)); // scan line
            };
            _animTimer.Start();

            this.Shown += async (s, e) => await CheckForUpdatesAsync();
        }

        // ── Background paint ─────────────────────────────────────────────────
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            var g = e.Graphics;

            // Radar grid
            using (var p = new Pen(gridLine, 1))
            {
                for (int x = 0; x < Width; x += 40) g.DrawLine(p, x, 0, x, Height);
                for (int y = 0; y < Height; y += 40) g.DrawLine(p, 0, y, Width, y);
            }

            // Scan line
            using (var scanBrush = new LinearGradientBrush(
                new Rectangle(0, (int)_scanY - 20, Width, 40),
                Color.FromArgb(0, termBorder), Color.FromArgb(30, termBorder),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(scanBrush, 0, (int)_scanY - 20, Width, 40);
            }

            // Outer border
            using (var bp = new Pen(termBorder, 2))
                g.DrawRectangle(bp, 1, 1, Width - 3, Height - 3);

            // Corner accents
            int cornerLen = 20;
            using (var cp = new Pen(termHighlight, 2))
            {
                g.DrawLine(cp, 2, 2, 2 + cornerLen, 2); g.DrawLine(cp, 2, 2, 2, 2 + cornerLen);
                g.DrawLine(cp, Width - 3, 2, Width - 3 - cornerLen, 2); g.DrawLine(cp, Width - 3, 2, Width - 3, 2 + cornerLen);
                g.DrawLine(cp, 2, Height - 3, 2 + cornerLen, Height - 3); g.DrawLine(cp, 2, Height - 3, 2, Height - 3 - cornerLen);
                g.DrawLine(cp, Width - 3, Height - 3, Width - 3 - cornerLen, Height - 3); g.DrawLine(cp, Width - 3, Height - 3, Width - 3, Height - 3 - cornerLen);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // ── Title — always visible ───────────────────────────────────────
            string title = "M . A . D .";
            var titleSize = g.MeasureString(title, fontTitle);
            float titleX = (Width - titleSize.Width) / 2f;
            float titleY = 30;

            // Glow behind title
            int glowAlpha = (int)(40 * _titleGlow);
            using (var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, redGlow)))
            {
                g.FillEllipse(glowBrush, titleX - 40, titleY - 15, titleSize.Width + 80, titleSize.Height + 30);
            }

            // Title text with glow-modulated color
            int r = (int)(180 + 75 * _titleGlow);
            int gb = (int)(30 + 20 * _titleGlow);
            using (var titleBrush = new SolidBrush(Color.FromArgb(r, gb, gb)))
            {
                g.DrawString(title, fontTitle, titleBrush, titleX, titleY);
            }

            // Subtitle
            string sub = "MUTUALLY ASSURED DESTRUCTION";
            var subSize = g.MeasureString(sub, fontSubtitle);
            float subX = (Width - subSize.Width) / 2f;
            using (var subBrush = new SolidBrush(termHighlight))
                g.DrawString(sub, fontSubtitle, subBrush, subX, titleY + titleSize.Height + 2);

            // Decorative lines flanking subtitle
            float lineY = titleY + titleSize.Height + 12;
            using (var lp = new Pen(Color.FromArgb(60, termBorder), 1))
            {
                g.DrawLine(lp, 40, lineY, subX - 15, lineY);
                g.DrawLine(lp, subX + subSize.Width + 15, lineY, Width - 40, lineY);
            }

            // Status bar at bottom
            string status = $"SYS.NET // STRATEGIC COMMAND TERMINAL // {DateTime.Now:HH:mm:ss}  {_versionText}";
            using (var statusBrush = new SolidBrush(Color.FromArgb(60, termText)))
                g.DrawString(status, fontTech, statusBrush, 15, Height - 20);

            // Classification stamp
            string stamp = "TOP SECRET // SCI // NOFORN";
            var stampSize = g.MeasureString(stamp, fontTech);
            using (var stampBrush = new SolidBrush(Color.FromArgb(40, redGlow)))
                g.DrawString(stamp, fontTech, stampBrush, Width - stampSize.Width - 15, Height - 20);
        }

        // ── UI construction ──────────────────────────────────────────────────
        private void BuildUI()
        {
            // ── Draggable header area ────────────────────────────────────────
            var dragArea = new Panel
            {
                Location = new Point(0, 0), Size = new Size(Width, 110),
                BackColor = Color.Transparent, Cursor = Cursors.SizeAll
            };
            dragArea.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
            this.Controls.Add(dragArea);

            // Close button
            var btnClose = MakeButton("[X]", Width - 55, 8, 45, 28, Color.Transparent, termWarning);
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 0, 0);
            btnClose.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnClose);
            btnClose.BringToFront();

            // ── Main Menu Panel ──────────────────────────────────────────────
            pnlMain = new Panel { Location = new Point(0, 115), Size = new Size(960, 540), BackColor = Color.Transparent };
            BuildMainMenu();
            this.Controls.Add(pnlMain);

            // ── Singleplayer Panel ───────────────────────────────────────────
            pnlSP = new Panel { Location = new Point(0, 115), Size = new Size(960, 540), BackColor = Color.Transparent, Visible = false };
            BuildSingleplayerPanel();
            this.Controls.Add(pnlSP);

            // ── MP Setup Panel ───────────────────────────────────────────────
            pnlMPSetup = new Panel { Location = new Point(0, 115), Size = new Size(960, 540), BackColor = Color.Transparent, Visible = false };
            BuildMPSetupPanel();
            this.Controls.Add(pnlMPSetup);

            // ── MP Lobby Panel ───────────────────────────────────────────────
            pnlMPLobby = new Panel { Location = new Point(0, 115), Size = new Size(960, 540), BackColor = Color.Transparent, Visible = false };
            BuildMPLobbyPanel();
            this.Controls.Add(pnlMPLobby);
        }

        private void BuildMainMenu()
        {
            // Center the menu buttons vertically
            int btnW = 380, btnH = 58, gap = 18;
            int startY = 40;
            int centerX = (960 - btnW) / 2;

            var btnSP = MakeMenuButton("SINGLEPLAYER", "Launch a solo campaign against AI nations",
                centerX, startY, btnW, btnH, termHighlight, () => ShowPanel(MenuState.Singleplayer));
            pnlMain.Controls.Add(btnSP);

            var btnMP = MakeMenuButton("MULTIPLAYER", "Connect to other commanders via network",
                centerX, startY + btnH + gap, btnW, btnH, termHighlight, () => ShowPanel(MenuState.MultiplayerSetup));
            pnlMain.Controls.Add(btnMP);

            var btnLb = MakeMenuButton("LEADERBOARD", "View global commander rankings",
                centerX, startY + (btnH + gap) * 2, btnW, btnH, termMoney, async () =>
                {
                    string url = txtServer?.Text.Trim() is { Length: > 0 } t ? t : ServerUrl;
                    var lbf = new LeaderboardForm(url);
                    lbf.Show(this);
                    await lbf.LoadAsync();
                });
            pnlMain.Controls.Add(btnLb);

            var btnQuit = MakeMenuButton("EXIT", "Terminate command session",
                centerX, startY + (btnH + gap) * 3, btnW, btnH, termWarning,
                () => { this.DialogResult = DialogResult.Cancel; this.Close(); });
            pnlMain.Controls.Add(btnQuit);

            // Nuclear symbol decoration
            var symbolLabel = new Label
            {
                Text = "☢",
                Font = new Font("Segoe UI Symbol", 60F, FontStyle.Regular),
                ForeColor = Color.FromArgb(25, redGlow),
                Location = new Point(centerX + btnW / 2 - 45, startY + (btnH + gap) * 4 + 10),
                Size = new Size(100, 90),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlMain.Controls.Add(symbolLabel);
        }

        private void BuildSingleplayerPanel()
        {
            // Back button
            var btnBack = MakeButton("< BACK", 30, 10, 100, 32, Color.Transparent, termText);
            btnBack.FlatAppearance.BorderColor = termBorder;
            btnBack.Click += (s, e) => ShowPanel(MenuState.Main);
            pnlSP.Controls.Add(btnBack);

            // Section header
            var header = MakeLabel("SELECT YOUR NATION", 0, 10, 960, fontLarge, amberText);
            header.TextAlign = ContentAlignment.MiddleCenter;
            pnlSP.Controls.Add(header);

            // Country list in a terminal box
            var listBox = new TerminalBox
            {
                Location = new Point(180, 50), Size = new Size(600, 350),
                Title = "AVAILABLE NATIONS", LineColor = termBorder, BackColor = bgDark
            };

            lstCountries = new ListBox
            {
                Location = new Point(10, 22), Size = new Size(578, 318),
                BackColor = Color.FromArgb(5, 10, 5), ForeColor = termText,
                Font = new Font("Consolas", 12F, FontStyle.Bold),
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 24
            };
            lstCountries.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                e.DrawBackground();
                bool selected = (e.State & DrawItemState.Selected) != 0;
                Color bg = selected ? Color.FromArgb(0, 50, 30) : Color.FromArgb(5, 10, 5);
                Color fg = selected ? termHighlight : termText;
                using (var bgBr = new SolidBrush(bg)) e.Graphics.FillRectangle(bgBr, e.Bounds);
                if (selected)
                    using (var bp = new Pen(termBorder, 1)) e.Graphics.DrawRectangle(bp, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                string text = lstCountries.Items[e.Index].ToString()!;
                string prefix = selected ? "> " : "  ";
                using (var fgBr = new SolidBrush(fg))
                    e.Graphics.DrawString(prefix + text, e.Font!, fgBr, e.Bounds.X + 5, e.Bounds.Y + 2);
            };
            foreach (var c in GameEngine.GetAllCountryNames(hardMode: false)) lstCountries.Items.Add(c);
            if (lstCountries.Items.Count > 0) lstCountries.SelectedIndex = 0;
            listBox.Controls.Add(lstCountries);
            pnlSP.Controls.Add(listBox);

            // Options
            chkHardMode = MakeCheckBox("HARD MODE — All nations including micro-states", 230, 410, redGlow);
            chkHardMode.CheckedChanged += (s, e) =>
            {
                string? prev = lstCountries.SelectedItem?.ToString();
                lstCountries.Items.Clear();
                foreach (var c in GameEngine.GetAllCountryNames(chkHardMode.Checked))
                    lstCountries.Items.Add(c);
                if (prev != null && lstCountries.Items.Contains(prev))
                    lstCountries.SelectedItem = prev;
                else if (lstCountries.Items.Count > 0)
                    lstCountries.SelectedIndex = 0;
            };
            pnlSP.Controls.Add(chkHardMode);

            var chkSpMinigames = MakeCheckBox("ENABLE MINIGAMES — Iron Dome intercept etc.", 230, 438, amberText);
            chkSpMinigames.Checked = true;
            pnlSP.Controls.Add(chkSpMinigames);

            // Launch button
            var btnLaunch = MakeButton("▶  LAUNCH CAMPAIGN", 310, 478, 340, 48, Color.FromArgb(120, 0, 0), Color.White);
            btnLaunch.Font = fontLarge;
            btnLaunch.FlatAppearance.BorderColor = redGlow;
            btnLaunch.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 0, 0);
            btnLaunch.Click += (s, e) =>
            {
                if (lstCountries.SelectedItem == null) return;
                SelectedCountry        = lstCountries.SelectedItem.ToString()!;
                IsMultiplayer          = false;
                MinigamesEnabled       = chkSpMinigames.Checked;
                GameEngine.HardMode    = chkHardMode.Checked;
                ServerUrl              = "https://fallout67.imperiuminteractive.workers.dev";
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            pnlSP.Controls.Add(btnLaunch);
        }

        private void BuildMPSetupPanel()
        {
            // Back button
            var btnBack = MakeButton("< BACK", 30, 10, 100, 32, Color.Transparent, termText);
            btnBack.FlatAppearance.BorderColor = termBorder;
            btnBack.Click += (s, e) => ShowPanel(MenuState.Main);
            pnlMPSetup.Controls.Add(btnBack);

            var header = MakeLabel("MULTIPLAYER — NETWORK UPLINK", 0, 10, 960, fontLarge, termHighlight);
            header.TextAlign = ContentAlignment.MiddleCenter;
            pnlMPSetup.Controls.Add(header);

            // Connection form in a terminal box
            var formBox = new TerminalBox
            {
                Location = new Point(200, 55), Size = new Size(560, 280),
                Title = "CONNECTION PARAMETERS", LineColor = termBorder, BackColor = bgDark
            };

            formBox.Controls.Add(MakeLabel("CALLSIGN:", 20, 35, 130, fontStd, amberText));
            txtName = MakeTextBox(155, 32, 280, "Commander");
            formBox.Controls.Add(txtName);

            formBox.Controls.Add(MakeLabel("SERVER:", 20, 75, 130, fontStd, amberText));
            txtServer = MakeTextBox(155, 72, 380, "https://fallout67.imperiuminteractive.workers.dev");
            formBox.Controls.Add(txtServer);

            // Separator
            var sep = new Label { Location = new Point(20, 115), Size = new Size(520, 1), BackColor = Color.FromArgb(40, termBorder) };
            formBox.Controls.Add(sep);

            // Create room
            var btnCreate = MakeButton("CREATE NEW ROOM", 20, 135, 220, 42, Color.FromArgb(0, 60, 40), Color.White);
            btnCreate.Font = fontStd;
            btnCreate.FlatAppearance.BorderColor = termHighlight;
            btnCreate.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 90, 60);
            btnCreate.Click += async (s, e) => await CreateRoomAsync();
            formBox.Controls.Add(btnCreate);

            // Join room
            formBox.Controls.Add(MakeLabel("— OR JOIN —", 250, 143, 100, fontSmall, dimText));

            formBox.Controls.Add(MakeLabel("CODE:", 350, 143, 55, fontStd, amberText));
            txtJoinCode = MakeTextBox(405, 140, 80, "");
            txtJoinCode.MaxLength = 6;
            txtJoinCode.CharacterCasing = CharacterCasing.Upper;
            txtJoinCode.TextAlign = HorizontalAlignment.Center;
            formBox.Controls.Add(txtJoinCode);

            var btnJoin = MakeButton("JOIN", 492, 135, 50, 42, Color.FromArgb(0, 40, 80), Color.White);
            btnJoin.FlatAppearance.BorderColor = termHighlight;
            btnJoin.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 60, 120);
            btnJoin.Click += async (s, e) => await JoinRoomAsync();
            formBox.Controls.Add(btnJoin);

            lblRoomCode = new Label
            {
                Location = new Point(20, 195), Size = new Size(520, 60),
                ForeColor = termText, Font = fontStd, BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            formBox.Controls.Add(lblRoomCode);

            pnlMPSetup.Controls.Add(formBox);
        }

        private void BuildMPLobbyPanel()
        {
            // Back button
            var btnLeave = MakeButton("< LEAVE LOBBY", 30, 10, 150, 32, Color.Transparent, termWarning);
            btnLeave.FlatAppearance.BorderColor = termWarning;
            btnLeave.Click += (s, e) =>
            {
                _mp?.Dispose();
                _mp = null;
                ShowPanel(MenuState.MultiplayerSetup);
            };
            pnlMPLobby.Controls.Add(btnLeave);

            // Room code display — top right
            var codeBox = new TerminalBox
            {
                Location = new Point(590, 5), Size = new Size(340, 105),
                Title = "ROOM CODE", LineColor = termHighlight, BackColor = Color.FromArgb(5, 20, 15)
            };
            lblLobbyCode = new Label
            {
                Location = new Point(10, 22), Size = new Size(320, 48),
                ForeColor = termHighlight, Font = new Font("Consolas", 32F, FontStyle.Bold),
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter
            };
            codeBox.Controls.Add(lblLobbyCode);
            var codeHint = MakeLabel("Share this code with other commanders", 10, 75, 320, fontTech, dimText);
            codeHint.TextAlign = ContentAlignment.MiddleCenter;
            codeBox.Controls.Add(codeHint);
            pnlMPLobby.Controls.Add(codeBox);

            // Player list
            var playersBox = new TerminalBox
            {
                Location = new Point(30, 55), Size = new Size(520, 220),
                Title = "CONNECTED COMMANDERS", LineColor = termBorder, BackColor = bgDark
            };
            lstPlayers = new ListBox
            {
                Location = new Point(8, 20), Size = new Size(502, 190),
                BackColor = Color.FromArgb(5, 10, 5), ForeColor = termText,
                Font = fontStd, BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 28
            };
            lstPlayers.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                Color bg = selected ? Color.FromArgb(0, 40, 25) : Color.FromArgb(5, 10, 5);
                using (var bgBr = new SolidBrush(bg)) e.Graphics.FillRectangle(bgBr, e.Bounds);
                string text = lstPlayers.Items[e.Index].ToString()!;
                Color fg = text.Contains("← YOU") ? termHighlight : termText;
                using (var fgBr = new SolidBrush(fg))
                    e.Graphics.DrawString(text, e.Font!, fgBr, e.Bounds.X + 10, e.Bounds.Y + 4);
                // Bottom separator line
                using (var lp = new Pen(Color.FromArgb(20, termBorder)))
                    e.Graphics.DrawLine(lp, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            };
            playersBox.Controls.Add(lstPlayers);
            pnlMPLobby.Controls.Add(playersBox);

            // Country selection
            var countryBox = new TerminalBox
            {
                Location = new Point(30, 285), Size = new Size(520, 70),
                Title = "YOUR NATION", LineColor = termBorder, BackColor = bgDark
            };
            cmbCountry = new ComboBox
            {
                Location = new Point(10, 25), Size = new Size(370, 28),
                Font = fontStd, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.Black, ForeColor = termText
            };
            foreach (var c in GameEngine.GetAllCountryNames(hardMode: false)) cmbCountry.Items.Add(c);
            cmbCountry.SelectedIndexChanged += async (s, e) =>
            {
                if (_mp != null && cmbCountry.SelectedItem != null)
                    await _mp.SelectCountryAsync(cmbCountry.SelectedItem.ToString()!);
            };
            countryBox.Controls.Add(cmbCountry);
            pnlMPLobby.Controls.Add(countryBox);

            // Status
            lblStatus = new Label
            {
                Location = new Point(30, 365), Size = new Size(900, 24),
                ForeColor = termText, Font = fontStd, BackColor = Color.Transparent
            };
            pnlMPLobby.Controls.Add(lblStatus);

            // Options
            chkMpHardMode = MakeCheckBox("HARD MODE", 30, 395, redGlow);
            chkMpHardMode.Visible = false;
            chkMpHardMode.CheckedChanged += (s, e) =>
            {
                string? current = cmbCountry.SelectedItem?.ToString();
                cmbCountry.Items.Clear();
                foreach (var c in GameEngine.GetAllCountryNames(chkMpHardMode.Checked))
                    cmbCountry.Items.Add(c);
                if (current != null && cmbCountry.Items.Contains(current))
                    cmbCountry.SelectedItem = current;
                else if (cmbCountry.Items.Count > 0)
                    cmbCountry.SelectedIndex = 0;
            };
            pnlMPLobby.Controls.Add(chkMpHardMode);

            chkMpMinigames = MakeCheckBox("ENABLE MINIGAMES", 30, 423, amberText);
            chkMpMinigames.Checked = true;
            pnlMPLobby.Controls.Add(chkMpMinigames);

            // Start game button
            btnStartGame = MakeButton("▶  START GAME", 30, 460, 340, 48, Color.FromArgb(120, 0, 0), Color.White);
            btnStartGame.Font = fontLarge;
            btnStartGame.FlatAppearance.BorderColor = redGlow;
            btnStartGame.FlatAppearance.MouseOverBackColor = Color.FromArgb(180, 0, 0);
            btnStartGame.Visible = false;
            btnStartGame.Click += async (s, e) =>
            {
                if (_mp != null) await _mp.StartGameAsync();
            };
            pnlMPLobby.Controls.Add(btnStartGame);
        }

        // ── Panel Visibility ─────────────────────────────────────────────────
        private void ShowPanel(MenuState state)
        {
            _state = state;
            pnlMain.Visible    = state == MenuState.Main;
            pnlSP.Visible      = state == MenuState.Singleplayer;
            pnlMPSetup.Visible = state == MenuState.MultiplayerSetup;
            pnlMPLobby.Visible = state == MenuState.MultiplayerLobby;
        }

        // ── Multiplayer: Create ──────────────────────────────────────────────
        private async Task CreateRoomAsync()
        {
            string name   = txtName.Text.Trim();
            string server = txtServer.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))   { ShowError("Enter a callsign."); return; }
            if (string.IsNullOrWhiteSpace(server))  { ShowError("Enter the server URL."); return; }

            lblRoomCode.ForeColor = termText;
            lblRoomCode.Text = "ESTABLISHING UPLINK...";
            try
            {
                _mp = new MultiplayerClient();
                HookMpEvents();
                string code = await _mp.CreateRoomAsync(server, name);
                lblRoomCode.ForeColor = termHighlight;
                lblRoomCode.Text = $"ROOM CREATED: {code}";
                TransitionToLobby();
            }
            catch (Exception ex)
            {
                lblRoomCode.ForeColor = termWarning;
                lblRoomCode.Text = $"CONNECTION FAILED: {ex.Message}";
                _mp?.Dispose(); _mp = null;
            }
        }

        // ── Multiplayer: Join ────────────────────────────────────────────────
        private async Task JoinRoomAsync()
        {
            string name   = txtName.Text.Trim();
            string server = txtServer.Text.Trim();
            string code   = txtJoinCode.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name))  { ShowError("Enter a callsign."); return; }
            if (string.IsNullOrWhiteSpace(code))  { ShowError("Enter a room code."); return; }
            if (string.IsNullOrWhiteSpace(server)) { ShowError("Enter the server URL."); return; }

            lblRoomCode.ForeColor = termText;
            lblRoomCode.Text = "JOINING ROOM...";
            try
            {
                _mp = new MultiplayerClient();
                HookMpEvents();
                await _mp.JoinRoomAsync(server, code, name);
                lblRoomCode.ForeColor = termHighlight;
                lblRoomCode.Text = $"JOINED ROOM: {code}";
                TransitionToLobby();
            }
            catch (Exception ex)
            {
                lblRoomCode.ForeColor = termWarning;
                lblRoomCode.Text = $"JOIN FAILED: {ex.Message}";
                _mp?.Dispose(); _mp = null;
            }
        }

        private void TransitionToLobby()
        {
            lblLobbyCode.Text = _mp?.RoomCode ?? "";
            ShowPanel(MenuState.MultiplayerLobby);
        }

        // ── Wire up MultiplayerClient events ─────────────────────────────────
        private void HookMpEvents()
        {
            if (_mp == null) return;
            _mp.OnRoomUpdated  += players => SafeInvoke(() => RefreshLobby(players));
            _mp.OnGameStart    += (seed, players) => SafeInvoke(() => LaunchGame(seed, players));
            _mp.OnError        += msg => SafeInvoke(() =>
            {
                lblStatus.ForeColor = termWarning;
                lblStatus.Text = $"[ERROR] {msg}";
            });
            _mp.OnDisconnected += () => SafeInvoke(() =>
            {
                lblStatus.ForeColor = termWarning;
                lblStatus.Text = "[DISCONNECTED] Lost connection to server.";
                btnStartGame.Visible = false;
            });
        }

        private void RefreshLobby(List<MpPlayer> players)
        {
            lstPlayers.Items.Clear();
            foreach (var p in players)
            {
                string flag  = p.Id == _mp?.LocalPlayerId ? "  ← YOU" : "";
                string ctry  = p.Country != null ? $"  [{p.Country}]" : "  [No country]";
                lstPlayers.Items.Add($"  {p.Name}{ctry}{flag}");
            }

            bool isHost = _mp?.IsHost == true;
            chkMpHardMode.Visible = isHost;
            btnStartGame.Visible  = isHost;

            var taken = players.Where(p => p.Country != null && p.Id != _mp?.LocalPlayerId)
                               .Select(p => p.Country!).ToHashSet();
            string? current = cmbCountry.SelectedItem?.ToString();
            cmbCountry.Items.Clear();
            foreach (var c in GameEngine.GetAllCountryNames(chkMpHardMode.Checked))
                if (!taken.Contains(c)) cmbCountry.Items.Add(c);
            if (current != null && cmbCountry.Items.Contains(current))
                cmbCountry.SelectedItem = current;

            int readyCount = players.Count(p => p.Country != null);
            lblStatus.ForeColor = termText;
            lblStatus.Text = $"{readyCount}/{players.Count} commanders ready" +
                             (_mp?.IsHost == true ? "  —  You are HOST. Press START when ready." : "  —  Waiting for host to start.");
        }

        private void LaunchGame(int seed, List<MpPlayer> players)
        {
            var me = players.FirstOrDefault(p => p.Id == _mp?.LocalPlayerId);
            if (me?.Country == null)
            {
                lblStatus.ForeColor = termWarning;
                lblStatus.Text = "[ERROR] You must select a country before the game starts.";
                return;
            }

            IsMultiplayer        = true;
            GameSeed             = seed;
            MpClient             = _mp;
            MpPlayers            = players;
            SelectedCountry      = me.Country;
            ServerUrl            = txtServer.Text.Trim();
            MinigamesEnabled     = chkMpMinigames.Checked;
            GameEngine.HardMode  = chkMpHardMode.Checked;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // ── Auto-update via Velopack ─────────────────────────────────────────
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var mgr = new UpdateManager(
                    new GithubSource("https://github.com/uziiDevelopment/fallout67", null, false));

                if (!mgr.IsInstalled) return;

                var currentVersion = mgr.CurrentVersion;
                _versionText = $"v{currentVersion}";
                this.Invalidate();

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null) return;

                var result = MessageBox.Show(
                    $"A new version ({newVersion.TargetFullRelease.Version}) is available.\nYou are currently on v{currentVersion}.\n\nDownload and install now?",
                    "UPDATE AVAILABLE",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    await mgr.DownloadUpdatesAsync(newVersion);
                    mgr.ApplyUpdatesAndRestart(newVersion);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Velopack] Update check failed: {ex}");
            }
        }

        // ── Widget helpers ───────────────────────────────────────────────────
        private Label MakeLabel(string text, int x, int y, int w, Font font, Color fg)
        {
            return new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, 28),
                ForeColor = fg, Font = font, BackColor = Color.Transparent
            };
        }

        private Button MakeButton(string text, int x, int y, int w, int h, Color bg, Color fg)
        {
            var btn = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                BackColor = bg, ForeColor = fg, Font = fontStd,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = termBorder;
            return btn;
        }

        private Panel MakeMenuButton(string title, string subtitle, int x, int y, int w, int h, Color accentColor, Action onClick)
        {
            var panel = new Panel
            {
                Location = new Point(x, y), Size = new Size(w, h),
                BackColor = Color.FromArgb(12, 18, 12), Cursor = Cursors.Hand
            };

            var lblTitle = new Label
            {
                Text = title, Font = fontLarge, ForeColor = accentColor,
                Location = new Point(20, 8), AutoSize = true, BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            var lblSub = new Label
            {
                Text = subtitle, Font = fontSmall, ForeColor = dimText,
                Location = new Point(20, 32), AutoSize = true, BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            var lblArrow = new Label
            {
                Text = "▶", Font = fontLarge, ForeColor = Color.FromArgb(40, accentColor),
                Location = new Point(w - 40, 15), AutoSize = true, BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };

            panel.Controls.Add(lblTitle);
            panel.Controls.Add(lblSub);
            panel.Controls.Add(lblArrow);

            // Border drawing
            panel.Paint += (s, e) =>
            {
                using (var bp = new Pen(Color.FromArgb(40, accentColor), 1))
                    e.Graphics.DrawRectangle(bp, 0, 0, panel.Width - 1, panel.Height - 1);
                // Left accent bar
                using (var ab = new SolidBrush(Color.FromArgb(60, accentColor)))
                    e.Graphics.FillRectangle(ab, 0, 0, 3, panel.Height);
            };

            // Hover effects
            var allControls = new Control[] { panel, lblTitle, lblSub, lblArrow };
            foreach (var ctrl in allControls)
            {
                ctrl.MouseEnter += (s, e) =>
                {
                    panel.BackColor = Color.FromArgb(20, 35, 20);
                    lblArrow.ForeColor = accentColor;
                    panel.Invalidate();
                };
                ctrl.MouseLeave += (s, e) =>
                {
                    if (!panel.ClientRectangle.Contains(panel.PointToClient(Cursor.Position)))
                    {
                        panel.BackColor = Color.FromArgb(12, 18, 12);
                        lblArrow.ForeColor = Color.FromArgb(40, accentColor);
                        panel.Invalidate();
                    }
                };
            }
            // Wire all controls to the same action
            foreach (var ctrl in new Control[] { panel, lblTitle, lblSub, lblArrow })
                ctrl.Click += (s, e) => onClick();

            return panel;
        }

        private TextBox MakeTextBox(int x, int y, int w, string text)
        {
            return new TextBox
            {
                Location = new Point(x, y), Size = new Size(w, 26),
                BackColor = Color.FromArgb(5, 10, 5), ForeColor = termText, Font = fontStd,
                BorderStyle = BorderStyle.FixedSingle, Text = text
            };
        }

        private CheckBox MakeCheckBox(string text, int x, int y, Color fg)
        {
            return new CheckBox
            {
                Text = text, Location = new Point(x, y), Size = new Size(500, 22),
                Font = fontStd, ForeColor = fg, BackColor = Color.Transparent
            };
        }

        private void ShowError(string msg)
        {
            MessageBox.Show(msg, "INPUT ERROR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SafeInvoke(Action a)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                if (InvokeRequired) Invoke(a);
                else a();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _animTimer?.Stop();
            _animTimer?.Dispose();
            if (DialogResult != DialogResult.OK) _mp?.Dispose();
            base.OnFormClosing(e);
        }

        // ── Reusable TerminalBox (same as ShopForm) ──────────────────────────
        private class TerminalBox : Panel
        {
            public string Title { get; set; } = "";
            public Color LineColor { get; set; } = Color.Lime;
            private Font titleFont = new Font("Consolas", 9F, FontStyle.Bold);

            public TerminalBox()
            {
                this.SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                this.BackColor = Color.FromArgb(8, 12, 8);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.None;

                Rectangle rect = new Rectangle(1, 7, this.Width - 3, this.Height - 9);

                using (Pen p = new Pen(LineColor, 1))
                    g.DrawRectangle(p, rect);

                if (!string.IsNullOrEmpty(Title))
                {
                    string displayTitle = $"-{Title}-";
                    SizeF size = g.MeasureString(displayTitle, titleFont);
                    using (SolidBrush clearBrush = new SolidBrush(this.BackColor))
                        g.FillRectangle(clearBrush, 15, 0, size.Width, 14);
                    using (SolidBrush textBrush = new SolidBrush(LineColor))
                        g.DrawString(displayTitle, titleFont, textBrush, 15, 0);
                }

                using (Pen thickPen = new Pen(LineColor, 2))
                {
                    g.DrawLine(thickPen, rect.X, rect.Y, rect.X + 5, rect.Y);
                    g.DrawLine(thickPen, rect.X, rect.Y, rect.X, rect.Y + 5);
                    g.DrawLine(thickPen, rect.Right, rect.Bottom, rect.Right - 5, rect.Bottom);
                    g.DrawLine(thickPen, rect.Right, rect.Bottom, rect.Right, rect.Bottom - 5);
                }
            }
        }
    }
}
