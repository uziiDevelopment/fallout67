using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace fallover_67
{
    public class LeaderboardForm : Form
    {
        private readonly string _serverUrl;

        private ListBox  _scoreList;
        private Label    _statusLabel;
        private Button   _refreshBtn;

        private Color bgDark    = Color.FromArgb(10, 14, 10);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color goldText  = Color.FromArgb(255, 215, 0);
        private Font  stdFont   = new Font("Consolas", 10F, FontStyle.Bold);
        private Font  bigFont   = new Font("Consolas", 16F, FontStyle.Bold);
        private Font  smallFont = new Font("Consolas", 9F, FontStyle.Bold);

        public LeaderboardForm(string serverUrl)
        {
            _serverUrl = serverUrl;
            BuildForm();
        }

        private void BuildForm()
        {
            this.Text            = "GLOBAL LEADERBOARD";
            this.Size            = new Size(700, 620);
            this.BackColor       = bgDark;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox     = false;

            var hdr = new Label
            {
                Text      = "══  FALLOUT 67  —  GLOBAL LEADERBOARD  ══",
                Location  = new Point(0, 15),
                Size      = new Size(700, 36),
                Font      = bigFont,
                ForeColor = goldText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(hdr);

            var colHdr = new Label
            {
                Text      = "  RANK   SCORE             NAME             (NATION, TIME)",
                Location  = new Point(10, 58),
                Size      = new Size(680, 22),
                Font      = smallFont,
                ForeColor = amberText,
                BackColor = Color.FromArgb(20, 30, 20)
            };
            this.Controls.Add(colHdr);

            _scoreList = new ListBox
            {
                Location      = new Point(10, 82),
                Size          = new Size(680, 450),
                BackColor     = Color.Black,
                ForeColor     = greenText,
                Font          = smallFont,
                BorderStyle   = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.None
            };
            this.Controls.Add(_scoreList);

            _statusLabel = new Label
            {
                Text      = "",
                Location  = new Point(10, 538),
                Size      = new Size(460, 24),
                Font      = smallFont,
                ForeColor = Color.FromArgb(130, 150, 130),
                BackColor = Color.Transparent
            };
            this.Controls.Add(_statusLabel);

            _refreshBtn = new Button
            {
                Text      = "REFRESH",
                Location  = new Point(480, 532),
                Size      = new Size(100, 32),
                BackColor = Color.FromArgb(20, 40, 20),
                ForeColor = greenText,
                Font      = stdFont,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand
            };
            _refreshBtn.Click += async (s, e) => await LoadAsync();
            this.Controls.Add(_refreshBtn);

            var closeBtn = new Button
            {
                Text      = "CLOSE",
                Location  = new Point(590, 532),
                Size      = new Size(100, 32),
                BackColor = Color.FromArgb(40, 10, 10),
                ForeColor = Color.White,
                Font      = stdFont,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand
            };
            closeBtn.Click += (s, e) => this.Close();
            this.Controls.Add(closeBtn);
        }

        public async Task LoadAsync()
        {
            _statusLabel.Text    = "Loading...";
            _refreshBtn.Enabled  = false;
            _scoreList.Items.Clear();

            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var resp = await http.GetStringAsync($"{_serverUrl.TrimEnd('/')}/api/leaderboard");
                var doc  = JsonDocument.Parse(resp);
                var arr  = doc.RootElement.GetProperty("scores").EnumerateArray();

                var scores = new List<LeaderboardEntry>();
                foreach (var e in arr)
                {
                    scores.Add(new LeaderboardEntry
                    {
                        Name      = e.GetProperty("name").GetString()   ?? "?",
                        Nation    = e.GetProperty("nation").GetString()  ?? "?",
                        Score     = e.GetProperty("score").GetInt64(),
                        Seconds   = e.TryGetProperty("seconds",   out var sv) ? sv.GetInt32() : 0,
                        NukesUsed = e.TryGetProperty("nukesUsed", out var nv) ? nv.GetInt32() : 0,
                    });
                }

                if (scores.Count == 0)
                {
                    _scoreList.Items.Add("  No scores yet — be the first to win!");
                    _statusLabel.Text = "";
                }
                else
                {
                    for (int i = 0; i < scores.Count; i++)
                    {
                        var e    = scores[i];
                        string t = $"{e.Seconds / 60}m{e.Seconds % 60:D2}s";
                        string medal = i == 0 ? "★" : i == 1 ? "▲" : i == 2 ? "●" : " ";
                        _scoreList.Items.Add($"  {medal} #{i + 1,-3}  {e.Score,12:N0}   {e.Name,-14}  ({e.Nation}, {t})");
                    }
                    _statusLabel.Text = $"Showing top {scores.Count} commanders worldwide";
                }
            }
            catch (Exception ex)
            {
                _scoreList.Items.Add($"  Failed to load: {ex.Message}");
                _statusLabel.Text = "Network error";
            }
            finally
            {
                _refreshBtn.Enabled = true;
            }
        }
    }
}
