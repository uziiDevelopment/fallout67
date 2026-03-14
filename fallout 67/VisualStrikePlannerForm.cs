using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.MapProviders;

namespace fallover_67
{
    public class VisualStrikePlannerForm : Form
    {
        private class SelectedTarget
        {
            public string Name;
            public int Count;
        }

        private List<string> _availableNations;
        private List<SelectedTarget> _selectedPlan = new List<SelectedTarget>();
        private int _maxNukes;
        private string _weaponName;
        private int _stock;

        private Panel pnlList;
        private RadarPanel mapPanel;
        private Label lblHeader;
        private Label lblTotal;
        private Button btnExecute;
        private Button btnAbort;

        private System.Windows.Forms.Timer _animTimer;
        private Dictionary<string, PointF> _currentScreenCoords = new Dictionary<string, PointF>();

        public List<(string target, int count)> FinalPlan { get; private set; }

        public VisualStrikePlannerForm(List<string> nations, string weaponName, int stock, int maxNukes)
        {
            _availableNations = nations.OrderBy(n => n).ToList();
            _weaponName = weaponName;
            _stock = stock;
            _maxNukes = maxNukes;

            InitializeUI();
            
            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (s, e) => mapPanel.Invalidate();
            _animTimer.Start();
        }

        private PointF ToScreenPoint(PointLatLng p)
        {
            GPoint gp = mapPanel.FromLatLngToLocal(p);
            return new PointF((float)gp.X, (float)gp.Y);
        }

        private void InitializeUI()
        {
            this.Text = "TACTICAL STRIKE - VISUAL COORDINATOR";
            this.Size = new Size(1150, 750);
            this.BackColor = Color.FromArgb(10, 10, 12);
            this.ForeColor = Color.Cyan;
            this.Font = new Font("Consolas", 10F, FontStyle.Bold);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.DoubleBuffered = true;

            // Border & Header
            Panel pnlHeader = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(20, 25, 30) };
            lblHeader = new Label
            {
                Text = $"STRIKE COORDINATOR // SYSTEM: {_weaponName.ToUpper()} // STATUS: READY",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Consolas", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(57, 255, 20)
            };
            pnlHeader.Controls.Add(lblHeader);
            this.Controls.Add(pnlHeader);

            // Left List (Techy Sidebar)
            pnlList = new Panel
            {
                Width = 340,
                Dock = DockStyle.Left,
                BackColor = Color.FromArgb(15, 18, 22),
                Padding = new Padding(8),
                AutoScroll = true
            };
            this.Controls.Add(pnlList);

            // Map Panel (Using Existing Radar Engine)
            mapPanel = new RadarPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                MapProvider = GMapProviders.OpenStreetMap,
                Position = new PointLatLng(20, 0),
                MinZoom = 2,
                MaxZoom = 4,
                Zoom = 2,
                ShowCenter = false,
                DragButton = MouseButtons.Right,
                GrayScaleMode = true,
                NegativeMode = true,
                ShowTileGridLines = false
            };
            mapPanel.Paint += PnlMap_Paint;
            mapPanel.MouseClick += PnlMap_MouseClick;
            this.Controls.Add(mapPanel);

