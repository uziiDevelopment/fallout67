using System;
using System.Drawing;
using System.Windows.Forms;

namespace fallover_67
{
    public class MainMenuForm : Form
    {
        private ListBox lstCountries;
        private Button btnStart;

        public MainMenuForm()
        {
            this.Text = "VAULT-TEC TERMINAL - CHOOSE YOUR NATION";
            this.Size = new Size(500, 450);
            this.BackColor = Color.FromArgb(20, 25, 20);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            Label lblTitle = new Label
            {
                Text = "SELECT YOUR NATION",
                Font = new Font("Consolas", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(57, 255, 20),
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Height = 40
            };
            this.Controls.Add(lblTitle);

            lstCountries = new ListBox
            {
                Location = new Point(50, 60),
                Size = new Size(380, 250),
                BackColor = Color.Black,
                ForeColor = Color.FromArgb(57, 255, 20),
                Font = new Font("Consolas", 12F, FontStyle.Bold)
            };

            // Populate list dynamically from the new massive list
            foreach (string c in GameEngine.GetAllCountryNames())
            {
                lstCountries.Items.Add(c);
            }
            lstCountries.SelectedIndex = 0;
            this.Controls.Add(lstCountries);

            btnStart = new Button
            {
                Text = "INITIALIZE WAR ROOM",
                Location = new Point(50, 330),
                Size = new Size(380, 50),
                BackColor = Color.FromArgb(35, 45, 35),
                ForeColor = Color.FromArgb(57, 255, 20),
                Font = new Font("Consolas", 14F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnStart.Click += BtnStart_Click;
            this.Controls.Add(btnStart);
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            if (lstCountries.SelectedItem != null)
            {
                GameEngine.InitializeWorld(lstCountries.SelectedItem.ToString());
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }
}