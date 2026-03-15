using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace fallover_67
{
    public class PlayerProfile
    {
        public string Username { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastPlayed { get; set; } = DateTime.UtcNow;

        // ── Match Stats ──────────────────────────────────────────────────
        public int MatchesPlayed { get; set; } = 0;
        public int MatchesWon { get; set; } = 0;
        public int MatchesLost { get; set; } = 0;

        // ── Combat Stats ─────────────────────────────────────────────────
        public long TotalKills { get; set; } = 0;
        public int TotalNukesLaunched { get; set; } = 0;
        public int StandardNukesLaunched { get; set; } = 0;
        public int TsarBombasLaunched { get; set; } = 0;
        public int BioPlaguesLaunched { get; set; } = 0;
        public int OrbitalLasersFired { get; set; } = 0;
        public int SatelliteKillersUsed { get; set; } = 0;
        public int NationsConquered { get; set; } = 0;
        public int NationsSurrendered { get; set; } = 0;

        // ── Defense Stats ────────────────────────────────────────────────
        public int MissilesIntercepted { get; set; } = 0;
        public long DamageAbsorbed { get; set; } = 0;

        // ── Troop Stats ──────────────────────────────────────────────────
        public int TroopMissionsLaunched { get; set; } = 0;
        public int TroopMissionsSucceeded { get; set; } = 0;
        public int TroopMissionsFailed { get; set; } = 0;

        // ── Diplomacy Stats ──────────────────────────────────────────────
        public int AlliancesFormed { get; set; } = 0;
        public int AlliancesBroken { get; set; } = 0;

        // ── Submarine Stats ──────────────────────────────────────────────
        public int SubmarinesDeployed { get; set; } = 0;
        public int SubmarineStrikesFired { get; set; } = 0;
        public int SubmarinesLost { get; set; } = 0;

        // ── Scoring ──────────────────────────────────────────────────────
        public long HighestScore { get; set; } = 0;
        public long TotalScoreEarned { get; set; } = 0;

        // ── Time Stats ───────────────────────────────────────────────────
        public long TotalPlayTimeSeconds { get; set; } = 0;
        public int LongestGameSeconds { get; set; } = 0;
        public int ShortestVictorySeconds { get; set; } = int.MaxValue;

        // ── Multiplayer Stats ────────────────────────────────────────────
        public int MultiplayerGamesPlayed { get; set; } = 0;
        public int MultiplayerWins { get; set; } = 0;

        // ── Favorite Nation ──────────────────────────────────────────────
        [JsonIgnore]
        public string FavoriteNation => _nationPlayCounts.Count > 0
            ? _nationPlayCounts.OrderByDescending(kv => kv.Value).First().Key
            : "None";

        public Dictionary<string, int> NationPlayCounts
        {
            get => _nationPlayCounts;
            set => _nationPlayCounts = value ?? new();
        }
        private Dictionary<string, int> _nationPlayCounts = new();

        // Helper — safely retrieve play-time in display format
        [JsonIgnore]
        public string PlayTimeFormatted
        {
            get
            {
                long total = TotalPlayTimeSeconds;
                if (total < 3600) return $"{total / 60}m";
                return $"{total / 3600}h {(total % 3600) / 60}m";
            }
        }

        [JsonIgnore]
        public float WinRate => MatchesPlayed > 0
            ? (float)MatchesWon / MatchesPlayed * 100f
            : 0f;
    }

    public static class ProfileManager
    {
        private static readonly string ProfileDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fallout67");
        private static readonly string ProfilePath = Path.Combine(ProfileDir, "profile.json");

        public static PlayerProfile CurrentProfile { get; private set; } = new PlayerProfile();
        public static bool HasProfile => !string.IsNullOrEmpty(CurrentProfile.Username);

        // ── Session state for continuous playtime tracking ───────────────
        private static DateTime _sessionStartTime;
        private static bool _isInGame = false;
        private static DateTime _lastAutoSave = DateTime.MinValue;
        private static bool _isMultiplayerSession = false;
        private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromSeconds(30);

        public static void Load()
        {
            try
            {
                if (File.Exists(ProfilePath))
                {
                    string json = File.ReadAllText(ProfilePath);
                    var loaded = JsonSerializer.Deserialize<PlayerProfile>(json);
                    if (loaded != null)
                        CurrentProfile = loaded;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profile] Load error: {ex.Message}");
                CurrentProfile = new PlayerProfile();
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ProfileDir);
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(CurrentProfile, opts);
                File.WriteAllText(ProfilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profile] Save error: {ex.Message}");
            }
        }

        // Auto-save: called after every stat change, but only writes to disk
        // at most every 30 seconds to avoid excessive I/O
        private static void AutoSave()
        {
            if ((DateTime.UtcNow - _lastAutoSave) >= AutoSaveInterval)
            {
                FlushPlayTime();
                Save();
                _lastAutoSave = DateTime.UtcNow;
            }
        }

        // Flush accumulated playtime from the current session into the profile
        private static void FlushPlayTime()
        {
            if (_isInGame)
            {
                var now = DateTime.UtcNow;
                long elapsed = (long)(now - _sessionStartTime).TotalSeconds;
                if (elapsed > 0)
                {
                    CurrentProfile.TotalPlayTimeSeconds += elapsed;
                    _sessionStartTime = now; // Reset so we don't double-count
                }
            }
        }

        public static void SetUsername(string name)
        {
            CurrentProfile.Username = name.Trim();
            Save();
        }

        // ── Called at game start ─────────────────────────────────────────
        public static void RecordGameStart(string nation, bool isMultiplayer)
        {
            _sessionStartTime = DateTime.UtcNow;
            _isInGame = true;
            _isMultiplayerSession = isMultiplayer;
            _lastAutoSave = DateTime.UtcNow;

            CurrentProfile.MatchesPlayed++;
            CurrentProfile.LastPlayed = DateTime.UtcNow;

            if (isMultiplayer)
                CurrentProfile.MultiplayerGamesPlayed++;

            if (!CurrentProfile.NationPlayCounts.ContainsKey(nation))
                CurrentProfile.NationPlayCounts[nation] = 0;
            CurrentProfile.NationPlayCounts[nation]++;

            Save();
        }

        // ── Called at game end (victory/defeat) ─────────────────────────
        public static void RecordGameEnd(bool victory, long score, int elapsedSeconds, bool isMultiplayer)
        {
            // Flush any remaining playtime
            FlushPlayTime();
            _isInGame = false;

            if (victory)
            {
                CurrentProfile.MatchesWon++;
                if (isMultiplayer)
                    CurrentProfile.MultiplayerWins++;

                if (elapsedSeconds < CurrentProfile.ShortestVictorySeconds)
                    CurrentProfile.ShortestVictorySeconds = elapsedSeconds;
            }
            else
            {
                CurrentProfile.MatchesLost++;
            }

            if (elapsedSeconds > CurrentProfile.LongestGameSeconds)
                CurrentProfile.LongestGameSeconds = elapsedSeconds;

            // Kills and nukes are already tracked in real-time by RecordKills/RecordNukeLaunch

            if (score > CurrentProfile.HighestScore)
                CurrentProfile.HighestScore = score;
            CurrentProfile.TotalScoreEarned += score;

            Save();
        }

        // ── Called when the game is closed without finishing ─────────────
        public static void FlushSession()
        {
            FlushPlayTime();
            _isInGame = false;
            Save();
        }

        // ── Incremental stat recording (all write in real-time) ─────────
        public static void RecordNukeLaunch(int weaponIndex)
        {
            CurrentProfile.TotalNukesLaunched++;
            switch (weaponIndex)
            {
                case 0: CurrentProfile.StandardNukesLaunched++; break;
                case 1: CurrentProfile.TsarBombasLaunched++; break;
                case 2: CurrentProfile.BioPlaguesLaunched++; break;
                case 3: CurrentProfile.OrbitalLasersFired++; break;
                case 4: CurrentProfile.SatelliteKillersUsed++; break;
            }
            AutoSave();
        }

        public static void RecordKills(long kills)
        {
            CurrentProfile.TotalKills += kills;
            AutoSave();
        }

        public static void RecordNationConquered(bool surrendered)
        {
            CurrentProfile.NationsConquered++;
            if (surrendered)
                CurrentProfile.NationsSurrendered++;
            AutoSave();
        }

        public static void RecordMissileIntercepted()
        {
            CurrentProfile.MissilesIntercepted++;
            AutoSave();
        }

        public static void RecordDamageAbsorbed(long damage)
        {
            CurrentProfile.DamageAbsorbed += damage;
            AutoSave();
        }

        public static void RecordTroopMission(bool success)
        {
            CurrentProfile.TroopMissionsLaunched++;
            if (success)
                CurrentProfile.TroopMissionsSucceeded++;
            else
                CurrentProfile.TroopMissionsFailed++;
            AutoSave();
        }

        public static void RecordAllianceFormed()
        {
            CurrentProfile.AlliancesFormed++;
            AutoSave();
        }

        public static void RecordAllianceBroken()
        {
            CurrentProfile.AlliancesBroken++;
            AutoSave();
        }

        public static void RecordSubmarineDeployed()
        {
            CurrentProfile.SubmarinesDeployed++;
            AutoSave();
        }

        public static void RecordSubmarineStrike()
        {
            CurrentProfile.SubmarineStrikesFired++;
            AutoSave();
        }

        public static void RecordSubmarineLost()
        {
            CurrentProfile.SubmarinesLost++;
            AutoSave();
        }

        // ── Sync profile to server ──────────────────────────────────────
        public static async Task SyncToServerAsync(string serverUrl)
        {
            if (!HasProfile) return;
            FlushPlayTime();
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var payload = JsonSerializer.Serialize(CurrentProfile);
                var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                await http.PostAsync($"{serverUrl.TrimEnd('/')}/api/profile", content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Profile] Sync error: {ex.Message}");
            }
        }

        // Shared deserialization options — case-insensitive to handle server casing
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static async Task<PlayerProfile?> FetchProfileAsync(string serverUrl, string username)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var resp = await http.GetStringAsync($"{serverUrl.TrimEnd('/')}/api/profile?name={Uri.EscapeDataString(username)}");
                return JsonSerializer.Deserialize<PlayerProfile>(resp, _jsonOpts);
            }
            catch
            {
                return null;
            }
        }

        public static async Task<List<PlayerProfile>> FetchAllProfilesAsync(string serverUrl)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var resp = await http.GetStringAsync($"{serverUrl.TrimEnd('/')}/api/profiles");
                var wrapper = JsonSerializer.Deserialize<ProfileListResponse>(resp, _jsonOpts);
                return wrapper?.Profiles ?? new List<PlayerProfile>();
            }
            catch
            {
                return new List<PlayerProfile>();
            }
        }

        private class ProfileListResponse
        {
            public List<PlayerProfile> Profiles { get; set; } = new();
        }
    }
}
