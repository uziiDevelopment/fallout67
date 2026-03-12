using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace fallover_67
{
    public class GameOverForm : Form
    {
        private readonly bool _victory;
        private readonly string _playerName;
        private readonly string _nation;
        private readonly long _finalScore;
        private readonly int _elapsedSeconds;
        private readonly int _nukesUsed;
        private readonly long _baseScore;
        private readonly long _timeBonus;
        private readonly long _nukePenalty;
        private readonly float _countryMult;
        private readonly string _serverUrl;

        // Leaderboard data (fetched async)
        private List<LeaderboardEntry> _scores = new();
        private int _playerRank = -1;
        private bool _leaderboardLoaded = false;
        private string _leaderboardError = "";

        // UI
        private Panel _leftPanel;
        private Panel _rightPanel;
        private Label _rankLabel;
        private ListBox _scoreList;
        private Label _statusLabel;
        private Button _exitBtn;

        // Theme
        private Color bgDark    = Color.FromArgb(10, 14, 10);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color redText   = Color.FromArgb(255, 50, 50);
        private Color cyanText  = Color.Cyan;
        private Color goldText  = Color.FromArgb(255, 215, 0);
        private Font stdFont    = new Font("Consolas", 10F, FontStyle.Bold);
        private Font bigFont    = new Font("Consolas", 18F, FontStyle.Bold);
        private Font hugeFont   = new Font("Consolas", 28F, FontStyle.Bold);
        private Font smallFont  = new Font("Consolas", 9F, FontStyle.Bold);

        public GameOverForm(
            bool victory,
            string playerName, string nation,
            long finalScore, long baseScore, long timeBonus, long nukePenalty,
            float countryMult, int elapsedSeconds, int nukesUsed,
            string serverUrl)
        {
            _victory        = victory;
            _playerName     = playerName;
            _nation         = nation;
            _finalScore     = finalScore;
            _baseScore      = baseScore;
            _timeBonus      = timeBonus;
            _nukePenalty    = nukePenalty;
            _countryMult    = countryMult;
            _elapsedSeconds = elapsedSeconds;
            _nukesUsed      = nukesUsed;
            _serverUrl      = serverUrl;

            BuildForm();
        }

        private void BuildForm()
        {
            this.Text            = _victory ? "⚛ WORLD DOMINATION ACHIEVED ⚛" : "☢ NUCLEAR ANNIHILATION ☢";
            this.Size            = new Size(1100, 700);
            this.BackColor       = bgDark;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;

            // ── Header ──────────────────────────────────────────────────────
            var headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size     = new Size(1100, 90),
                BackColor = _victory ? Color.FromArgb(20, 40, 20) : Color.FromArgb(40, 10, 10)
            };

            var titleLbl = new Label
            {
                Text      = _victory ? "⚛  WORLD DOMINATION ACHIEVED  ⚛" : "☢  NUCLEAR ANNIHILATION  ☢",
                Location  = new Point(0, 10),
                Size      = new Size(1100, 45),
                Font      = hugeFont,
                ForeColor = _victory ? goldText : redText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var subtitleLbl = new Label
            {
                Text      = _victory
                    ? $"Commander {_playerName}  |  Nation: {_nation}  |  The world bows before you."
                    : $"Commander {_playerName}  |  Nation: {_nation}  |  Your empire has fallen.",
                Location  = new Point(0, 55),
                Size      = new Size(1100, 28),
                Font      = stdFont,
                ForeColor = _victory ? greenText : Color.FromArgb(200, 100, 100),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };

            headerPanel.Controls.Add(titleLbl);
            headerPanel.Controls.Add(subtitleLbl);
            this.Controls.Add(headerPanel);

            // ── Left panel: score breakdown ──────────────────────────────────
            _leftPanel = new Panel
            {
                Location  = new Point(10, 100),
                Size      = new Size(500, 530),
                BackColor = Color.FromArgb(15, 22, 15)
            };
            _leftPanel.Paint += (s, e) => DrawPanelBorder(e.Graphics, _leftPanel.ClientRectangle, _victory ? greenText : redText);

            BuildScorePanel();
            this.Controls.Add(_leftPanel);

            // ── Right panel: leaderboard ─────────────────────────────────────
            _rightPanel = new Panel
            {
                Location  = new Point(525, 100),
                Size      = new Size(555, 530),
                BackColor = Color.FromArgb(15, 22, 15)
            };
            _rightPanel.Paint += (s, e) => DrawPanelBorder(e.Graphics, _rightPanel.ClientRectangle, amberText);

            BuildLeaderboardPanel();
            this.Controls.Add(_rightPanel);

            // ── Exit button ──────────────────────────────────────────────────
            _exitBtn = new Button
            {
                Text      = "RETURN TO MAIN MENU",
                Location  = new Point(400, 643),
                Size      = new Size(300, 42),
                BackColor = _victory ? Color.FromArgb(20, 60, 20) : Color.FromArgb(60, 10, 10),
                ForeColor = Color.White,
                Font      = bigFont,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand
            };
            _exitBtn.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
            this.Controls.Add(_exitBtn);
        }

        private void BuildScorePanel()
        {
            string timeStr = _elapsedSeconds >= 3600
                ? $"{_elapsedSeconds / 3600}h {_elapsedSeconds % 3600 / 60}m {_elapsedSeconds % 60}s"
                : $"{_elapsedSeconds / 60}m {_elapsedSeconds % 60}s";

            var hdr = new Label
            {
                Text      = "── SCORE BREAKDOWN ──",
                Location  = new Point(10, 15),
                Size      = new Size(480, 28),
                Font      = bigFont,
                ForeColor = _victory ? goldText : redText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _leftPanel.Controls.Add(hdr);

            int y = 60;
            void Row(string label, string value, Color color)
            {
                var lbl = new Label { Text = label, Location = new Point(20, y), Size = new Size(260, 26), Font = stdFont, ForeColor = amberText, BackColor = Color.Transparent };
                var val = new Label { Text = value,  Location = new Point(290, y), Size = new Size(190, 26), Font = stdFont, ForeColor = color,     BackColor = Color.Transparent };
                _leftPanel.Controls.Add(lbl);
                _leftPanel.Controls.Add(val);
                y += 30;
            }

            void Divider()
            {
                var d = new Label { Location = new Point(20, y + 4), Size = new Size(460, 2), BackColor = Color.FromArgb(60, greenText) };
                _leftPanel.Controls.Add(d);
                y += 16;
            }

            Row("NATION",         _nation,                       greenText);
            Row("TIME PLAYED",    timeStr,                        greenText);
            Row("NUKES FIRED",    _nukesUsed.ToString(),          _nukesUsed > 20 ? redText : greenText);
            Row("COUNTRY BONUS",  $"×{_countryMult:F1}",          cyanText);
            Divider();
            Row("BASE SCORE",     $"{_baseScore:N0}",             greenText);
            Row("TIME BONUS",     $"+{_timeBonus:N0}",            greenText);
            Row("NUKE PENALTY",   $"-{_nukePenalty:N0}",          _nukePenalty > 0 ? redText : greenText);
            Divider();

            // Final score — big
            var finalLbl = new Label
            {
                Text      = "FINAL SCORE",
                Location  = new Point(20, y + 8),
                Size      = new Size(230, 38),
                Font      = new Font("Consolas", 14F, FontStyle.Bold),
                ForeColor = amberText,
                BackColor = Color.Transparent
            };
            var finalVal = new Label
            {
                Text      = $"{_finalScore:N0}",
                Location  = new Point(255, y + 4),
                Size      = new Size(230, 46),
                Font      = new Font("Consolas", 20F, FontStyle.Bold),
                ForeColor = _victory ? goldText : redText,
                BackColor = Color.Transparent
            };
            _leftPanel.Controls.Add(finalLbl);
            _leftPanel.Controls.Add(finalVal);
            y += 60;

            // Rank placeholder (updated once leaderboard loads)
            _rankLabel = new Label
            {
                Text      = "Fetching your rank...",
                Location  = new Point(20, y + 10),
                Size      = new Size(460, 36),
                Font      = new Font("Consolas", 13F, FontStyle.Bold),
                ForeColor = cyanText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _leftPanel.Controls.Add(_rankLabel);
        }

        private void BuildLeaderboardPanel()
        {
            var hdr = new Label
            {
                Text      = "── GLOBAL LEADERBOARD ──",
                Location  = new Point(10, 15),
                Size      = new Size(535, 28),
                Font      = bigFont,
                ForeColor = amberText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _rightPanel.Controls.Add(hdr);

            _scoreList = new ListBox
            {
                Location        = new Point(10, 55),
                Size            = new Size(535, 420),
                BackColor       = Color.Black,
                ForeColor       = greenText,
                Font            = smallFont,
                BorderStyle     = BorderStyle.None,
                SelectionMode   = SelectionMode.None
            };
            _rightPanel.Controls.Add(_scoreList);

            _statusLabel = new Label
            {
                Text      = "Loading leaderboard...",
                Location  = new Point(10, 485),
                Size      = new Size(535, 28),
                Font      = stdFont,
                ForeColor = Color.FromArgb(130, 150, 130),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _rightPanel.Controls.Add(_statusLabel);
        }

        // Called after form loads — submit score (if victory) then load leaderboard
        public async Task LoadAsync()
        {
            try
            {
                if (_victory)
                    await SubmitAndLoadAsync();
                else
                    await LoadLeaderboardOnlyAsync();
            }
            catch (Exception ex)
            {
                SafeInvoke(() =>
                {
                    _statusLabel.Text = $"Network error: {ex.Message}";
                    _rankLabel.Text   = "Could not reach leaderboard";
                });
            }
        }

        private async Task SubmitAndLoadAsync()
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var payload = JsonSerializer.Serialize(new
            {
                name      = _playerName,
                nation    = _nation,
                score     = _finalScore,
                seconds   = _elapsedSeconds,
                nukesUsed = _nukesUsed
            });
            var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            await http.PostAsync($"{_serverUrl.TrimEnd('/')}/api/score", content);

            await LoadLeaderboardOnlyAsync();
        }

        private async Task LoadLeaderboardOnlyAsync()
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var resp = await http.GetStringAsync($"{_serverUrl.TrimEnd('/')}/api/leaderboard");
            var doc  = JsonDocument.Parse(resp);
            var arr  = doc.RootElement.GetProperty("scores").EnumerateArray();

            _scores = new List<LeaderboardEntry>();
            foreach (var e in arr)
            {
                _scores.Add(new LeaderboardEntry
                {
                    Name      = e.GetProperty("name").GetString()   ?? "?",
                    Nation    = e.GetProperty("nation").GetString()  ?? "?",
                    Score     = e.GetProperty("score").GetInt64(),
                    Seconds   = e.TryGetProperty("seconds",   out var sv) ? sv.GetInt32() : 0,
                    NukesUsed = e.TryGetProperty("nukesUsed", out var nv) ? nv.GetInt32() : 0,
                    Date      = e.TryGetProperty("date",      out var dv) ? dv.GetString() ?? "" : ""
                });
            }

            if (_victory)
            {
                _playerRank = -1;
                for (int i = 0; i < _scores.Count; i++)
                {
                    if (_scores[i].Score == _finalScore && _scores[i].Name == _playerName)
                    {
                        _playerRank = i + 1;
                        break;
                    }
                }
            }

            SafeInvoke(PopulateLeaderboard);
        }

        private void PopulateLeaderboard()
        {
            _scoreList.Items.Clear();

            if (_scores.Count == 0)
            {
                _scoreList.Items.Add("  No scores yet — be the first!");
                _statusLabel.Text = "";
                if (_victory) _rankLabel.Text = "You're #1!";
                return;
            }

            for (int i = 0; i < _scores.Count; i++)
            {
                var e    = _scores[i];
                string t = $"{e.Seconds / 60}m{e.Seconds % 60:D2}s";
                bool   isMe = _victory && (i + 1) == _playerRank;
                string marker = isMe ? "►" : " ";
                _scoreList.Items.Add($" {marker}#{i + 1,-3}  {e.Score,12:N0}   {e.Name,-14}  ({e.Nation}, {t})");
            }

            // Highlight player row
            if (_victory && _playerRank > 0)
            {
                _scoreList.SelectedIndex = _playerRank - 1;
                _rankLabel.Text = _playerRank == 1
                    ? "★  YOU ARE #1 ON THE GLOBAL LEADERBOARD!  ★"
                    : $"You placed  #{_playerRank}  on the global leaderboard!";
                _rankLabel.ForeColor = _playerRank <= 3 ? goldText : cyanText;
            }
            else if (_victory)
            {
                _rankLabel.Text = "Score submitted (not in top 20 yet)";
            }

            _statusLabel.Text = $"Top {_scores.Count} commanders worldwide";
            _leaderboardLoaded = true;
        }

        private static void DrawPanelBorder(Graphics g, Rectangle r, Color c)
        {
            using var pen = new Pen(Color.FromArgb(80, c), 1);
            g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
        }

        private void SafeInvoke(Action a)
        {
            if (IsHandleCreated && !IsDisposed)
            {
                if (InvokeRequired) Invoke(a);
                else a();
            }
        }
    }

    public class LeaderboardEntry
    {
        public string Name      { get; set; } = "";
        public string Nation    { get; set; } = "";
        public long   Score     { get; set; }
        public int    Seconds   { get; set; }
        public int    NukesUsed { get; set; }
        public string Date      { get; set; } = "";
    }
}
