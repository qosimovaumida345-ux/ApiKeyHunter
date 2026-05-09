using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AntigravityMobile.Models;

namespace AntigravityMobile.Services
{
    /// <summary>
    /// Antigravity Mobile Controller Server v2026
    /// WebSocket orqali Android qurilmalarni masofadan boshqarish serveri.
    /// Funksiyalar: Screen Mirroring, Remote Click/Type, App Launch, File Transfer
    /// </summary>
    public class MobileControllerServer
    {
        // ==================== FIELDS ====================
        private HttpListener? _listener;
        private readonly int _port;
        private bool _isRunning;
        private CancellationTokenSource? _cts;

        private readonly ConcurrentDictionary<string, DeviceConnection> _connectedDevices = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeat = new();
        private readonly ConcurrentDictionary<string, Queue<MobileCommand>> _commandQueues = new();
        private readonly ConcurrentDictionary<string, DeviceInfo> _deviceInfoMap = new();

        // Frame buffer for each device
        private readonly ConcurrentDictionary<string, byte[]> _latestFrames = new();
        private readonly ConcurrentDictionary<string, long> _frameCounters = new();
        private readonly ConcurrentDictionary<string, double> _fpsCounters = new();

        // Performance monitoring
        private readonly ConcurrentDictionary<string, long> _totalBytesReceived = new();
        private readonly ConcurrentDictionary<string, long> _totalBytesSent = new();
        private DateTime _serverStartTime;

        // Connection retry tracking
        private readonly ConcurrentDictionary<string, int> _reconnectAttempts = new();
        private const int MaxReconnectAttempts = 10;
        private const int HeartbeatTimeoutMs = 15000;
        private const int HeartbeatCheckIntervalMs = 5000;

        // ==================== EVENTS ====================
        public event Action<string, byte[]>? OnScreenFrameReceived;
        public event Action<string, string>? OnMessageReceived;
        public event Action<string>? OnDeviceConnected;
        public event Action<string>? OnDeviceDisconnected;
        public event Action<string, DeviceInfo>? OnDeviceInfoReceived;
        public event Action<string>? OnLog;

        // ==================== PROPERTIES ====================
        public bool IsRunning => _isRunning;
        public int Port => _port;
        public int ConnectedDeviceCount => _connectedDevices.Count;
        public IReadOnlyCollection<string> ConnectedDeviceIds => _connectedDevices.Keys.ToList();

        // ==================== CONSTRUCTOR ====================
        public MobileControllerServer(int port = 8888)
        {
            _port = port;
        }

        // ==================== SERVER LIFECYCLE ====================
        
        /// <summary>
        /// Serverni ishga tushirish va WebSocket ulanishlarini kutish
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning) return;

            _cts = new CancellationTokenSource();
            _serverStartTime = DateTime.Now;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://+:{_port}/");
                _listener.Start();
                _isRunning = true;

                Log($"Server started on port {_port}");
                Log($"Local IP: {GetLocalIPAddress()}");
                Log($"Connection URL: ws://{GetLocalIPAddress()}:{_port}/ws");

                // Background tasks
                _ = Task.Run(() => HeartbeatMonitorAsync(_cts.Token));
                _ = Task.Run(() => PerformanceMonitorAsync(_cts.Token));
                _ = Task.Run(() => CommandDispatcherAsync(_cts.Token));

                // Main accept loop
                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();

