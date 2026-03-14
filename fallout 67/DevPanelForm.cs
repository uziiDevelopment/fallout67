using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

namespace fallover_67
{
    public class DevPanelForm : Form
    {
        private ComboBox cmbNations;
        private NumericUpDown numPop, numNukes, numMoney, numIndustry, numAnger;
        private CheckBox chkDefeated, chkHostile;
        private Button btnApply, btnHealAll;
        private Nation _selectedNation;
        private bool _isPlayerSelected = false;

        public DevPanelForm()
        {
            this.Text = "TACTICAL OVERRIDE — DEVELOPER CONSOLE";
            this.Size = new Size(400, 500);
            this.BackColor = Color.FromArgb(20, 20, 25);
            this.ForeColor = Color.LimeGreen;
            this.Font = new Font("Consolas", 9F, FontStyle.Bold);
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.StartPosition = FormStartPosition.CenterScreen;

            InitializeComponents();
            LoadNations();
        }

        private void InitializeComponents()
        {
            int y = 20;

            AddLabel("SELECT NATION:", 10, y);
            cmbNations = new ComboBox { Location = new Point(140, y), Width = 220, BackColor = Color.Black, ForeColor = Color.Cyan, FlatStyle = FlatStyle.Flat };
            cmbNations.SelectedIndexChanged += (s, e) => LoadNationData();
            this.Controls.Add(cmbNations);
            y += 40;

            AddLabel("POPULATION:", 10, y);
            numPop = CreateNum(140, y, 0, 2000000000, 1000000);
            y += 35;

            AddLabel("NUKES:", 10, y);
            numNukes = CreateNum(140, y, 0, 999);
            y += 35;

            AddLabel("TREASURY ($M):", 10, y);
            numMoney = CreateNum(140, y, 0, 1000000);
            y += 35;

            AddLabel("INDUSTRY LVL:", 10, y);
            numIndustry = CreateNum(140, y, 1, 10);
            y += 35;

            AddLabel("ANGER LEVEL:", 10, y);
            numAnger = CreateNum(140, y, 0, 10);
            y += 35;

            chkDefeated = new CheckBox { Text = "IS DEFEATED", Location = new Point(140, y), AutoSize = true };
            this.Controls.Add(chkDefeated);
            y += 30;

            chkHostile = new CheckBox { Text = "IS HOSTILE TO PLAYER", Location = new Point(140, y), AutoSize = true };
            this.Controls.Add(chkHostile);
            y += 40;

            btnApply = new Button
            {
                Text = "APPLY CHANGES",
                Location = new Point(20, y),
                Size = new Size(340, 40),
                BackColor = Color.FromArgb(0, 60, 0),
                FlatStyle = FlatStyle.Flat
            };
            btnApply.Click += (s, e) => SaveNationData();
            this.Controls.Add(btnApply);
            y += 50;

            btnHealAll = new Button
            {
                Text = "GLOBAL RESET (HEAL ALL)",
                Location = new Point(20, y),
                Size = new Size(340, 30),
                BackColor = Color.FromArgb(60, 0, 0),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 8F, FontStyle.Bold)
            };
            btnHealAll.Click += (s, e) => GlobalHeal();
            this.Controls.Add(btnHealAll);
        }

        private void AddLabel(string text, int x, int y)
        {
            this.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });
        }

        private NumericUpDown CreateNum(int x, int y, decimal min, decimal max, decimal step = 1)
        {
            var n = new NumericUpDown { Location = new Point(x, y), Width = 220, Minimum = min, Maximum = max, Increment = step, BackColor = Color.Black, ForeColor = Color.White };
            this.Controls.Add(n);
            return n;
        }

        private void LoadNations()
        {
            cmbNations.Items.Clear();
            
            // Add Player Nation first
            string pName = GameEngine.Player.NationName;
            cmbNations.Items.Add($"{pName} (PLAYER)");

            foreach (var n in GameEngine.Nations.Keys.OrderBy(k => k))
            {
                if (n == pName) continue;
                cmbNations.Items.Add(n);
            }
            
            if (cmbNations.Items.Count > 0) cmbNations.SelectedIndex = 0;
        }

        private void LoadNationData()
        {
            string selection = cmbNations.SelectedItem.ToString();
            _isPlayerSelected = selection.EndsWith("(PLAYER)");
            
            if (_isPlayerSelected)
            {
                numPop.Value = GameEngine.Player.Population;
                numNukes.Value = GameEngine.Player.StandardNukes;
                numMoney.Value = GameEngine.Player.Money;
                numIndustry.Value = 10; // Player doesn't have IndustryLevel in PlayerState yet, default to max
                numAnger.Value = 0;
                chkDefeated.Checked = false;
                chkHostile.Checked = false;
            }
            else if (GameEngine.Nations.TryGetValue(selection, out _selectedNation))
            {
                numPop.Value = _selectedNation.Population;
                numNukes.Value = _selectedNation.Nukes;
                numMoney.Value = _selectedNation.Money;
                numIndustry.Value = _selectedNation.IndustryLevel;
                numAnger.Value = _selectedNation.AngerLevel;
                chkDefeated.Checked = _selectedNation.IsDefeated;
                chkHostile.Checked = _selectedNation.IsHostileToPlayer;
            }
        }

        private void SaveNationData()
        {
            if (_isPlayerSelected)
            {
                GameEngine.Player.Population = (long)numPop.Value;
                GameEngine.Player.StandardNukes = (int)numNukes.Value;
                GameEngine.Player.Money = (long)numMoney.Value;
                MessageBox.Show($"[DEV] Player State ({GameEngine.Player.NationName}) updated.", "SYSTEM OVERRIDE");
                return;
            }

            if (_selectedNation == null) return;

            _selectedNation.Population = (long)numPop.Value;
            _selectedNation.Nukes = (int)numNukes.Value;
            _selectedNation.Money = (long)numMoney.Value;
            _selectedNation.IndustryLevel = (int)numIndustry.Value;
            _selectedNation.AngerLevel = (int)numAnger.Value;
            _selectedNation.IsDefeated = chkDefeated.Checked;
            _selectedNation.IsHostileToPlayer = chkHostile.Checked;

            MessageBox.Show($"[DEV] Parameters for {_selectedNation.Name} updated successfully.", "SYSTEM OVERRIDE");
        }

        private void GlobalHeal()
        {
            if (MessageBox.Show("Perform global population restoration and hostility reset?", "CONFIRM GLOBAL OVERRIDE", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                foreach (var n in GameEngine.Nations.Values)
                {
                    n.Population = n.MaxPopulation;
                    n.IsDefeated = false;
                    n.IsHostileToPlayer = false;
                    n.AngerLevel = 0;
                    n.Nukes = 50;
                }
                LoadNationData();
            }
        }
    }
}
