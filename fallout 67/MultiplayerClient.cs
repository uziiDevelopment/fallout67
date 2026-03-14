using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace fallover_67
{
    public class MpPlayer
    {
        public string Id      { get; set; } = "";
        public string Name    { get; set; } = "";
        public string? Country { get; set; }
        public string Color   { get; set; } = "#00FFFF";
    }

    public class MultiplayerClient : IDisposable
    {
        private ClientWebSocket   _ws  = new ClientWebSocket();
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private string        _serverBaseUrl = "";
        private string        _playerName    = "";
        private bool          _isExplicitDisconnect = false;
        private int           _reconnectDelay = 1000;

        public string        LocalPlayerId { get; private set; } = "";
        public bool          IsHost        { get; private set; }
        public string        RoomCode      { get; private set; } = "";
        public List<MpPlayer> Players      { get; private set; } = new();
        public bool          IsConnected   => _ws.State == WebSocketState.Open;
        public bool          IsReconnecting { get; private set; }

        // ── Events (fired on a thread-pool thread — Invoke to UI thread before touching UI) ──
        public event Action<string>?                     OnError;
        public event Action<List<MpPlayer>>?             OnRoomUpdated;    // lobby state changed
        public event Action<int, List<MpPlayer>>?        OnGameStart;      // seed, players
        public event Action<string, JsonElement>?        OnGameAction;     // senderId, action payload
        public event Action<string, string, string>?     OnChat;           // senderId, name, text
        public event Action?                             OnDisconnected;
        public event Action<int>?                        OnReconnecting;   // attempt number

        // ── Connect helpers ──────────────────────────────────────────────────
        public async Task<string> CreateRoomAsync(string serverBaseUrl, string playerName)
        {
            _serverBaseUrl = serverBaseUrl;
            _playerName    = playerName;
            _isExplicitDisconnect = false;

            using var http = new HttpClient();
            var resp = await http.PostAsync($"{NormalizeHttp(serverBaseUrl)}/api/create", null);
            resp.EnsureSuccessStatusCode();
            var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            RoomCode = doc.RootElement.GetProperty("code").GetString()!;
            await ConnectWsAsync();
            return RoomCode;
        }

        public async Task JoinRoomAsync(string serverBaseUrl, string code, string playerName)
        {
            _serverBaseUrl = serverBaseUrl;
            _playerName    = playerName;
            _isExplicitDisconnect = false;
            RoomCode       = code.Trim().ToUpperInvariant();
            await ConnectWsAsync();
        }

        private async Task ConnectWsAsync()
        {
            if (_ws.State == WebSocketState.Open) return;
            
            // Dispose old if needed
            if (_ws.State != WebSocketState.None)
            {
                try { _ws.Dispose(); } catch { }
                _ws = new ClientWebSocket();
            }

            var uri = $"{NormalizeWs(_serverBaseUrl)}/ws?code={Uri.EscapeDataString(RoomCode)}&name={Uri.EscapeDataString(_playerName)}";
            await _ws.ConnectAsync(new Uri(uri), _cts.Token);
            IsReconnecting = false;
            _reconnectDelay = 1000; // Reset backoff
            
            _ = Task.Run(ReadLoopAsync, _cts.Token);
        }

        // ── Outbound messages ────────────────────────────────────────────────
        public Task SelectCountryAsync(string country)    => SendAsync(new { type = "select_country", country });
        public Task StartGameAsync()                       => SendAsync(new { type = "start_game" });
        public Task SendGameActionAsync(object action)    => SendAsync(new { type = "game_action", action });
        public Task SendChatAsync(string text)             => SendAsync(new { type = "chat", text });

        public async Task SendAsync(object payload)
        {
            if (_ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
        }

        // ── Read loop ────────────────────────────────────────────────────────
        private async Task ReadLoopAsync()
        {
            var buf = new byte[65_536];
            try
            {
                while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(buf, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType == WebSocketMessageType.Text)
                        HandleMessage(Encoding.UTF8.GetString(buf, 0, result.Count));
                }
            }
            catch (Exception)
            {
                // Network error or server drop
            }
            finally
            {
                if (!_isExplicitDisconnect && !_cts.IsCancellationRequested)
                {
                    _ = Task.Run(HandleAutoReconnectAsync);
                }
                else
                {
                    OnDisconnected?.Invoke();
                }
            }
        }

        private async Task HandleAutoReconnectAsync()
        {
            IsReconnecting = true;
            int attempt = 1;
            
            while (!_isExplicitDisconnect && !_cts.IsCancellationRequested && _ws.State != WebSocketState.Open)
            {
                OnReconnecting?.Invoke(attempt++);
                try
                {
                    await ConnectWsAsync();
                    return; // Success!
                }
                catch
                {
                    await Task.Delay(_reconnectDelay);
                    _reconnectDelay = Math.Min(_reconnectDelay * 2, 15000); // Max 15s delay
                }
            }
        }

        private void HandleMessage(string json)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); } catch { return; }

            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var tp)) return;
            string type = tp.GetString() ?? "";

            switch (type)
            {
                case "welcome":
                    LocalPlayerId = root.GetProperty("playerId").GetString() ?? "";
                    IsHost        = root.GetProperty("isHost").GetBoolean();
                    if (root.TryGetProperty("room", out var roomEl) &&
                        roomEl.TryGetProperty("players", out var pl))
                        Players = ParsePlayers(pl);
                    OnRoomUpdated?.Invoke(Players);
                    break;

                case "player_joined":
                case "player_left":
                case "country_selected":
                    if (root.TryGetProperty("players", out var pls)) Players = ParsePlayers(pls);
                    OnRoomUpdated?.Invoke(Players);
                    break;

                case "you_are_host":
                    IsHost = true;
                    OnRoomUpdated?.Invoke(Players);
                    break;

                case "game_start":
                    int seed     = root.GetProperty("seed").GetInt32();
                    var starters = ParsePlayers(root.GetProperty("players"));
                    Players = starters;
                    OnGameStart?.Invoke(seed, starters);
                    break;

                case "game_action":
                    string senderId = root.GetProperty("senderId").GetString() ?? "";
                    if (root.TryGetProperty("action", out var act))
                        OnGameAction?.Invoke(senderId, act);
                    break;

                case "chat":
                    OnChat?.Invoke(
                        root.GetProperty("senderId").GetString() ?? "",
                        root.GetProperty("name").GetString()     ?? "",
                        root.GetProperty("text").GetString()     ?? "");
                    break;

                case "error":
                    OnError?.Invoke(root.GetProperty("message").GetString() ?? "Unknown error");
                    break;
            }
        }

        private static List<MpPlayer> ParsePlayers(JsonElement el)
        {
            var list = new List<MpPlayer>();
            if (el.ValueKind != JsonValueKind.Array) return list;
            foreach (var p in el.EnumerateArray())
            {
                list.Add(new MpPlayer
                {
                    Id      = p.TryGetProperty("id",      out var id)  ? id.GetString()  ?? "" : "",
                    Name    = p.TryGetProperty("name",    out var nm)  ? nm.GetString()  ?? "" : "",
                    Country = p.TryGetProperty("country", out var co) && co.ValueKind != JsonValueKind.Null
                                ? co.GetString() : null,
                    Color   = p.TryGetProperty("color",   out var cl)  ? cl.GetString()  ?? "#00FFFF" : "#00FFFF",
                });
            }
            return list;
        }

        private static string NormalizeHttp(string url) =>
            url.TrimEnd('/').Replace("wss://", "https://").Replace("ws://", "http://");

        private static string NormalizeWs(string url) =>
            url.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://");

        public void Dispose()
        {
            _isExplicitDisconnect = true;
            _cts.Cancel();
            try { _ws.Abort(); } catch { }
            _ws.Dispose();
        }
    }
}