                        if (context.Request.IsWebSocketRequest)
                        {
                            _ = Task.Run(() => HandleWebSocketConnectionAsync(context));
                        }
                        else if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/status")
                        {
                            await HandleStatusRequestAsync(context);
                        }
                        else if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/devices")
                        {
                            await HandleDeviceListRequestAsync(context);
                        }
                        else if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/command")
                        {
                            await HandleCommandRequestAsync(context);
                        }
                        else
                        {
                            await HandleDefaultRequestAsync(context);
                        }
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (HttpListenerException) { break; }
                    catch (Exception ex)
                    {
                        Log($"Accept error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Server start error: {ex.Message}");
                _isRunning = false;
            }
        }

        /// <summary>
        /// Serverni to'xtatish
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _cts?.Cancel();

            // Close all connections
            foreach (var kvp in _connectedDevices)
            {
                try
                {
                    var ws = kvp.Value.WebSocket;
                    if (ws.State == WebSocketState.Open)
                    {
                        ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None)
                            .Wait(TimeSpan.FromSeconds(2));
                    }
                }
                catch { }
            }

            _connectedDevices.Clear();
            _lastHeartbeat.Clear();
            _commandQueues.Clear();
            _latestFrames.Clear();

            try { _listener?.Stop(); } catch { }
            Log("Server stopped.");
        }

        // ==================== WEBSOCKET CONNECTION HANDLER ====================

        /// <summary>
        /// Yangi WebSocket ulanishni qabul qilish va boshqarish
        /// </summary>
        private async Task HandleWebSocketConnectionAsync(HttpListenerContext context)
        {
            WebSocket? webSocket = null;
            string deviceId = "";

            try
            {
                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                webSocket = wsContext.WebSocket;
                deviceId = context.Request.QueryString["deviceId"] ?? $"device_{Guid.NewGuid().ToString()[..8]}";
                string deviceName = context.Request.QueryString["deviceName"] ?? "Unknown";
                string deviceModel = context.Request.QueryString["model"] ?? "Unknown";
                int screenWidth = int.TryParse(context.Request.QueryString["width"], out int w) ? w : 1080;
                int screenHeight = int.TryParse(context.Request.QueryString["height"], out int h) ? h : 2400;

                var connection = new DeviceConnection
                {
                    DeviceId = deviceId,
                    WebSocket = webSocket,
                    ConnectedAt = DateTime.Now,
                    RemoteEndpoint = context.Request.RemoteEndPoint?.ToString() ?? "unknown"
                };

                var deviceInfo = new DeviceInfo
                {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    Model = deviceModel,
                    ScreenWidth = screenWidth,
                    ScreenHeight = screenHeight,
                    ConnectedAt = DateTime.Now,
                    Status = "Connected"
                };

                _connectedDevices[deviceId] = connection;
                _lastHeartbeat[deviceId] = DateTime.Now;
                _deviceInfoMap[deviceId] = deviceInfo;
                _commandQueues[deviceId] = new Queue<MobileCommand>();
                _frameCounters[deviceId] = 0;
                _fpsCounters[deviceId] = 0;
                _totalBytesReceived[deviceId] = 0;
                _totalBytesSent[deviceId] = 0;
                _reconnectAttempts[deviceId] = 0;

                OnDeviceConnected?.Invoke(deviceId);
                OnDeviceInfoReceived?.Invoke(deviceId, deviceInfo);
                Log($"Device connected: {deviceId} ({deviceName} - {deviceModel}) from {connection.RemoteEndpoint}");

                // Send welcome
                await SendJsonAsync(deviceId, new { type = "welcome", serverId = "antigravity-v2026", timestamp = DateTime.Now });

                // Start receiving messages
                await ReceiveLoopAsync(deviceId, webSocket);
            }
            catch (Exception ex)
            {
                Log($"Connection error for {deviceId}: {ex.Message}");
            }
            finally
            {
                // Cleanup
                _connectedDevices.TryRemove(deviceId, out _);
                _lastHeartbeat.TryRemove(deviceId, out _);
                _commandQueues.TryRemove(deviceId, out _);
                _deviceInfoMap.TryRemove(deviceId, out _);

                if (webSocket != null && webSocket.State != WebSocketState.Closed)
                {
                    try { await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None); }
                    catch { }
                }

                OnDeviceDisconnected?.Invoke(deviceId);
                Log($"Device disconnected: {deviceId}");
            }
        }

        /// <summary>
        /// Ulanishdan ma'lumot qabul qilish sikli
        /// </summary>
        private async Task ReceiveLoopAsync(string deviceId, WebSocket webSocket)
        {
            byte[] buffer = new byte[2 * 1024 * 1024]; // 2MB buffer (HD frames uchun)
            var messageBuffer = new List<byte>();

            while (webSocket.State == WebSocketState.Open && _isRunning)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts!.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log($"Device {deviceId} requested close");
                        break;
                    }

                    messageBuffer.AddRange(buffer.Take(result.Count));

                    if (!result.EndOfMessage) continue;

                    byte[] fullMessage = messageBuffer.ToArray();
                    messageBuffer.Clear();

                    // Update heartbeat
                    _lastHeartbeat[deviceId] = DateTime.Now;

                    // Track bytes received
                    if (_totalBytesReceived.ContainsKey(deviceId))
                        _totalBytesReceived[deviceId] += fullMessage.Length;

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        // Screen frame (JPEG/PNG data)
                        await ProcessScreenFrame(deviceId, fullMessage);
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string textMsg = Encoding.UTF8.GetString(fullMessage);
                        await ProcessTextMessage(deviceId, textMsg);
                    }
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    Log($"Device {deviceId} connection dropped");
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Receive error from {deviceId}: {ex.Message}");
                    break;
                }
            }
        }

        /// <summary>
        /// Screen frame ni qayta ishlash
        /// </summary>
        private async Task ProcessScreenFrame(string deviceId, byte[] data)
        {
            _latestFrames[deviceId] = data;

            if (_frameCounters.ContainsKey(deviceId))
                _frameCounters[deviceId]++;

            OnScreenFrameReceived?.Invoke(deviceId, data);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Text xabarni qayta ishlash (heartbeat, status, accessibility info)
        /// </summary>
        private async Task ProcessTextMessage(string deviceId, string message)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                if (msg == null) return;

                string msgType = msg.ContainsKey("type") ? msg["type"].ToString()! : "unknown";

                switch (msgType)
                {
                    case "heartbeat":
                        _lastHeartbeat[deviceId] = DateTime.Now;
                        await SendJsonAsync(deviceId, new { type = "heartbeat_ack", timestamp = DateTime.Now });
                        break;

                    case "accessibility_result":
                        string resultText = msg.ContainsKey("result") ? msg["result"].ToString()! : "";
                        OnMessageReceived?.Invoke(deviceId, $"[Accessibility] {resultText}");
                        break;

                    case "screen_text":
                        string screenText = msg.ContainsKey("text") ? msg["text"].ToString()! : "";
                        OnMessageReceived?.Invoke(deviceId, $"[ScreenText] {screenText}");
                        break;

                    case "element_found":
                        string elementInfo = msg.ContainsKey("info") ? msg["info"].ToString()! : "";
                        OnMessageReceived?.Invoke(deviceId, $"[Element] {elementInfo}");
                        break;

                    case "command_result":
                        string cmdResult = msg.ContainsKey("success") ? msg["success"].ToString()! : "false";
                        string cmdAction = msg.ContainsKey("action") ? msg["action"].ToString()! : "";
                        OnMessageReceived?.Invoke(deviceId, $"[CmdResult] {cmdAction}: {cmdResult}");
                        break;

                    case "error":
                        string errorMsg = msg.ContainsKey("message") ? msg["message"].ToString()! : "Unknown error";
                        OnMessageReceived?.Invoke(deviceId, $"[ERROR] {errorMsg}");
                        break;

                    case "device_info_update":
                        if (_deviceInfoMap.ContainsKey(deviceId))
                        {
                            var info = _deviceInfoMap[deviceId];
                            if (msg.ContainsKey("battery")) info.BatteryLevel = Convert.ToInt32(msg["battery"]);
                            if (msg.ContainsKey("wifi")) info.WifiName = msg["wifi"].ToString()!;
                            OnDeviceInfoReceived?.Invoke(deviceId, info);
                        }
                        break;

                    case "qr_scanned":
                        string qrData = msg.ContainsKey("data") ? msg["data"].ToString()! : "";
                        OnMessageReceived?.Invoke(deviceId, $"[QR_SCAN] {qrData}");
                        break;

                    case "app_opened":
                        string pkgName = msg.ContainsKey("package") ? msg["package"].ToString()! : "";
                        OnMessageReceived?.Invoke(deviceId, $"[APP] Opened: {pkgName}");
                        break;

                    case "clipboard":
                        string clipText = msg.ContainsKey("text") ? msg["text"].ToString()! : "";
                        OnMessageReceived?.Invoke(deviceId, $"[Clipboard] {clipText}");
                        break;

                    default:
                        OnMessageReceived?.Invoke(deviceId, message);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Message parse error from {deviceId}: {ex.Message}");
                OnMessageReceived?.Invoke(deviceId, message);
            }

            await Task.CompletedTask;
        }

        // ==================== COMMAND SENDING ====================

        /// <summary>
        /// JSON buyruq yuborish
        /// </summary>
        public async Task SendJsonAsync(string deviceId, object data)
        {
            if (!_connectedDevices.TryGetValue(deviceId, out var conn)) return;
            if (conn.WebSocket.State != WebSocketState.Open) return;

            try
            {
                string json = JsonConvert.SerializeObject(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await conn.WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

                if (_totalBytesSent.ContainsKey(deviceId))
                    _totalBytesSent[deviceId] += bytes.Length;
            }
            catch (Exception ex)
            {
                Log($"Send error to {deviceId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Command yuborish (action-based)
        /// </summary>
        public async Task SendCommandAsync(string deviceId, MobileCommand command)
        {
            await SendJsonAsync(deviceId, command);
        }

        /// <summary>
        /// Ekranda berilgan koordinataga click yuborish
        /// </summary>
        public async Task SendClickAsync(string deviceId, int x, int y)
        {
            await SendJsonAsync(deviceId, new { action = "click", x, y });
            Log($"[{deviceId}] Click at ({x}, {y})");
        }

        /// <summary>
        /// Long press (uzoq bosish) yuborish
        /// </summary>
        public async Task SendLongPressAsync(string deviceId, int x, int y, int durationMs = 1000)
        {
            await SendJsonAsync(deviceId, new { action = "long_press", x, y, duration = durationMs });
            Log($"[{deviceId}] Long press at ({x}, {y}) for {durationMs}ms");
        }

        /// <summary>
        /// Swipe (suring) yuborish
        /// </summary>
        public async Task SendSwipeAsync(string deviceId, int startX, int startY, int endX, int endY, int durationMs = 300)
        {
            await SendJsonAsync(deviceId, new { action = "swipe", startX, startY, endX, endY, duration = durationMs });
            Log($"[{deviceId}] Swipe from ({startX},{startY}) to ({endX},{endY})");
        }

        /// <summary>
        /// Matn kiritish
        /// </summary>
        public async Task SendTextAsync(string deviceId, string text)
        {
            await SendJsonAsync(deviceId, new { action = "type", text });
            Log($"[{deviceId}] Type: {text}");
        }

        /// <summary>
        /// Matn clear qilib keyin yozish (input field tozalash)
        /// </summary>
        public async Task SendClearAndTypeAsync(string deviceId, string text)
        {
            await SendJsonAsync(deviceId, new { action = "clear_and_type", text });
        }

        /// <summary>
        /// Klaviatura tugmasi bosish (Back, Home, Enter va h.k.)
        /// </summary>
        public async Task SendKeyEventAsync(string deviceId, string keyName)
        {
            await SendJsonAsync(deviceId, new { action = "key_event", key = keyName });
            Log($"[{deviceId}] Key: {keyName}");
        }

        /// <summary>
        /// Back tugmasi
        /// </summary>
        public async Task SendBackAsync(string deviceId)
        {
            await SendKeyEventAsync(deviceId, "BACK");
        }

        /// <summary>
        /// Home tugmasi
        /// </summary>
        public async Task SendHomeAsync(string deviceId)
        {
            await SendKeyEventAsync(deviceId, "HOME");
        }

        /// <summary>
        /// Enter tugmasi
        /// </summary>
        public async Task SendEnterAsync(string deviceId)
        {
            await SendKeyEventAsync(deviceId, "ENTER");
        }

        /// <summary>
        /// Tab tugmasi (Keyingi input field ga o'tish)
        /// </summary>
        public async Task SendTabAsync(string deviceId)
        {
            await SendKeyEventAsync(deviceId, "TAB");
        }

        /// <summary>
        /// Ilovani ochish (package name bo'yicha)
        /// </summary>
        public async Task SendOpenAppAsync(string deviceId, string packageName)
        {
            await SendJsonAsync(deviceId, new { action = "open_app", package_name = packageName });
            Log($"[{deviceId}] Open app: {packageName}");
        }

        /// <summary>
        /// URL ni brauzerda ochish
        /// </summary>
        public async Task SendOpenUrlAsync(string deviceId, string url)
        {
            await SendJsonAsync(deviceId, new { action = "open_url", url });
            Log($"[{deviceId}] Open URL: {url}");
        }

        /// <summary>
        /// Google Account Settings ochish
        /// </summary>
        public async Task SendOpenGoogleAccountSettingsAsync(string deviceId)
        {
            await SendJsonAsync(deviceId, new { action = "open_google_account_settings" });
            Log($"[{deviceId}] Open Google Account Settings");
        }

        /// <summary>
        /// Ekrandagi matnni o'qish (Accessibility orqali)
        /// </summary>
        public async Task SendReadScreenTextAsync(string deviceId)
        {
            await SendJsonAsync(deviceId, new { action = "read_screen_text" });
        }

        /// <summary>
        /// Berilgan ID yoki matn bo'yicha elementni topish va bosish
        /// </summary>
        public async Task SendFindAndClickAsync(string deviceId, string textToFind)
        {
            await SendJsonAsync(deviceId, new { action = "find_and_click", text = textToFind });
            Log($"[{deviceId}] Find and click: '{textToFind}'");
        }

        /// <summary>
        /// Element mavjudligini tekshirish
        /// </summary>
        public async Task SendCheckElementAsync(string deviceId, string textOrId)
        {
            await SendJsonAsync(deviceId, new { action = "check_element", text = textOrId });
        }

        /// <summary>
        /// Screenshot olish buyrug'i
        /// </summary>
        public async Task SendTakeScreenshotAsync(string deviceId)
        {
            await SendJsonAsync(deviceId, new { action = "screenshot" });
        }

        /// <summary>
        /// QR kod skanerini ochish buyrug'i
        /// </summary>
        public async Task SendScanQRCodeAsync(string deviceId)
        {
            await SendJsonAsync(deviceId, new { action = "scan_qr" });
            Log($"[{deviceId}] QR Scanner activated");
        }

        /// <summary>
        /// Clipboard ga matn nusxalash
        /// </summary>
        public async Task SendSetClipboardAsync(string deviceId, string text)
        {
            await SendJsonAsync(deviceId, new { action = "set_clipboard", text });
        }

        /// <summary>
        /// Clipboard dan matn olish
        /// </summary>
        public async Task SendGetClipboardAsync(string deviceId)
        {
            await SendJsonAsync(deviceId, new { action = "get_clipboard" });
        }

        /// <summary>
        /// Scroll (pastga yoki yuqoriga)
        /// </summary>
        public async Task SendScrollAsync(string deviceId, string direction = "down", int amount = 500)
        {
            int centerX = 540, startY, endY;
            if (direction == "down") { startY = 1200; endY = 1200 - amount; }
            else { startY = 800; endY = 800 + amount; }
            await SendSwipeAsync(deviceId, centerX, startY, centerX, endY, 200);
        }

        /// <summary>
        /// Accessibility Service orqali matn bo'yicha elementni topish va unga matn yozish
        /// </summary>
        public async Task SendFindAndTypeAsync(string deviceId, string hintText, string valueToType)
        {
            await SendJsonAsync(deviceId, new { action = "find_and_type", hint = hintText, text = valueToType });
            Log($"[{deviceId}] Find '{hintText}' and type '{valueToType}'");
        }

        /// <summary>
        /// Telefonning Wi-Fi yoki VPN proxy sozlamalarini o'zgartirish
        /// </summary>
        public async Task SendSetProxyAsync(string deviceId, string proxyHost, int proxyPort)
        {
            await SendJsonAsync(deviceId, new { action = "set_proxy", host = proxyHost, port = proxyPort });
            Log($"[{deviceId}] Set proxy: {proxyHost}:{proxyPort}");
        }

        /// <summary>
        /// Proxy tozalash
        /// </summary>
        public async Task SendClearProxyAsync(string deviceId)
        {
            await SendJsonAsync(deviceId, new { action = "clear_proxy" });
        }

        /// <summary>
        /// Wait for specific text appearing on screen (polling)
        /// </summary>
        public async Task<bool> WaitForTextOnScreenAsync(string deviceId, string expectedText, int timeoutMs = 30000, int pollIntervalMs = 1000)
        {
            var tcs = new TaskCompletionSource<bool>();
            string? foundText = null;

            void handler(string dId, string msg)
            {
                if (dId == deviceId && msg.Contains("[ScreenText]"))
                {
                    foundText = msg;
                    if (msg.Contains(expectedText))
                        tcs.TrySetResult(true);
                }
            }

            OnMessageReceived += handler;

            try
            {
                var startTime = DateTime.Now;
                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    await SendReadScreenTextAsync(deviceId);
                    if (await Task.WhenAny(tcs.Task, Task.Delay(pollIntervalMs)) == tcs.Task)
                        return true;
                }
                return false;
            }
            finally
            {
                OnMessageReceived -= handler;
            }
        }

        /// <summary>
        /// Komandani navbatga qo'shish
        /// </summary>
        public void QueueCommand(string deviceId, MobileCommand command)
        {
            if (_commandQueues.TryGetValue(deviceId, out var queue))
            {
                lock (queue) queue.Enqueue(command);
            }
        }

        // ==================== HTTP API ENDPOINTS ====================

        private async Task HandleStatusRequestAsync(HttpListenerContext context)
        {
            var status = new
            {
                server = "Antigravity Mobile Controller v2026",
                running = _isRunning,
                uptime = (DateTime.Now - _serverStartTime).ToString(@"hh\:mm\:ss"),
                connectedDevices = _connectedDevices.Count,
                devices = _deviceInfoMap.Values.ToList()
            };

            string json = JsonConvert.SerializeObject(status, Formatting.Indented);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private async Task HandleDeviceListRequestAsync(HttpListenerContext context)
        {
            var devices = _deviceInfoMap.Values.Select(d => new
            {
                d.DeviceId,
                d.DeviceName,
                d.Model,
                d.ScreenWidth,
                d.ScreenHeight,
                d.Status,
                d.BatteryLevel,
                ConnectedAt = d.ConnectedAt.ToString("HH:mm:ss"),
                LastHeartbeat = _lastHeartbeat.ContainsKey(d.DeviceId) ? _lastHeartbeat[d.DeviceId].ToString("HH:mm:ss") : "N/A",
                FPS = _fpsCounters.ContainsKey(d.DeviceId) ? _fpsCounters[d.DeviceId] : 0
            });

            string json = JsonConvert.SerializeObject(devices, Formatting.Indented);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        private async Task HandleCommandRequestAsync(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream);
            string body = await reader.ReadToEndAsync();
            var cmd = JsonConvert.DeserializeObject<MobileCommand>(body);

            if (cmd != null && !string.IsNullOrEmpty(cmd.Action))
            {
                string targetDevice = context.Request.QueryString["deviceId"] ?? _connectedDevices.Keys.FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(targetDevice))
                {
                    await SendCommandAsync(targetDevice, cmd);
                    context.Response.StatusCode = 200;
                    byte[] ok = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                    await context.Response.OutputStream.WriteAsync(ok);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    byte[] err = Encoding.UTF8.GetBytes("{\"error\":\"no device\"}");
                    await context.Response.OutputStream.WriteAsync(err);
                }
            }

            context.Response.Close();
        }

        private async Task HandleDefaultRequestAsync(HttpListenerContext context)
        {
            string html = @"<!DOCTYPE html><html><head><title>Antigravity Mobile</title>
            <style>body{background:#0a0a0f;color:#00ff88;font-family:monospace;padding:40px;}
            h1{font-size:28px;}a{color:#00ccff;}</style></head>
            <body><h1>Antigravity Mobile Controller v2026</h1>
            <p>WebSocket Server is running.</p>
            <p>Endpoints: <a href='/status'>/status</a> | <a href='/devices'>/devices</a></p>
            <p>WebSocket: ws://HOST:" + _port + @"/ws</p></body></html>";

            byte[] bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }

        // ==================== BACKGROUND TASKS ====================

        /// <summary>
        /// Heartbeat monitoring — ulanish uzilganlarni aniqlash
        /// </summary>
        private async Task HeartbeatMonitorAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatCheckIntervalMs, ct);

                var now = DateTime.Now;
                var timedOut = _lastHeartbeat
                    .Where(kvp => (now - kvp.Value).TotalMilliseconds > HeartbeatTimeoutMs)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var deviceId in timedOut)
                {
                    if (_connectedDevices.TryRemove(deviceId, out var conn))
                    {
                        Log($"Device {deviceId} timed out (no heartbeat)");
                        try
                        {
                            if (conn.WebSocket.State == WebSocketState.Open)
                                await conn.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Timeout", CancellationToken.None);
                        }
                        catch { }
                        OnDeviceDisconnected?.Invoke(deviceId);
                    }
                    _lastHeartbeat.TryRemove(deviceId, out _);
                }
            }
        }

        /// <summary>
        /// FPS va performance monitoring
        /// </summary>
        private async Task PerformanceMonitorAsync(CancellationToken ct)
        {
            var lastFrameCounts = new Dictionary<string, long>();

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1000, ct);

                foreach (var kvp in _frameCounters)
                {
                    long prev = lastFrameCounts.ContainsKey(kvp.Key) ? lastFrameCounts[kvp.Key] : 0;
                    double fps = kvp.Value - prev;
                    _fpsCounters[kvp.Key] = fps;
                    lastFrameCounts[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Command queue dispatcher
        /// </summary>
        private async Task CommandDispatcherAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);

                foreach (var kvp in _commandQueues)
                {
                    if (kvp.Value.Count > 0)
                    {
                        MobileCommand cmd;
                        lock (kvp.Value) cmd = kvp.Value.Dequeue();
                        await SendCommandAsync(kvp.Key, cmd);
                    }
                }
            }
        }

        // ==================== UTILITY ====================

        /// <summary>
        /// Lokal IP manzilini topish
        /// </summary>
        public static string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return ip?.ToString() ?? "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }

        /// <summary>
        /// Device ning so'nggi ekran kadrini olish
        /// </summary>
        public byte[]? GetLatestFrame(string deviceId)
        {
            return _latestFrames.TryGetValue(deviceId, out var frame) ? frame : null;
        }

        /// <summary>
        /// Device haqida ma'lumot olish
        /// </summary>
        public DeviceInfo? GetDeviceInfo(string deviceId)
        {
            return _deviceInfoMap.TryGetValue(deviceId, out var info) ? info : null;
        }

        /// <summary>
        /// FPS olish
        /// </summary>
        public double GetDeviceFps(string deviceId)
        {
            return _fpsCounters.TryGetValue(deviceId, out var fps) ? fps : 0;
        }

        private void Log(string msg)
        {
            OnLog?.Invoke($"[Server] {msg}");
        }
    }

    // ==================== HELPER MODELS ====================

    public class DeviceConnection
    {
        public string DeviceId { get; set; } = "";
        public WebSocket WebSocket { get; set; } = null!;
        public DateTime ConnectedAt { get; set; }
        public string RemoteEndpoint { get; set; } = "";
    }

    public class DeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Model { get; set; } = "";
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public int BatteryLevel { get; set; }
        public string WifiName { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime ConnectedAt { get; set; }
    }
}
