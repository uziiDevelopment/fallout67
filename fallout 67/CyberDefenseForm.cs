using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace fallover_67
{
    /// <summary>
    /// Counter-hack minigame: When an enemy hacks your nation, you must complete
    /// a pattern-matching sequence to regain control. Hex codes scroll across the screen
    /// and you must click the matching ones before time runs out.
    /// Success = kick the hacker out. Failure = they keep control.
    /// </summary>
    public class CyberDefenseForm : Form
    {
        // ── Result ─────────────────────────────────────────────────────────
        public bool DefenseSuccess { get; private set; } = false;

        // ── Game State ─────────────────────────────────────────────────────
        private List<string> _targetCodes = new();   // codes the player must find
        private List<int> _foundIndices = new();      // which targets have been matched
        private List<ScrollingCode> _codes = new();   // all visible codes on screen
        private float _timeLeft;
        private float _maxTime;
        private bool _finished = false;
        private int _mistakes = 0;
        private int _maxMistakes = 3;
        private string _hackerName;

        private class ScrollingCode
        {
            public string Text;
            public float X, Y;
            public float Speed;
            public float Alpha = 1f;
            public bool IsTarget;       // one of the codes player needs
            public int TargetIndex = -1; // which target code index
            public bool Clicked;
            public bool Wrong;          // flash red on wrong click
            public float ClickAnim;     // animation timer
            public int Column;
        }

        // ── Timing ─────────────────────────────────────────────────────────
        private System.Windows.Forms.Timer _gameTick;
        private float _spawnTimer = 0f;

        // ── Layout ─────────────────────────────────────────────────────────
        private const int W = 700;
        private const int H = 550;
        private const int CodeAreaY = 120;
        private const int CodeAreaH = 340;
        private const int NumColumns = 5;
        private float _columnWidth;

        // ── Visual ─────────────────────────────────────────────────────────
        private Color bgColor = Color.FromArgb(8, 5, 15);
        private Color textColor = Color.FromArgb(0, 255, 180);
        private Color targetHighlight = Color.FromArgb(255, 200, 0);
        private Color dangerColor = Color.FromArgb(255, 50, 50);
        private Color successColor = Color.FromArgb(0, 255, 100);
        private Color codeColor = Color.FromArgb(40, 100, 80);
        private Color headerColor = Color.FromArgb(255, 60, 60);
        private Font hudFont = new Font("Consolas", 11F, FontStyle.Bold);
        private Font titleFont = new Font("Consolas", 14F, FontStyle.Bold);
        private Font codeFont = new Font("Consolas", 13F, FontStyle.Bold);
        private Font bigFont = new Font("Consolas", 20F, FontStyle.Bold);
        private Font smallFont = new Font("Consolas", 9F);
        private Random _rng = new Random();

        private float _scanY = 0f;
        private float _shakeX = 0f, _shakeY = 0f;
        private float _warningFlash = 0f;

        public CyberDefenseForm(string hackerName, int difficulty)
        {
            _hackerName = hackerName;
            _columnWidth = (W - 40f) / NumColumns;

            // Number of codes to find scales with difficulty
            int numTargets = difficulty <= 2 ? 4 : difficulty <= 4 ? 6 : 8;
            _maxTime = difficulty <= 2 ? 20f : difficulty <= 4 ? 15f : 12f;
            _timeLeft = _maxTime;
            _maxMistakes = difficulty <= 2 ? 4 : 3;

            // Generate target codes
            for (int i = 0; i < numTargets; i++)
                _targetCodes.Add($"{_rng.Next(0x1000, 0xFFFF):X4}");

            BuildForm();
        }

        private void BuildForm()
        {
            this.Text = $"⚠ CYBER ATTACK — {_hackerName.ToUpper()} IS IN YOUR SYSTEM ⚠";
            this.ClientSize = new Size(W, H);
            this.BackColor = bgColor;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;

            this.Paint += OnPaint;
            this.MouseClick += OnClick;

            _gameTick = new System.Windows.Forms.Timer { Interval = 16 };
            _gameTick.Tick += OnTick;
            _gameTick.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            float dt = 0.016f;
            _timeLeft -= dt;
            _scanY = (_scanY + 1.5f) % H;
            _warningFlash += dt * 4f;

            // Shake effect when time is low
            if (_timeLeft < _maxTime * 0.3f)
            {
                _shakeX = (float)(_rng.NextDouble() * 4 - 2);
                _shakeY = (float)(_rng.NextDouble() * 4 - 2);
            }
            else { _shakeX = 0; _shakeY = 0; }

            // Spawn codes
            _spawnTimer += dt;
            float spawnRate = 0.3f - (_maxTime - _timeLeft) * 0.005f; // speeds up over time
            spawnRate = Math.Max(0.12f, spawnRate);

            if (_spawnTimer >= spawnRate)
            {
                _spawnTimer = 0;
                SpawnCode();
            }

            // Move codes down
            for (int i = _codes.Count - 1; i >= 0; i--)
            {
                var c = _codes[i];
                c.Y += c.Speed;
                if (c.ClickAnim > 0) c.ClickAnim -= dt * 3f;
                if (c.Y > CodeAreaY + CodeAreaH + 20 || c.Clicked)
                {
                    c.Alpha -= dt * 4f;
                    if (c.Alpha <= 0) _codes.RemoveAt(i);
                }
            }

            // Time up
            if (_timeLeft <= 0 && !_finished)
            {
                _timeLeft = 0;
                DefenseSuccess = false;
                _finished = true;
                FinishAfterDelay(1500);
            }

            // Mistakes exceeded
            if (_mistakes >= _maxMistakes && !_finished)
            {
                DefenseSuccess = false;
                _finished = true;
                FinishAfterDelay(1500);
            }

            this.Invalidate();
        }

        private void SpawnCode()
        {
            int col = _rng.Next(NumColumns);
            float x = 20 + col * _columnWidth + _columnWidth * 0.15f;

            // Decide if this should be a target code
            bool isTarget = false;
            int targetIdx = -1;
            var remaining = Enumerable.Range(0, _targetCodes.Count).Where(i => !_foundIndices.Contains(i)).ToList();

            if (remaining.Count > 0 && _rng.NextDouble() < 0.25)
            {
                targetIdx = remaining[_rng.Next(remaining.Count)];
                isTarget = true;
            }

            string text = isTarget ? _targetCodes[targetIdx] : $"{_rng.Next(0x1000, 0xFFFF):X4}";

            _codes.Add(new ScrollingCode
            {
                Text = text,
                X = x,
                Y = CodeAreaY - 10,
                Speed = 1.2f + (float)_rng.NextDouble() * 1.5f,
                IsTarget = isTarget,
                TargetIndex = targetIdx,
                Column = col
            });
        }

        private void OnClick(object sender, MouseEventArgs e)
        {
            if (_finished || e.Button != MouseButtons.Left) return;

            // Find the closest code to click position
            ScrollingCode best = null;
            float bestDist = 50; // max click distance

            foreach (var c in _codes)
            {
                if (c.Clicked || c.Alpha < 0.5f) continue;
                if (c.Y < CodeAreaY || c.Y > CodeAreaY + CodeAreaH) continue;

                float dx = e.X - (c.X + 25);
                float dy = e.Y - c.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            if (best == null) return;

            if (best.IsTarget && !_foundIndices.Contains(best.TargetIndex))
            {
                // Correct!
                _foundIndices.Add(best.TargetIndex);
                best.Clicked = true;
                best.ClickAnim = 1f;

                // Check win
                if (_foundIndices.Count >= _targetCodes.Count && !_finished)
                {
                    DefenseSuccess = true;
                    _finished = true;
                    FinishAfterDelay(1200);
                }
            }
            else
            {
                // Wrong code!
                best.Wrong = true;
                best.ClickAnim = 1f;
                _mistakes++;
            }
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.TranslateTransform(_shakeX, _shakeY);
            g.Clear(bgColor);

            // Warning border flash
            float flashAlpha = (float)(Math.Sin(_warningFlash) * 0.3 + 0.3);
            using var borderPen = new Pen(Color.FromArgb((int)(flashAlpha * 255), dangerColor), 3);
            g.DrawRectangle(borderPen, 1, 1, W - 3, H - 3);

            // Scanline
            using (var scanBr = new SolidBrush(Color.FromArgb(6, 255, 0, 0)))
                g.FillRectangle(scanBr, 0, _scanY, W, 2);

            // Header
            using var dangerBr = new SolidBrush(headerColor);
            g.DrawString($"⚠ INTRUSION ALERT — {_hackerName.ToUpper()} IN YOUR NETWORK ⚠", titleFont, dangerBr, 10, 8);

            using var instrBr = new SolidBrush(Color.FromArgb(200, targetHighlight));
            g.DrawString("FIND AND CLICK THE HIGHLIGHTED CODES TO PURGE THE INTRUDER", hudFont, instrBr, 10, 32);

            // Target codes panel
            float panelX = 10, panelY = 55;
            using var panelBg = new SolidBrush(Color.FromArgb(20, 30, 40));
            g.FillRectangle(panelBg, panelX, panelY, W - 20, 50);
            using var panelBorder = new Pen(Color.FromArgb(60, targetHighlight), 1);
            g.DrawRectangle(panelBorder, panelX, panelY, W - 20, 50);

            using var labelBr = new SolidBrush(Color.FromArgb(150, textColor));
            g.DrawString("TARGET SIGNATURES:", smallFont, labelBr, panelX + 8, panelY + 4);

            float tcX = panelX + 10;
            for (int i = 0; i < _targetCodes.Count; i++)
            {
                bool found = _foundIndices.Contains(i);
                Color tc = found ? successColor : targetHighlight;
                using var tcBr = new SolidBrush(tc);
                string display = found ? $"[{_targetCodes[i]}] ✓" : $"[{_targetCodes[i]}]";
                g.DrawString(display, codeFont, tcBr, tcX, panelY + 22);
                tcX += g.MeasureString(display + "  ", codeFont).Width;
            }

            // Code area background
            using var areaBg = new SolidBrush(Color.FromArgb(6, 10, 18));
            g.FillRectangle(areaBg, 10, CodeAreaY, W - 20, CodeAreaH);

            // Column dividers
            using var colPen = new Pen(Color.FromArgb(15, 40, 50), 1);
            for (int c = 1; c < NumColumns; c++)
                g.DrawLine(colPen, 20 + c * _columnWidth, CodeAreaY, 20 + c * _columnWidth, CodeAreaY + CodeAreaH);

            // Draw scrolling codes
            foreach (var c in _codes)
            {
                if (c.Y < CodeAreaY - 15 || c.Y > CodeAreaY + CodeAreaH + 15) continue;

                Color col;
                if (c.Wrong && c.ClickAnim > 0)
                    col = Color.FromArgb((int)(c.Alpha * 255), dangerColor);
                else if (c.Clicked)
                    col = Color.FromArgb((int)(c.Alpha * 255), successColor);
                else if (c.IsTarget && !_foundIndices.Contains(c.TargetIndex))
                    col = Color.FromArgb((int)(c.Alpha * 255), targetHighlight);
                else
                    col = Color.FromArgb((int)(c.Alpha * 180), codeColor);

                using var codeBr = new SolidBrush(col);
                g.DrawString(c.Text, codeFont, codeBr, c.X, c.Y);

                // Click animation
                if (c.ClickAnim > 0)
                {
                    float r = (1f - c.ClickAnim) * 25f;
                    int a = (int)(c.ClickAnim * 150);
                    Color ring = c.Wrong ? dangerColor : successColor;
                    using var ringPen = new Pen(Color.FromArgb(a, ring), 2);
                    g.DrawEllipse(ringPen, c.X + 15 - r, c.Y + 5 - r, r * 2, r * 2);
                }
            }

            // Clip border for code area
            using var clipPen = new Pen(Color.FromArgb(30, textColor), 1);
            g.DrawRectangle(clipPen, 10, CodeAreaY, W - 20, CodeAreaH);

            // Bottom HUD
            int hudY = CodeAreaY + CodeAreaH + 15;

            // Timer
            Color tmColor = _timeLeft > _maxTime * 0.3f ? textColor : dangerColor;
            using var tmBr = new SolidBrush(tmColor);
            g.DrawString($"TIME: {_timeLeft:F1}s", titleFont, tmBr, 15, hudY);

            // Timer bar
            float tPct = _timeLeft / _maxTime;
            g.FillRectangle(new SolidBrush(Color.FromArgb(20, 30, 30)), 15, hudY + 30, W - 30, 10);
            g.FillRectangle(new SolidBrush(tmColor), 15, hudY + 30, (int)((W - 30) * tPct), 10);

            // Progress
            float pPct = (float)_foundIndices.Count / _targetCodes.Count;
            using var progBr = new SolidBrush(successColor);
            string progText = $"PURGE: {_foundIndices.Count}/{_targetCodes.Count}";
            var progSz = g.MeasureString(progText, titleFont);
            g.DrawString(progText, titleFont, progBr, W - progSz.Width - 15, hudY);

            // Mistakes
            using var mistBr = new SolidBrush(_mistakes >= _maxMistakes - 1 ? dangerColor : Color.FromArgb(180, 180, 180));
            g.DrawString($"ERRORS: {_mistakes}/{_maxMistakes}", hudFont, mistBr, W / 2 - 50, hudY);

            // Result overlay
            if (_finished)
            {
                using var overlay = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
                g.FillRectangle(overlay, 0, 0, W, H);

                string result = DefenseSuccess ? "INTRUDER PURGED — SYSTEMS RESTORED" : "DEFENSE FAILED — BREACH CONTINUES";
                Color rc = DefenseSuccess ? successColor : dangerColor;
                using var resBr = new SolidBrush(rc);
                var sz = g.MeasureString(result, bigFont);
                g.DrawString(result, bigFont, resBr, (W - sz.Width) / 2, (H - sz.Height) / 2);
            }

            g.ResetTransform();
        }

        private void FinishAfterDelay(int ms)
        {
            var closeTimer = new System.Windows.Forms.Timer { Interval = ms };
            closeTimer.Tick += (s, ev) =>
            {
                closeTimer.Stop();
                closeTimer.Dispose();
                if (IsHandleCreated && !IsDisposed)
                {
                    if (InvokeRequired) Invoke((Action)(() => { DialogResult = DialogResult.OK; Close(); }));
                    else { DialogResult = DialogResult.OK; Close(); }
                }
            };
            closeTimer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _gameTick?.Stop();
            _gameTick?.Dispose();
            base.OnFormClosing(e);
        }
    }
}
