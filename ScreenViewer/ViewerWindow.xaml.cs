using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Canvas;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace ScreenViewer
{
    public partial class ViewerWindow : Window
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private bool _isConnected;
        private int _deviceWidth = 1080;
        private int _deviceHeight = 2400;
        private long _frameCount;
        private DateTime _fpsTimer = DateTime.Now;
        private double _currentFps;

        // Mouse drag for swipe
        private bool _isDragging;
        private Point _dragStart;
        private DateTime _dragStartTime;

        public ViewerWindow()
        {
            InitializeComponent();
            Closing += (s, e) => Disconnect();
        }

        // ==================== CONNECTION ====================

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected) { Disconnect(); return; }

            string ip = TxtIp.Text.Trim();
            string port = TxtPort.Text.Trim();
            string deviceId = $"viewer_{Environment.MachineName}";

            string url = $"ws://{ip}:{port}/?deviceId={deviceId}&deviceName=ScreenViewer&model=PC&width=1080&height=2400";

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            try
            {
                BtnConnect.Content = "Connecting...";
                BtnConnect.IsEnabled = false;

                await _ws.ConnectAsync(new Uri(url), _cts.Token);
                _isConnected = true;

                BtnConnect.Content = "Disconnect";
                BtnConnect.IsEnabled = true;
                BtnConnect.Foreground = new SolidColorBrush(Color.FromRgb(255, 68, 68));
                OverlayNoConnection.Visibility = Visibility.Collapsed;
                TxtDeviceName.Text = $" | {ip}:{port}";

                _ = Task.Run(() => ReceiveLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}");
                BtnConnect.Content = "Connect";
                BtnConnect.IsEnabled = true;
            }
        }

        private void Disconnect()
        {
            _isConnected = false;
            _cts?.Cancel();
            try { _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(1000); }
            catch { }
            _ws?.Dispose();
            _ws = null;

            Dispatcher.Invoke(() =>
            {
                BtnConnect.Content = "Connect";
                BtnConnect.IsEnabled = true;
                BtnConnect.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 136));
                OverlayNoConnection.Visibility = Visibility.Visible;
                TxtDeviceName.Text = " | No Device";
            });
        }

        // ==================== RECEIVE LOOP ====================

        private async Task ReceiveLoop(CancellationToken ct)
        {
            byte[] buffer = new byte[2 * 1024 * 1024];
            var messageBuffer = new System.Collections.Generic.List<byte>();

            while (_isConnected && _ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                    if (!result.EndOfMessage) continue;

                    byte[] fullMessage = messageBuffer.ToArray();
                    messageBuffer.Clear();

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        // Screen frame
                        _frameCount++;
                        UpdateFps();

                        Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.StreamSource = new MemoryStream(fullMessage);
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.EndInit();
                                bitmap.Freeze();
                                ImgScreen.Source = bitmap;
                            }
                            catch { }
                        });
                    }
                    else if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string msg = Encoding.UTF8.GetString(fullMessage);
                        HandleTextMessage(msg);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { break; }
                catch { }
            }

            Disconnect();
        }

        private void HandleTextMessage(string message)
        {
            try
            {
                var json = JsonConvert.DeserializeObject<dynamic>(message);
                string? type = json?.type?.ToString();

                if (type == "welcome")
                {
                    Dispatcher.Invoke(() => TxtDeviceName.Text += " [Connected]");
                }
                else if (type == "device_info_update")
                {
                    int w = json?.width ?? 1080;
                    int h = json?.height ?? 2400;
                    _deviceWidth = w;
                    _deviceHeight = h;
                }
            }
            catch { }
        }

        private void UpdateFps()
        {
            var now = DateTime.Now;
            if ((now - _fpsTimer).TotalSeconds >= 1)
            {
                _currentFps = _frameCount / (now - _fpsTimer).TotalSeconds;
                _frameCount = 0;
                _fpsTimer = now;

                Dispatcher.Invoke(() =>
                {
                    TxtFps.Text = $"{_currentFps:F1} FPS";
                });
            }
        }

        // ==================== SEND COMMANDS ====================

        private async Task SendJsonAsync(object data)
        {
            if (_ws?.State != WebSocketState.Open) return;
            try
            {
                string json = JsonConvert.SerializeObject(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }

        // ==================== MOUSE -> TOUCH ====================

        private void ImgScreen_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(ImgScreen);
            _dragStartTime = DateTime.Now;

            // Show touch indicator
            var pos = e.GetPosition(TouchOverlay);
            Canvas.SetLeft(TouchDot, pos.X - 15);
            Canvas.SetTop(TouchDot, pos.Y - 15);
            TouchDot.Visibility = Visibility.Visible;

            ImgScreen.CaptureMouse();
        }

        private void ImgScreen_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var pos = e.GetPosition(TouchOverlay);
            Canvas.SetLeft(TouchDot, pos.X - 15);
            Canvas.SetTop(TouchDot, pos.Y - 15);
        }

        private async void ImgScreen_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            TouchDot.Visibility = Visibility.Collapsed;
            ImgScreen.ReleaseMouseCapture();

            var endPos = e.GetPosition(ImgScreen);
            double duration = (DateTime.Now - _dragStartTime).TotalMilliseconds;

            // Convert UI coordinates to device coordinates
            double scaleX = _deviceWidth / ImgScreen.ActualWidth;
            double scaleY = _deviceHeight / ImgScreen.ActualHeight;

            int startX = (int)(_dragStart.X * scaleX);
            int startY = (int)(_dragStart.Y * scaleY);
            int endX = (int)(endPos.X * scaleX);
            int endY = (int)(endPos.Y * scaleY);

            double distance = Math.Sqrt(Math.Pow(endX - startX, 2) + Math.Pow(endY - startY, 2));

            if (distance < 15)
            {
                // Click
                if (duration > 500)
                {
                    // Long press
                    await SendJsonAsync(new { action = "long_press", x = startX, y = startY, duration = (int)duration });
                }
                else
                {
                    // Normal click
                    await SendJsonAsync(new { action = "click", x = startX, y = startY });
                }
            }
            else
            {
                // Swipe
                await SendJsonAsync(new { action = "swipe", startX, startY, endX, endY, duration = Math.Max(200, (int)duration) });
            }
        }

        // ==================== NAVIGATION BUTTONS ====================

        private async void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            await SendJsonAsync(new { action = "key_event", key = "BACK" });
        }

        private async void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            await SendJsonAsync(new { action = "key_event", key = "HOME" });
        }

        private async void BtnRecents_Click(object sender, RoutedEventArgs e)
        {
            await SendJsonAsync(new { action = "key_event", key = "RECENTS" });
        }

        private async void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            // Save current frame to Desktop
            if (ImgScreen.Source is BitmapImage bmp)
            {
                try
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"antigravity_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = new FileStream(path, FileMode.Create);
                    encoder.Save(fs);

                    MessageBox.Show($"Screenshot saved: {path}", "Screenshot", MessageBoxButton.OK);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                }
            }
        }

        // ==================== TEXT INPUT ====================

        private async void BtnType_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtInput.Text;
            if (string.IsNullOrEmpty(text)) return;

            await SendJsonAsync(new { action = "type", text });
            TxtInput.Clear();
        }

        private async void BtnEnter_Click(object sender, RoutedEventArgs e)
        {
            await SendJsonAsync(new { action = "key_event", key = "ENTER" });
        }

        private async void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string text = TxtInput.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    await SendJsonAsync(new { action = "type", text });
                    TxtInput.Clear();
                }
                await SendJsonAsync(new { action = "key_event", key = "ENTER" });
            }
        }
    }
}
