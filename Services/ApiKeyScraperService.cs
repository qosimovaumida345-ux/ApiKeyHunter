using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AntigravityMobile.Models;

namespace AntigravityMobile.Services
{
    /// <summary>
    /// Antigravity API Key Scraper v2026
    /// Google Account bilan har xil AI platformalarga avtomatik sign up qilib,
    /// API keylarni oladi va saqlaydi:
    /// - Google Gemini (AI Studio)
    /// - Groq Cloud
    /// - Mistral AI
    /// - HuggingFace
    /// - OpenRouter
    /// - Together AI
    /// - Fireworks AI
    /// - Cohere
    /// </summary>
    public class ApiKeyScraperService
    {
        // ==================== FIELDS ====================
        private readonly MobileControllerServer _server;
        private readonly string _deviceId;
        private readonly string _dataDir;
        private readonly string _apiKeysFile;
        private readonly HttpClient _httpClient;
        private readonly Random _rng = new();

        // Tracking
        private int _totalKeysObtained;
        private int _totalKeysFailed;

        // Events
        public event Action<string>? OnLog;
        public event Action<ApiKeyData>? OnApiKeyObtained;

        // Platform URLs
        private static readonly Dictionary<string, PlatformConfig> Platforms = new()
        {
            ["gemini"] = new PlatformConfig
            {
                Name = "Google Gemini",
                SignUpUrl = "https://aistudio.google.com/app/apikey",
                ConsoleUrl = "https://aistudio.google.com/app/apikey",
                ApiTestUrl = "https://generativelanguage.googleapis.com/v1beta/models?key=",
                NeedsGoogleAuth = true,
                KeyPrefix = "AIza"
            },
            ["groq"] = new PlatformConfig
            {
                Name = "Groq Cloud",
                SignUpUrl = "https://console.groq.com/login",
                ConsoleUrl = "https://console.groq.com/keys",
                ApiTestUrl = "https://api.groq.com/openai/v1/models",
                NeedsGoogleAuth = true,
                KeyPrefix = "gsk_"
            },
            ["mistral"] = new PlatformConfig
            {
                Name = "Mistral AI",
                SignUpUrl = "https://console.mistral.ai/",
                ConsoleUrl = "https://console.mistral.ai/api-keys/",
                ApiTestUrl = "https://api.mistral.ai/v1/models",
                NeedsGoogleAuth = true,
                KeyPrefix = ""
            },
            ["huggingface"] = new PlatformConfig
            {
                Name = "HuggingFace",
                SignUpUrl = "https://huggingface.co/join",
                ConsoleUrl = "https://huggingface.co/settings/tokens",
                ApiTestUrl = "https://huggingface.co/api/whoami-v2",
                NeedsGoogleAuth = false,
                KeyPrefix = "hf_"
            },
            ["openrouter"] = new PlatformConfig
            {
                Name = "OpenRouter",
                SignUpUrl = "https://openrouter.ai/auth",
                ConsoleUrl = "https://openrouter.ai/settings/keys",
                ApiTestUrl = "https://openrouter.ai/api/v1/models",
                NeedsGoogleAuth = true,
                KeyPrefix = "sk-or-"
            },
            ["together"] = new PlatformConfig
            {
                Name = "Together AI",
                SignUpUrl = "https://api.together.xyz/signin",
                ConsoleUrl = "https://api.together.xyz/settings/api-keys",
                ApiTestUrl = "https://api.together.xyz/v1/models",
                NeedsGoogleAuth = true,
                KeyPrefix = ""
            },
            ["fireworks"] = new PlatformConfig
            {
                Name = "Fireworks AI",
                SignUpUrl = "https://fireworks.ai/login",
                ConsoleUrl = "https://fireworks.ai/account/api-keys",
                ApiTestUrl = "https://api.fireworks.ai/inference/v1/models",
                NeedsGoogleAuth = true,
                KeyPrefix = "fw_"
            },
            ["cohere"] = new PlatformConfig
            {
                Name = "Cohere",
                SignUpUrl = "https://dashboard.cohere.com/",
                ConsoleUrl = "https://dashboard.cohere.com/api-keys",
                ApiTestUrl = "https://api.cohere.ai/v1/models",
                NeedsGoogleAuth = true,
                KeyPrefix = ""
            }
        };

