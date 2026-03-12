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

        // Scoring multiplier when this nation is chosen as the player's starting country.
        // Higher = harder start = bigger score bonus. Superpower default = 1.0.
        public float ScoreMultiplier { get; set; } = 1.0f;

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

        // When false, only nations with population ≥ 5,000,000 are included as AI opponents.
        // Set by LobbyForm before calling InitializeWorld.
        public static bool HardMode { get; set; } = false;

        // The 28 major-power nations shown in casual mode (pop always ≥ 5M)
        private static readonly HashSet<string> CasualCountries = new HashSet<string>
        {
            "USA", "Canada", "Mexico", "Cuba", "Brazil", "Argentina", "UK", "France", "Germany",
            "Italy", "Spain", "Ukraine", "Russia", "Turkey", "Israel", "Egypt", "Nigeria",
            "South Africa", "Saudi Arabia", "Iran", "Pakistan", "India", "China", "Japan",
            "South Korea", "North Korea", "Indonesia", "Australia"
        };

        // All nation data — defined once, shared by InitializeWorld and GetAllCountryNames
        private static List<Nation> BuildRawNations() => new List<Nation>
        {
                // ==========================================
                // NORTH & CENTRAL AMERICA + CARIBBEAN
                // ==========================================
                new Nation("USA",          330_000_000,   35, 5, 12000, 0.175f, 0.340f, 1.0f),
                new Nation("Little Saint James Island",          200_000_000,   50, 10, 25000, 0.175f, 0.340f, 6.0f),
                new Nation("Canada",        39_000_000,    6, 2,  2500, 0.190f, 0.200f, 1.4f),
                new Nation("Mexico",       128_000_000,    0, 2,  1500, 0.160f, 0.450f, 1.8f),
                new Nation("Cuba",          11_000_000,    2, 1,   500, 0.235f, 0.465f, 2.5f),
                new Nation("Guatemala",     17_000_000,    0, 1,   300, 0.165f, 0.470f, 2.8f),
                new Nation("Belize",           400_000,    0, 1,    50, 0.170f, 0.465f, 3.5f),
                new Nation("Honduras",      10_000_000,    0, 1,   200, 0.175f, 0.475f, 3.0f),
                new Nation("El Salvador",    6_000_000,    0, 1,   200, 0.170f, 0.480f, 3.0f),
                new Nation("Nicaragua",      7_000_000,    0, 1,   150, 0.180f, 0.485f, 3.1f),
                new Nation("Costa Rica",     5_000_000,    0, 1,   400, 0.185f, 0.495f, 2.5f),
                new Nation("Panama",         4_000_000,    0, 1,   500, 0.190f, 0.505f, 2.4f),
                new Nation("Jamaica",        3_000_000,    0, 1,   150, 0.220f, 0.470f, 2.8f),
                new Nation("Haiti",         11_000_000,    0, 1,   100, 0.245f, 0.470f, 3.5f),
                new Nation("Dominican Rep.",11_000_000,    0, 1,   400, 0.255f, 0.470f, 2.5f),
                new Nation("Bahamas",          400_000,    0, 1,   150, 0.220f, 0.430f, 3.0f),
                new Nation("Trinidad & Tobago",1_500_000,  0, 1,   250, 0.270f, 0.510f, 2.7f),
                new Nation("Barbados",         280_000,    0, 1,   100, 0.275f, 0.490f, 3.2f),
                new Nation("Antigua & Barbuda", 97_000,    0, 1,    50, 0.260f, 0.460f, 3.5f),
                new Nation("Saint Lucia",      180_000,    0, 1,    50, 0.270f, 0.480f, 3.6f),
                new Nation("Grenada",          125_000,    0, 1,    50, 0.265f, 0.500f, 3.6f),
                new Nation("St. Vincent",      110_000,    0, 1,    50, 0.268f, 0.490f, 3.6f),
                new Nation("St. Kitts & Nevis", 47_000,    0, 1,    50, 0.260f, 0.470f, 3.8f),
                new Nation("Dominica",          72_000,    0, 1,    50, 0.265f, 0.475f, 3.7f),

                // ==========================================
                // SOUTH AMERICA
                // ==========================================
                new Nation("Brazil",       214_000_000,    0, 2,  2000, 0.310f, 0.660f, 1.6f),
                new Nation("Argentina",     45_000_000,    0, 1,  1000, 0.270f, 0.830f, 2.0f),
                new Nation("Colombia",      51_000_000,    0, 2,   800, 0.220f, 0.530f, 2.1f),
                new Nation("Venezuela",     28_000_000,    0, 1,   300, 0.240f, 0.510f, 2.8f),
                new Nation("Guyana",           800_000,    0, 1,   150, 0.260f, 0.520f, 3.0f),
                new Nation("Suriname",         600_000,    0, 1,   100, 0.270f, 0.525f, 3.1f),
                new Nation("Ecuador",       18_000_000,    0, 1,   400, 0.200f, 0.560f, 2.5f),
                new Nation("Peru",          33_000_000,    0, 1,   600, 0.210f, 0.600f, 2.3f),
                new Nation("Bolivia",       12_000_000,    0, 1,   300, 0.240f, 0.650f, 2.7f),
                new Nation("Paraguay",       7_000_000,    0, 1,   300, 0.260f, 0.700f, 2.6f),
                new Nation("Chile",         19_000_000,    0, 2,  1000, 0.200f, 0.750f, 2.0f),
                new Nation("Uruguay",        3_500_000,    0, 1,   400, 0.280f, 0.780f, 2.4f),

                // ==========================================
                // EUROPE
                // ==========================================
                new Nation("UK",            69_000_000,   12, 3,  4500, 0.440f, 0.210f, 1.2f),
                new Nation("Spain",         47_000_000,    0, 3,  3000, 0.435f, 0.320f, 1.6f),
                new Nation("France",        66_000_000,   10, 3,  4000, 0.470f, 0.290f, 1.2f),
                new Nation("Germany",       84_000_000,   15, 4,  6000, 0.510f, 0.240f, 1.3f),
                new Nation("Italy",         59_000_000,    0, 3,  4000, 0.520f, 0.325f, 1.5f),
                new Nation("Ukraine",       43_000_000,    0, 2,  1000, 0.580f, 0.250f, 2.0f),
                new Nation("Russia",       144_000_000,   30, 5,  8000, 0.690f, 0.175f, 1.0f),
                new Nation("Portugal",      10_000_000,    0, 2,  1200, 0.410f, 0.330f, 2.0f),
                new Nation("Ireland",        5_000_000,    0, 2,  1500, 0.420f, 0.200f, 1.9f),
                new Nation("Iceland",          370_000,    0, 1,   300, 0.380f, 0.120f, 2.8f),
                new Nation("Norway",         5_400_000,    0, 2,  2500, 0.480f, 0.140f, 1.8f),
                new Nation("Sweden",        10_000_000,    0, 2,  2500, 0.500f, 0.150f, 1.7f),
                new Nation("Finland",        5_500_000,    0, 2,  2000, 0.540f, 0.140f, 1.8f),
                new Nation("Denmark",        6_000_000,    0, 2,  2000, 0.490f, 0.190f, 1.8f),
                new Nation("Estonia",        1_300_000,    0, 1,   400, 0.550f, 0.160f, 2.5f),
                new Nation("Latvia",         1_900_000,    0, 1,   400, 0.550f, 0.170f, 2.5f),
                new Nation("Lithuania",      2_800_000,    0, 1,   500, 0.550f, 0.180f, 2.4f),
                new Nation("Belarus",        9_000_000,    0, 1,   600, 0.570f, 0.210f, 2.5f),
                new Nation("Poland",        38_000_000,    0, 3,  2000, 0.530f, 0.220f, 1.6f),
                new Nation("Czechia",       10_500_000,    0, 2,  1200, 0.520f, 0.240f, 1.9f),
                new Nation("Slovakia",       5_400_000,    0, 1,   800, 0.530f, 0.250f, 2.2f),
                new Nation("Hungary",       10_000_000,    0, 1,  1000, 0.535f, 0.260f, 2.1f),
                new Nation("Austria",        9_000_000,    0, 2,  1500, 0.515f, 0.260f, 1.9f),
                new Nation("Switzerland",    8_700_000,    0, 2,  3000, 0.490f, 0.270f, 1.7f),
                new Nation("Belgium",       11_600_000,    0, 2,  1800, 0.480f, 0.230f, 1.8f),
                new Nation("Netherlands",   17_500_000,    0, 3,  2500, 0.485f, 0.220f, 1.7f),
                new Nation("Luxembourg",       640_000,    0, 1,   800, 0.482f, 0.240f, 2.6f),
                new Nation("Romania",       19_000_000,    0, 2,  1200, 0.560f, 0.280f, 2.0f),
                new Nation("Moldova",        2_600_000,    0, 1,   200, 0.575f, 0.265f, 3.0f),
                new Nation("Bulgaria",       6_800_000,    0, 1,   700, 0.565f, 0.300f, 2.3f),
                new Nation("Greece",        10_000_000,    0, 2,  1200, 0.550f, 0.330f, 2.0f),
                new Nation("Serbia",         7_000_000,    0, 1,   600, 0.540f, 0.290f, 2.4f),
                new Nation("Croatia",        4_000_000,    0, 1,   500, 0.530f, 0.290f, 2.4f),
                new Nation("Bosnia & Herz.", 3_200_000,    0, 1,   300, 0.535f, 0.295f, 2.7f),
                new Nation("Slovenia",       2_100_000,    0, 1,   400, 0.520f, 0.280f, 2.5f),
                new Nation("Albania",        2_800_000,    0, 1,   300, 0.545f, 0.310f, 2.6f),
                new Nation("North Macedonia",2_000_000,    0, 1,   200, 0.550f, 0.305f, 2.8f),
                new Nation("Montenegro",       600_000,    0, 1,   100, 0.540f, 0.300f, 3.0f),
                new Nation("Kosovo",         1_800_000,    0, 1,   100, 0.545f, 0.300f, 3.2f),
                new Nation("Malta",            500_000,    0, 1,   200, 0.520f, 0.350f, 3.0f),
                new Nation("Andorra",           79_000,    0, 1,   100, 0.450f, 0.310f, 3.5f),
                new Nation("Monaco",            39_000,    0, 1,   500, 0.475f, 0.300f, 3.5f),
                new Nation("Liechtenstein",     39_000,    0, 1,   200, 0.500f, 0.265f, 3.5f),
                new Nation("San Marino",        34_000,    0, 1,   100, 0.522f, 0.320f, 3.8f),
                new Nation("Vatican City",         800,    0, 1,   500, 0.521f, 0.326f, 4.0f),

                // ==========================================
                // MIDDLE EAST & CAUCASUS
                // ==========================================
                new Nation("Turkey",        84_000_000,    2, 3,  2500, 0.575f, 0.330f, 1.4f),
                new Nation("Israel",         9_000_000,   20, 4,  2000, 0.562f, 0.400f, 1.8f),
                new Nation("Egypt",        104_000_000,    0, 2,  1000, 0.548f, 0.435f, 1.8f),
                new Nation("Saudi Arabia",  35_000_000,    0, 3,  5000, 0.610f, 0.455f, 1.7f),
                new Nation("Iran",          85_000_000,    5, 3,  2000, 0.638f, 0.385f, 1.6f),
                new Nation("Syria",         21_000_000,    0, 1,   200, 0.580f, 0.370f, 3.0f),
                new Nation("Iraq",          43_000_000,    0, 2,   800, 0.600f, 0.380f, 2.2f),
                new Nation("Jordan",        11_000_000,    0, 1,   400, 0.575f, 0.395f, 2.5f),
                new Nation("Lebanon",        5_000_000,    0, 1,   200, 0.570f, 0.380f, 2.8f),
                new Nation("Kuwait",         4_000_000,    0, 1,  1000, 0.615f, 0.400f, 2.2f),
                new Nation("UAE",            9_000_000,    0, 2,  2000, 0.640f, 0.430f, 1.9f),
                new Nation("Qatar",          2_900_000,    0, 1,  1500, 0.630f, 0.420f, 2.0f),
                new Nation("Bahrain",        1_500_000,    0, 1,   500, 0.625f, 0.415f, 2.4f),
                new Nation("Oman",           5_000_000,    0, 1,   800, 0.650f, 0.450f, 2.3f),
                new Nation("Yemen",         33_000_000,    0, 1,   200, 0.610f, 0.480f, 3.2f),
                new Nation("Cyprus",         1_200_000,    0, 1,   300, 0.565f, 0.360f, 2.5f),
                new Nation("Georgia",        3_700_000,    0, 1,   300, 0.600f, 0.310f, 2.7f),
                new Nation("Armenia",        2_900_000,    0, 1,   200, 0.605f, 0.320f, 2.8f),
                new Nation("Azerbaijan",    10_000_000,    0, 1,   600, 0.615f, 0.315f, 2.4f),

                // ==========================================
                // CENTRAL & SOUTH ASIA
                // ==========================================
                new Nation("Pakistan",     225_000_000,   15, 3,  1500, 0.655f, 0.430f, 1.5f),
                new Nation("India",      1_450_000_000,   18, 4,  3000, 0.685f, 0.470f, 1.3f),
                new Nation("Afghanistan",   40_000_000,    0, 1,   200, 0.660f, 0.390f, 3.0f),
                new Nation("Kazakhstan",    19_000_000,    0, 2,  1000, 0.660f, 0.250f, 2.1f),
                new Nation("Uzbekistan",    35_000_000,    0, 1,   500, 0.650f, 0.300f, 2.4f),
                new Nation("Turkmenistan",   6_000_000,    0, 1,   400, 0.630f, 0.320f, 2.5f),
                new Nation("Kyrgyzstan",     6_000_000,    0, 1,   200, 0.680f, 0.290f, 2.8f),
                new Nation("Tajikistan",     9_000_000,    0, 1,   200, 0.670f, 0.310f, 2.8f),
                new Nation("Bangladesh",   169_000_000,    0, 2,   800, 0.720f, 0.440f, 2.2f),
                new Nation("Sri Lanka",     22_000_000,    0, 1,   400, 0.700f, 0.520f, 2.6f),
                new Nation("Nepal",         30_000_000,    0, 1,   300, 0.710f, 0.410f, 2.7f),
                new Nation("Bhutan",           700_000,    0, 1,   100, 0.720f, 0.415f, 3.2f),
                new Nation("Maldives",         500_000,    0, 1,   100, 0.690f, 0.530f, 3.5f),

                // ==========================================
                // EAST & SOUTHEAST ASIA
                // ==========================================
                new Nation("China",      1_419_000_000,   35, 5, 10000, 0.760f, 0.390f, 1.0f),
                new Nation("North Korea",   26_000_000,   15, 3,   500, 0.815f, 0.305f, 1.8f),
                new Nation("South Korea",   51_000_000,    0, 4,  5000, 0.840f, 0.355f, 1.5f),
                new Nation("Japan",        125_000_000,    0, 4,  6000, 0.875f, 0.325f, 1.5f),
                new Nation("Indonesia",    273_000_000,    0, 3,  3000, 0.800f, 0.610f, 1.9f),
                new Nation("Mongolia",       3_300_000,    0, 1,   200, 0.750f, 0.270f, 2.8f),
                new Nation("Taiwan",        23_000_000,    0, 3,  3000, 0.810f, 0.430f, 1.8f),
                new Nation("Myanmar",       54_000_000,    0, 1,   400, 0.740f, 0.460f, 2.6f),
                new Nation("Thailand",      71_000_000,    0, 2,  1500, 0.750f, 0.490f, 2.0f),
                new Nation("Vietnam",       98_000_000,    0, 2,  1200, 0.770f, 0.470f, 2.1f),
                new Nation("Laos",           7_000_000,    0, 1,   200, 0.760f, 0.475f, 2.9f),
                new Nation("Cambodia",      16_000_000,    0, 1,   300, 0.765f, 0.500f, 2.7f),
                new Nation("Malaysia",      33_000_000,    0, 2,  1800, 0.770f, 0.550f, 1.9f),
                new Nation("Singapore",      5_600_000,    0, 2,  2500, 0.775f, 0.560f, 1.7f),
                new Nation("Philippines",  113_000_000,    0, 2,  1200, 0.820f, 0.490f, 2.1f),
                new Nation("Brunei",           400_000,    0, 1,   400, 0.790f, 0.540f, 2.5f),
                new Nation("Timor-Leste",    1_300_000,    0, 1,   100, 0.830f, 0.630f, 3.5f),

                // ==========================================
                // AFRICA
                // ==========================================
                new Nation("Nigeria",      211_000_000,    0, 2,  1500, 0.495f, 0.555f, 2.2f),
                new Nation("South Africa",  60_000_000,    2, 2,  1200, 0.545f, 0.770f, 2.1f),
                new Nation("Morocco",       37_000_000,    0, 2,   800, 0.420f, 0.380f, 2.2f),
                new Nation("Algeria",       44_000_000,    0, 2,  1000, 0.450f, 0.390f, 2.1f),
                new Nation("Tunisia",       12_000_000,    0, 1,   400, 0.480f, 0.370f, 2.5f),
                new Nation("Libya",          6_800_000,    0, 1,   500, 0.510f, 0.400f, 2.6f),
                new Nation("Sudan",         45_000_000,    0, 1,   300, 0.540f, 0.470f, 2.8f),
                new Nation("South Sudan",   11_000_000,    0, 1,   100, 0.540f, 0.510f, 3.5f),
                new Nation("Mali",          21_000_000,    0, 1,   200, 0.430f, 0.460f, 3.0f),
                new Nation("Niger",         25_000_000,    0, 1,   150, 0.470f, 0.465f, 3.2f),
                new Nation("Chad",          17_000_000,    0, 1,   150, 0.500f, 0.480f, 3.1f),
                new Nation("Mauritania",     4_600_000,    0, 1,   150, 0.410f, 0.450f, 3.2f),
                new Nation("Senegal",       17_000_000,    0, 1,   300, 0.390f, 0.480f, 2.8f),
                new Nation("Gambia",         2_600_000,    0, 1,    50, 0.385f, 0.485f, 3.5f),
                new Nation("Guinea-Bissau",  2_000_000,    0, 1,    50, 0.380f, 0.490f, 3.6f),
                new Nation("Guinea",        13_000_000,    0, 1,   200, 0.390f, 0.500f, 3.1f),
                new Nation("Sierra Leone",   8_000_000,    0, 1,   100, 0.385f, 0.510f, 3.3f),
                new Nation("Liberia",        5_000_000,    0, 1,   100, 0.390f, 0.520f, 3.4f),
                new Nation("Ivory Coast",   27_000_000,    0, 1,   500, 0.410f, 0.530f, 2.6f),
                new Nation("Ghana",         32_000_000,    0, 1,   600, 0.430f, 0.530f, 2.5f),
                new Nation("Togo",           8_000_000,    0, 1,   100, 0.440f, 0.530f, 3.2f),
                new Nation("Benin",         13_000_000,    0, 1,   150, 0.450f, 0.530f, 3.1f),
                new Nation("Burkina Faso",  22_000_000,    0, 1,   200, 0.430f, 0.490f, 3.0f),
                new Nation("Cape Verde",       500_000,    0, 1,   100, 0.350f, 0.470f, 3.3f),
                new Nation("Cameroon",      27_000_000,    0, 1,   400, 0.480f, 0.540f, 2.7f),
                new Nation("Central African Rep.",5_000_000,0,1,   100, 0.510f, 0.530f, 3.5f),
                new Nation("Equatorial Guinea", 1_600_000, 0, 1,   200, 0.470f, 0.550f, 3.2f),
                new Nation("Gabon",          2_300_000,    0, 1,   300, 0.475f, 0.560f, 2.9f),
                new Nation("Congo",          5_800_000,    0, 1,   200, 0.490f, 0.570f, 3.0f),
                new Nation("DR Congo",      95_000_000,    0, 1,   500, 0.510f, 0.590f, 2.5f),
                new Nation("Sao Tome & Prin.", 200_000,    0, 1,    50, 0.460f, 0.550f, 3.8f),
                new Nation("Ethiopia",     120_000_000,    0, 2,   700, 0.580f, 0.500f, 2.3f),
                new Nation("Somalia",       17_000_000,    0, 1,   100, 0.600f, 0.510f, 3.6f),
                new Nation("Djibouti",       1_000_000,    0, 1,   100, 0.590f, 0.490f, 3.2f),
                new Nation("Eritrea",        3_600_000,    0, 1,   100, 0.570f, 0.480f, 3.4f),
                new Nation("Kenya",         54_000_000,    0, 1,   800, 0.570f, 0.550f, 2.4f),
                new Nation("Uganda",        47_000_000,    0, 1,   500, 0.550f, 0.550f, 2.6f),
                new Nation("Rwanda",        13_000_000,    0, 1,   200, 0.540f, 0.560f, 2.8f),
                new Nation("Burundi",       12_000_000,    0, 1,   100, 0.540f, 0.570f, 3.2f),
                new Nation("Tanzania",      63_000_000,    0, 1,   600, 0.560f, 0.590f, 2.5f),
                new Nation("Seychelles",       100_000,    0, 1,   100, 0.610f, 0.590f, 3.5f),
                new Nation("Angola",        34_000_000,    0, 1,   600, 0.490f, 0.630f, 2.6f),
                new Nation("Zambia",        19_000_000,    0, 1,   300, 0.530f, 0.650f, 2.8f),
                new Nation("Zimbabwe",      15_000_000,    0, 1,   200, 0.540f, 0.680f, 2.9f),
                new Nation("Malawi",        20_000_000,    0, 1,   150, 0.550f, 0.640f, 3.1f),
                new Nation("Mozambique",    32_000_000,    0, 1,   300, 0.560f, 0.670f, 2.8f),
                new Nation("Madagascar",    28_000_000,    0, 1,   200, 0.590f, 0.680f, 3.0f),
                new Nation("Namibia",        2_500_000,    0, 1,   200, 0.500f, 0.720f, 2.9f),
                new Nation("Botswana",       2_400_000,    0, 1,   300, 0.530f, 0.720f, 2.7f),
                new Nation("Lesotho",        2_200_000,    0, 1,   100, 0.540f, 0.760f, 3.2f),
                new Nation("Eswatini",       1_100_000,    0, 1,   100, 0.550f, 0.750f, 3.2f),
                new Nation("Comoros",          800_000,    0, 1,    50, 0.580f, 0.630f, 3.6f),
                new Nation("Mauritius",      1_200_000,    0, 1,   200, 0.620f, 0.690f, 3.0f),

                // ==========================================
                // OCEANIA
                // ==========================================
                new Nation("Australia",     26_000_000,    4, 1,  1000, 0.855f, 0.750f, 1.7f),
                new Nation("New Zealand",    5_100_000,    0, 2,  1500, 0.890f, 0.850f, 1.8f),
                new Nation("Papua New Guinea",9_000_000,   0, 1,   200, 0.850f, 0.600f, 2.9f),
                new Nation("Fiji",             900_000,    0, 1,   100, 0.920f, 0.650f, 3.2f),
                new Nation("Solomon Islands",  700_000,    0, 1,    50, 0.880f, 0.620f, 3.5f),
                new Nation("Vanuatu",          300_000,    0, 1,    50, 0.900f, 0.640f, 3.6f),
                new Nation("Samoa",            200_000,    0, 1,    50, 0.950f, 0.630f, 3.5f),
                new Nation("Kiribati",         100_000,    0, 1,    50, 0.930f, 0.580f, 3.8f),
                new Nation("Micronesia",       100_000,    0, 1,    50, 0.880f, 0.550f, 3.8f),
                new Nation("Tonga",            100_000,    0, 1,    50, 0.950f, 0.660f, 3.6f),
                new Nation("Marshall Islands",  40_000,    0, 1,    50, 0.910f, 0.540f, 3.9f),
                new Nation("Palau",             20_000,    0, 1,    50, 0.850f, 0.540f, 3.9f),
                new Nation("Tuvalu",            10_000,    0, 1,    50, 0.940f, 0.600f, 4.0f),
                new Nation("Nauru",             10_000,    0, 1,    50, 0.920f, 0.590f, 4.0f),
        };

        public static void InitializeWorld(string playerChoice, int seed = -1)
        {
            Nations.Clear();
            ActiveMissions.Clear();
            Player = new PlayerState();

            var rawNations = BuildRawNations();

            // In casual mode, only include nations with population ≥ 5,000,000
            var filteredNations = HardMode
                ? rawNations
                : rawNations.Where(n => n.Population >= 5_000_000).ToList();

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

            // Extract Player — copy chosen nation's data into PlayerState and remove from AI pool
            var chosen = Nations[playerChoice];
            Player.NationName = chosen.Name;
            Player.Population = chosen.MaxPopulation;
            Player.StandardNukes += chosen.Nukes;
            Player.Allies = new List<string>(chosen.Allies);
            Player.MapX = chosen.MapX;
            Player.MapY = chosen.MapY;
            Player.ScoreMultiplier = chosen.ScoreMultiplier;

            Nations.Remove(playerChoice);
        }

        public static List<string> GetAllCountryNames(bool hardMode = false)
        {
            if (!hardMode) return new List<string>(CasualCountries);
            // Hard mode: all nations with pop ≥ 5M
            return BuildRawNations()
                .Where(n => n.Population >= 5_000_000)
                .Select(n => n.Name)
                .ToList();
        }
    }
}
