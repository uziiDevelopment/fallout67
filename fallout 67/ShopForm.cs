using System;
using System.Drawing;
using System.Windows.Forms;

namespace fallover_67
{
    public class ShopForm : Form
    {
        private Color bgDark = Color.FromArgb(20, 25, 20);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Font stdFont = new Font("Consolas", 11F, FontStyle.Bold);
        private Label lblTreasury;

        public ShopForm()
        {
            this.Text = "BLACK MARKET WEAPONS & DEFENSE";
            this.Size = new Size(600, 700);
            this.BackColor = bgDark;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;

            lblTreasury = new Label
            {
                Text = $"TREASURY: ${GameEngine.Player.Money:N0}M",
                Font = new Font("Consolas", 16F, FontStyle.Bold),
                ForeColor = Color.Cyan,
                Dock = DockStyle.Top,
                Height = 50,
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(lblTreasury);

            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20)
            };

            // Arsenal
            panel.Controls.Add(CreateShopItem("Standard Nuke", "Basic atomic payload.", 500, () => GameEngine.Player.StandardNukes++));
            panel.Controls.Add(CreateShopItem("Tsar Bomba", "Massive thermonuclear destruction.", 2000, () => GameEngine.Player.MegaNukes++));
            panel.Controls.Add(CreateShopItem("Bio-Plague", "Weaponized pathogen. Ignores enemy defenses.", 1500, () => GameEngine.Player.BioPlagues++));
            panel.Controls.Add(CreateShopItem("Orbital Laser", "Precision strike. Vaporizes enemy nukes/money.", 3000, () => GameEngine.Player.OrbitalLasers++));

            // Defenses
            panel.Controls.Add(CreateShopItem("Iron Dome Upgrade", "Intercepts incoming missiles.", 1000, () => GameEngine.Player.IronDomeLevel++));
            panel.Controls.Add(CreateShopItem("Deep Bunkers", "Saves a % of population from blast radius.", 1500, () => GameEngine.Player.BunkerLevel++));
            panel.Controls.Add(CreateShopItem("Vaccine Program", "Protects population from biological attacks.", 800, () => GameEngine.Player.VaccineLevel++));

            this.Controls.Add(panel);
        }

        private Panel CreateShopItem(string name, string desc, long cost, Action onBuy)
        {
            Panel p = new Panel { Size = new Size(520, 70), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(5) };

            Label lblName = new Label { Text = $"{name} (${cost}M)", Font = stdFont, ForeColor = greenText, Location = new Point(10, 10), AutoSize = true };
            Label lblDesc = new Label { Text = desc, Font = new Font("Consolas", 9F), ForeColor = Color.Gray, Location = new Point(10, 35), AutoSize = true };

            Button btnBuy = new Button
            {
                Text = "BUY",
                Location = new Point(410, 15),
                Size = new Size(90, 40),
                BackColor = Color.Black,
                ForeColor = greenText,
                FlatStyle = FlatStyle.Flat
            };

            btnBuy.Click += (s, e) =>
            {
                if (GameEngine.Player.Money >= cost)
                {
                    GameEngine.Player.Money -= cost;
                    onBuy.Invoke();
                    lblTreasury.Text = $"TREASURY: ${GameEngine.Player.Money:N0}M";
                    MessageBox.Show($"Purchased {name}!");
                }
                else
                {
                    MessageBox.Show("INSUFFICIENT FUNDS!");
                }
            };

            p.Controls.Add(lblName);
            p.Controls.Add(lblDesc);
            p.Controls.Add(btnBuy);
            return p;
        }
    }
}