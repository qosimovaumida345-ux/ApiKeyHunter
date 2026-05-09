using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AntigravityMobile.Services;
using AntigravityMobile.Models;
using Newtonsoft.Json;

namespace AntigravityMobile
{
    public partial class MainWindow : Window
    {
        private MobileControllerServer? _server;
        private GoogleAccountFactory? _factory;
        private ApiKeyScraperService? _scraper;
        private CancellationTokenSource? _factoryCts;
        private CancellationTokenSource? _scraperCts;
        private readonly string _dataDir;

        public MainWindow()
        {
            InitializeComponent();
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
            AppLog("Antigravity Mobile Controller initialized.");
            LoadSavedAccounts();
            LoadSavedApiKeys();
        }

        // ==================== TAB NAVIGATION ====================
        private void HideAllPanels()
        {
            PanelDevice.Visibility = Visibility.Collapsed;
            PanelGoogle.Visibility = Visibility.Collapsed;
            PanelApiKeys.Visibility = Visibility.Collapsed;
            PanelAccounts.Visibility = Visibility.Collapsed;
            PanelLogs.Visibility = Visibility.Collapsed;
        }

        private void BtnTabDevice_Click(object sender, RoutedEventArgs e) { HideAllPanels(); PanelDevice.Visibility = Visibility.Visible; }
        private void BtnTabGoogle_Click(object sender, RoutedEventArgs e) { HideAllPanels(); PanelGoogle.Visibility = Visibility.Visible; RefreshDeviceComboBox(); }
        private void BtnTabApiKeys_Click(object sender, RoutedEventArgs e) { HideAllPanels(); PanelApiKeys.Visibility = Visibility.Visible; }
        private void BtnTabAccounts_Click(object sender, RoutedEventArgs e) { HideAllPanels(); PanelAccounts.Visibility = Visibility.Visible; LoadSavedAccounts(); }
        private void BtnTabLogs_Click(object sender, RoutedEventArgs e) { HideAllPanels(); PanelLogs.Visibility = Visibility.Visible; }

