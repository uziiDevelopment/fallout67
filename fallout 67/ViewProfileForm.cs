using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace fallover_67
{
    public class ViewProfileForm : Form
    {
        private readonly PlayerProfile _profile;

        // Theme
        private Color bgDark = Color.FromArgb(8, 12, 8);
        private Color panelBg = Color.FromArgb(12, 18, 12);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color redText = Color.FromArgb(255, 50, 50);
        private Color cyanText = Color.FromArgb(0, 255, 200);
        private Color goldText = Color.FromArgb(255, 215, 0);
        private Color borderColor = Color.FromArgb(0, 120, 0);

        private Font fontHuge = new Font("Consolas", 22F, FontStyle.Bold);
        private Font fontLarge = new Font("Consolas", 14F, FontStyle.Bold);
        private Font fontStd = new Font("Consolas", 10F, FontStyle.Bold);
        private Font fontSmall = new Font("Consolas", 9F, FontStyle.Bold);
        private Font fontTech = new Font("Consolas", 7F, FontStyle.Regular);

        public ViewProfileForm(PlayerProfile profile)
        {
            _profile = profile;
            BuildForm();
        }

        private void BuildForm()
        {
            var p = _profile;

            this.Text = $"INTEL DOSSIER — {p.Username.ToUpper()}";
            this.ClientSize = new Size(700, 580);
            this.BackColor = bgDark;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.DoubleBuffered = true;

            // ── Header ──────────────────────────────────────────────────────
            var headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(700, 70),
                BackColor = Color.FromArgb(15, 10, 25)
            };
            headerPanel.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(60, amberText), 1);
                e.Graphics.DrawLine(pen, 0, 69, 700, 69);
            };

            var titleLbl = new Label
            {
                Text = $"INTEL DOSSIER  —  {p.Username.ToUpper()}",
                Location = new Point(0, 8),
                Size = new Size(700, 35),
                Font = fontHuge,
                ForeColor = amberText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(titleLbl);

            string rank = GetRank(p);
            var rankLbl = new Label
            {
                Text = $"RANK: {rank}  |  ACTIVE SINCE: {p.CreatedAt:yyyy-MM-dd}  |  LAST SEEN: {p.LastPlayed:yyyy-MM-dd}",
                Location = new Point(0, 44),
                Size = new Size(700, 20),
                Font = fontTech,
                ForeColor = Color.FromArgb(130, 150, 130),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            headerPanel.Controls.Add(rankLbl);
            this.Controls.Add(headerPanel);

            // ── Left: Combat ────────────────────────────────────────────────
            var combatBox = MakeTerminalBox("COMBAT RECORD", 10, 80, 335, 220);
            int y = 22;
            void CombatRow(string label, string value, Color color)
            {
                combatBox.Controls.Add(new Label { Text = label, Location = new Point(12, y), Size = new Size(170, 20), Font = fontSmall, ForeColor = amberText, BackColor = Color.Transparent });
                combatBox.Controls.Add(new Label { Text = value, Location = new Point(185, y), Size = new Size(135, 20), Font = fontSmall, ForeColor = color, BackColor = Color.Transparent });
                y += 20;
            }

            CombatRow("TOTAL KILLS", $"{p.TotalKills:N0}", redText);
            CombatRow("NUKES LAUNCHED", $"{p.TotalNukesLaunched:N0}", greenText);
            CombatRow("  Standard", $"{p.StandardNukesLaunched:N0}", Color.FromArgb(40, 160, 40));
            CombatRow("  Tsar Bomba", $"{p.TsarBombasLaunched:N0}", Color.FromArgb(40, 160, 40));
            CombatRow("  Bio-Plague", $"{p.BioPlaguesLaunched:N0}", Color.FromArgb(40, 160, 40));
            CombatRow("  Orbital Laser", $"{p.OrbitalLasersFired:N0}", Color.FromArgb(40, 160, 40));
            CombatRow("  Sat Killer", $"{p.SatelliteKillersUsed:N0}", Color.FromArgb(40, 160, 40));
            CombatRow("NATIONS CONQUERED", $"{p.NationsConquered:N0}", goldText);
            CombatRow("INTERCEPTED", $"{p.MissilesIntercepted:N0}", cyanText);
            this.Controls.Add(combatBox);

            // ── Right: Matches ──────────────────────────────────────────────
            var matchBox = MakeTerminalBox("MATCH RECORD", 355, 80, 335, 220);
            y = 22;
            void MatchRow(string label, string value, Color color)
            {
                matchBox.Controls.Add(new Label { Text = label, Location = new Point(12, y), Size = new Size(170, 20), Font = fontSmall, ForeColor = amberText, BackColor = Color.Transparent });
                matchBox.Controls.Add(new Label { Text = value, Location = new Point(185, y), Size = new Size(135, 20), Font = fontSmall, ForeColor = color, BackColor = Color.Transparent });
                y += 20;
            }

            MatchRow("MATCHES PLAYED", $"{p.MatchesPlayed}", greenText);
            MatchRow("VICTORIES", $"{p.MatchesWon}", goldText);
            MatchRow("DEFEATS", $"{p.MatchesLost}", redText);
            MatchRow("WIN RATE", $"{p.WinRate:F1}%", p.WinRate >= 50 ? goldText : amberText);
            MatchRow("MP GAMES", $"{p.MultiplayerGamesPlayed}", cyanText);
            MatchRow("MP WINS", $"{p.MultiplayerWins}", cyanText);
            MatchRow("PLAY TIME", p.PlayTimeFormatted, greenText);
            string fastest = p.ShortestVictorySeconds < int.MaxValue ? FormatTime(p.ShortestVictorySeconds) : "N/A";
            MatchRow("FASTEST VICTORY", fastest, goldText);
            MatchRow("FAVORITE NATION", p.FavoriteNation, cyanText);
            this.Controls.Add(matchBox);

            // ── Bottom Left: Scores ─────────────────────────────────────────
            var scoreBox = MakeTerminalBox("SCORES", 10, 310, 335, 100);
            y = 22;
            void ScoreRow(string label, string value, Color color)
            {
                scoreBox.Controls.Add(new Label { Text = label, Location = new Point(12, y), Size = new Size(170, 20), Font = fontSmall, ForeColor = amberText, BackColor = Color.Transparent });
                scoreBox.Controls.Add(new Label { Text = value, Location = new Point(185, y), Size = new Size(135, 20), Font = fontSmall, ForeColor = color, BackColor = Color.Transparent });
                y += 22;
            }

            ScoreRow("HIGHEST SCORE", $"{p.HighestScore:N0}", goldText);
            ScoreRow("TOTAL EARNED", $"{p.TotalScoreEarned:N0}", greenText);
            ScoreRow("DAMAGE TAKEN", $"{p.DamageAbsorbed:N0}", redText);
            this.Controls.Add(scoreBox);

            // ── Bottom Right: Operations ────────────────────────────────────
            var opsBox = MakeTerminalBox("OPERATIONS", 355, 310, 335, 100);
            y = 22;
            void OpsRow(string label, string value, Color color)
            {
                opsBox.Controls.Add(new Label { Text = label, Location = new Point(12, y), Size = new Size(170, 20), Font = fontSmall, ForeColor = amberText, BackColor = Color.Transparent });
                opsBox.Controls.Add(new Label { Text = value, Location = new Point(185, y), Size = new Size(135, 20), Font = fontSmall, ForeColor = color, BackColor = Color.Transparent });
                y += 22;
            }

            string troopLine = $"{p.TroopMissionsSucceeded}/{p.TroopMissionsLaunched}";
            OpsRow("TROOP MISSIONS", troopLine, greenText);
            OpsRow("ALLIANCES", $"Formed: {p.AlliancesFormed} | Broken: {p.AlliancesBroken}", amberText);
            OpsRow("SUBMARINES", $"Deployed: {p.SubmarinesDeployed}", cyanText);
            this.Controls.Add(opsBox);

            // ── Big stat highlight ──────────────────────────────────────────
            var highlightBox = MakeTerminalBox("THREAT ASSESSMENT", 10, 420, 680, 100);
            
            // K/D-style stat
            float avgKillsPerGame = p.MatchesPlayed > 0 ? (float)p.TotalKills / p.MatchesPlayed : 0;
            
            var kpgLabel = new Label
            {
                Text = $"AVG KILLS/GAME: {avgKillsPerGame:N0}",
                Location = new Point(20, 25),
                Size = new Size(300, 30),
                Font = fontLarge,
                ForeColor = redText,
                BackColor = Color.Transparent
            };
            highlightBox.Controls.Add(kpgLabel);

            float avgNukesPerGame = p.MatchesPlayed > 0 ? (float)p.TotalNukesLaunched / p.MatchesPlayed : 0;
            var npgLabel = new Label
            {
                Text = $"AVG NUKES/GAME: {avgNukesPerGame:F1}",
                Location = new Point(340, 25),
                Size = new Size(300, 30),
                Font = fontLarge,
                ForeColor = amberText,
                BackColor = Color.Transparent
            };
            highlightBox.Controls.Add(npgLabel);

            // Threat level based on stats
            int threatLevel = CalculateThreatLevel(p);
            string threatStr = new string('▰', threatLevel) + new string('▱', 10 - threatLevel);
            Color threatColor = threatLevel >= 8 ? redText : threatLevel >= 5 ? amberText : greenText;
            
            var threatLabel = new Label
            {
                Text = $"DANGER LEVEL: [{threatStr}] {threatLevel}/10",
                Location = new Point(20, 60),
                Size = new Size(640, 28),
                Font = fontStd,
                ForeColor = threatColor,
                BackColor = Color.Transparent
            };
            highlightBox.Controls.Add(threatLabel);
            this.Controls.Add(highlightBox);

            // ── Close Button ────────────────────────────────────────────────
            var btnClose = new Button
            {
                Text = "CLOSE DOSSIER",
                Location = new Point(260, 530),
                Size = new Size(180, 38),
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

        private int CalculateThreatLevel(PlayerProfile p)
        {
            int level = 0;
            if (p.MatchesWon >= 5) level++;
            if (p.MatchesWon >= 20) level++;
            if (p.MatchesWon >= 50) level++;
            if (p.WinRate >= 40) level++;
            if (p.WinRate >= 70) level++;
            if (p.TotalKills >= 500_000_000) level++;
            if (p.TotalKills >= 5_000_000_000) level++;
            if (p.TotalNukesLaunched >= 100) level++;
            if (p.NationsConquered >= 50) level++;
            if (p.HighestScore >= 1_000_000) level++;
            return Math.Min(10, level);
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
