using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace fallover_67
{
    public class PlayerListForm : Form
    {
        private readonly string _serverUrl;
        private List<PlayerProfile> _allProfiles = new();
        private List<PlayerProfile> _filteredProfiles = new();

        // Theme
        private Color bgDark = Color.FromArgb(8, 12, 8);
        private Color panelBg = Color.FromArgb(12, 18, 12);
        private Color rowAlt = Color.FromArgb(16, 24, 16);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color redText = Color.FromArgb(255, 50, 50);
        private Color cyanText = Color.FromArgb(0, 255, 200);
        private Color goldText = Color.FromArgb(255, 215, 0);
        private Color dimText = Color.FromArgb(50, 80, 50);
        private Color borderColor = Color.FromArgb(0, 120, 0);

        private Font fontHuge = new Font("Consolas", 20F, FontStyle.Bold);
        private Font fontLarge = new Font("Consolas", 14F, FontStyle.Bold);
        private Font fontStd = new Font("Consolas", 10F, FontStyle.Bold);
        private Font fontSmall = new Font("Consolas", 9F, FontStyle.Bold);
        private Font fontTech = new Font("Consolas", 7F, FontStyle.Regular);
        private Font fontRow = new Font("Consolas", 9F, FontStyle.Regular);

        private TextBox txtSearch;
        private Panel listPanel;
        private Label lblStatus;
        private Label lblCount;

        public PlayerListForm(string serverUrl)
        {
            _serverUrl = serverUrl;
            BuildForm();
        }

        private void BuildForm()
        {
            this.Text = "☢ COMMANDER DATABASE ☢";
            this.ClientSize = new Size(1000, 640);
            this.BackColor = bgDark;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.DoubleBuffered = true;

            // ── Header ──────────────────────────────────────────────────────
            var headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1000, 60),
                BackColor = Color.FromArgb(10, 18, 12)
            };
            headerPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(60, cyanText), 1);
                e.Graphics.DrawLine(pen, 0, 59, 1000, 59);
            };

            var titleLbl = new Label
            {
                Text = "☢  COMMANDER DATABASE  ☢",
                Location = new Point(0, 8),
                Size = new Size(1000, 32),
                Font = fontHuge,
                ForeColor = cyanText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(titleLbl);

            var subtitleLbl = new Label
            {
                Text = "GLOBAL PLAYER REGISTRY  —  VIEW STATS FOR ANY COMMANDER",
                Location = new Point(0, 40),
                Size = new Size(1000, 16),
                Font = fontTech,
                ForeColor = Color.FromArgb(80, 120, 80),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(subtitleLbl);
            this.Controls.Add(headerPanel);

            // ── Search Bar ──────────────────────────────────────────────────
            var searchBox = MakeTerminalBox("SEARCH", 10, 68, 980, 50);

            txtSearch = new TextBox
            {
                Location = new Point(15, 20),
                Size = new Size(500, 24),
                BackColor = Color.FromArgb(5, 10, 5),
                ForeColor = greenText,
                Font = fontStd,
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 24
            };
            txtSearch.PlaceholderText = "Type to filter by username...";
            txtSearch.TextChanged += (s, e) => ApplyFilter();
            searchBox.Controls.Add(txtSearch);

            lblCount = new Label
            {
                Text = "",
                Location = new Point(530, 22),
                Size = new Size(200, 20),
                Font = fontSmall,
                ForeColor = dimText,
                BackColor = Color.Transparent
            };
            searchBox.Controls.Add(lblCount);

            var btnRefresh = new Button
            {
                Text = "REFRESH",
                Location = new Point(860, 17),
                Size = new Size(100, 28),
                BackColor = Color.FromArgb(0, 40, 30),
                ForeColor = cyanText,
                Font = fontSmall,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRefresh.FlatAppearance.BorderColor = cyanText;
            btnRefresh.Click += async (s, e) => await LoadProfilesAsync();
            searchBox.Controls.Add(btnRefresh);
            this.Controls.Add(searchBox);

            // ── Column Headers ──────────────────────────────────────────────
            var headerRow = new Panel
            {
                Location = new Point(10, 125),
                Size = new Size(980, 24),
                BackColor = Color.FromArgb(18, 30, 18)
            };
            headerRow.Paint += (s, e) =>
            {
                using var pen = new Pen(borderColor, 1);
                e.Graphics.DrawLine(pen, 0, 23, 980, 23);
            };

            int[] colX = { 10, 50, 330, 460, 550, 650, 750, 860 };
            string[] colNames = { "#", "COMMANDER", "RANK", "W/L", "KILLS", "NUKES", "SCORE", "PLAY TIME" };
            for (int i = 0; i < colNames.Length; i++)
            {
                headerRow.Controls.Add(new Label
                {
                    Text = colNames[i],
                    Location = new Point(colX[i], 3),
                    AutoSize = true,
                    Font = fontSmall,
                    ForeColor = amberText,
                    BackColor = Color.Transparent
                });
            }
            this.Controls.Add(headerRow);

            // ── Scrollable List Panel ────────────────────────────────────────
            listPanel = new Panel
            {
                Location = new Point(10, 150),
                Size = new Size(980, 430),
                BackColor = panelBg,
                AutoScroll = true
            };
            listPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(borderColor, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, listPanel.Width - 1, listPanel.Height - 1);
            };
            this.Controls.Add(listPanel);

            // ── Status Bar ──────────────────────────────────────────────────
            lblStatus = new Label
            {
                Text = "LOADING...",
                Location = new Point(10, 585),
                Size = new Size(600, 20),
                Font = fontSmall,
                ForeColor = amberText,
                BackColor = Color.Transparent
            };
            this.Controls.Add(lblStatus);

            // ── Close Button ────────────────────────────────────────────────
            var btnClose = new Button
            {
                Text = "CLOSE",
                Location = new Point(820, 582),
                Size = new Size(170, 36),
                BackColor = Color.FromArgb(40, 10, 10),
                ForeColor = Color.White,
                Font = fontStd,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = redText;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        public async Task LoadProfilesAsync()
        {
            lblStatus.Text = "CONNECTING TO GLOBAL DATABASE...";
            lblStatus.ForeColor = amberText;

            _allProfiles = await ProfileManager.FetchAllProfilesAsync(_serverUrl);

            if (_allProfiles.Count == 0)
            {
                lblStatus.Text = "NO PROFILES FOUND ON SERVER  —  Players must sync their profile first.";
                lblStatus.ForeColor = Color.FromArgb(120, 80, 40);
            }
            else
            {
                lblStatus.Text = $"DATABASE ONLINE  —  {_allProfiles.Count} commander(s) registered.";
                lblStatus.ForeColor = greenText;
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = txtSearch.Text.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(query))
                _filteredProfiles = new List<PlayerProfile>(_allProfiles);
            else
                _filteredProfiles = _allProfiles
                    .Where(p => p.Username.ToLowerInvariant().Contains(query))
                    .ToList();

            // Sort by highest score desc
            _filteredProfiles = _filteredProfiles
                .OrderByDescending(p => p.HighestScore)
                .ToList();

            lblCount.Text = $"{_filteredProfiles.Count} of {_allProfiles.Count}";
            RebuildList();
        }

        private void RebuildList()
        {
            listPanel.SuspendLayout();
            listPanel.Controls.Clear();

            int rowH = 38;
            int[] colX = { 10, 50, 330, 460, 550, 650, 750, 860 };

            for (int i = 0; i < _filteredProfiles.Count; i++)
            {
                var p = _filteredProfiles[i];
                bool isAlt = i % 2 == 1;

                var row = new Panel
                {
                    Location = new Point(0, i * rowH),
                    Size = new Size(960, rowH),
                    BackColor = isAlt ? rowAlt : panelBg,
                    Cursor = Cursors.Hand
                };

                // Row number
                row.Controls.Add(MakeCell($"{i + 1}.", colX[0], dimText));

                // Username
                var nameLbl = MakeCell(p.Username, colX[1], cyanText);
                nameLbl.AutoSize = true;
                nameLbl.Font = fontStd;
                row.Controls.Add(nameLbl);

                // Rank
                row.Controls.Add(MakeCell(GetRankShort(p), colX[2], amberText));

                // W/L
                string wl = $"{p.MatchesWon}/{p.MatchesLost}";
                row.Controls.Add(MakeCell(wl, colX[3], p.MatchesWon > p.MatchesLost ? goldText : greenText));

                // Kills
                row.Controls.Add(MakeCell(FormatNumber(p.TotalKills), colX[4], redText));

                // Nukes
                row.Controls.Add(MakeCell($"{p.TotalNukesLaunched}", colX[5], greenText));

                // High Score
                row.Controls.Add(MakeCell(FormatNumber(p.HighestScore), colX[6], goldText));

                // Play Time
                row.Controls.Add(MakeCell(p.PlayTimeFormatted, colX[7], dimText));

                // Bottom border
                row.Paint += (s, e) =>
                {
                    using var pen = new Pen(Color.FromArgb(20, 40, 20), 1);
                    e.Graphics.DrawLine(pen, 0, rowH - 1, 960, rowH - 1);
                };

                // Hover
                var allControls = row.Controls.Cast<Control>().ToArray();
                foreach (var ctrl in allControls.Append(row))
                {
                    ctrl.MouseEnter += (s, e) => row.BackColor = Color.FromArgb(25, 40, 25);
                    ctrl.MouseLeave += (s, e) =>
                    {
                        if (!row.ClientRectangle.Contains(row.PointToClient(Cursor.Position)))
                            row.BackColor = isAlt ? rowAlt : panelBg;
                    };
                }

                // Click to view profile
                var profile = p;
                foreach (var ctrl in allControls.Append(row))
                {
                    ctrl.Click += (s, e) =>
                    {
                        var viewForm = new ViewProfileForm(profile);
                        viewForm.ShowDialog(this);
                    };
                }

                listPanel.Controls.Add(row);
            }

            // Empty state
            if (_filteredProfiles.Count == 0 && _allProfiles.Count > 0)
            {
                listPanel.Controls.Add(new Label
                {
                    Text = "No commanders match your search.",
                    Location = new Point(0, 20),
                    Size = new Size(780, 30),
                    Font = fontStd,
                    ForeColor = dimText,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleCenter
                });
            }
            else if (_allProfiles.Count == 0)
            {
                listPanel.Controls.Add(new Label
                {
                    Text = "No commanders registered yet.\nOpen your PROFILE and click SAVE & SYNC to be the first!",
                    Location = new Point(0, 60),
                    Size = new Size(780, 60),
                    Font = fontStd,
                    ForeColor = amberText,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleCenter
                });
            }

            listPanel.ResumeLayout();
        }

        private Label MakeCell(string text, int x, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, 10),
                AutoSize = true,
                Font = fontRow,
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        private string GetRankShort(PlayerProfile p)
        {
            if (p.MatchesWon >= 100) return "★5 SUPREME";
            if (p.MatchesWon >= 50) return "★4 WARLORD";
            if (p.MatchesWon >= 25) return "★3 GENERAL";
            if (p.MatchesWon >= 10) return "★2 COLONEL";
            if (p.MatchesWon >= 5) return "★1 CAPTAIN";
            if (p.MatchesPlayed >= 1) return "RECRUIT";
            return "CADET";
        }

        private string FormatNumber(long n)
        {
            if (n >= 1_000_000_000) return $"{n / 1_000_000_000.0:F1}B";
            if (n >= 1_000_000) return $"{n / 1_000_000.0:F1}M";
            if (n >= 1_000) return $"{n / 1_000.0:F1}K";
            return n.ToString("N0");
        }

        private Panel MakeTerminalBox(string title, int x, int y, int w, int h)
        {
            var panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = panelBg
            };
            panel.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var rect = new Rectangle(0, 7, w - 1, h - 9);
                using var pen = new Pen(borderColor, 1);
                g.DrawRectangle(pen, rect);

                if (!string.IsNullOrEmpty(title))
                {
                    string display = $"-{title}-";
                    var size = g.MeasureString(display, fontTech);
                    using var clearBrush = new SolidBrush(panelBg);
                    g.FillRectangle(clearBrush, 15, 0, size.Width, 14);
                    using var textBrush = new SolidBrush(borderColor);
                    g.DrawString(display, fontTech, textBrush, 15, 0);
                }

                using var thickPen = new Pen(borderColor, 2);
                g.DrawLine(thickPen, rect.X, rect.Y, rect.X + 5, rect.Y);
                g.DrawLine(thickPen, rect.X, rect.Y, rect.X, rect.Y + 5);
                g.DrawLine(thickPen, rect.Right, rect.Bottom, rect.Right - 5, rect.Bottom);
                g.DrawLine(thickPen, rect.Right, rect.Bottom, rect.Right, rect.Bottom - 5);
            };
            return panel;
        }
    }
}