            // Footer
            Panel pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Color.FromArgb(20, 25, 30), Padding = new Padding(15) };
            
            btnExecute = new Button
            {
                Text = "CONFIRM SALVO",
                Dock = DockStyle.Right,
                Width = 220,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 0, 0),
                ForeColor = Color.White,
                Enabled = false,
                Font = new Font("Consolas", 11F, FontStyle.Bold)
            };
            btnExecute.FlatAppearance.BorderColor = Color.Red;
            btnExecute.Click += (s, e) => {
                FinalPlan = _selectedPlan.Select(p => (p.Name, p.Count)).ToList();
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnAbort = new Button
            {
                Text = "ABORT PLAN",
                Dock = DockStyle.Left,
                Width = 150,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.LightGray
            };
            btnAbort.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            lblTotal = new Label
            {
                Text = $"TOTAL NUKES ENGAGED: 0 / {_maxNukes}",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Yellow,
                Font = new Font("Consolas", 12F, FontStyle.Bold)
            };

            pnlFooter.Controls.Add(lblTotal);
            pnlFooter.Controls.Add(btnExecute);
            pnlFooter.Controls.Add(btnAbort);
            this.Controls.Add(pnlFooter);

            PopulateList();
        }

        private void PopulateList()
        {
            pnlList.SuspendLayout();
            pnlList.Controls.Clear();
            int y = 0;
            foreach (var name in _availableNations)
            {
                var pnl = new Panel
                {
                    Location = new Point(5, y),
                    Width = 310,
                    Height = 40,
                    BackColor = Color.FromArgb(30, 35, 40),
                    Tag = name
                };
                
                var lbl = new Label
                {
                    Name = "lblName",
                    Text = name,
                    Location = new Point(0, 0),
                    Size = new Size(200, 40),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(5, 0, 0, 0),
                    ForeColor = Color.Silver,
                    Cursor = Cursors.Hand,
                    Tag = name
                };
                lbl.Click += (s, e) => ToggleNation(name);
                pnl.Controls.Add(lbl);

                var btnMinus = new Button { Name = "btnMinus", Text = "▼", Size = new Size(30, 30), Location = new Point(205, 5), FlatStyle = FlatStyle.Flat, Visible = false, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.Cyan };
                var lblCnt = new Label { Name = "lblCnt", Text = "1", Size = new Size(30, 30), Location = new Point(237, 5), TextAlign = ContentAlignment.MiddleCenter, Visible = false, ForeColor = Color.Yellow, Font = new Font("Consolas", 10F, FontStyle.Bold) };
                var btnPlus = new Button { Name = "btnPlus", Text = "▲", Size = new Size(30, 30), Location = new Point(270, 5), FlatStyle = FlatStyle.Flat, Visible = false, BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.Cyan };

                btnMinus.Click += (s, e) => AdjustCount(name, -1);
                btnPlus.Click += (s, e) => AdjustCount(name, 1);

                pnl.Controls.Add(btnMinus);
                pnl.Controls.Add(lblCnt);
                pnl.Controls.Add(btnPlus);

                pnlList.Controls.Add(pnl);
                y += 45;
            }
            pnlList.ResumeLayout();
        }

        private void AdjustCount(string name, int delta)
        {
            var target = _selectedPlan.FirstOrDefault(p => p.Name == name);
            if (target == null) return;

            int currentTotal = _selectedPlan.Sum(p => p.Count);
            if (delta > 0 && currentTotal >= _maxNukes) return;
            
            target.Count = Math.Max(1, target.Count + delta);
            UpdateUI();
        }

        private void ToggleNation(string name)
        {
            var existing = _selectedPlan.FirstOrDefault(p => p.Name == name);
            if (existing != null)
            {
                _selectedPlan.Remove(existing);
            }
            else
            {
                if (_selectedPlan.Sum(p => p.Count) >= _maxNukes) return;
                _selectedPlan.Add(new SelectedTarget { Name = name, Count = 1 });
            }
            UpdateUI();
        }

        private void UpdateUI()
        {
            int total = _selectedPlan.Sum(p => p.Count);
            lblTotal.Text = $"TOTAL NUKES ENGAGED: {total} / {_maxNukes}";
            btnExecute.Enabled = total > 0;
            btnExecute.BackColor = total > 0 ? Color.DarkRed : Color.FromArgb(60, 0, 0);

            foreach (Control ctrl in pnlList.Controls)
            {
                if (ctrl is Panel pnl && pnl.Tag is string name)
                {
                    var target = _selectedPlan.FirstOrDefault(p => p.Name == name);
                    int idx = _selectedPlan.IndexOf(target);
                    
                    var lblName = pnl.Controls.Find("lblName", false).FirstOrDefault() as Label;
                    var btnM = pnl.Controls.Find("btnMinus", false).FirstOrDefault() as Button;
                    var lblC = pnl.Controls.Find("lblCnt", false).FirstOrDefault() as Label;
                    var btnP = pnl.Controls.Find("btnPlus", false).FirstOrDefault() as Button;

                    if (lblName == null) continue;

                    if (idx >= 0)
                    {
                        pnl.BackColor = Color.FromArgb(0, 80, 0);
                        lblName.ForeColor = Color.SpringGreen;
                        lblName.Text = $"[{idx + 1}] {name}";
                        if (btnM != null) btnM.Visible = true;
                        if (btnP != null) btnP.Visible = true;
                        if (lblC != null)
                        {
                            lblC.Visible = true;
                            lblC.Text = target.Count.ToString();
                        }
                    }
                    else
                    {
                        pnl.BackColor = Color.FromArgb(30, 35, 40);
                        lblName.ForeColor = Color.Silver;
                        lblName.Text = name;
                        if (btnM != null) btnM.Visible = false;
                        if (btnP != null) btnP.Visible = false;
                        if (lblC != null) lblC.Visible = false;
                    }
                }
            }
            mapPanel.Invalidate();
        }

        private void PnlMap_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = mapPanel.Width;
            int h = mapPanel.Height;

            // Target Overlay Dimming
            using (var b = new SolidBrush(Color.FromArgb(120, 0, 10, 0)))
                g.FillRectangle(b, 0, 0, w, h);

            _currentScreenCoords.Clear();
            foreach (var nation in GameEngine.Nations.Values)
            {
                PointF sc = ToScreenPoint(new PointLatLng(nation.MapY, nation.MapX));
                _currentScreenCoords[nation.Name] = sc;

                var target = _selectedPlan.FirstOrDefault(p => p.Name == nation.Name);
                bool isSelected = target != null;
                bool isTargetable = _availableNations.Contains(nation.Name);

                if (isSelected)
                {
                    float pulse = (float)(Math.Sin(DateTime.Now.Ticks / 1500000.0) * 4 + 10);
                    using (var p = new Pen(Color.FromArgb(200, 255, 0, 0), 2))
                    {
                        g.DrawEllipse(p, sc.X - pulse, sc.Y - pulse, pulse * 2, pulse * 2);
                        g.DrawLine(p, sc.X - pulse - 5, sc.Y, sc.X + pulse + 5, sc.Y);
                        g.DrawLine(p, sc.X, sc.Y - pulse - 5, sc.X, sc.Y + pulse + 5);
                    }
                    
                    // Show count on map
                    if (target.Count > 1) {
                        using (var f = new Font("Consolas", 10F, FontStyle.Bold))
                        using (var b = new SolidBrush(Color.Yellow))
                            g.DrawString($"x{target.Count}", f, b, sc.X + 12, sc.Y - 12);
                    }
                }

                Color dotColor = isSelected ? Color.Red : (isTargetable ? Color.FromArgb(0, 255, 100) : Color.FromArgb(60, 60, 60));
                using (var b = new SolidBrush(dotColor))
                    g.FillEllipse(b, sc.X - 3, sc.Y - 3, 6, 6);
            }

            // Connection Path
            if (_selectedPlan.Count > 1)
            {
                using (var p = new Pen(Color.FromArgb(180, 255, 255, 0), 2))
                {
                    p.DashStyle = DashStyle.Dot;
                    for (int i = 0; i < _selectedPlan.Count - 1; i++)
                    {
                        if (_currentScreenCoords.TryGetValue(_selectedPlan[i].Name, out var p1) &&
                            _currentScreenCoords.TryGetValue(_selectedPlan[i+1].Name, out var p2))
                        {
                            g.DrawLine(p, p1, p2);
                        }
                    }
                }
            }
        }

        private void PnlMap_MouseClick(object sender, MouseEventArgs e)
        {
            string closest = null;
            float minDist = 625; // 25px radius squared
            foreach (var kvp in _currentScreenCoords)
            {
                float dx = kvp.Value.X - e.X;
                float dy = kvp.Value.Y - e.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 < minDist)
                {
                    closest = kvp.Key;
                    minDist = d2;
                }
            }

            if (closest != null && _availableNations.Contains(closest))
            {
                ToggleNation(closest);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Color.FromArgb(0, 255, 255), 2))
                e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
        }
    }
}
