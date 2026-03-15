using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace fallover_67
{
    public class ShopForm : Form
    {
        // --- REFINED TACTICAL COLORS (Lower Contrast, Better Vibe) ---
        private readonly Color bgDark = Color.FromArgb(8, 12, 8);         // Deep radar green-black
        private readonly Color gridLine = Color.FromArgb(15, 30, 15);     // Faint background grid
        private readonly Color termBorder = Color.FromArgb(0, 120, 0);    // Muted border green
        private readonly Color termText = Color.FromArgb(80, 220, 80);    // Soft primary text
        private readonly Color termHighlight = Color.FromArgb(0, 255, 200); // Cyan highlights
        private readonly Color termWarning = Color.FromArgb(220, 100, 50);  // Muted orange/red
        private readonly Color termMoney = Color.FromArgb(200, 200, 50);    // Soft gold

        // --- FONTS ---
        private readonly Font fontLarge = new Font("Consolas", 14F, FontStyle.Bold);
        private readonly Font fontStandard = new Font("Consolas", 9F, FontStyle.Regular);
        private readonly Font fontBold = new Font("Consolas", 10F, FontStyle.Bold);
        private readonly Font fontTech = new Font("Consolas", 7F, FontStyle.Regular);

        private TerminalBox mainContainer;
        private FlowLayoutPanel gridPanel;
        private Label lblTreasury;
        private Label lblLog;
        private string currentCategory = "ALL";

        // Dragging API
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();

        public ShopForm()
        {
            this.Text = "BLACK MARKET TERMINAL";
            this.Size = new Size(1100, 750);
            this.BackColor = bgDark;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.None;
            this.DoubleBuffered = true;

            BuildUI();
            LoadItems();
        }

        // Draws the cool radar grid on the form background
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            using (Pen p = new Pen(gridLine, 1))
            {
                for (int x = 0; x < Width; x += 40) e.Graphics.DrawLine(p, x, 0, x, Height);
                for (int y = 0; y < Height; y += 40) e.Graphics.DrawLine(p, 0, y, Width, y);
            }
            
            // Outer Form Border
            using (Pen borderPen = new Pen(termBorder, 2))
            {
                e.Graphics.DrawRectangle(borderPen, 1, 1, Width - 2, Height - 2);
            }
        }

        private void BuildUI()
        {
            this.Controls.Clear();

            // --- TOP HEADER ---
            Panel header = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent, Cursor = Cursors.Default };
            header.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); } };
            
            Label lblTitle = new Label { Text = "SYS.NET // GLOBAL BLACK MARKET UPLINK // SECURE", Font = fontBold, ForeColor = termBorder, Location = new Point(15, 12), AutoSize = true };
            header.Controls.Add(lblTitle);

            Button btnClose = new Button { Text = "[X]", Font = fontBold, ForeColor = termWarning, FlatStyle = FlatStyle.Flat, Dock = DockStyle.Right, Width = 60, Cursor = Cursors.Hand };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 0, 0);
            btnClose.Click += (s, e) => this.Close();
            header.Controls.Add(btnClose);

            // --- BOTTOM FOOTER ---
            TerminalBox footer = new TerminalBox { Dock = DockStyle.Bottom, Height = 100, Title = "TERMINAL LOG", LineColor = termBorder };
            footer.Padding = new Padding(15, 25, 15, 10);
            
            lblLog = new Label { Text = "> Uplink established. Encryption key valid.", Font = fontStandard, ForeColor = termText, Dock = DockStyle.Fill };
            footer.Controls.Add(lblLog);

            lblTreasury = new Label { Font = fontLarge, ForeColor = termMoney, Dock = DockStyle.Right, Width = 250, TextAlign = ContentAlignment.BottomRight };
            footer.Controls.Add(lblTreasury);

            // --- MAIN GRID ---
            mainContainer = new TerminalBox { Dock = DockStyle.Fill, Title = "AVAILABLE ASSETS", LineColor = termBorder, BackColor = bgDark };
            mainContainer.Padding = new Padding(5, 30, 5, 5);
            
            gridPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10), BackColor = Color.Transparent };
            gridPanel.MouseEnter += (s, e) => gridPanel.Focus();
            mainContainer.Controls.Add(gridPanel);
            
            // --- ADD IN CAREFUL ORDER ---
            this.Controls.Add(header);
            this.Controls.Add(footer);
            this.Controls.Add(mainContainer);

            header.BringToFront();
            footer.BringToFront();
            mainContainer.SendToBack();

            UpdateTreasury();
        }

        private void LoadItems()
        {
            gridPanel.SuspendLayout();
            gridPanel.Controls.Clear();

            // ARSENAL
            if (currentCategory == "ALL" || currentCategory == "ARSENAL")
            {
                gridPanel.Controls.Add(CreateCard("Standard Nuke", "Yield: 15 Megatons\nBasic atomic payload.", 500, () => GameEngine.Player.StandardNukes++, 0, 0, "0x1A"));
                gridPanel.Controls.Add(CreateCard("Tsar Bomba", "Yield: 50 Megatons\nMassive thermonuclear destruction.", 2000, () => GameEngine.Player.MegaNukes++, 0, 0, "0x2B"));
                gridPanel.Controls.Add(CreateCard("Bio-Plague", "Type: Pathogen X\nIgnores standard enemy defenses.", 1500, () => GameEngine.Player.BioPlagues++, 0, 0, "0x3C"));
                gridPanel.Controls.Add(CreateCard("Orbital Laser", "Type: Directed Energy\nVaporizes enemy stockpiles.", 3000, () => GameEngine.Player.OrbitalLasers++, 0, 0, "0x4D"));
                gridPanel.Controls.Add(CreateCard("Sat Killer", "Type: Kinetic EMP\nBlinds enemy map for 90s.", 4000, () => GameEngine.Player.SatelliteMissiles++, 0, 0, "0x5E"));
            }

            // DEFENSES (Passing Current Level and Max Level to draw the cool visual bars)
            if (currentCategory == "ALL" || currentCategory == "DEFENSE")
            {
                gridPanel.Controls.Add(CreateCard("Iron Dome", "Intercepts incoming\nballistic missiles.", 1000, 
                    () => GameEngine.Player.IronDomeLevel++, GameEngine.Player.IronDomeLevel, 4, "0xD1"));
                
                gridPanel.Controls.Add(CreateCard("Deep Bunkers", "Saves population from\nblast radius zones.", 1500, 
                    () => GameEngine.Player.BunkerLevel++, GameEngine.Player.BunkerLevel, 4, "0xD2"));
                
                gridPanel.Controls.Add(CreateCard("Vaccine", "Protects population from\nbiological attacks.", 800, 
                    () => GameEngine.Player.VaccineLevel++, GameEngine.Player.VaccineLevel, 4, "0xD3"));
            }

            // INDUSTRY & SPECIALS
            if (currentCategory == "ALL" || currentCategory == "INDUSTRY")
            {
                gridPanel.Controls.Add(CreateCard("Industrial Complex", "Increases national output.\nUnlocks submarine tech.", 2500, 
                    () => GameEngine.Player.IndustryLevel++, GameEngine.Player.IndustryLevel, 5, "0xI1"));

                if (GameEngine.Player.IndustryLevel >= 2)
                {
                    gridPanel.Controls.Add(CreateCard("Nuclear Sub", "Stealth nuclear platform.\nSpawns in Arctic waters.", 1200, 
                        () => BuySubmarine(), 0, 0, "0xS1"));
                }
            }

            gridPanel.ResumeLayout();
        }

        private Control CreateCard(string name, string desc, long cost, Action onBuy, int currentLvl, int maxLvl, string hexId)
        {
            bool isMaxed = maxLvl > 0 && currentLvl >= maxLvl;
            Color accentColor = isMaxed ? termBorder : termHighlight;

            TerminalBox card = new TerminalBox { 
                Width = 260, Height = 180, Margin = new Padding(10), Title = name.ToUpper(), 
                LineColor = isMaxed ? Color.FromArgb(30, 50, 30) : termBorder, Cursor = isMaxed ? Cursors.No : Cursors.Hand 
            };

            // Fake Tech ID
            Label lblId = new Label { Text = $"ID:{hexId}", Font = fontTech, ForeColor = termBorder, Location = new Point(210, 5), AutoSize = true };
            
            // Description
            Label lblDesc = new Label { Text = desc, Font = fontStandard, ForeColor = termText, Location = new Point(15, 30), Size = new Size(230, 40) };

            // Level Bar Graphic (e.g. [■■□□])
            Label lblLevel = new Label { Font = fontStandard, ForeColor = accentColor, Location = new Point(15, 80), AutoSize = true };
            if (maxLvl > 0)
            {
                string bar = "SYS.LVL: [";
                for (int i = 0; i < maxLvl; i++) bar += i < currentLvl ? "■" : "-";
                bar += "]";
                lblLevel.Text = bar;
            }
            else { lblLevel.Text = "SYS.LVL: [N/A]"; lblLevel.ForeColor = termBorder; }

            // Cost
            Label lblCost = new Label { Text = isMaxed ? "COST: ---" : $"COST: ${cost:N0}M", Font = fontBold, ForeColor = isMaxed ? termBorder : termMoney, Location = new Point(15, 110), AutoSize = true };

            // Action Text
            Label lblAction = new Label { Text = isMaxed ? "STATUS: MAX CAPACITY" : "> CLICK TO ACQUIRE <", Font = fontBold, ForeColor = isMaxed ? termBorder : termWarning, Location = new Point(15, 145), AutoSize = true };

            card.Controls.Add(lblId);
            card.Controls.Add(lblDesc);
            card.Controls.Add(lblLevel);
            card.Controls.Add(lblCost);
            card.Controls.Add(lblAction);

            // Hover & Click Logic
            if (!isMaxed)
            {
                var clickTargets = new Control[] { card, lblId, lblDesc, lblLevel, lblCost, lblAction };
                foreach (var ctrl in clickTargets)
                {
                    ctrl.MouseEnter += (s, e) => { card.BackColor = Color.FromArgb(15, 35, 15); lblAction.Text = "> [ AUTHORIZE ] <"; lblAction.ForeColor = termHighlight; };
                    ctrl.MouseLeave += (s, e) => { card.BackColor = Color.Transparent; lblAction.Text = "> CLICK TO ACQUIRE <"; lblAction.ForeColor = termWarning; };
                    ctrl.Click += (s, e) =>
                    {
                        if (GameEngine.Player.Money >= cost)
                        {
                            GameEngine.Player.Money -= cost;
                            onBuy.Invoke();
                            UpdateTreasury();
                            WriteLog($"[SUCCESS] ACQUIRED ASSET: {name.ToUpper()}");
                            LoadItems(); // Refresh to update levels
                        }
                        else
                        {
                            WriteLog($"[DENIED] INSUFFICIENT FUNDS FOR {name.ToUpper()}", termWarning);
                        }
                    };
                }
            }
            return card;
        }

        private void UpdateTreasury()
        {
            lblTreasury.Text = $"TREASURY:\n${GameEngine.Player.Money:N0}M";
        }

        private void BuySubmarine()
        {
            string subName = "USS " + (GameEngine.Submarines.Count + 1);
            
            // Custom naming dialog
            var dlg = new Form { Text = "COMMISSION SUBMARINE", Size = new Size(350, 180), BackColor = bgDark, FormBorderStyle = FormBorderStyle.FixedToolWindow, StartPosition = FormStartPosition.CenterParent };
            var lbl = new Label { Text = "ENTER REGISTRY NAME:", ForeColor = termHighlight, Font = fontBold, Location = new Point(20, 20), AutoSize = true };
            var txt = new TextBox { Text = subName, Location = new Point(20, 50), Width = 290, BackColor = Color.Black, ForeColor = termHighlight, Font = fontStandard, BorderStyle = BorderStyle.FixedSingle };
            var btn = new Button { Text = "CONFIRM", Location = new Point(20, 90), Width = 100, Height = 30, BackColor = termBorder, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btn.Click += (s, e) => { subName = txt.Text.Trim(); dlg.DialogResult = DialogResult.OK; dlg.Close(); };
            dlg.Controls.AddRange(new Control[] { lbl, txt, btn });
            
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            // Arctic coordinates (Top of the map)
            Random rnd = new Random();
            float spawnX = (float)(rnd.NextDouble() * 360 - 180);
            float spawnY = (float)(80 + rnd.NextDouble() * 5); // 80 to 85 Latitude

            var sub = new Submarine {
                Name = subName,
                OwnerId = GameEngine.Player.NationName,
                MapX = spawnX,
                MapY = spawnY,
                TargetX = spawnX,
                TargetY = spawnY,
                Health = 100,
                NukeCount = 0
            };
            GameEngine.Submarines.Add(sub);
            ProfileManager.RecordSubmarineDeployed();
            WriteLog($"[ASSET DEPLOYED] {subName} is active in ARCTIC SECTOR.");
            
            // Broadcast creation
            var parent = this.Owner as ControlPanelForm;
            if (parent != null && parent.IsMultiplayer)
            {
                parent.BroadcastSubCreate(sub);
            }
        }

        private async void WriteLog(string message, Color? color = null)
        {
            lblLog.ForeColor = color ?? termHighlight;
            lblLog.Text = $"> {message}\n> Awaiting commander input...";
            
            // Tech Blink Effect
            lblLog.Visible = false; await Task.Delay(40);
            lblLog.Visible = true; await Task.Delay(40);
            lblLog.Visible = false; await Task.Delay(40);
            lblLog.Visible = true;
        }

        // =========================================================================
        // CUSTOM TERMINAL BOX CONTROL
        // Completely prevents clipping/cut-offs by drawing borders mathematically
        // =========================================================================
        private class TerminalBox : Panel
        {
            public string Title { get; set; } = "";
            public Color LineColor { get; set; } = Color.Lime;
            private Font titleFont = new Font("Consolas", 9F, FontStyle.Bold);

            public TerminalBox()
            {
                this.SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                this.BackColor = Color.FromArgb(8, 12, 8); // Deep radar green-black
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.None; // Keeps lines crisp

                // Draw exactly 1 pixel inside the bounds so nothing is cut off
                Rectangle rect = new Rectangle(1, 7, this.Width - 3, this.Height - 9);

                using (Pen p = new Pen(LineColor, 1))
                {
                    g.DrawRectangle(p, rect);
                }

                // Draw title breaking the top line
                if (!string.IsNullOrEmpty(Title))
                {
                    string displayTitle = $"-{Title}-";
                    SizeF size = g.MeasureString(displayTitle, titleFont);
                    
                    // Clear the line behind text (Requires knowing parent background, we assume Dark Green)
                    using (SolidBrush clearBrush = new SolidBrush(Color.FromArgb(8, 12, 8)))
                    {
                        g.FillRectangle(clearBrush, 15, 0, size.Width, 14);
                    }
                    
                    using (SolidBrush textBrush = new SolidBrush(LineColor))
                    {
                        g.DrawString(displayTitle, titleFont, textBrush, 15, 0);
                    }
                }
                
                // Draw cool little corner accents
                using (Pen thickPen = new Pen(LineColor, 2))
                {
                    g.DrawLine(thickPen, rect.X, rect.Y, rect.X + 5, rect.Y); // Top Left
                    g.DrawLine(thickPen, rect.X, rect.Y, rect.X, rect.Y + 5); 
                    
                    g.DrawLine(thickPen, rect.Right, rect.Bottom, rect.Right - 5, rect.Bottom); // Bottom Right
                    g.DrawLine(thickPen, rect.Right, rect.Bottom, rect.Right, rect.Bottom - 5); 
                }
            }
        }
    }
}