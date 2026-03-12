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
                new Nation("USA", 330_000_000, 35, 5, 12000, 0.18f, 0.35f),
                new Nation("Canada", 39_000_000, 6, 2, 2500, 0.20f, 0.22f),
                new Nation("Mexico", 128_000_000, 0, 2, 1500, 0.17f, 0.44f),
                new Nation("Cuba", 11_000_000, 2, 1, 500, 0.24f, 0.46f),
                new Nation("Brazil", 214_000_000, 0, 2, 2000, 0.32f, 0.65f),
                new Nation("Argentina", 45_000_000, 0, 1, 1000, 0.28f, 0.82f),
                new Nation("UK", 69_000_000, 12, 3, 4500, 0.465f, 0.26f),
                new Nation("France", 66_000_000, 10, 3, 4000, 0.48f, 0.31f),
                new Nation("Germany", 84_000_000, 15, 4, 6000, 0.51f, 0.29f),
                new Nation("Italy", 59_000_000, 0, 3, 4000, 0.52f, 0.34f),
                new Nation("Spain", 47_000_000, 0, 3, 3000, 0.46f, 0.35f),
                new Nation("Ukraine", 43_000_000, 0, 2, 1000, 0.57f, 0.28f),
                new Nation("Russia", 144_000_000, 30, 5, 8000, 0.68f, 0.20f),
                new Nation("Turkey", 84_000_000, 2, 3, 2500, 0.57f, 0.35f),
                new Nation("Israel", 9_000_000, 20, 4, 2000, 0.56f, 0.40f),
                new Nation("Egypt", 104_000_000, 0, 2, 1000, 0.55f, 0.43f),
                new Nation("Nigeria", 211_000_000, 0, 2, 1500, 0.50f, 0.55f),
                new Nation("South Africa", 60_000_000, 2, 2, 1200, 0.55f, 0.76f),
                new Nation("Saudi Arabia", 35_000_000, 0, 3, 5000, 0.60f, 0.46f),
                new Nation("Iran", 85_000_000, 5, 3, 2000, 0.63f, 0.40f),
                new Nation("Pakistan", 225_000_000, 15, 3, 1500, 0.66f, 0.43f),
                new Nation("India", 1_450_000_000, 18, 4, 3000, 0.69f, 0.47f),
                new Nation("China", 1_419_000_000, 35, 5, 10000, 0.76f, 0.41f),
                new Nation("Japan", 125_000_000, 0, 4, 6000, 0.86f, 0.35f),
                new Nation("South Korea", 51_000_000, 0, 4, 5000, 0.83f, 0.36f),
                new Nation("North Korea", 26_000_000, 15, 3, 500, 0.82f, 0.34f),
                new Nation("Indonesia", 273_000_000, 0, 3, 3000, 0.80f, 0.62f),
                new Nation("Australia", 26_000_000, 4, 1, 1000, 0.85f, 0.75f)
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