using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace fallover_67
{
    /// <summary>
    /// Watch Dogs-style hacking minigame: Route data through a circuit grid.
    /// Player clicks nodes to rotate pipe segments, connecting SOURCE → TARGET.
    /// On success, returns HackSuccess = true. The caller decides hijack vs self-nuke.
    /// </summary>
    public class HackingMinigameForm : Form
    {
        // ── Result ─────────────────────────────────────────────────────────
        public bool HackSuccess { get; private set; } = false;

        // ── Grid ───────────────────────────────────────────────────────────
        private int _gridW, _gridH;
        private PipeNode[,] _grid;
        private Point _source, _target;
        private HashSet<Point> _poweredNodes = new();
        private HashSet<Point> _solutionPath = new();

        // ── Timing ─────────────────────────────────────────────────────────
        private float _timeLeft;
        private float _maxTime;
        private System.Windows.Forms.Timer _gameTick;
        private bool _finished = false;
        private float _glitchTimer = 0f;

        // ── Layout ─────────────────────────────────────────────────────────
        private const int CellSize = 64;
        private const int GridOffsetX = 40;
        private const int GridOffsetY = 80;
        private int _formW, _formH;

        // ── Visual ─────────────────────────────────────────────────────────
        private Color bgColor = Color.FromArgb(5, 5, 15);
        private Color gridBg = Color.FromArgb(10, 15, 25);
        private Color gridLine = Color.FromArgb(20, 40, 60);
        private Color pipeColor = Color.FromArgb(40, 80, 120);
        private Color poweredColor = Color.FromArgb(0, 255, 180);
        private Color sourceColor = Color.FromArgb(0, 200, 255);
        private Color targetColor = Color.FromArgb(255, 50, 80);
        private Color textColor = Color.FromArgb(0, 255, 180);
        private Color warningColor = Color.FromArgb(255, 80, 40);
        private Color headerColor = Color.FromArgb(0, 180, 255);
        private Font hudFont = new Font("Consolas", 11F, FontStyle.Bold);
        private Font titleFont = new Font("Consolas", 14F, FontStyle.Bold);
        private Font nodeFont = new Font("Consolas", 8F, FontStyle.Bold);
        private Font bigFont = new Font("Consolas", 22F, FontStyle.Bold);
        private Random _rng = new Random();

        // ── Scanline effect ────────────────────────────────────────────────
        private float _scanY = 0f;
        private float _hackProgress = 0f; // visual progress bar

        // ── Data corruption visual ─────────────────────────────────────────
        private List<DataPacket> _packets = new();
        private class DataPacket
        {
            public float X, Y;
            public float Speed;
            public float Alpha;
            public string Text;
        }

        private string _targetNation;

        public HackingMinigameForm(string targetNation, int difficulty)
        {
            _targetNation = targetNation;

            // Grid size scales with difficulty
            _gridW = difficulty <= 2 ? 5 : difficulty <= 4 ? 6 : 7;
            _gridH = difficulty <= 2 ? 4 : difficulty <= 4 ? 5 : 6;

            // Time limit
            _maxTime = difficulty <= 2 ? 45f : difficulty <= 4 ? 35f : 25f;
            _timeLeft = _maxTime;

            _formW = GridOffsetX * 2 + _gridW * CellSize;
            _formH = GridOffsetY + _gridH * CellSize + 80;

            _grid = new PipeNode[_gridW, _gridH];
            GeneratePuzzle();
            ScrambleGrid();

            BuildForm();
        }

        private void BuildForm()
        {
            this.Text = $"CYBER INTRUSION — {_targetNation.ToUpper()} DEFENSE NETWORK";
            this.ClientSize = new Size(_formW, _formH);
            this.BackColor = bgColor;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Hand;

            this.Paint += OnPaint;
            this.MouseClick += OnClick;

            _gameTick = new System.Windows.Forms.Timer { Interval = 16 };
            _gameTick.Tick += OnTick;
            _gameTick.Start();
        }

        // ── Pipe Node Types ────────────────────────────────────────────────
        private enum PipeType
        {
            Straight,   // ─  connects 2 opposite sides
            Corner,     // ┐  connects 2 adjacent sides
            Tee,        // ┬  connects 3 sides
            Cross       // ┼  connects all 4 sides (no rotation needed)
        }

        private class PipeNode
        {
            public PipeType Type;
            public int Rotation; // 0, 1, 2, 3 (× 90°)
            public bool IsSource;
            public bool IsTarget;
            public bool IsPowered;
            public bool IsEmpty; // no pipe here
            public float RotAnim; // visual rotation animation
        }

        // Directions: 0=Up, 1=Right, 2=Down, 3=Left
        private static readonly int[] DX = { 0, 1, 0, -1 };
        private static readonly int[] DY = { -1, 0, 1, 0 };

        private bool[] GetConnections(PipeNode node)
        {
            if (node.IsEmpty) return new[] { false, false, false, false };

            bool[] baseConn = node.Type switch
            {
                PipeType.Straight => new[] { true, false, true, false },
                PipeType.Corner => new[] { true, true, false, false },
                PipeType.Tee => new[] { true, true, false, true },
                PipeType.Cross => new[] { true, true, true, true },
                _ => new[] { false, false, false, false }
            };

            // Apply rotation
            bool[] rotated = new bool[4];
            for (int i = 0; i < 4; i++)
                rotated[(i + node.Rotation) % 4] = baseConn[i];
            return rotated;
        }

        // ── Puzzle Generation (guaranteed solvable) ────────────────────────
        private void GeneratePuzzle()
        {
            // Place source on left edge, target on right edge
            _source = new Point(0, _rng.Next(_gridH));
            _target = new Point(_gridW - 1, _rng.Next(_gridH));

            // BFS to carve a random path from source to target
            var path = FindRandomPath(_source, _target);
            _solutionPath = new HashSet<Point>(path);

            // Set pipe types along the path based on direction changes
            for (int i = 0; i < path.Count; i++)
            {
                Point p = path[i];
                var dirs = new List<int>();

                if (i > 0)
                {
                    Point prev = path[i - 1];
                    for (int d = 0; d < 4; d++)
                        if (prev.X == p.X + DX[d] && prev.Y == p.Y + DY[d])
                            dirs.Add(d);
                }
                if (i < path.Count - 1)
                {
                    Point next = path[i + 1];
                    for (int d = 0; d < 4; d++)
                        if (next.X == p.X + DX[d] && next.Y == p.Y + DY[d])
                            dirs.Add(d);
                }

                // Source/target endpoints only have one connection direction to the path
                if (i == 0 && dirs.Count == 1) dirs.Insert(0, 3); // source connects from left
                if (i == path.Count - 1 && dirs.Count == 1) dirs.Add(1); // target connects to right

                _grid[p.X, p.Y] = CreateNodeForDirections(dirs);
            }

            // Fill remaining cells with random pipes (some empty for variety)
            for (int x = 0; x < _gridW; x++)
            {
                for (int y = 0; y < _gridH; y++)
                {
                    if (_grid[x, y] != null) continue;
                    if (_rng.NextDouble() < 0.15)
                    {
                        _grid[x, y] = new PipeNode { IsEmpty = true };
                    }
                    else
                    {
                        PipeType t = (PipeType)_rng.Next(4);
                        _grid[x, y] = new PipeNode { Type = t, Rotation = _rng.Next(4) };
                    }
                }
            }

            _grid[_source.X, _source.Y].IsSource = true;
            _grid[_target.X, _target.Y].IsTarget = true;
        }

        private PipeNode CreateNodeForDirections(List<int> dirs)
        {
            if (dirs.Count >= 4) return new PipeNode { Type = PipeType.Cross, Rotation = 0 };

            if (dirs.Count == 3)
            {
                // Tee: find the missing direction
                for (int r = 0; r < 4; r++)
                {
                    bool[] conn = { true, true, false, true };
                    bool[] rotated = new bool[4];
                    for (int i = 0; i < 4; i++) rotated[(i + r) % 4] = conn[i];
                    if (dirs.All(d => rotated[d]))
                        return new PipeNode { Type = PipeType.Tee, Rotation = r };
                }
            }

            if (dirs.Count == 2)
            {
                int d0 = dirs[0], d1 = dirs[1];
                // Check if straight (opposite sides)
                if ((d0 + 2) % 4 == d1)
                {
                    int rot = d0 % 2 == 0 ? 0 : 1;
                    return new PipeNode { Type = PipeType.Straight, Rotation = rot };
                }
                // Corner
                for (int r = 0; r < 4; r++)
                {
                    bool[] conn = { true, true, false, false };
                    bool[] rotated = new bool[4];
                    for (int i = 0; i < 4; i++) rotated[(i + r) % 4] = conn[i];
                    if (rotated[d0] && rotated[d1])
                        return new PipeNode { Type = PipeType.Corner, Rotation = r };
                }
            }

            return new PipeNode { Type = PipeType.Straight, Rotation = 0 };
        }

        private List<Point> FindRandomPath(Point start, Point end)
        {
            // Random DFS with backtracking
            var visited = new HashSet<Point>();
            var path = new List<Point>();
            if (DFS(start, end, visited, path)) return path;
            // Fallback: straight line
            path.Clear();
            for (int x = start.X; x <= end.X; x++) path.Add(new Point(x, start.Y));
            if (start.Y != end.Y)
                for (int y = Math.Min(start.Y, end.Y); y <= Math.Max(start.Y, end.Y); y++)
                    if (y != start.Y) path.Add(new Point(end.X, y));
            return path;
        }

        private bool DFS(Point cur, Point end, HashSet<Point> visited, List<Point> path)
        {
            visited.Add(cur);
            path.Add(cur);
            if (cur == end) return true;

            // Randomize direction order for variety
            var dirs = Enumerable.Range(0, 4).OrderBy(_ => _rng.Next()).ToList();
            foreach (int d in dirs)
            {
                int nx = cur.X + DX[d], ny = cur.Y + DY[d];
                if (nx < 0 || nx >= _gridW || ny < 0 || ny >= _gridH) continue;
                var np = new Point(nx, ny);
                if (visited.Contains(np)) continue;
                // Bias toward target
                if (path.Count > _gridW * _gridH * 0.7) continue; // prevent too-long paths
                if (DFS(np, end, visited, path)) return true;
            }
            path.RemoveAt(path.Count - 1);
            return false;
        }

        private void ScrambleGrid()
        {
            // Randomly rotate every node so the puzzle needs solving
            for (int x = 0; x < _gridW; x++)
                for (int y = 0; y < _gridH; y++)
                    if (!_grid[x, y].IsEmpty && _grid[x, y].Type != PipeType.Cross)
                        _grid[x, y].Rotation = (_grid[x, y].Rotation + 1 + _rng.Next(3)) % 4;
        }

        // ── Power flow (BFS from source) ───────────────────────────────────
        private void RecalcPower()
        {
            _poweredNodes.Clear();
            foreach (var row in Enumerable.Range(0, _gridH))
                foreach (var col in Enumerable.Range(0, _gridW))
                    _grid[col, row].IsPowered = false;

            var queue = new Queue<Point>();
            queue.Enqueue(_source);
            _poweredNodes.Add(_source);
            _grid[_source.X, _source.Y].IsPowered = true;

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var curNode = _grid[cur.X, cur.Y];
                var curConn = GetConnections(curNode);

                for (int d = 0; d < 4; d++)
                {
                    if (!curConn[d]) continue;
                    int nx = cur.X + DX[d], ny = cur.Y + DY[d];
                    if (nx < 0 || nx >= _gridW || ny < 0 || ny >= _gridH) continue;
                    var np = new Point(nx, ny);
                    if (_poweredNodes.Contains(np)) continue;

                    var neighbor = _grid[nx, ny];
                    if (neighbor.IsEmpty) continue;
                    var nConn = GetConnections(neighbor);
                    int opposite = (d + 2) % 4;
                    if (!nConn[opposite]) continue;

                    _poweredNodes.Add(np);
                    neighbor.IsPowered = true;
                    queue.Enqueue(np);
                }
            }

            // Check win
            if (_grid[_target.X, _target.Y].IsPowered && !_finished)
            {
                HackSuccess = true;
                _finished = true;
                FinishAfterDelay(1500);
            }

            _hackProgress = (float)_poweredNodes.Count / (_gridW * _gridH);
        }

        // ── Click ──────────────────────────────────────────────────────────
        private void OnClick(object sender, MouseEventArgs e)
        {
            if (_finished) return;
            if (e.Button != MouseButtons.Left) return;

            int gx = (e.X - GridOffsetX) / CellSize;
            int gy = (e.Y - GridOffsetY) / CellSize;
            if (gx < 0 || gx >= _gridW || gy < 0 || gy >= _gridH) return;

            var node = _grid[gx, gy];
            if (node.IsEmpty || node.Type == PipeType.Cross) return;

            node.Rotation = (node.Rotation + 1) % 4;
            RecalcPower();
            this.Invalidate();
        }

        // ── Game tick ──────────────────────────────────────────────────────
        private void OnTick(object sender, EventArgs e)
        {
            float dt = 0.016f;
            _timeLeft -= dt;
            _scanY = (_scanY + 2f) % _formH;
            _glitchTimer += dt;

            // Spawn data packets
            if (_rng.NextDouble() < 0.08)
            {
                _packets.Add(new DataPacket
                {
                    X = _rng.Next(_formW),
                    Y = -10,
                    Speed = 1f + (float)_rng.NextDouble() * 2f,
                    Alpha = 0.3f + (float)_rng.NextDouble() * 0.4f,
                    Text = _rng.NextDouble() < 0.5 ? $"0x{_rng.Next(0xFFFF):X4}" : $"{_rng.Next(256)}.{_rng.Next(256)}.{_rng.Next(256)}.{_rng.Next(256)}"
                });
            }

            for (int i = _packets.Count - 1; i >= 0; i--)
            {
                _packets[i].Y += _packets[i].Speed;
                if (_packets[i].Y > _formH) _packets.RemoveAt(i);
            }

            if (_timeLeft <= 0 && !_finished)
            {
                _timeLeft = 0;
                HackSuccess = false;
                _finished = true;
                FinishAfterDelay(1200);
            }

            this.Invalidate();
        }

        // ── Painting ───────────────────────────────────────────────────────
        private void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background
            g.Clear(bgColor);

            // Floating data packets
            foreach (var pkt in _packets)
            {
                using var br = new SolidBrush(Color.FromArgb((int)(pkt.Alpha * 60), 0, 255, 180));
                g.DrawString(pkt.Text, nodeFont, br, pkt.X, pkt.Y);
            }

            // Scanline
            using (var scanBr = new SolidBrush(Color.FromArgb(8, 0, 255, 200)))
                g.FillRectangle(scanBr, 0, _scanY, _formW, 3);

            // Header
            using var headerBr = new SolidBrush(headerColor);
            g.DrawString($"BREACH PROTOCOL — {_targetNation.ToUpper()} DEFENSE MAINFRAME", titleFont, headerBr, 10, 8);

            using var instrBr = new SolidBrush(Color.FromArgb(180, textColor));
            g.DrawString("CLICK NODES TO ROTATE — CONNECT SOURCE [S] TO TARGET [T]", hudFont, instrBr, 10, 32);

            // Timer
            Color timerColor = _timeLeft > _maxTime * 0.3f ? textColor : warningColor;
            using var timerBr = new SolidBrush(timerColor);
            string timerText = $"DECRYPT: {_timeLeft:F1}s";
            var timerSize = g.MeasureString(timerText, titleFont);
            g.DrawString(timerText, titleFont, timerBr, _formW - timerSize.Width - 15, 8);

            // Timer bar
            float timerPct = _timeLeft / _maxTime;
            int barW = _formW - 80;
            g.FillRectangle(new SolidBrush(Color.FromArgb(30, 50, 60)), 40, 58, barW, 8);
            g.FillRectangle(new SolidBrush(timerColor), 40, 58, (int)(barW * timerPct), 8);

            // Grid background
            using var gridBgBr = new SolidBrush(gridBg);
            g.FillRectangle(gridBgBr, GridOffsetX - 2, GridOffsetY - 2, _gridW * CellSize + 4, _gridH * CellSize + 4);

            // Grid lines
            using var gridPen = new Pen(gridLine, 1);
            for (int x = 0; x <= _gridW; x++)
                g.DrawLine(gridPen, GridOffsetX + x * CellSize, GridOffsetY, GridOffsetX + x * CellSize, GridOffsetY + _gridH * CellSize);
            for (int y = 0; y <= _gridH; y++)
                g.DrawLine(gridPen, GridOffsetX, GridOffsetY + y * CellSize, GridOffsetX + _gridW * CellSize, GridOffsetY + y * CellSize);

            // Draw nodes
            for (int x = 0; x < _gridW; x++)
            {
                for (int y = 0; y < _gridH; y++)
                {
                    DrawNode(g, x, y);
                }
            }

            // Progress bar at bottom
            int pbY = _formH - 50;
            g.FillRectangle(new SolidBrush(Color.FromArgb(15, 25, 35)), 40, pbY, barW, 14);
            g.FillRectangle(new SolidBrush(poweredColor), 40, pbY, (int)(barW * _hackProgress), 14);
            using var pbPen = new Pen(Color.FromArgb(60, poweredColor), 1);
            g.DrawRectangle(pbPen, 40, pbY, barW, 14);
            using var pbBr = new SolidBrush(textColor);
            g.DrawString($"NETWORK PENETRATION: {_hackProgress * 100:F0}%", hudFont, pbBr, 42, pbY - 18);

            // Win/Lose overlay
            if (_finished)
            {
                using var overlay = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                g.FillRectangle(overlay, 0, 0, _formW, _formH);

                string resultText = HackSuccess ? "ACCESS GRANTED" : "INTRUSION DETECTED — CONNECTION LOST";
                Color rc = HackSuccess ? poweredColor : warningColor;
                using var resBr = new SolidBrush(rc);
                var sz = g.MeasureString(resultText, bigFont);
                g.DrawString(resultText, bigFont, resBr, (_formW - sz.Width) / 2, (_formH - sz.Height) / 2);

                if (HackSuccess)
                {
                    string sub = "DEFENSE NETWORK COMPROMISED — SELECTING EXPLOIT...";
                    using var subBr = new SolidBrush(Color.FromArgb(200, textColor));
                    var ssz = g.MeasureString(sub, hudFont);
                    g.DrawString(sub, hudFont, subBr, (_formW - ssz.Width) / 2, (_formH + sz.Height) / 2 + 10);
                }
            }
        }

        private void DrawNode(Graphics g, int gx, int gy)
        {
            var node = _grid[gx, gy];
            float cx = GridOffsetX + gx * CellSize + CellSize / 2f;
            float cy = GridOffsetY + gy * CellSize + CellSize / 2f;
            float half = CellSize / 2f - 4;

            if (node.IsEmpty)
            {
                // Dark cell with subtle X
                using var xPen = new Pen(Color.FromArgb(20, 40, 40), 1);
                g.DrawLine(xPen, cx - 8, cy - 8, cx + 8, cy + 8);
                g.DrawLine(xPen, cx + 8, cy - 8, cx - 8, cy + 8);
                return;
            }

            Color pColor = node.IsPowered ? poweredColor : pipeColor;
            if (node.IsSource) pColor = sourceColor;
            if (node.IsTarget && node.IsPowered) pColor = poweredColor;
            else if (node.IsTarget) pColor = targetColor;

            float pipeW = 8f;
            using var pipePen = new Pen(pColor, pipeW) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var glowPen = new Pen(Color.FromArgb(node.IsPowered ? 40 : 15, pColor), pipeW + 6);

            var conn = GetConnections(node);

            // Draw connections as lines from center to edges
            for (int d = 0; d < 4; d++)
            {
                if (!conn[d]) continue;
                float ex = cx + DX[d] * half;
                float ey = cy + DY[d] * half;
                g.DrawLine(glowPen, cx, cy, ex, ey);
                g.DrawLine(pipePen, cx, cy, ex, ey);
            }

            // Center hub
            float hubR = 6f;
            using var hubBr = new SolidBrush(pColor);
            g.FillEllipse(hubBr, cx - hubR, cy - hubR, hubR * 2, hubR * 2);

            // Powered pulse
            if (node.IsPowered)
            {
                float pulse = (float)(Math.Sin(_glitchTimer * 6) * 0.3 + 0.7);
                using var pulseBr = new SolidBrush(Color.FromArgb((int)(40 * pulse), poweredColor));
                g.FillEllipse(pulseBr, cx - hubR - 4, cy - hubR - 4, (hubR + 4) * 2, (hubR + 4) * 2);
            }

            // Source/Target labels
            if (node.IsSource)
            {
                using var lbl = new SolidBrush(sourceColor);
                g.DrawString("S", hudFont, lbl, cx - 6, cy - 28);
            }
            if (node.IsTarget)
            {
                using var lbl = new SolidBrush(node.IsPowered ? poweredColor : targetColor);
                g.DrawString("T", hudFont, lbl, cx - 6, cy - 28);
            }
        }

        // ── Finish ─────────────────────────────────────────────────────────
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
