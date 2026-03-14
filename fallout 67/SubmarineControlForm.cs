using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace fallover_67
{
    public class SubmarineControlForm : Form
    {
        private Submarine _sub;
        private ControlPanelForm _parent;
        
        private Label lblStatus;
        private Button btnMove, btnLoad, btnLaunch;

        private Color bgDark = Color.FromArgb(10, 15, 10);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);

        public SubmarineControlForm(Submarine sub, ControlPanelForm parent)
        {
            _sub = sub;
            _parent = parent;

            this.Text = $"TAC-COM // {sub.Name.ToUpper()}";
            this.Size = new Size(400, 300);
            this.BackColor = bgDark;
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.StartPosition = FormStartPosition.CenterParent;

            this.Paint += (s, e) => {
                using (Pen p = new Pen(greenText, 2))
                    e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
            };

            BuildUI();
            UpdateStatus();
        }

        private void BuildUI()
        {
            Label lblTitle = new Label { 
                Text = $"SUB SURFACE ASSET: {_sub.Name.ToUpper()}", 
                ForeColor = greenText, Font = new Font("Consolas", 12, FontStyle.Bold),
                Location = new Point(20, 20), Size = new Size(360, 30) 
            };
            this.Controls.Add(lblTitle);

            lblStatus = new Label {
                ForeColor = Color.White, Font = new Font("Consolas", 10),
                Location = new Point(20, 60), Size = new Size(360, 80)
            };
            this.Controls.Add(lblStatus);

            btnMove = CreateButton("SET DESTINATION", 20, 150, 170, 40, Color.FromArgb(20, 60, 20));
            btnMove.Click += (s, e) => {
                _parent.EnterSubMoveMode(_sub);
                this.Close();
            };
            
            btnLoad = CreateButton("LOAD NUKES", 210, 150, 170, 40, Color.FromArgb(60, 60, 20));
            btnLoad.Click += (s, e) => {
                if (GameEngine.Player.StandardNukes > 0 && _sub.NukeCount < _sub.MaxNukeCount)
                {
                    GameEngine.Player.StandardNukes--;
                    _sub.NukeCount++;
                    UpdateStatus();
                    _parent.RefreshData();
                }
            };

            btnLaunch = CreateButton("LAUNCH STRIKE", 20, 200, 360, 50, Color.DarkRed);
            btnLaunch.Click += (s, e) => {
                if (_sub.NukeCount > 0)
                {
                    _parent.EnterSubFireMode(_sub);
                    this.Close();
                }
                else
                {
                    MessageBox.Show("No nukes loaded in submarine tubes.", "LAUNCH PREVENTED");
                }
            };

            this.Controls.Add(btnMove);
            this.Controls.Add(btnLoad);
            this.Controls.Add(btnLaunch);
        }

        private void UpdateStatus()
        {
            string status = _sub.IsMoving ? "MOVING TO COORDINATES" : "STATIONARY";
            if (_sub.IsDestroyed) status = "DESTROYED (WRECK)";

            lblStatus.Text = $"STATUS: {status}\n" +
                             $"LOCATION: {_sub.MapX:F2}, {_sub.MapY:F2}\n" +
                             $"EQUIPPED: {_sub.NukeCount} / {_sub.MaxNukeCount} THERMONUCLEAR WARHEADS\n" +
                             $"HULL INTEGRITY: {_sub.Health}%";
            
            btnLaunch.Enabled = !_sub.IsDestroyed && _sub.NukeCount > 0;
            btnMove.Enabled = !_sub.IsDestroyed;
            btnLoad.Enabled = !_sub.IsDestroyed;
        }

        private Button CreateButton(string t, int x, int y, int w, int h, Color bg)
        {
            return new Button { 
                Text = t, Location = new Point(x, y), Size = new Size(w, h), 
                BackColor = bg, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
        }
    }
}