        // ==================== SERVER CONTROL ====================
        private async void BtnStartServer_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtPort.Text, out int port)) { MessageBox.Show("Invalid port!"); return; }

            _server = new MobileControllerServer(port);
            _server.OnScreenFrameReceived += OnFrameReceived;
            _server.OnMessageReceived += OnDeviceMessage;
            _server.OnDeviceConnected += OnDeviceConnected;
            _server.OnDeviceDisconnected += OnDeviceDisconnected;

            BtnStartServer.IsEnabled = false;
            BtnStopServer.IsEnabled = true;

            TxtServerStatus.Text = $"Server: Running (:{port})";
            TxtServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 136));
            AppLog($"WebSocket Server started on port {port}");

            await Task.Run(() => _server.StartAsync());
        }

        private void BtnStopServer_Click(object sender, RoutedEventArgs e)
        {
            _server?.Stop();
            BtnStartServer.IsEnabled = true;
            BtnStopServer.IsEnabled = false;
            TxtServerStatus.Text = "Server: Stopped";
            TxtServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 68, 68));
            AppLog("Server stopped.");
        }

        private void OnDeviceConnected(string deviceId)
        {
            Dispatcher.Invoke(() =>
            {
                LstDevices.Items.Add(deviceId);
                TxtDeviceCount.Text = $"Devices: {LstDevices.Items.Count}";
                AppLog($"Device connected: {deviceId}");
            });
        }

        private void OnDeviceDisconnected(string deviceId)
        {
            Dispatcher.Invoke(() =>
            {
                LstDevices.Items.Remove(deviceId);
                TxtDeviceCount.Text = $"Devices: {LstDevices.Items.Count}";
                AppLog($"Device disconnected: {deviceId}");
            });
        }

        private void OnFrameReceived(string deviceId, byte[] jpegData)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(jpegData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    ImgPhoneScreen.Source = bitmap;
                }
                catch { }
            });
        }

        private void OnDeviceMessage(string deviceId, string message)
        {
            Dispatcher.Invoke(() => AppLog($"[{deviceId}] {message}"));
        }

        // ==================== GOOGLE FACTORY ====================
        private void RefreshDeviceComboBox()
        {
            CmbDeviceGoogle.Items.Clear();
            foreach (var item in LstDevices.Items)
                CmbDeviceGoogle.Items.Add(item);
            if (CmbDeviceGoogle.Items.Count > 0) CmbDeviceGoogle.SelectedIndex = 0;
        }

        private async void BtnStartGoogleFactory_Click(object sender, RoutedEventArgs e)
        {
            if (_server == null) { MessageBox.Show("Server ni avval start qiling!"); return; }
            if (CmbDeviceGoogle.SelectedItem == null) { MessageBox.Show("Device tanlang!"); return; }
            if (!int.TryParse(TxtAccountCount.Text, out int count) || count < 1) { MessageBox.Show("Nechta account?"); return; }

            string deviceId = CmbDeviceGoogle.SelectedItem.ToString()!;
            string password = TxtGooglePassword.Text.Trim();

            _factoryCts = new CancellationTokenSource();
            _factory = new GoogleAccountFactory(_server, deviceId, _dataDir);

            BtnStartGoogleFactory.IsEnabled = false;
            BtnStopGoogleFactory.IsEnabled = true;

            await Task.Run(async () =>
            {
                for (int i = 0; i < count; i++)
                {
                    if (_factoryCts.Token.IsCancellationRequested) break;

                    string firstName = NameGenerator.RandomFirstName();
                    string lastName = NameGenerator.RandomLastName();
                    
                    Dispatcher.Invoke(() => TxtGoogleProgress.Text = $"Creating {i + 1}/{count}: {firstName} {lastName}...");
                    Dispatcher.Invoke(() => AppLog($"[GoogleFactory] Creating account {i + 1}/{count}: {firstName} {lastName}"));

                    try
                    {
                        var account = await _factory.CreateAccountAsync(firstName, lastName, password, _factoryCts.Token);
                        if (account != null)
                        {
                            Dispatcher.Invoke(() => AppLog($"[GoogleFactory] SUCCESS: {account.Email}"));
                        }
                        else
                        {
                            Dispatcher.Invoke(() => AppLog($"[GoogleFactory] FAILED for {firstName} {lastName}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => AppLog($"[GoogleFactory] ERROR: {ex.Message}"));
                    }

                    await Task.Delay(3000); // Delay between accounts
                }

                Dispatcher.Invoke(() =>
                {
                    TxtGoogleProgress.Text = "Status: Completed";
                    BtnStartGoogleFactory.IsEnabled = true;
                    BtnStopGoogleFactory.IsEnabled = false;
                    LoadSavedAccounts();
                });
            });
        }

        private void BtnStopGoogleFactory_Click(object sender, RoutedEventArgs e)
        {
            _factoryCts?.Cancel();
            BtnStartGoogleFactory.IsEnabled = true;
            BtnStopGoogleFactory.IsEnabled = false;
            TxtGoogleProgress.Text = "Status: Stopped";
        }

        // ==================== API KEY SCRAPER ====================
        private async void BtnStartScrape_Click(object sender, RoutedEventArgs e)
        {
            if (_server == null) { MessageBox.Show("Server ni avval start qiling!"); return; }

            var accountsFile = Path.Combine(_dataDir, "google_accounts.json");
            if (!File.Exists(accountsFile)) { MessageBox.Show("Avval Google accountlar yarating!"); return; }

            var accounts = JsonConvert.DeserializeObject<List<GoogleAccountData>>(File.ReadAllText(accountsFile));
            if (accounts == null || accounts.Count == 0) { MessageBox.Show("Hech qanday account topilmadi!"); return; }

            var platforms = new List<string>();
            if (ChkGemini.IsChecked == true) platforms.Add("gemini");
            if (ChkGroq.IsChecked == true) platforms.Add("groq");
            if (ChkMistral.IsChecked == true) platforms.Add("mistral");
            if (ChkHuggingFace.IsChecked == true) platforms.Add("huggingface");

            string deviceId = LstDevices.Items.Count > 0 ? LstDevices.Items[0].ToString()! : "";
            if (string.IsNullOrEmpty(deviceId)) { MessageBox.Show("Device ulang!"); return; }

            _scraperCts = new CancellationTokenSource();
            _scraper = new ApiKeyScraperService(_server, deviceId, _dataDir);
            BtnStartScrape.IsEnabled = false;

            await Task.Run(async () =>
            {
                foreach (var account in accounts)
                {
                    if (_scraperCts.Token.IsCancellationRequested) break;

                    foreach (var platform in platforms)
                    {
                        if (_scraperCts.Token.IsCancellationRequested) break;
                        Dispatcher.Invoke(() =>
                        {
                            TxtApiProgress.Text = $"Scraping {platform} for {account.Email}...";
                            AppLog($"[ApiScraper] {platform} key for {account.Email}...");
                        });

                        try
                        {
                            string? key = await _scraper.ScrapeApiKeyAsync(account, platform, _scraperCts.Token);
                            if (!string.IsNullOrEmpty(key))
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    LstApiKeys.Items.Add($"[{platform.ToUpper()}] {account.Email}: {key}");
                                    AppLog($"[ApiScraper] Got {platform} key: {key.Substring(0, Math.Min(20, key.Length))}...");
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => AppLog($"[ApiScraper] Error: {ex.Message}"));
                        }

                        await Task.Delay(2000);
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    TxtApiProgress.Text = "Status: Completed";
                    BtnStartScrape.IsEnabled = true;
                });
            });
        }

        // ==================== ACCOUNTS VIEW ====================
        private void LoadSavedAccounts()
        {
            try
            {
                var file = Path.Combine(_dataDir, "google_accounts.json");
                if (File.Exists(file))
                {
                    var accounts = JsonConvert.DeserializeObject<List<GoogleAccountData>>(File.ReadAllText(file));
                    GridAccounts.ItemsSource = accounts;
                }
            }
            catch { }
        }

        private void LoadSavedApiKeys()
        {
            try
            {
                var file = Path.Combine(_dataDir, "api_keys.json");
                if (File.Exists(file))
                {
                    var keys = JsonConvert.DeserializeObject<List<ApiKeyData>>(File.ReadAllText(file));
                    if (keys != null)
                    {
                        foreach (var k in keys)
                            LstApiKeys.Items.Add($"[{k.Platform.ToUpper()}] {k.Email}: {k.ApiKey}");
                    }
                }
            }
            catch { }
        }

        private void BtnRefreshAccounts_Click(object sender, RoutedEventArgs e) => LoadSavedAccounts();

        private void BtnExportAccounts_Click(object sender, RoutedEventArgs e)
        {
            var file = Path.Combine(_dataDir, "google_accounts.json");
            if (File.Exists(file))
            {
                var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "antigravity_accounts_export.json");
                File.Copy(file, desktopPath, true);
                MessageBox.Show($"Exported to {desktopPath}");
            }
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e) => TxtLogs.Text = "";

        // ==================== LOGGING ====================
        private void AppLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            if (Dispatcher.CheckAccess())
            {
                TxtLogs.AppendText(line);
                TxtLogs.ScrollToEnd();
            }
            else
            {
                Dispatcher.Invoke(() => { TxtLogs.AppendText(line); TxtLogs.ScrollToEnd(); });
            }

            // File log
            try
            {
                File.AppendAllText(Path.Combine(_dataDir, "app_log.txt"), line);
            }
            catch { }
        }
    }
}
