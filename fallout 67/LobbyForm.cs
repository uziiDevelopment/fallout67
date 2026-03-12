using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        private Color bgDark    = Color.FromArgb(15, 20, 15);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color redText   = Color.FromArgb(255, 50, 50);
        private Color cyanText  = Color.Cyan;
        private Font  stdFont   = new Font("Consolas", 10F, FontStyle.Bold);
        private Font  bigFont   = new Font("Consolas", 13F, FontStyle.Bold);

        // ── SP widgets ───────────────────────────────────────────────────────
        private Panel    pnlSP;
        private ListBox  lstCountries;
        private CheckBox chkHardMode;

        // ── MP widgets ───────────────────────────────────────────────────────
        private Panel   pnlMPSetup;   // create / join controls
        private Panel   pnlMPLobby;  // in-lobby player list + country select
        private TextBox txtName, txtServer, txtJoinCode;
        private Label   lblRoomCode, lblLobbyCode, lblStatus;
        private ListBox lstPlayers;
        private ComboBox cmbCountry;
        private Button   btnStartGame;
        private CheckBox chkMpMinigames;
        private CheckBox chkMpHardMode;
        private MultiplayerClient? _mp;

        public LobbyForm()
        {
            this.Text            = "VAULT-TEC LAUNCH CONTROL — NETWORK TERMINAL";
            this.Size            = new Size(930, 640);
            this.BackColor       = bgDark;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;

            BuildUI();
            this.Shown += async (s, e) => await CheckForUpdatesAsync();
        }

        // ── Auto-update via Velopack ─────────────────────────────────────────
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                var mgr = new UpdateManager(
                    new GithubSource("https://github.com/uziiDevelopment/fallout67", null, false));

                if (!mgr.IsInstalled)
                {
                    // App was not installed via Velopack Setup — auto-update unavailable
                    return;
                }

                var currentVersion = mgr.CurrentVersion;
                this.Text = $"VAULT-TEC LAUNCH CONTROL — NETWORK TERMINAL  (v{currentVersion})";

                var newVersion = await mgr.CheckForUpdatesAsync();
                if (newVersion == null) return;

                var result = MessageBox.Show(
                    $"A new version ({newVersion.TargetFullRelease.Version}) is available.\nYou are currently on v{currentVersion}.\n\nDownload and install now?",
                    "VAULT-TEC UPDATE AVAILABLE",
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
                MessageBox.Show(
                    $"Auto-update check failed:\n{ex.Message}",
                    "Update Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ── UI construction ──────────────────────────────────────────────────
        private void BuildUI()
        {
            // Title
            var title = MakeLabel("► FALLOUT 67 ◄  VAULT-TEC NETWORK TERMINAL", 0, 18, 930, bigFont, greenText);
            title.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(title);

            // Mode buttons
            var btnSP = MakeButton("[ SINGLEPLAYER ]", 80,  60, 280, 50, bgDark, greenText);
            var btnMP = MakeButton("[ MULTIPLAYER  ]", 460, 60, 280, 50, bgDark, cyanText);
            btnSP.Click += (s, e) => ShowSingleplayer();
            btnMP.Click += (s, e) => ShowMultiplayerSetup();
            this.Controls.Add(btnSP);
            this.Controls.Add(btnMP);

            // Leaderboard button — top right, always visible
            var btnLb = MakeButton("[ LEADERBOARD ]", 680, 60, 220, 50, bgDark, amberText);
            btnLb.Click += async (s, e) =>
            {
                string url = txtServer?.Text.Trim() is { Length: > 0 } t ? t : ServerUrl;
                var lbf = new LeaderboardForm(url);
                lbf.Show(this);
                await lbf.LoadAsync();
            };
            // Adjust existing button positions to make room on the right
            this.Controls.Add(btnLb);

            // Divider
            var div = new Label { Location = new Point(10, 120), Size = new Size(900, 2), BackColor = Color.FromArgb(50, greenText) };
            this.Controls.Add(div);

            // ── Singleplayer Panel ──────────────────────────────────────────
            pnlSP = new Panel { Location = new Point(10, 130), Size = new Size(900, 460), BackColor = bgDark, Visible = false };
            var spHeader = MakeLabel("SELECT YOUR NATION — SINGLEPLAYER", 0, 0, 900, stdFont, amberText);
            spHeader.TextAlign = ContentAlignment.MiddleCenter;
            pnlSP.Controls.Add(spHeader);

            lstCountries = new ListBox
            {
                Location = new Point(100, 30), Size = new Size(590, 340),
                BackColor = Color.Black, ForeColor = greenText, Font = new Font("Consolas", 12F, FontStyle.Bold),
                BorderStyle = BorderStyle.FixedSingle
            };
            foreach (var c in GameEngine.GetAllCountryNames(hardMode: false)) lstCountries.Items.Add(c);
            lstCountries.SelectedIndex = 0;

            chkHardMode = new CheckBox
            {
                Text      = "Hard Mode (all nations, including micro-states)",
                Location  = new Point(100, 378),
                Size      = new Size(420, 22),
                Font      = stdFont, ForeColor = redText, BackColor = bgDark,
                Checked   = false
            };
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

            var chkSpMinigames = new CheckBox
            {
                Text      = "Enable minigames (Iron Dome intercept etc.)",
                Location  = new Point(100, 406),
                Size      = new Size(390, 22),
                Font      = stdFont, ForeColor = amberText, BackColor = bgDark,
                Checked   = true
            };
            pnlSP.Controls.Add(chkSpMinigames);

            var btnSpLaunch = MakeButton("► LAUNCH SINGLEPLAYER ◄", 100, 432, 590, 42, Color.DarkRed, Color.White);
            btnSpLaunch.Click += (s, e) =>
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

            pnlSP.Controls.Add(lstCountries);
            pnlSP.Controls.Add(btnSpLaunch);
            this.Controls.Add(pnlSP);

            // ── MP Setup Panel ──────────────────────────────────────────────
            pnlMPSetup = new Panel { Location = new Point(10, 130), Size = new Size(900, 460), BackColor = bgDark, Visible = false };

            pnlMPSetup.Controls.Add(MakeLabel("PLAYER NAME:", 10, 8, 140, stdFont, amberText));
            txtName   = MakeTextBox(155, 5, 200, "Commander");
            pnlMPSetup.Controls.Add(txtName);

            pnlMPSetup.Controls.Add(MakeLabel("SERVER URL:", 10, 45, 140, stdFont, amberText));
            txtServer = MakeTextBox(155, 42, 400, "https://fallout67.imperiuminteractive.workers.dev");
            pnlMPSetup.Controls.Add(txtServer);

            var btnCreate = MakeButton("CREATE ROOM", 10,  85, 180, 38, Color.DarkGoldenrod, Color.White);
            var btnJoinLbl = MakeLabel("JOIN CODE:", 210, 93, 110, stdFont, amberText);
            txtJoinCode   = MakeTextBox(325, 90, 120, "XXXXXX");
            var btnJoin   = MakeButton("JOIN", 455, 85, 80, 38, Color.DarkBlue, Color.White);

            btnCreate.Click += async (s, e) => await CreateRoomAsync();
            btnJoin.Click   += async (s, e) => await JoinRoomAsync();

            pnlMPSetup.Controls.Add(btnCreate);
            pnlMPSetup.Controls.Add(btnJoinLbl);
            pnlMPSetup.Controls.Add(txtJoinCode);
            pnlMPSetup.Controls.Add(btnJoin);

            lblRoomCode = MakeLabel("", 10, 135, 870, stdFont, cyanText);
            pnlMPSetup.Controls.Add(lblRoomCode);

            this.Controls.Add(pnlMPSetup);

            // ── MP Lobby Panel ──────────────────────────────────────────────
            pnlMPLobby = new Panel { Location = new Point(10, 130), Size = new Size(900, 460), BackColor = bgDark, Visible = false };

            pnlMPLobby.Controls.Add(MakeLabel("PLAYERS IN LOBBY:", 10, 0, 350, stdFont, amberText));

            // Room code box — top right of the lobby panel
            var codeBox = new Panel { Location = new Point(475, 0), Size = new Size(305, 120), BackColor = Color.FromArgb(10, 30, 10) };
            codeBox.Controls.Add(MakeLabel("► ROOM CODE — SHARE WITH FRIENDS ◄", 5, 5, 295, new Font("Consolas", 8F, FontStyle.Bold), amberText));
            lblLobbyCode = new Label
            {
                Location = new Point(5, 28), Size = new Size(295, 52),
                ForeColor = greenText, Font = new Font("Consolas", 28F, FontStyle.Bold),
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter
            };
            codeBox.Controls.Add(lblLobbyCode);
            codeBox.Controls.Add(MakeLabel("Enter this code on the JOIN screen", 5, 88, 295, new Font("Consolas", 8F, FontStyle.Regular), Color.FromArgb(130, 150, 130)));
            pnlMPLobby.Controls.Add(codeBox);

            lstPlayers = new ListBox
            {
                Location = new Point(10, 22), Size = new Size(450, 200),
                BackColor = Color.Black, ForeColor = greenText, Font = stdFont, BorderStyle = BorderStyle.FixedSingle
            };
            pnlMPLobby.Controls.Add(lstPlayers);

            pnlMPLobby.Controls.Add(MakeLabel("YOUR COUNTRY:", 10, 232, 180, stdFont, amberText));
            cmbCountry = new ComboBox
            {
                Location = new Point(195, 229), Size = new Size(265, 28),
                Font = stdFont, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.Black, ForeColor = greenText
            };
            foreach (var c in GameEngine.GetAllCountryNames(hardMode: false)) cmbCountry.Items.Add(c);
            cmbCountry.SelectedIndexChanged += async (s, e) =>
            {
                if (_mp != null && cmbCountry.SelectedItem != null)
                    await _mp.SelectCountryAsync(cmbCountry.SelectedItem.ToString()!);
            };
            pnlMPLobby.Controls.Add(cmbCountry);

            lblStatus = MakeLabel("Waiting for players...", 10, 268, 870, stdFont, greenText);
            pnlMPLobby.Controls.Add(lblStatus);

            chkMpHardMode = new CheckBox
            {
                Text      = "Hard Mode (all nations, including micro-states)",
                Location  = new Point(10, 330),
                Size      = new Size(450, 22),
                Font      = stdFont, ForeColor = redText, BackColor = bgDark,
                Checked   = false,
                Visible   = false  // only shown to host
            };
            chkMpHardMode.CheckedChanged += (s, e) =>
            {
                // Rebuild country dropdown when host toggles hard mode
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

            chkMpMinigames = new CheckBox
            {
                Text      = "Enable minigames (Iron Dome intercept etc.)",
                Location  = new Point(10, 355),
                Size      = new Size(450, 22),
                Font      = stdFont, ForeColor = amberText, BackColor = bgDark,
                Checked   = true
            };
            pnlMPLobby.Controls.Add(chkMpMinigames);

            btnStartGame = MakeButton("► START GAME ◄", 10, 382, 450, 45, Color.DarkRed, Color.White);
            btnStartGame.Visible = false;
            btnStartGame.Click  += async (s, e) =>
            {
                if (_mp != null) await _mp.StartGameAsync();
            };
            pnlMPLobby.Controls.Add(btnStartGame);

            var btnLeave = MakeButton("LEAVE LOBBY", 520, 300, 150, 45, Color.DarkSlateGray, Color.White);
            btnLeave.Click += (s, e) =>
            {
                _mp?.Dispose();
                _mp = null;
                pnlMPLobby.Visible = false;
                pnlMPSetup.Visible = true;
            };
            pnlMPLobby.Controls.Add(btnLeave);

            this.Controls.Add(pnlMPLobby);
        }

        // ── Panel Visibility ─────────────────────────────────────────────────
        private void ShowSingleplayer()         { pnlSP.Visible = true; pnlMPSetup.Visible = false; pnlMPLobby.Visible = false; }
        private void ShowMultiplayerSetup()     { pnlSP.Visible = false; pnlMPSetup.Visible = true; pnlMPLobby.Visible = false; }

        // ── Multiplayer: Create ──────────────────────────────────────────────
        private async Task CreateRoomAsync()
        {
            string name   = txtName.Text.Trim();
            string server = txtServer.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))   { MessageBox.Show("Enter a player name."); return; }
            if (string.IsNullOrWhiteSpace(server))  { MessageBox.Show("Enter the server URL."); return; }

            lblRoomCode.Text = "Connecting...";
            try
            {
                _mp = new MultiplayerClient();
                HookMpEvents();
                string code = await _mp.CreateRoomAsync(server, name);
                lblRoomCode.Text = $"ROOM CODE: {code}  ◄ Share this with friends";
                TransitionToLobby();
            }
            catch (Exception ex)
            {
                lblRoomCode.Text = "";
                MessageBox.Show($"Failed to create room:\n{ex.Message}");
                _mp?.Dispose(); _mp = null;
            }
        }

        // ── Multiplayer: Join ────────────────────────────────────────────────
        private async Task JoinRoomAsync()
        {
            string name   = txtName.Text.Trim();
            string server = txtServer.Text.Trim();
            string code   = txtJoinCode.Text.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(name))  { MessageBox.Show("Enter a player name."); return; }
            if (string.IsNullOrWhiteSpace(code))  { MessageBox.Show("Enter a room code."); return; }
            if (string.IsNullOrWhiteSpace(server)) { MessageBox.Show("Enter the server URL."); return; }

            lblRoomCode.Text = "Joining...";
            try
            {
                _mp = new MultiplayerClient();
                HookMpEvents();
                await _mp.JoinRoomAsync(server, code, name);
                lblRoomCode.Text = $"JOINED ROOM: {code}";
                TransitionToLobby();
            }
            catch (Exception ex)
            {
                lblRoomCode.Text = "";
                MessageBox.Show($"Failed to join room:\n{ex.Message}");
                _mp?.Dispose(); _mp = null;
            }
        }

        private void TransitionToLobby()
        {
            lblLobbyCode.Text  = _mp?.RoomCode ?? "";
            pnlMPSetup.Visible = false;
            pnlMPLobby.Visible = true;
        }

        // ── Wire up MultiplayerClient events ─────────────────────────────────
        private void HookMpEvents()
        {
            if (_mp == null) return;
            _mp.OnRoomUpdated  += players => SafeInvoke(() => RefreshLobby(players));
            _mp.OnGameStart    += (seed, players) => SafeInvoke(() => LaunchGame(seed, players));
            _mp.OnError        += msg => SafeInvoke(() => lblStatus.Text = $"[ERROR] {msg}");
            _mp.OnDisconnected += () => SafeInvoke(() =>
            {
                lblStatus.Text = "[DISCONNECTED] Lost connection to server.";
                btnStartGame.Visible = false;
            });
        }

        private void RefreshLobby(List<MpPlayer> players)
        {
            lstPlayers.Items.Clear();
            foreach (var p in players)
            {
                string flag  = p.Id == _mp?.LocalPlayerId ? " ← YOU" : "";
                string ctry  = p.Country != null ? $" [{p.Country}]" : " [No country]";
                lstPlayers.Items.Add($"{p.Name}{ctry}{flag}");
            }

            bool isHost = _mp?.IsHost == true;
            chkMpHardMode.Visible = isHost;
            btnStartGame.Visible  = isHost;

            // Re-populate dropdown without taken countries
            var taken = players.Where(p => p.Country != null && p.Id != _mp?.LocalPlayerId)
                               .Select(p => p.Country!).ToHashSet();
            string? current = cmbCountry.SelectedItem?.ToString();
            cmbCountry.Items.Clear();
            foreach (var c in GameEngine.GetAllCountryNames(chkMpHardMode.Checked))
                if (!taken.Contains(c)) cmbCountry.Items.Add(c);
            if (current != null && cmbCountry.Items.Contains(current))
                cmbCountry.SelectedItem = current;
            int readyCount = players.Count(p => p.Country != null);
            lblStatus.Text = $"{readyCount}/{players.Count} players have selected a country." +
                             (_mp?.IsHost == true ? "  (You are host — press START when ready)" : "  (Waiting for host to start)");
        }

        private void LaunchGame(int seed, List<MpPlayer> players)
        {
            // Find local player's country
            var me = players.FirstOrDefault(p => p.Id == _mp?.LocalPlayerId);
            if (me?.Country == null)
            {
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
            return new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h),
                BackColor = bg, ForeColor = fg, Font = stdFont,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
            };
        }

        private TextBox MakeTextBox(int x, int y, int w, string placeholder)
        {
            return new TextBox
            {
                Location = new Point(x, y), Size = new Size(w, 26),
                BackColor = Color.Black, ForeColor = greenText, Font = stdFont,
                BorderStyle = BorderStyle.FixedSingle, Text = placeholder
            };
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
            // Only dispose MP client if we're NOT launching the game
            if (DialogResult != DialogResult.OK) _mp?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
