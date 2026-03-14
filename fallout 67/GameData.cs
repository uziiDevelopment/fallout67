using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;

namespace fallover_67
{
    // ── Diplomacy Data ──────────────────────────────────────────────────────
    public enum SummitPhase { FlyingToSummit, InSummit, Returning }
    public enum SummitOutcome { Accepted, Rejected }

    public class DiplomacyState
    {
        public Dictionary<string, int> AllianceAge { get; set; } = new();  // ally name → ticks since formed
        public int BetrayalCooldown { get; set; } = 0;                      // ticks until can betray/be betrayed again
        public string? LastBetrayedBy { get; set; }
        public float DiplomacyMood { get; set; } = 0.5f;                   // 0=hostile, 1=friendly toward player
    }

    public class SummitFlight
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Nation1 { get; set; } = "";   // initiator
        public string Nation2 { get; set; } = "";   // target
        public string HostNation { get; set; } = ""; // summit location (largest of the two)
        public float StartLat { get; set; }
        public float StartLng { get; set; }
        public float EndLat { get; set; }
        public float EndLng { get; set; }
        public float Progress { get; set; } = 0f;
        public float Speed { get; set; } = 0.15f;
        public SummitPhase Phase { get; set; } = SummitPhase.FlyingToSummit;
        public float SummitTimer { get; set; } = 0f;  // seconds spent at summit
        public bool IsPlayerInitiated { get; set; }
        public bool IsPlayerPlane { get; set; }       // player's own plane in flight
        public bool ShotDown { get; set; }
        public float NegotiationBonus { get; set; }   // from minigame
        public SummitOutcome? Result { get; set; }
    }

    public class Nation
    {
        public string Name { get; set; }
        public long Population { get; set; }
        public long MaxPopulation { get; set; }
        public int Nukes { get; set; }
        public long Money { get; set; }
        public int Difficulty { get; set; }
        public bool IsDefeated { get; set; } = false;
        public bool IsLooted { get; set; } = false;
        public bool IsHostileToPlayer { get; set; } = false;

        public float MapX { get; set; }
        public float MapY { get; set; }

        public List<string> Allies { get; set; } = new List<string>();
        public DiplomacyState Diplomacy { get; set; } = new DiplomacyState();

        // 0–10: increases when this nation is struck; drives multi-missile salvos
        public int AngerLevel { get; set; } = 0;

        // True for nations controlled by another human player in multiplayer
        public bool IsHumanControlled { get; set; } = false;

        // Scoring multiplier when this nation is chosen as the player's starting country.
        // Higher = harder start = bigger score bonus. Superpower default = 1.0.
        public float ScoreMultiplier { get; set; } = 1.0f;

        // Satellite jamming — set when this nation's satellites are shot down
        public DateTime SatelliteBlindUntil { get; set; } = DateTime.MinValue;
        public bool IsSatelliteBlind => DateTime.Now < SatelliteBlindUntil;

        public int IndustryLevel { get; set; } = 1;

        public Nation(string name, long pop, int nukes, int difficulty, long money, float x, float y, float scoreMultiplier = 1.0f)
        {
            Name = name;
            Population = pop;
            MaxPopulation = pop;
            Nukes = nukes;
            Difficulty = difficulty;
            Money = money;
            MapX = x;
            MapY = y;
            ScoreMultiplier = scoreMultiplier;
        }
    }

    public class PlayerState
    {
        public string NationName { get; set; }
        public long Population { get; set; }
        public long MaxPopulation { get; set; }
        public long Money { get; set; } = 2500;
        public int NukesUsed { get; set; } = 0;
        public float ScoreMultiplier { get; set; } = 1.0f;

        // Player Base Map Location
        public float MapX { get; set; }
        public float MapY { get; set; }

        public int StandardNukes { get; set; } = 15;
        public int MegaNukes { get; set; } = 0;
        public int BioPlagues { get; set; } = 0;
        public int OrbitalLasers { get; set; } = 0;

        public int IronDomeLevel { get; set; } = 0;
        public int BunkerLevel { get; set; } = 0;
        public int VaccineLevel { get; set; } = 0;

        public int SatelliteMissiles { get; set; } = 0;

        // When the player's own satellites are jammed
        public DateTime SatelliteBlindUntil { get; set; } = DateTime.MinValue;
        public bool IsSatelliteBlind => DateTime.Now < SatelliteBlindUntil;

        public int IndustryLevel { get; set; } = 1;

        public List<string> Allies { get; set; } = new List<string>();
        public int BetrayalCooldown { get; set; } = 0;
        public int MaxAllies { get; set; } = 3;
        public int DiplomacyCooldown { get; set; } = 0;  // ticks until next summit attempt
    }

    public class Submarine
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Submarine";
        public string OwnerId { get; set; } = ""; // Multiplayer ID or Nation Name
        public float MapX { get; set; }
        public float MapY { get; set; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public List<PointF> Waypoints { get; set; } = new List<PointF>();
        public bool IsMoving { get; set; } = false;
        public int NukeCount { get; set; } = 0;
        public int MaxNukeCount { get; set; } = 5;
        public int Health { get; set; } = 100;
        public bool IsDestroyed => Health <= 0;
        public DateTime RevealedUntil { get; set; } = DateTime.MinValue;
        public bool IsRevealed => DateTime.Now < RevealedUntil;
    }

    public class TroopMission
    {
        public string TargetNation { get; set; }
        public int TimeRemainingSeconds { get; set; } = 120;
        public bool IsActive { get; set; } = true;

        // Fraction of player population deployed (e.g. 0.05, 0.15, 0.30, 0.50)
        public double TroopFraction { get; set; } = 0.10;
        // Actual headcount deducted from player population at launch; returned on success
        public long TroopCount { get; set; } = 0;
    }

    public static class GameEngine
    {
        public static PlayerState Player = new PlayerState();
        public static Dictionary<string, Nation> Nations = new Dictionary<string, Nation>();
        public static List<TroopMission> ActiveMissions = new List<TroopMission>();
        public static List<Submarine> Submarines = new List<Submarine>();
        public static List<SummitFlight> ActiveSummits = new List<SummitFlight>();

        // When false, only nations with population ≥ 5,000,000 are included as AI opponents.
        // Set by LobbyForm before calling InitializeWorld.
        public static bool HardMode { get; set; } = false;

        // The 40 nations available on the map
        private static readonly HashSet<string> CasualCountries = new HashSet<string>
        {
            "USA", "Russia", "China", "UK", "France", "Germany", "India", "Pakistan",
            "Israel", "Iran", "Saudi Arabia", "Brazil", "Mexico", "Canada", "Australia",
            "Japan", "South Korea", "North Korea", "Turkey", "Egypt", "Nigeria",
            "South Africa", "Indonesia", "Ukraine",
            "Cuba", "Timor-Leste", "Argentina", "Poland", "Vietnam",
            "Iraq", "Ethiopia", "Colombia", "Kazakhstan", "Taiwan", "Libya",
            "Uzbekistan", "Turkmenistan", "Kyrgyzstan", "Tajikistan", "Afghanistan"
        };

        // All nation data — defined once, shared by InitializeWorld and GetAllCountryNames
        private static List<Nation> BuildRawNations() => new List<Nation>
        {
                // ==========================================
                // CORE 24 NATIONS
                // ==========================================
                new Nation("USA",          330_000_000,   35, 5, 12000, 0.175f, 0.340f, 1.0f),
                new Nation("Russia",       144_000_000,   30, 5,  8000, 0.690f, 0.175f, 1.0f),
                new Nation("China",      1_419_000_000,   35, 5, 10000, 0.760f, 0.390f, 1.0f),
                new Nation("UK",            69_000_000,   12, 3,  4500, 0.440f, 0.210f, 1.2f),
                new Nation("France",        66_000_000,   10, 3,  4000, 0.470f, 0.290f, 1.2f),
                new Nation("Germany",       84_000_000,   15, 4,  6000, 0.510f, 0.240f, 1.3f),
                new Nation("India",      1_450_000_000,   18, 4,  3000, 0.685f, 0.470f, 1.3f),
                new Nation("Pakistan",     225_000_000,   15, 3,  1500, 0.655f, 0.430f, 1.5f),
                new Nation("Israel",         9_000_000,   20, 4,  2000, 0.562f, 0.400f, 1.8f),
                new Nation("Iran",          85_000_000,   12, 3,  2000, 0.638f, 0.385f, 1.6f),
                new Nation("Saudi Arabia",  35_000_000,   10, 3,  5000, 0.610f, 0.455f, 1.7f),
                new Nation("Brazil",       214_000_000,   10, 2,  2000, 0.310f, 0.660f, 1.6f),
                new Nation("Mexico",       128_000_000,   10, 2,  1500, 0.160f, 0.450f, 1.8f),
                new Nation("Canada",        39_000_000,   10, 2,  2500, 0.190f, 0.200f, 1.4f),
                new Nation("Australia",     26_000_000,   10, 2,  1000, 0.855f, 0.750f, 1.7f),
                new Nation("Japan",        125_000_000,   10, 4,  6000, 0.875f, 0.325f, 1.5f),
                new Nation("South Korea",   51_000_000,   10, 4,  5000, 0.840f, 0.355f, 1.5f),
                new Nation("North Korea",   26_000_000,   15, 3,   500, 0.815f, 0.305f, 1.8f),
                new Nation("Turkey",        84_000_000,   10, 3,  2500, 0.575f, 0.330f, 1.4f),
                new Nation("Egypt",        104_000_000,   10, 2,  1000, 0.548f, 0.435f, 1.8f),
                new Nation("Nigeria",      211_000_000,   10, 2,  1500, 0.495f, 0.555f, 2.2f),
                new Nation("South Africa",  60_000_000,   10, 2,  1200, 0.545f, 0.770f, 2.1f),
                new Nation("Indonesia",    273_000_000,   10, 3,  3000, 0.800f, 0.610f, 1.9f),
                new Nation("Ukraine",       43_000_000,   10, 2,  1000, 0.580f, 0.250f, 2.0f),
                new Nation("Cuba",          11_000_000,   10, 2,   500, 0.235f, 0.465f, 2.5f),
                new Nation("Timor-Leste",    1_300_000,   10, 1,   100, 0.830f, 0.630f, 3.5f),
                new Nation("Argentina",     45_000_000,   10, 2,  1000, 0.270f, 0.830f, 2.0f),
                new Nation("Poland",        38_000_000,   10, 3,  2000, 0.530f, 0.220f, 1.6f),
                new Nation("Vietnam",       98_000_000,   10, 2,  1200, 0.770f, 0.470f, 2.1f),
                new Nation("Iraq",          43_000_000,   10, 2,   800, 0.600f, 0.380f, 2.2f),
                new Nation("Ethiopia",     120_000_000,   10, 2,   700, 0.580f, 0.500f, 2.3f),
                new Nation("Colombia",      51_000_000,   10, 2,   800, 0.220f, 0.530f, 2.1f),
                new Nation("Kazakhstan",    19_000_000,   10, 2,  1000, 0.660f, 0.250f, 2.1f),
                new Nation("Taiwan",        23_000_000,   10, 3,  3000, 0.810f, 0.430f, 1.8f),
                new Nation("Libya",          6_800_000,   10, 2,   500, 0.510f, 0.400f, 2.6f),
                new Nation("Uzbekistan",    35_000_000,   10, 2,   500, 0.650f, 0.300f, 2.4f),
                new Nation("Turkmenistan",   6_000_000,   10, 2,   400, 0.630f, 0.320f, 2.5f),
                new Nation("Kyrgyzstan",     6_000_000,   10, 1,   200, 0.680f, 0.290f, 2.8f),
                new Nation("Tajikistan",     9_000_000,   10, 1,   200, 0.670f, 0.310f, 2.8f),
                new Nation("Afghanistan",   40_000_000,   10, 2,   200, 0.660f, 0.390f, 3.0f),
        };

        public static void InitializeWorld(string playerChoice, int seed = -1)
        {
            Nations.Clear();
            ActiveMissions.Clear();
            Submarines.Clear();
            ActiveSummits.Clear();
            Player = new PlayerState();

            var rawNations = BuildRawNations();

            var filteredNations = rawNations.Where(n => CasualCountries.Contains(n.Name)).ToList();

            foreach (var n in filteredNations) Nations.Add(n.Name, n);

            // Generate Alliances — use shared seed in multiplayer so all clients get the same world
            Random rnd = seed >= 0 ? new Random(seed) : new Random();
            var names = Nations.Keys.ToList();
            foreach (var name in names)
            {
                if (rnd.NextDouble() > 0.4)
                {
                    string ally = names[rnd.Next(names.Count)];
                    if (ally != name && !Nations[name].Allies.Contains(ally))
                    {
                        Nations[name].Allies.Add(ally);
                        Nations[ally].Allies.Add(name);
                    }
                }
            }

            // Initialize diplomacy moods based on difficulty
            foreach (var n in Nations.Values)
            {
                n.Diplomacy.DiplomacyMood = Math.Max(0.1f, 0.6f - n.Difficulty * 0.08f);
                foreach (var allyName in n.Allies)
                    n.Diplomacy.AllianceAge[allyName] = 0;
            }

            // Extract Player — copy chosen nation's data into PlayerState and remove from AI pool
            var chosen = Nations[playerChoice];
            Player.NationName = chosen.Name;
            Player.Population = chosen.MaxPopulation;
            Player.MaxPopulation = chosen.MaxPopulation;
            Player.StandardNukes += chosen.Nukes;
            Player.Allies = new List<string>(chosen.Allies);
            Player.MapX = chosen.MapX;
            Player.MapY = chosen.MapY;
            Player.ScoreMultiplier = chosen.ScoreMultiplier;

            Nations.Remove(playerChoice);
        }

        public static List<string> GetAllCountryNames(bool hardMode = false)
        {
            return new List<string>(CasualCountries);
        }
    }

    public static class MapUtility
    {
        private static readonly float[][] Continents = new float[][] {
            new float[] { -170, -56, -34, 72 },  // Americas
            new float[] { -19, -35, 190, 78 },   // Eurasia + Africa
            new float[] { 112, -44, 154, -10 },  // Australia
            new float[] { -180, -90, 180, -60 }, // Antarctica
            new float[] { -82, 10, -35, 15 }     // Central America bridge
        };

        public static bool IsWater(float lng, float lat)
        {
            foreach (var box in Continents)
            {
                if (lng >= box[0] && lng <= box[2] && lat >= box[1] && lat <= box[3])
                    return false;
            }
            return true;
        }

        public static bool IsPathClear(float x1, float y1, float x2, float y2)
        {
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f;
                if (!IsWater(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t)) return false;
            }
            return true;
        }

        public static List<PointF> FindPath(float startX, float startY, float endX, float endY)
        {
            if (IsPathClear(startX, startY, endX, endY)) return new List<PointF> { new PointF(endX, endY) };

            // Simple 15-degree BFS grid pathfinding
            int gridW = 36, gridH = 18;
            float stepX = 10, stepY = 10;
            
            var queue = new Queue<(int x, int y, List<PointF> path)>();
            var visited = new bool[gridW, gridH];

            int startGX = (int)((startX + 180) / stepX) % gridW;
            int startGY = (int)((startY + 90) / stepY) % gridH;
            int endGX = (int)((endX + 180) / stepX) % gridW;
            int endGY = (int)((endY + 90) / stepY) % gridH;

            queue.Enqueue((startGX, startGY, new List<PointF>()));
            visited[startGX, startGY] = true;

            int limit = 500;
            while (queue.Count > 0 && limit-- > 0)
            {
                var cur = queue.Dequeue();
                if (cur.x == endGX && cur.y == endGY)
                {
                    cur.path.Add(new PointF(endX, endY));
                    return cur.path;
                }

                int[] dx = { 0, 0, 1, -1, 1, 1, -1, -1 };
                int[] dy = { 1, -1, 0, 0, 1, -1, 1, -1 };

                for (int i = 0; i < 8; i++)
                {
                    int nx = (cur.x + dx[i] + gridW) % gridW;
                    int ny = cur.y + dy[i];
                    if (ny < 0 || ny >= gridH || visited[nx, ny]) continue;
                    
                    float nLng = nx * stepX - 180 + stepX/2;
                    float nLat = ny * stepY - 90 + stepY/2;

                    if (IsWater(nLng, nLat))
                    {
                        visited[nx, ny] = true;
                        var nPath = new List<PointF>(cur.path);
                        nPath.Add(new PointF(nLng, nLat));
                        queue.Enqueue((nx, ny, nPath));
                    }
                }
            }
            return new List<PointF> { new PointF(endX, endY) }; // Fallback
        }
    }
}
