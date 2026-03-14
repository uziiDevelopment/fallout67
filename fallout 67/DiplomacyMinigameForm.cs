using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace fallover_67
{
    public class DiplomacyMinigameForm : Form
    {
        public float NegotiationBonus { get; private set; } = 0f;

        private string _targetNation;
        private float _baseProbability;

        // Three negotiation rounds
        private readonly string[] _roundNames = { "MILITARY COOPERATION", "TRADE AGREEMENT", "NON-AGGRESSION PACT" };
        private readonly float[] _roundScores = new float[3];
        private int _currentRound = 0;
        private bool _roundActive = true;

        // Timing bar
        private float _cursorPos = 0f;    // 0-1
        private float _cursorSpeed = 0.8f;
        private int _cursorDir = 1;

        // Timing
        private System.Windows.Forms.Timer _gameTick;
        private bool _finished = false;

        // Layout
        private const int W = 700;
        private const int H = 480;
        private const int BarX = 50;
        private const int BarW = 600;
        private const int BarH = 40;

        // Theme
        private Color bgColor = Color.FromArgb(10, 15, 10);
        private Color greenText = Color.FromArgb(57, 255, 20);
        private Color amberText = Color.FromArgb(255, 176, 0);
        private Color cyanText = Color.Cyan;
        private Font titleFont = new Font("Consolas", 16F, FontStyle.Bold);
        private Font hudFont = new Font("Consolas", 11F, FontStyle.Bold);
        private Font smallFont = new Font("Consolas", 9F, FontStyle.Bold);

        public DiplomacyMinigameForm(string targetNation, float baseProbability)
        {
            _targetNation = targetNation;
            _baseProbability = baseProbability;

            this.Text = $"DIPLOMATIC SUMMIT — {targetNation.ToUpper()}";
            this.ClientSize = new Size(W, H);
            this.BackColor = bgColor;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;

            this.Paint += OnPaint;
            this.MouseClick += OnClick;
            this.KeyDown += OnKeyDown;

            _gameTick = new System.Windows.Forms.Timer { Interval = 16 };
            _gameTick.Tick += OnTick;
            _gameTick.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_finished || !_roundActive) return;

            float dt = 0.016f;
            float speed = _cursorSpeed + _currentRound * 0.4f; // gets faster each round
            _cursorPos += speed * dt * _cursorDir;

            if (_cursorPos >= 1f) { _cursorPos = 1f; _cursorDir = -1; }
            else if (_cursorPos <= 0f) { _cursorPos = 0f; _cursorDir = 1; }

            this.Invalidate();
        }

        private void OnClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) LockIn();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) LockIn();
            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private void LockIn()
        {
            if (_finished || !_roundActive) return;

            // Score based on distance from center (0.5)
            float dist = Math.Abs(_cursorPos - 0.5f); // 0 = perfect center, 0.5 = edge
            float score;
            if (dist < 0.10f) score = 0.10f;       // center 20% = perfect
            else if (dist < 0.20f) score = 0.07f;   // next ring
            else if (dist < 0.30f) score = 0.04f;   // outer ring
            else score = 0.01f;                       // edges = minimal

            _roundScores[_currentRound] = score;
            _roundActive = false;

            // Advance to next round or finish
            var advanceTimer = new System.Windows.Forms.Timer { Interval = 600 };
            advanceTimer.Tick += (s, ev) =>
            {
                advanceTimer.Stop();
                advanceTimer.Dispose();
                _currentRound++;
                if (_currentRound >= 3)
                {
                    _finished = true;
                    NegotiationBonus = _roundScores[0] + _roundScores[1] + _roundScores[2];

                    // Show result briefly then close
                    var closeTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                    closeTimer.Tick += (s2, ev2) =>
                    {
                        closeTimer.Stop();
                        closeTimer.Dispose();
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    };
                    closeTimer.Start();
                }
                else
                {
                    _cursorPos = 0f;
                    _cursorDir = 1;
                    _roundActive = true;
                }
                this.Invalidate();
            };
            advanceTimer.Start();
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Grid
            using var gridPen = new Pen(Color.FromArgb(15, 0, 80, 0), 1);
            for (int x = 0; x < W; x += 40) g.DrawLine(gridPen, x, 0, x, H);
            for (int y = 0; y < H; y += 40) g.DrawLine(gridPen, 0, y, W, y);

            // Title
            using var titleBrush = new SolidBrush(amberText);
            g.DrawString($"DIPLOMATIC SUMMIT — {_targetNation.ToUpper()}", titleFont, titleBrush, 20, 15);

            // Subtitle
            using var subBrush = new SolidBrush(greenText);
            g.DrawString("Lock the cursor in the CENTER for maximum diplomatic leverage", smallFont, subBrush, 20, 45);
            g.DrawString("Click or press SPACE to lock in each round", smallFont, subBrush, 20, 62);

            // Probability gauge
            float totalBonus = 0;
            for (int i = 0; i < 3; i++) totalBonus += _roundScores[i];
            float displayProb = Math.Min(0.95f, _baseProbability + totalBonus);
            using var probBrush = new SolidBrush(displayProb > 0.5f ? greenText : amberText);
            g.DrawString($"ACCEPTANCE PROBABILITY: {displayProb * 100:F0}%", hudFont, probBrush, 20, 90);

            // Draw the probability bar
            g.FillRectangle(Brushes.Black, BarX, 115, BarW, 16);
            int probFill = (int)(BarW * displayProb);
            using var probFillBrush = new SolidBrush(Color.FromArgb(120, displayProb > 0.5f ? greenText : amberText));
            g.FillRectangle(probFillBrush, BarX, 115, probFill, 16);
            using var probBorderPen = new Pen(Color.FromArgb(80, greenText), 1);
            g.DrawRectangle(probBorderPen, BarX, 115, BarW, 16);

            // Draw rounds
            for (int i = 0; i < 3; i++)
            {
                int roundY = 155 + i * 85;
                bool isCurrent = (i == _currentRound && !_finished);
                bool isCompleted = (i < _currentRound || _finished);

                // Round label
                Color labelColor = isCurrent ? cyanText : isCompleted ? greenText : Color.FromArgb(80, greenText);
                using var labelBrush = new SolidBrush(labelColor);
                g.DrawString($"[{i + 1}] {_roundNames[i]}", hudFont, labelBrush, BarX, roundY);

                // Timing bar
                int barY = roundY + 22;
                Color barBg = Color.FromArgb(isCurrent ? 40 : 20, greenText);
                using var barBgBrush = new SolidBrush(barBg);
                g.FillRectangle(barBgBrush, BarX, barY, BarW, BarH);

                // Zone indicators
                int centerZone = (int)(BarW * 0.10f);
                int midZone = (int)(BarW * 0.20f);
                int outerZone = (int)(BarW * 0.30f);

                // Perfect zone (center 20%)
                using var perfectBrush = new SolidBrush(Color.FromArgb(isCurrent ? 50 : 20, 0, 255, 100));
                g.FillRectangle(perfectBrush, BarX + BarW / 2 - centerZone, barY, centerZone * 2, BarH);

                // Good zone
                using var goodBrush = new SolidBrush(Color.FromArgb(isCurrent ? 30 : 10, 255, 255, 0));
                g.FillRectangle(goodBrush, BarX + BarW / 2 - midZone, barY, midZone - centerZone, BarH);
                g.FillRectangle(goodBrush, BarX + BarW / 2 + centerZone, barY, midZone - centerZone, BarH);

                // Center line
                using var centerPen = new Pen(Color.FromArgb(60, greenText), 1) { DashStyle = DashStyle.Dash };
                g.DrawLine(centerPen, BarX + BarW / 2, barY, BarX + BarW / 2, barY + BarH);

                // Border
                using var barPen = new Pen(Color.FromArgb(isCurrent ? 120 : 40, greenText), 1);
                g.DrawRectangle(barPen, BarX, barY, BarW, BarH);

                // Cursor or locked position
                if (isCurrent && _roundActive)
                {
                    int cursorX = BarX + (int)(_cursorPos * BarW);
                    using var cursorPen = new Pen(Color.White, 3);
                    g.DrawLine(cursorPen, cursorX, barY - 3, cursorX, barY + BarH + 3);

                    // Glow
                    using var glowBrush = new SolidBrush(Color.FromArgb(60, Color.White));
                    g.FillEllipse(glowBrush, cursorX - 8, barY + BarH / 2 - 8, 16, 16);
                }
                else if (isCompleted)
                {
                    // Show score
                    string scoreText = _roundScores[i] >= 0.10f ? "PERFECT" :
                                       _roundScores[i] >= 0.07f ? "GOOD" :
                                       _roundScores[i] >= 0.04f ? "OK" : "WEAK";
                    Color scoreColor = _roundScores[i] >= 0.10f ? greenText :
                                       _roundScores[i] >= 0.07f ? cyanText :
                                       _roundScores[i] >= 0.04f ? amberText : Color.Red;
                    using var scoreBrush = new SolidBrush(scoreColor);
                    g.DrawString($"{scoreText} (+{_roundScores[i] * 100:F0}%)", hudFont, scoreBrush, BarX + BarW + 10, barY + 8);
                }
            }

            // Final result
            if (_finished)
            {
                int resultY = 420;
                string resultText = $"NEGOTIATION COMPLETE — TOTAL BONUS: +{NegotiationBonus * 100:F0}%";
                using var resultBrush = new SolidBrush(amberText);
                g.DrawString(resultText, hudFont, resultBrush, 20, resultY);

                float finalProb = Math.Min(0.95f, _baseProbability + NegotiationBonus);
                string outcomeHint = finalProb > 0.6f ? "Outcome looking favorable..." :
                                     finalProb > 0.3f ? "It could go either way..." :
                                     "This will be a tough sell...";
                using var hintBrush = new SolidBrush(greenText);
                g.DrawString(outcomeHint, smallFont, hintBrush, 20, resultY + 25);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _gameTick?.Stop();
            _gameTick?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
