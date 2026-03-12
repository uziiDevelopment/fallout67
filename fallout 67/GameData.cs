using System;
using System.Collections.Generic;
using System.Linq;

namespace fallover_67
{
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

        // 0–10: increases when this nation is struck; drives multi-missile salvos
        public int AngerLevel { get; set; } = 0;

        // True for nations controlled by another human player in multiplayer
        public bool IsHumanControlled { get; set; } = false;

        public Nation(string name, long pop, int nukes, int difficulty, long money, float x, float y)
        {
            Name = name;
            Population = pop;
            MaxPopulation = pop;
            Nukes = nukes;
            Difficulty = difficulty;
            Money = money;
            MapX = x;
            MapY = y;
        }
    }

    public class PlayerState
    {
        public string NationName { get; set; }
        public long Population { get; set; }
        public long Money { get; set; } = 2500;

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

        public List<string> Allies { get; set; } = new List<string>();
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

        public static void InitializeWorld(string playerChoice, int seed = -1)
        {
            Nations.Clear();
            ActiveMissions.Clear();
            Player = new PlayerState();

            // Calibrated X and Y coordinates for the provided map
            var rawNations = new List<Nation>
            {
                new Nation("USA",          330_000_000,   35, 5, 12000, 0.175f, 0.340f),
                new Nation("Canada",        39_000_000,    6, 2,  2500, 0.190f, 0.200f),
                new Nation("Mexico",       128_000_000,    0, 2,  1500, 0.160f, 0.450f),
                new Nation("Cuba",          11_000_000,    2, 1,   500, 0.235f, 0.465f),
                new Nation("Brazil",       214_000_000,    0, 2,  2000, 0.310f, 0.660f),
                new Nation("Argentina",     45_000_000,    0, 1,  1000, 0.270f, 0.830f),
                // Europe — spread horizontally and vertically to avoid cluster
                new Nation("UK",            69_000_000,   12, 3,  4500, 0.440f, 0.210f),
                new Nation("Spain",         47_000_000,    0, 3,  3000, 0.435f, 0.320f),
                new Nation("France",        66_000_000,   10, 3,  4000, 0.470f, 0.290f),
                new Nation("Germany",       84_000_000,   15, 4,  6000, 0.510f, 0.240f),
                new Nation("Italy",         59_000_000,    0, 3,  4000, 0.520f, 0.325f),
                new Nation("Ukraine",       43_000_000,    0, 2,  1000, 0.580f, 0.250f),
                new Nation("Russia",       144_000_000,   30, 5,  8000, 0.690f, 0.175f),
                // Middle East
                new Nation("Turkey",        84_000_000,    2, 3,  2500, 0.575f, 0.330f),
                new Nation("Israel",         9_000_000,   20, 4,  2000, 0.562f, 0.400f),
                new Nation("Egypt",        104_000_000,    0, 2,  1000, 0.548f, 0.435f),
                new Nation("Saudi Arabia",  35_000_000,    0, 3,  5000, 0.610f, 0.455f),
                new Nation("Iran",          85_000_000,    5, 3,  2000, 0.638f, 0.385f),
                // Africa
                new Nation("Nigeria",      211_000_000,    0, 2,  1500, 0.495f, 0.555f),
                new Nation("South Africa",  60_000_000,    2, 2,  1200, 0.545f, 0.770f),
                // South / Central Asia
                new Nation("Pakistan",     225_000_000,   15, 3,  1500, 0.655f, 0.430f),
                new Nation("India",      1_450_000_000,   18, 4,  3000, 0.685f, 0.470f),
                // East Asia — Korea separated clearly
                new Nation("China",      1_419_000_000,   35, 5, 10000, 0.760f, 0.390f),
                new Nation("North Korea",   26_000_000,   15, 3,   500, 0.815f, 0.305f),
                new Nation("South Korea",   51_000_000,    0, 4,  5000, 0.840f, 0.355f),
                new Nation("Japan",        125_000_000,    0, 4,  6000, 0.875f, 0.325f),
                new Nation("Indonesia",    273_000_000,    0, 3,  3000, 0.800f, 0.610f),
                new Nation("Australia",     26_000_000,    4, 1,  1000, 0.855f, 0.750f)
            };

            foreach (var n in rawNations) Nations.Add(n.Name, n);

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

            // Extract Player
            var chosen = Nations[playerChoice];
            Player.NationName = chosen.Name;
            Player.Population = chosen.MaxPopulation;
            Player.StandardNukes += chosen.Nukes;
            Player.Allies = new List<string>(chosen.Allies);
            Player.MapX = chosen.MapX; // Save location for the map rendering
            Player.MapY = chosen.MapY;

            Nations.Remove(playerChoice);
        }

        public static List<string> GetAllCountryNames()
        {
            return new List<string> { "USA", "Canada", "Mexico", "Cuba", "Brazil", "Argentina", "UK", "France", "Germany", "Italy", "Spain", "Ukraine", "Russia", "Turkey", "Israel", "Egypt", "Nigeria", "South Africa", "Saudi Arabia", "Iran", "Pakistan", "India", "China", "Japan", "South Korea", "North Korea", "Indonesia", "Australia" };
        }
    }
}