        // ==================== CONSTRUCTOR ====================
        public ApiKeyScraperService(MobileControllerServer server, string deviceId, string dataDir)
        {
            _server = server;
            _deviceId = deviceId;
            _dataDir = dataDir;
            _apiKeysFile = Path.Combine(_dataDir, "api_keys.json");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36");
        }

        // ==================== MAIN SCRAPING FLOW ====================

        /// <summary>
        /// Berilgan platform uchun API key olish
        /// </summary>
        public async Task<string?> ScrapeApiKeyAsync(GoogleAccountData account, string platform, CancellationToken ct)
        {
            if (!Platforms.ContainsKey(platform))
            {
                Log($"Unknown platform: {platform}");
                return null;
            }

            var config = Platforms[platform];
            Log($"Scraping {config.Name} API key using {account.Email}...");

            try
            {
                string? apiKey = null;

                switch (platform)
                {
                    case "gemini":
                        apiKey = await ScrapeGeminiKeyAsync(account, ct);
                        break;
                    case "groq":
                        apiKey = await ScrapeGroqKeyAsync(account, ct);
                        break;
                    case "mistral":
                        apiKey = await ScrapeMistralKeyAsync(account, ct);
                        break;
                    case "huggingface":
                        apiKey = await ScrapeHuggingFaceKeyAsync(account, ct);
                        break;
                    case "openrouter":
                        apiKey = await ScrapeOpenRouterKeyAsync(account, ct);
                        break;
                    case "together":
                        apiKey = await ScrapeTogetherKeyAsync(account, ct);
                        break;
                    case "fireworks":
                        apiKey = await ScrapeFireworksKeyAsync(account, ct);
                        break;
                    case "cohere":
                        apiKey = await ScrapeCohereKeyAsync(account, ct);
                        break;
                }

                if (!string.IsNullOrEmpty(apiKey))
                {
                    // Verify key
                    bool valid = await VerifyApiKeyAsync(platform, apiKey);

                    var keyData = new ApiKeyData
                    {
                        Email = account.Email,
                        Platform = platform,
                        ApiKey = apiKey,
                        ObtainedAt = DateTime.Now
                    };

                    SaveApiKey(keyData);
                    _totalKeysObtained++;
                    OnApiKeyObtained?.Invoke(keyData);
                    Log($"API key obtained for {config.Name}: {apiKey[..Math.Min(20, apiKey.Length)]}... (Valid: {valid})");
                    return apiKey;
                }
                else
                {
                    _totalKeysFailed++;
                    Log($"Failed to obtain {config.Name} API key for {account.Email}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _totalKeysFailed++;
                Log($"Error scraping {config.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Barcha platformalardan API key olish (bir account uchun)
        /// </summary>
        public async Task<Dictionary<string, string>> ScrapeAllKeysAsync(GoogleAccountData account, List<string> platforms, CancellationToken ct)
        {
            var result = new Dictionary<string, string>();

            foreach (var platform in platforms)
            {
                if (ct.IsCancellationRequested) break;

                string? key = await ScrapeApiKeyAsync(account, platform, ct);
                if (!string.IsNullOrEmpty(key))
                {
                    result[platform] = key;
                }

                // Platformalar orasida kutish
                await SafeDelay(3000, ct);
            }

            return result;
        }

        /// <summary>
        /// Bir nechta account uchun barcha API keylarni olish (mass scraping)
        /// </summary>
        public async Task MassScrapeAsync(List<GoogleAccountData> accounts, List<string> platforms, CancellationToken ct)
        {
            Log($"Starting mass scrape: {accounts.Count} accounts, {platforms.Count} platforms");

            int total = accounts.Count * platforms.Count;
            int done = 0;

            foreach (var account in accounts)
            {
                if (ct.IsCancellationRequested) break;

                // Bu accountga login qilish
                var factory = new GoogleAccountFactory(_server, _deviceId, _dataDir);
                await factory.LoginToGoogleAccountAsync(account.Email, account.Password, ct);
                await SafeDelay(3000, ct);

                foreach (var platform in platforms)
                {
                    if (ct.IsCancellationRequested) break;

                    await ScrapeApiKeyAsync(account, platform, ct);
                    done++;
                    Log($"Progress: {done}/{total}");
                    await SafeDelay(2000, ct);
                }

                // Logout
                await factory.LogoutFromGoogleAccountAsync(account.Email, ct);
                await SafeDelay(2000, ct);
            }

            Log($"Mass scrape complete: {_totalKeysObtained} keys obtained, {_totalKeysFailed} failed");
        }

        // ==================== PLATFORM-SPECIFIC SCRAPERS ====================

        /// <summary>
        /// Google Gemini (AI Studio) API key olish
        /// </summary>
        private async Task<string?> ScrapeGeminiKeyAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log("[Gemini] Opening AI Studio...");
            await _server.SendOpenUrlAsync(_deviceId, "https://aistudio.google.com/app/apikey");
            await SafeDelay(8000, ct);

            // Google login agar kerak bo'lsa
            await HandleGoogleAuthOnWebAsync(account, ct);
            await SafeDelay(3000, ct);

            // "Get API key" yoki "Create API key" bosish
            Log("[Gemini] Looking for Create API Key button...");
            await _server.SendFindAndClickAsync(_deviceId, "Create API key");
            await SafeDelay(3000, ct);

            // Agar yangi project yaratish kerak bo'lsa
            await _server.SendFindAndClickAsync(_deviceId, "Create API key in new project");
            await SafeDelay(5000, ct);

            // API key ni ekrandan o'qish
            await _server.SendReadScreenTextAsync(_deviceId);
            await SafeDelay(2000, ct);

            // Copy tugmasini bosish
            await _server.SendFindAndClickAsync(_deviceId, "Copy");
            await SafeDelay(1000, ct);

            // Clipboard dan olish
            await _server.SendGetClipboardAsync(_deviceId);
            await SafeDelay(1000, ct);

            // API keyni topish uchun ekrandagi matnni tekshirish
            string? key = await ExtractKeyFromScreenAsync("AIza", ct);
            return key;
        }

        /// <summary>
        /// Groq Cloud API key olish
        /// </summary>
        private async Task<string?> ScrapeGroqKeyAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log("[Groq] Opening Groq Console...");
            await _server.SendOpenUrlAsync(_deviceId, "https://console.groq.com/login");
            await SafeDelay(6000, ct);

            // "Continue with Google" bosish
            await _server.SendFindAndClickAsync(_deviceId, "Continue with Google");
            await SafeDelay(3000, ct);

            await HandleGoogleAuthOnWebAsync(account, ct);
            await SafeDelay(5000, ct);

            // API Keys sahifasiga o'tish
            await _server.SendOpenUrlAsync(_deviceId, "https://console.groq.com/keys");
            await SafeDelay(5000, ct);

            // "Create API Key" bosish
            await _server.SendFindAndClickAsync(_deviceId, "Create API Key");
            await SafeDelay(2000, ct);

            // Key name kiritish
            await _server.SendFindAndTypeAsync(_deviceId, "Name", $"antigravity-{_rng.Next(1000, 9999)}");
            await SafeDelay(500, ct);

            // Submit
            await _server.SendFindAndClickAsync(_deviceId, "Submit");
            await SafeDelay(3000, ct);

            // Key ni nusxalash
            await _server.SendFindAndClickAsync(_deviceId, "Copy");
            await SafeDelay(1000, ct);
            await _server.SendGetClipboardAsync(_deviceId);
            await SafeDelay(1000, ct);

            string? key = await ExtractKeyFromScreenAsync("gsk_", ct);
            return key;
        }

        /// <summary>
        /// Mistral AI API key olish
        /// </summary>
        private async Task<string?> ScrapeMistralKeyAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log("[Mistral] Opening Mistral Console...");
            await _server.SendOpenUrlAsync(_deviceId, "https://console.mistral.ai/");
            await SafeDelay(6000, ct);

            // Google sign in
            await _server.SendFindAndClickAsync(_deviceId, "Sign in with Google");
            await SafeDelay(3000, ct);

            await HandleGoogleAuthOnWebAsync(account, ct);
            await SafeDelay(5000, ct);

            // API Keys page
            await _server.SendOpenUrlAsync(_deviceId, "https://console.mistral.ai/api-keys/");
            await SafeDelay(5000, ct);

            // Create new key
            await _server.SendFindAndClickAsync(_deviceId, "Create new key");
            await SafeDelay(2000, ct);

            // Name
            await _server.SendFindAndTypeAsync(_deviceId, "Name", $"ag-{_rng.Next(1000, 9999)}");
            await SafeDelay(500, ct);

            // Create
            await _server.SendFindAndClickAsync(_deviceId, "Create");
            await SafeDelay(3000, ct);

            // Copy
            await _server.SendFindAndClickAsync(_deviceId, "Copy");
            await SafeDelay(1000, ct);
            await _server.SendGetClipboardAsync(_deviceId);
            await SafeDelay(1000, ct);

            string? key = await ExtractKeyFromScreenAsync("", ct);
            return key;
        }

        /// <summary>
        /// HuggingFace API token olish
        /// </summary>
        private async Task<string?> ScrapeHuggingFaceKeyAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log("[HuggingFace] Opening HuggingFace...");
            await _server.SendOpenUrlAsync(_deviceId, "https://huggingface.co/join");
            await SafeDelay(6000, ct);

            // Sign up with email
            string hfUsername = account.Email.Replace("@gmail.com", "").Replace(".", "") + _rng.Next(100, 999);
            await _server.SendFindAndTypeAsync(_deviceId, "Email", account.Email);
            await SafeDelay(500, ct);
            await _server.SendFindAndTypeAsync(_deviceId, "Password", account.Password);
            await SafeDelay(500, ct);
            await _server.SendFindAndTypeAsync(_deviceId, "Username", hfUsername);
            await SafeDelay(500, ct);

            // Sign up
            await _server.SendFindAndClickAsync(_deviceId, "Sign Up");
            await SafeDelay(5000, ct);

            // Token page
            await _server.SendOpenUrlAsync(_deviceId, "https://huggingface.co/settings/tokens/new");
            await SafeDelay(5000, ct);

            // Create token
            await _server.SendFindAndTypeAsync(_deviceId, "Name", $"antigravity-{_rng.Next(1000, 9999)}");
            await SafeDelay(500, ct);

            // Write access
            await _server.SendFindAndClickAsync(_deviceId, "Write");
            await SafeDelay(500, ct);

            // Create
            await _server.SendFindAndClickAsync(_deviceId, "Create token");
            await SafeDelay(3000, ct);

            // Copy
            await _server.SendFindAndClickAsync(_deviceId, "Copy");
            await SafeDelay(1000, ct);
            await _server.SendGetClipboardAsync(_deviceId);
            await SafeDelay(1000, ct);

            string? key = await ExtractKeyFromScreenAsync("hf_", ct);
            return key;
        }

        /// <summary>
        /// OpenRouter API key olish
        /// </summary>
        private async Task<string?> ScrapeOpenRouterKeyAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log("[OpenRouter] Opening OpenRouter...");
            await _server.SendOpenUrlAsync(_deviceId, "https://openrouter.ai/auth");
            await SafeDelay(6000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Continue with Google");
            await SafeDelay(3000, ct);

            await HandleGoogleAuthOnWebAsync(account, ct);
            await SafeDelay(5000, ct);

            await _server.SendOpenUrlAsync(_deviceId, "https://openrouter.ai/settings/keys");
            await SafeDelay(5000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Create Key");
            await SafeDelay(2000, ct);

            await _server.SendFindAndTypeAsync(_deviceId, "Name", $"ag-{_rng.Next(1000, 9999)}");
            await SafeDelay(500, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Create");
            await SafeDelay(3000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Copy");
            await SafeDelay(1000, ct);
            await _server.SendGetClipboardAsync(_deviceId);
            await SafeDelay(1000, ct);

            string? key = await ExtractKeyFromScreenAsync("sk-or-", ct);
            return key;
        }

        /// <summary>
        /// Together AI API key olish
        /// </summary>
        private async Task<string?> ScrapeTogetherKeyAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log("[Together] Opening Together AI...");
            await _server.SendOpenUrlAsync(_deviceId, "https://api.together.xyz/signin");
            await SafeDelay(6000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Continue with Google");
            await SafeDelay(3000, ct);

            await HandleGoogleAuthOnWebAsync(account, ct);
            await SafeDelay(5000, ct);

            await _server.SendOpenUrlAsync(_deviceId, "https://api.together.xyz/settings/api-keys");
            await SafeDelay(5000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Create");
            await SafeDelay(3000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Copy");
            await SafeDelay(1000, ct);
            await _server.SendGetClipboardAsync(_deviceId);
            await SafeDelay(1000, ct);

            string? key = await ExtractKeyFromScreenAsync("", ct);
            return key;
        }

        /// <summary>
        /// Fireworks AI API key olish
        /// </summary>
        private async Task<string?> ScrapeFireworksKeyAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log("[Fireworks] Opening Fireworks AI...");
            await _server.SendOpenUrlAsync(_deviceId, "https://fireworks.ai/login");
            await SafeDelay(6000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Continue with Google");
            await SafeDelay(3000, ct);

            await HandleGoogleAuthOnWebAsync(account, ct);
            await SafeDelay(5000, ct);

            await _server.SendOpenUrlAsync(_deviceId, "https://fireworks.ai/account/api-keys");
            await SafeDelay(5000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Create API Key");
            await SafeDelay(3000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Copy");
            await SafeDelay(1000, ct);
            await _server.SendGetClipboardAsync(_deviceId);
            await SafeDelay(1000, ct);

            string? key = await ExtractKeyFromScreenAsync("fw_", ct);
            return key;
        }

        /// <summary>
        /// Cohere API key olish
        /// </summary>
        private async Task<string?> ScrapeCohereKeyAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log("[Cohere] Opening Cohere Dashboard...");
            await _server.SendOpenUrlAsync(_deviceId, "https://dashboard.cohere.com/");
            await SafeDelay(6000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Sign in with Google");
            await SafeDelay(3000, ct);

            await HandleGoogleAuthOnWebAsync(account, ct);
            await SafeDelay(5000, ct);

            await _server.SendOpenUrlAsync(_deviceId, "https://dashboard.cohere.com/api-keys");
            await SafeDelay(5000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Create Trial Key");
            await SafeDelay(3000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Copy");
            await SafeDelay(1000, ct);
            await _server.SendGetClipboardAsync(_deviceId);
            await SafeDelay(1000, ct);

            string? key = await ExtractKeyFromScreenAsync("", ct);
            return key;
        }

        // ==================== SHARED AUTH FLOW ====================

        /// <summary>
        /// Web sahifadagi Google OAuth login ni bajarish
        /// </summary>
        private async Task HandleGoogleAuthOnWebAsync(GoogleAccountData account, CancellationToken ct)
        {
            Log($"Handling Google OAuth for {account.Email}...");

            // Google sign in sahifada email tanlash yoki kiritish
            // Agar allaqachon telefonda login bo'lsa, account tanlash kerak
            bool foundAccount = await TryFindAndClick(new[] { account.Email, account.Email.Replace("@gmail.com", "") }, ct);

            if (!foundAccount)
            {
                // Email kiritish kerak
                bool foundField = await TryFindAndType(new[] { "Email", "email", "Phone" }, account.Email, ct);
                if (!foundField)
                {
                    await _server.SendTextAsync(_deviceId, account.Email);
                }
                await SafeDelay(500, ct);

                // Next
                await _server.SendFindAndClickAsync(_deviceId, "Next");
                await SafeDelay(3000, ct);

                // Parol
                bool foundPass = await TryFindAndType(new[] { "password", "Password", "Enter your password" }, account.Password, ct);
                if (!foundPass)
                {
                    await _server.SendTextAsync(_deviceId, account.Password);
                }
                await SafeDelay(500, ct);

                // Next
                await _server.SendFindAndClickAsync(_deviceId, "Next");
                await SafeDelay(4000, ct);
            }

            // "Allow" yoki "Continue" bosish (OAuth permissions)
            await TryFindAndClick(new[] { "Allow", "Continue", "Accept", "Confirm" }, ct);
            await SafeDelay(3000, ct);
        }

        // ==================== API KEY VERIFICATION ====================

        /// <summary>
        /// Olingan API keyni tekshirish
        /// </summary>
        public async Task<bool> VerifyApiKeyAsync(string platform, string apiKey)
        {
            if (!Platforms.ContainsKey(platform)) return false;
            var config = Platforms[platform];

            try
            {
                HttpRequestMessage request;

                if (platform == "gemini")
                {
                    request = new HttpRequestMessage(HttpMethod.Get, config.ApiTestUrl + apiKey);
                }
                else
                {
                    request = new HttpRequestMessage(HttpMethod.Get, config.ApiTestUrl);
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                }

                var response = await _httpClient.SendAsync(request);
                bool valid = response.IsSuccessStatusCode;
                Log($"API key verification for {config.Name}: {(valid ? "VALID" : "INVALID")} (HTTP {(int)response.StatusCode})");
                return valid;
            }
            catch (Exception ex)
            {
                Log($"API key verification error for {config.Name}: {ex.Message}");
                return false;
            }
        }

        // ==================== KEY EXTRACTION ====================

        /// <summary>
        /// Ekrandagi matndan API keyni ajratib olish
        /// </summary>
        private async Task<string?> ExtractKeyFromScreenAsync(string prefix, CancellationToken ct)
        {
            // Wait for message from device containing the key
            string? foundKey = null;
            var tcs = new TaskCompletionSource<string?>();

            void handler(string dId, string msg)
            {
                if (dId != _deviceId) return;

                // Clipboard dan kelgan key
                if (msg.Contains("[Clipboard]"))
                {
                    string clipText = msg.Replace("[Clipboard]", "").Trim();
                    if (!string.IsNullOrEmpty(clipText) && (string.IsNullOrEmpty(prefix) || clipText.StartsWith(prefix)))
                    {
                        foundKey = clipText;
                        tcs.TrySetResult(clipText);
                    }
                }

                // Screen text dan key izlash
                if (msg.Contains("[ScreenText]"))
                {
                    string screenText = msg.Replace("[ScreenText]", "").Trim();
                    string? extracted = ExtractKeyFromText(screenText, prefix);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        foundKey = extracted;
                        tcs.TrySetResult(extracted);
                    }
                }
            }

            _server.OnMessageReceived += handler;

            try
            {
                // Read screen text
                await _server.SendReadScreenTextAsync(_deviceId);
                await SafeDelay(2000, ct);

                // Get clipboard
                await _server.SendGetClipboardAsync(_deviceId);

                // Wait up to 10 seconds for key
                await Task.WhenAny(tcs.Task, Task.Delay(10000, ct));
                return foundKey;
            }
            finally
            {
                _server.OnMessageReceived -= handler;
            }
        }

        /// <summary>
        /// Matn ichidan API key ni topib ajratish
        /// </summary>
        private string? ExtractKeyFromText(string text, string prefix)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // Pattern matching for known key formats
            var words = text.Split(new[] { ' ', '\n', '\t', '\r', '"', '\'', '`' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                string trimmed = word.Trim();

                // Gemini keys: AIza...
                if (trimmed.StartsWith("AIza") && trimmed.Length > 30)
                    return trimmed;

                // Groq keys: gsk_...
                if (trimmed.StartsWith("gsk_") && trimmed.Length > 20)
                    return trimmed;

                // HuggingFace: hf_...
                if (trimmed.StartsWith("hf_") && trimmed.Length > 10)
                    return trimmed;

                // OpenRouter: sk-or-...
                if (trimmed.StartsWith("sk-or-") && trimmed.Length > 20)
                    return trimmed;

                // Fireworks: fw_...
                if (trimmed.StartsWith("fw_") && trimmed.Length > 20)
                    return trimmed;

                // Generic long alphanumeric string
                if (!string.IsNullOrEmpty(prefix) && trimmed.StartsWith(prefix) && trimmed.Length > 15)
                    return trimmed;

                // Generic key pattern (32+ chars, alnum + dashes)
                if (string.IsNullOrEmpty(prefix) && trimmed.Length >= 32 && trimmed.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                    return trimmed;
            }

            return null;
        }

        // ==================== PERSISTENCE ====================

        private void SaveApiKey(ApiKeyData keyData)
        {
            try
            {
                var keys = LoadAllApiKeys();

                // Remove duplicate (same email + platform)
                keys.RemoveAll(k => k.Email == keyData.Email && k.Platform == keyData.Platform);
                keys.Add(keyData);

                string json = JsonConvert.SerializeObject(keys, Formatting.Indented);
                File.WriteAllText(_apiKeysFile, json);
            }
            catch (Exception ex)
            {
                Log($"Error saving API key: {ex.Message}");
            }
        }

        public List<ApiKeyData> LoadAllApiKeys()
        {
            try
            {
                if (File.Exists(_apiKeysFile))
                {
                    string json = File.ReadAllText(_apiKeysFile);
                    return JsonConvert.DeserializeObject<List<ApiKeyData>>(json) ?? new List<ApiKeyData>();
                }
            }
            catch { }
            return new List<ApiKeyData>();
        }

        /// <summary>
        /// Barcha keylarni platforma bo'yicha guruhlash va data papkasiga saqlash
        /// </summary>
        public void ExportKeysByPlatform()
        {
            var keys = LoadAllApiKeys();

            foreach (var group in keys.GroupBy(k => k.Platform))
            {
                string fileName = Path.Combine(_dataDir, $"{group.Key}_api_keys.txt");
                var lines = group.Select(k => $"{k.Email}:{k.ApiKey}");
                File.WriteAllLines(fileName, lines);
                Log($"Exported {group.Count()} {group.Key} keys to {fileName}");
            }
        }

        // ==================== HELPERS ====================

        private async Task<bool> TryFindAndClick(string[] texts, CancellationToken ct)
        {
            foreach (string text in texts)
            {
                await _server.SendFindAndClickAsync(_deviceId, text);
                await SafeDelay(300, ct);
            }
            return true;
        }

        private async Task<bool> TryFindAndType(string[] hints, string value, CancellationToken ct)
        {
            foreach (string hint in hints)
            {
                await _server.SendFindAndTypeAsync(_deviceId, hint, value);
                await SafeDelay(300, ct);
            }
            return true;
        }

        private async Task SafeDelay(int ms, CancellationToken ct)
        {
            try { await Task.Delay(ms, ct); }
            catch (OperationCanceledException) { }
        }

        private void Log(string msg) => OnLog?.Invoke($"[ApiScraper] {msg}");
    }

    // ==================== PLATFORM CONFIG ====================

    public class PlatformConfig
    {
        public string Name { get; set; } = "";
        public string SignUpUrl { get; set; } = "";
        public string ConsoleUrl { get; set; } = "";
        public string ApiTestUrl { get; set; } = "";
        public bool NeedsGoogleAuth { get; set; }
        public string KeyPrefix { get; set; } = "";
    }
}
