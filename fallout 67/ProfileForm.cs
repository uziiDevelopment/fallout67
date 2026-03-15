using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace fallover_67
{
    public class ProfileForm : Form
    {
        private readonly string _serverUrl;

        // Theme
        private Color bgDark = Color.FromArgb(8, 12, 8);
        private Color panelBg = Color.FromArgb(12, 18, 12);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color redText = Color.FromArgb(255, 50, 50);
        private Color cyanText = Color.FromArgb(0, 255, 200);
        private Color goldText = Color.FromArgb(255, 215, 0);
        private Color dimText = Color.FromArgb(50, 80, 50);
        private Color borderColor = Color.FromArgb(0, 120, 0);

        private Font fontHuge = new Font("Consolas", 24F, FontStyle.Bold);
        private Font fontLarge = new Font("Consolas", 14F, FontStyle.Bold);
        private Font fontStd = new Font("Consolas", 10F, FontStyle.Bold);
        private Font fontSmall = new Font("Consolas", 9F, FontStyle.Bold);
        private Font fontTech = new Font("Consolas", 7F, FontStyle.Regular);

        private TextBox txtUsername;
        private Button btnSave;
        private Label lblSaveStatus;

        public ProfileForm(string serverUrl)
        {
            _serverUrl = serverUrl;
            BuildForm();
        }

        private void BuildForm()
        {
            this.Text = "☢ COMMANDER PROFILE ☢";
            this.ClientSize = new Size(880, 700);
            this.BackColor = bgDark;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.DoubleBuffered = true;

            var p = ProfileManager.CurrentProfile;

            // ── Header ──────────────────────────────────────────────────────
            var headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(880, 80),
                BackColor = Color.FromArgb(10, 25, 15)
            };
            headerPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(60, cyanText), 1);
                e.Graphics.DrawLine(pen, 0, 79, 880, 79);
            };

            var titleLbl = new Label
            {
                Text = "☢  COMMANDER PROFILE  ☢",
                Location = new Point(0, 10),
                Size = new Size(880, 40),
                Font = fontHuge,
                ForeColor = cyanText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(titleLbl);

            string rank = GetRank(p);
            var rankLbl = new Label
            {
                Text = $"RANK: {rank}  |  SINCE: {p.CreatedAt:yyyy-MM-dd}  |  LAST ACTIVE: {p.LastPlayed:yyyy-MM-dd}",
                Location = new Point(0, 50),
                Size = new Size(880, 22),
                Font = fontSmall,
                ForeColor = amberText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(rankLbl);
            this.Controls.Add(headerPanel);

            // ── Username Section ─────────────────────────────────────────────
            var usernameBox = MakeTerminalBox("CALLSIGN", 15, 90, 850, 60);

            var lblUsername = new Label
            {
                Text = "USERNAME:",
                Location = new Point(15, 22),
                Size = new Size(100, 26),
                Font = fontStd,
                ForeColor = amberText,
                BackColor = Color.Transparent
            };
            usernameBox.Controls.Add(lblUsername);

            txtUsername = new TextBox
            {
                Text = p.Username,
                Location = new Point(120, 22),
                Size = new Size(280, 26),
                BackColor = Color.FromArgb(5, 10, 5),
                ForeColor = greenText,
                Font = fontStd,
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 24
            };
            usernameBox.Controls.Add(txtUsername);

            btnSave = new Button
            {
                Text = "SAVE & SYNC",
                Location = new Point(415, 19),
                Size = new Size(140, 30),
                BackColor = Color.FromArgb(0, 60, 40),
                ForeColor = Color.White,
                Font = fontStd,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderColor = cyanText;
            btnSave.Click += async (s, e) => await SaveAndSyncAsync();
            usernameBox.Controls.Add(btnSave);

            lblSaveStatus = new Label
            {
                Text = "",
                Location = new Point(570, 24),
                Size = new Size(270, 22),
                Font = fontSmall,
                ForeColor = greenText,
                BackColor = Color.Transparent
            };
            usernameBox.Controls.Add(lblSaveStatus);
            this.Controls.Add(usernameBox);

            // ── Left Column: Combat Stats ───────────────────────────────────
            var combatBox = MakeTerminalBox("COMBAT RECORD", 15, 160, 420, 250);
            int y = 22;
            void CombatRow(string label, string value, Color color)
            {
                combatBox.Controls.Add(new Label { Text = label, Location = new Point(15, y), Size = new Size(210, 22), Font = fontSmall, ForeColor = amberText, BackColor = Color.Transparent });
                combatBox.Controls.Add(new Label { Text = value, Location = new Point(230, y), Size = new Size(170, 22), Font = fontSmall, ForeColor = color, BackColor = Color.Transparent });
                y += 22;
            }

            CombatRow("TOTAL KILLS", $"{p.TotalKills:N0}", redText);
            CombatRow("NUKES LAUNCHED", $"{p.TotalNukesLaunched:N0}", greenText);
            CombatRow("  ├ Standard", $"{p.StandardNukesLaunched:N0}", dimGreen());
            CombatRow("  ├ Tsar Bomba", $"{p.TsarBombasLaunched:N0}", dimGreen());
            CombatRow("  ├ Bio-Plague", $"{p.BioPlaguesLaunched:N0}", dimGreen());
            CombatRow("  ├ Orbital Laser", $"{p.OrbitalLasersFired:N0}", dimGreen());
            CombatRow("  └ Satellite Killer", $"{p.SatelliteKillersUsed:N0}", dimGreen());
            CombatRow("NATIONS CONQUERED", $"{p.NationsConquered:N0}", goldText);
            CombatRow("  └ Surrendered", $"{p.NationsSurrendered:N0}", dimGreen());
            CombatRow("MISSILES INTERCEPTED", $"{p.MissilesIntercepted:N0}", cyanText);
            this.Controls.Add(combatBox);

            // ── Right Column: Match Stats ───────────────────────────────────
            var matchBox = MakeTerminalBox("MATCH HISTORY", 445, 160, 420, 250);
            y = 22;
            void MatchRow(string label, string value, Color color)
            {
                matchBox.Controls.Add(new Label { Text = label, Location = new Point(15, y), Size = new Size(210, 22), Font = fontSmall, ForeColor = amberText, BackColor = Color.Transparent });
                matchBox.Controls.Add(new Label { Text = value, Location = new Point(230, y), Size = new Size(170, 22), Font = fontSmall, ForeColor = color, BackColor = Color.Transparent });
                y += 22;
            }

            MatchRow("MATCHES PLAYED", $"{p.MatchesPlayed}", greenText);
            MatchRow("VICTORIES", $"{p.MatchesWon}", goldText);
            MatchRow("DEFEATS", $"{p.MatchesLost}", redText);
            MatchRow("WIN RATE", $"{p.WinRate:F1}%", p.WinRate >= 50 ? goldText : amberText);
            MatchRow("MULTIPLAYER GAMES", $"{p.MultiplayerGamesPlayed}", cyanText);
            MatchRow("MULTIPLAYER WINS", $"{p.MultiplayerWins}", cyanText);
            MatchRow("TOTAL PLAY TIME", p.PlayTimeFormatted, greenText);
            MatchRow("LONGEST GAME", FormatTime(p.LongestGameSeconds), amberText);
            string fastest = p.ShortestVictorySeconds < int.MaxValue ? FormatTime(p.ShortestVictorySeconds) : "N/A";
            MatchRow("FASTEST VICTORY", fastest, goldText);
            MatchRow("FAVORITE NATION", p.FavoriteNation, cyanText);
            this.Controls.Add(matchBox);

            // ── Bottom Left: Score Stats ────────────────────────────────────
            var scoreBox = MakeTerminalBox("SCORING RECORD", 15, 420, 420, 120);
            y = 22;
            void ScoreRow(string label, string value, Color color)
            {
                scoreBox.Controls.Add(new Label { Text = label, Location = new Point(15, y), Size = new Size(210, 22), Font = fontSmall, ForeColor = amberText, BackColor = Color.Transparent });
                scoreBox.Controls.Add(new Label { Text = value, Location = new Point(230, y), Size = new Size(170, 22), Font = fontSmall, ForeColor = color, BackColor = Color.Transparent });
                y += 22;
            }

            ScoreRow("HIGHEST SCORE", $"{p.HighestScore:N0}", goldText);
            ScoreRow("TOTAL SCORE EARNED", $"{p.TotalScoreEarned:N0}", greenText);
            ScoreRow("DAMAGE ABSORBED", $"{p.DamageAbsorbed:N0}", redText);
            this.Controls.Add(scoreBox);

            // ── Bottom Right: Operations Stats ──────────────────────────────
            var opsBox = MakeTerminalBox("OPERATIONS", 445, 420, 420, 120);
            y = 22;
            void OpsRow(string label, string value, Color color)
            {
                opsBox.Controls.Add(new Label { Text = label, Location = new Point(15, y), Size = new Size(210, 22), Font = fontSmall, ForeColor = amberText, BackColor = Color.Transparent });
                opsBox.Controls.Add(new Label { Text = value, Location = new Point(230, y), Size = new Size(170, 22), Font = fontSmall, ForeColor = color, BackColor = Color.Transparent });
                y += 22;
            }

            string troopLine = $"{p.TroopMissionsSucceeded}/{p.TroopMissionsLaunched}";
            OpsRow("TROOP MISSIONS", troopLine, greenText);
            OpsRow("ALLIANCES FORMED", $"{p.AlliancesFormed}", cyanText);
            OpsRow("ALLIANCES BROKEN", $"{p.AlliancesBroken}", redText);
            string subLine = $"Deployed: {p.SubmarinesDeployed}  |  Lost: {p.SubmarinesLost}";
            OpsRow("SUBMARINES", subLine, amberText);
            this.Controls.Add(opsBox);

            // ── Browse Other Players ────────────────────────────────────────
            var btnBrowse = new Button
            {
                Text = "BROWSE COMMANDERS",
                Location = new Point(15, 555),
                Size = new Size(240, 42),
                BackColor = Color.FromArgb(0, 40, 60),
                ForeColor = cyanText,
                Font = fontStd,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnBrowse.FlatAppearance.BorderColor = cyanText;
            btnBrowse.Click += async (s, e) =>
            {
                var listForm = new PlayerListForm(_serverUrl);
                listForm.Show(this);
                await listForm.LoadProfilesAsync();
            };
            this.Controls.Add(btnBrowse);

            // ── Close Button ────────────────────────────────────────────────
            var btnClose = new Button
            {
                Text = "CLOSE",
                Location = new Point(625, 555),
                Size = new Size(240, 42),
                BackColor = Color.FromArgb(40, 10, 10),
                ForeColor = Color.White,
                Font = fontLarge,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = redText;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);

            // Status bar
            var statusBar = new Label
            {
                Text = $"PROFILE SYSTEM // LOCAL SAVE: {GetProfilePath()} // {DateTime.Now:HH:mm:ss}",
                Location = new Point(0, 610),
                Size = new Size(880, 20),
                Font = fontTech,
                ForeColor = Color.FromArgb(40, 60, 40),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(statusBar);
        }

        private async Task SaveAndSyncAsync()
        {
            string name = txtUsername.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter a callsign.", "INPUT ERROR");
                return;
            }

            ProfileManager.SetUsername(name);
            lblSaveStatus.ForeColor = greenText;
            lblSaveStatus.Text = "Profile saved locally.";

            try
            {
                btnSave.Enabled = false;
                btnSave.Text = "SYNCING...";
                await ProfileManager.SyncToServerAsync(_serverUrl);
                lblSaveStatus.ForeColor = cyanText;
                lblSaveStatus.Text = "✓ Synced to server!";
            }
            catch
            {
                lblSaveStatus.ForeColor = amberText;
                lblSaveStatus.Text = "Saved locally (server unreachable)";
            }
            finally
            {
                btnSave.Enabled = true;
                btnSave.Text = "SAVE & SYNC";
            }
        }

        private string GetRank(PlayerProfile p)
        {
            if (p.MatchesWon >= 100) return "★★★★★ SUPREME COMMANDER";
            if (p.MatchesWon >= 50) return "★★★★ WAR LORD";
            if (p.MatchesWon >= 25) return "★★★ GENERAL";
            if (p.MatchesWon >= 10) return "★★ COLONEL";
            if (p.MatchesWon >= 5) return "★ CAPTAIN";
            if (p.MatchesPlayed >= 1) return "RECRUIT";
            return "CADET";
        }

        private string FormatTime(int seconds)
        {
            if (seconds <= 0) return "0m 0s";
            if (seconds >= 3600) return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
            return $"{seconds / 60}m {seconds % 60}s";
        }

        private Color dimGreen() => Color.FromArgb(40, 160, 40);

        private string GetProfilePath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Fallout67", "profile.json");
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
