using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
        private ClientWebSocket?   _ws;
        private CancellationTokenSource _cts = new();
        private string        _serverBaseUrl = "";
        private string        _playerName    = "";
        private volatile bool _disposed;
        private volatile bool _isExplicitDisconnect;
        private int           _reconnectDelay = 1000;
        private const int     MaxReconnectDelay = 15_000;
        private const int     MaxReconnectAttempts = 30;
        private const int     PingIntervalMs = 25_000;  // Cloudflare hibernates idle sockets ~30s

        // Serialise all outbound sends through a single channel so they can't interleave
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        // Queue messages that arrive while disconnected; drain on reconnect
        private readonly ConcurrentQueue<byte[]> _outboundQueue = new();
        private const int MaxQueuedMessages = 64;

        public string         LocalPlayerId  { get; private set; } = "";
        public bool           IsHost         { get; private set; }
        public string         RoomCode       { get; private set; } = "";
        public List<MpPlayer> Players        { get; private set; } = new();
        public bool           IsConnected    => _ws?.State == WebSocketState.Open;
        public bool           IsReconnecting { get; private set; }

        // ── Events (fired on a thread-pool thread — Invoke to UI thread before touching UI) ──
        public event Action<string>?                     OnError;
        public event Action<List<MpPlayer>>?             OnRoomUpdated;
        public event Action<int, List<MpPlayer>>?        OnGameStart;
        public event Action<string, JsonElement>?        OnGameAction;
        public event Action<string, string, string>?     OnChat;
        public event Action?                             OnDisconnected;
        public event Action<int>?                        OnReconnecting;
        public event Action?                             OnReconnected;
        public event Action<string, string>?             OnPlayerDisconnected;  // playerId, name
        public event Action<string, string>?             OnPlayerReconnected;   // playerId, name

        // ── Connect helpers ──────────────────────────────────────────────────
        public async Task<string> CreateRoomAsync(string serverBaseUrl, string playerName)
        {
            _serverBaseUrl = serverBaseUrl;
            _playerName    = playerName;
            _isExplicitDisconnect = false;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var resp = await http.PostAsync($"{NormalizeHttp(serverBaseUrl)}/api/create", null);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
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
            // Tear down any previous socket cleanly
            if (_ws != null)
            {
                try
                {
                    if (_ws.State == WebSocketState.Open)
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnecting", CancellationToken.None);
                }
                catch { }
                try { _ws.Dispose(); } catch { }
            }

            // Renew CTS if the old one was cancelled (e.g., after a failed reconnect cycle)
            if (_cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }

            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            var uri = $"{NormalizeWs(_serverBaseUrl)}/ws?code={Uri.EscapeDataString(RoomCode)}&name={Uri.EscapeDataString(_playerName)}";

            // Connect with a 10-second timeout so we don't hang forever
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(10));
            await _ws.ConnectAsync(new Uri(uri), connectCts.Token);

            IsReconnecting = false;
            _reconnectDelay = 1000;

            // Drain any messages that queued while disconnected
            await DrainOutboundQueueAsync();

            // Start read loop and ping loop
            _ = Task.Run(ReadLoopAsync);
            _ = Task.Run(PingLoopAsync);
        }

        // ── Outbound messages ────────────────────────────────────────────────
        public Task SelectCountryAsync(string country) => SendAsync(new { type = "select_country", country });
        public Task StartGameAsync()                    => SendAsync(new { type = "start_game" });
        public Task SendGameActionAsync(object action) => SendAsync(new { type = "game_action", action });
        public Task SendChatAsync(string text)          => SendAsync(new { type = "chat", text });

        public async Task SendAsync(object payload)
        {
            if (_disposed) return;

            byte[] bytes;
            try { bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)); }
            catch { return; }

            if (_ws?.State != WebSocketState.Open)
            {
                // Queue for delivery on reconnect (drop oldest if full)
                if (_outboundQueue.Count < MaxQueuedMessages)
                    _outboundQueue.Enqueue(bytes);
                return;
            }

            await SendBytesAsync(bytes);
        }

        private async Task SendBytesAsync(byte[] bytes)
        {
            if (_ws?.State != WebSocketState.Open) return;

            // Serialise sends so two concurrent callers can't corrupt the WebSocket
            bool acquired = false;
            try
            {
                acquired = await _sendLock.WaitAsync(5000, _cts.Token);
                if (!acquired || _ws?.State != WebSocketState.Open) return;

                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                if (acquired) _sendLock.Release();
            }
        }

        private async Task DrainOutboundQueueAsync()
        {
            while (_outboundQueue.TryDequeue(out var bytes))
            {
                await SendBytesAsync(bytes);
                // Tiny yield so we don't flood the server on reconnect
                await Task.Delay(20);
            }
        }

        // ── Read loop ────────────────────────────────────────────────────────
        private async Task ReadLoopAsync()
        {
            // Use a MemoryStream to reassemble fragmented messages
            var buf = new byte[8192];
            using var msgStream = new MemoryStream();

            try
            {
                while (_ws?.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await _ws.ReceiveAsync(buf, _cts.Token);
                    }
                    catch (OperationCanceledException) { break; }

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        msgStream.Write(buf, 0, result.Count);

                        if (result.EndOfMessage)
                        {
                            // Full message assembled
                            string json = Encoding.UTF8.GetString(msgStream.GetBuffer(), 0, (int)msgStream.Length);
                            msgStream.SetLength(0);

                            try { HandleMessage(json); }
                            catch (Exception ex)
                            {
                                // Don't let one bad message kill the read loop
                                System.Diagnostics.Debug.WriteLine($"[MP] HandleMessage error: {ex.Message}");
                            }
                        }
                        // If !EndOfMessage, keep accumulating fragments
                    }
                }
            }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
            catch (Exception) { }
            finally
            {
                if (!_isExplicitDisconnect && !_disposed)
                    _ = Task.Run(HandleAutoReconnectAsync);
                else
                    OnDisconnected?.Invoke();
            }
        }

        // ── Ping/keepalive loop ──────────────────────────────────────────────
        private async Task PingLoopAsync()
        {
            // Cloudflare Workers hibernate idle WebSockets after ~30s.
            // Send a lightweight ping to keep the connection alive.
            try
            {
                while (_ws?.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    await Task.Delay(PingIntervalMs, _cts.Token);
                    if (_ws?.State != WebSocketState.Open) break;

                    // Send a tiny JSON message the server will just ignore (unknown type = no-op)
                    await SendBytesAsync(Encoding.UTF8.GetBytes("{\"type\":\"ping\"}"));
                }
            }
            catch { }
        }

        // ── Reconnection ─────────────────────────────────────────────────────
        private async Task HandleAutoReconnectAsync()
        {
            if (_isExplicitDisconnect || _disposed) return;

            IsReconnecting = true;

            for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
            {
                if (_isExplicitDisconnect || _disposed) break;

                OnReconnecting?.Invoke(attempt);

                try
                {
                    await ConnectWsAsync();
                    // Success — notify UI
                    OnReconnected?.Invoke();
                    return;
                }
                catch (Exception)
                {
                    await Task.Delay(_reconnectDelay);
                    _reconnectDelay = Math.Min(_reconnectDelay * 2, MaxReconnectDelay);
                }
            }

            // Exhausted all attempts
            IsReconnecting = false;
            OnDisconnected?.Invoke();
        }

        // ── Inbound message dispatch ─────────────────────────────────────────
        private void HandleMessage(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var tp)) return;
            string type = tp.GetString() ?? "";

            switch (type)
            {
                case "welcome":
                    LocalPlayerId = SafeGetString(root, "playerId");
                    IsHost        = SafeGetBool(root, "isHost");
                    if (root.TryGetProperty("room", out var roomEl) &&
                        roomEl.TryGetProperty("players", out var pl))
                        Players = ParsePlayers(pl);
                    OnRoomUpdated?.Invoke(Players);
                    break;

                case "player_joined":
                case "player_left":
                case "country_selected":
                    if (root.TryGetProperty("players", out var pls))
                        Players = ParsePlayers(pls);
                    OnRoomUpdated?.Invoke(Players);
                    break;

                case "player_disconnected":
                    OnPlayerDisconnected?.Invoke(
                        SafeGetString(root, "playerId"),
                        SafeGetString(root, "name"));
                    break;

                case "player_reconnected":
                    if (root.TryGetProperty("players", out var rps))
                        Players = ParsePlayers(rps);
                    OnPlayerReconnected?.Invoke(
                        SafeGetString(root, "playerId"),
                        SafeGetString(root, "name"));
                    OnRoomUpdated?.Invoke(Players);
                    break;

                case "you_are_host":
                    IsHost = true;
                    OnRoomUpdated?.Invoke(Players);
                    break;

                case "game_start":
                    int seed = SafeGetInt(root, "seed");
                    if (root.TryGetProperty("players", out var sp))
                    {
                        var starters = ParsePlayers(sp);
                        Players = starters;
                        OnGameStart?.Invoke(seed, starters);
                    }
                    break;

                case "game_action":
                    string senderId = SafeGetString(root, "senderId");
                    if (root.TryGetProperty("action", out var act))
                        OnGameAction?.Invoke(senderId, act.Clone());  // Clone so doc disposal doesn't invalidate it
                    break;

                case "chat":
                    OnChat?.Invoke(
                        SafeGetString(root, "senderId"),
                        SafeGetString(root, "name"),
                        SafeGetString(root, "text"));
                    break;

                case "error":
                    OnError?.Invoke(SafeGetString(root, "message", "Unknown error"));
                    break;

                // Server-side pong or unknown types — silently ignore
                default:
                    break;
            }
        }

        // ── Safe JSON accessors (never throw on missing/wrong-type keys) ────
        private static string SafeGetString(JsonElement el, string key, string fallback = "")
        {
            if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? fallback;
            return fallback;
        }

        private static int SafeGetInt(JsonElement el, string key, int fallback = 0)
        {
            if (el.TryGetProperty(key, out var v))
            {
                if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out int n)) return n;
            }
            return fallback;
        }

        private static bool SafeGetBool(JsonElement el, string key, bool fallback = false)
        {
            if (el.TryGetProperty(key, out var v))
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;
            }
            return fallback;
        }

        // ── Player parsing ───────────────────────────────────────────────────
        private static List<MpPlayer> ParsePlayers(JsonElement el)
        {
            var list = new List<MpPlayer>();
            if (el.ValueKind != JsonValueKind.Array) return list;
            foreach (var p in el.EnumerateArray())
            {
                list.Add(new MpPlayer
                {
                    Id      = SafeGetString(p, "id"),
                    Name    = SafeGetString(p, "name"),
                    Country = p.TryGetProperty("country", out var co) && co.ValueKind == JsonValueKind.String
                                ? co.GetString() : null,
                    Color   = SafeGetString(p, "color", "#00FFFF"),
                });
            }
            return list;
        }

        // ── URL normalization ────────────────────────────────────────────────
        private static string NormalizeHttp(string url) =>
            url.TrimEnd('/').Replace("wss://", "https://").Replace("ws://", "http://");

        private static string NormalizeWs(string url) =>
            url.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://");

        // ── Dispose ──────────────────────────────────────────────────────────
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _isExplicitDisconnect = true;

            try { _cts.Cancel(); } catch { }

            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
            }

            try { _cts.Dispose(); } catch { }
            _sendLock.Dispose();
        }
    }
}
