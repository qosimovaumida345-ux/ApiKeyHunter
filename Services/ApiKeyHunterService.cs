using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AntigravityMobile.Models;

namespace AntigravityMobile.Services
{
    /// <summary>
    /// Antigravity API Key Hunter v2026
    /// Kompyuterdagi barcha papka va fayllardan API keylarni qidirib topadi.
    /// Shuningdek barcha BEPUL API provayderlarni ro'yxatini beradi.
    /// 
    /// Qidiruv joylari:
    /// - Antigravity AppData papkasi
    /// - User Desktop, Documents papkalari
    /// - .env fayllar
    /// - config.json, settings.json fayllar
    /// - Python/Node.js loyihalar ichidagi .env
    /// - Browser profilelardan (Chrome, Firefox, Edge)
    /// - VS Code settings va extensions
    /// - Git config va credential files
    /// </summary>
    public class ApiKeyHunterService
    {
        // ==================== FIELDS ====================
        private readonly string _dataDir;
        private readonly string _outputFile;
        private readonly List<FoundApiKey> _foundKeys = new();

        // Events
        public event Action<string>? OnLog;
        public event Action<FoundApiKey>? OnKeyFound;
        public event Action<int>? OnProgressUpdated;

        // Key patterns (Regex)
        private static readonly Dictionary<string, KeyPattern[]> KeyPatterns = new()
        {
            ["gemini"] = new[] {
                new KeyPattern("AIza[0-9A-Za-z_-]{35}", "Google Gemini / AI Studio"),
            },
            ["openai"] = new[] {
                new KeyPattern("sk-[A-Za-z0-9]{20}T3BlbkFJ[A-Za-z0-9]{20}", "OpenAI (Legacy)"),
                new KeyPattern("sk-proj-[A-Za-z0-9_-]{40,}", "OpenAI Project Key"),
            },
            ["anthropic"] = new[] {
                new KeyPattern("sk-ant-[A-Za-z0-9_-]{40,}", "Anthropic Claude"),
            },
            ["groq"] = new[] {
                new KeyPattern("gsk_[A-Za-z0-9]{50,}", "Groq Cloud"),
            },
            ["mistral"] = new[] {
                new KeyPattern("[A-Za-z0-9]{32}", "Mistral AI (generic 32-char)"),
            },
            ["huggingface"] = new[] {
                new KeyPattern("hf_[A-Za-z0-9]{34}", "HuggingFace Token"),
            },
            ["openrouter"] = new[] {
                new KeyPattern("sk-or-v1-[A-Za-z0-9]{64}", "OpenRouter"),
            },
            ["together"] = new[] {
                new KeyPattern("[a-f0-9]{64}", "Together AI (64-char hex)"),
            },
            ["fireworks"] = new[] {
                new KeyPattern("fw_[A-Za-z0-9_-]{40,}", "Fireworks AI"),
            },
            ["cohere"] = new[] {
                new KeyPattern("[A-Za-z0-9]{40}", "Cohere (40-char alnum)"),
            },
            ["replicate"] = new[] {
                new KeyPattern("r8_[A-Za-z0-9]{37}", "Replicate"),
            },
            ["deepseek"] = new[] {
                new KeyPattern("sk-[a-f0-9]{32}", "DeepSeek"),
            },
            ["perplexity"] = new[] {
                new KeyPattern("pplx-[A-Za-z0-9]{48}", "Perplexity AI"),
            },
            ["github"] = new[] {
                new KeyPattern("ghp_[A-Za-z0-9]{36}", "GitHub Personal Token"),
                new KeyPattern("gho_[A-Za-z0-9]{36}", "GitHub OAuth Token"),
            },
            ["cloudflare"] = new[] {
                new KeyPattern("v1\\.0-[a-f0-9]{24}-[a-f0-9]{146}", "Cloudflare Workers AI"),
            },
        };

        // Bepul API provayderlari ro'yxati
        public static readonly List<FreeApiProvider> FreeProviders = new()
        {
            new FreeApiProvider {
                Name = "Google Gemini",
                Url = "https://aistudio.google.com/app/apikey",
                FreeLimit = "15 RPM, 1M tokens/month",
                Models = "Gemini 2.0 Flash, Gemini 1.5 Pro",
                SignUpMethod = "Google Account",
                KeyPrefix = "AIza"
            },
            new FreeApiProvider {
                Name = "Groq Cloud",
                Url = "https://console.groq.com/keys",
                FreeLimit = "30 RPM, 14,400 req/day",
                Models = "LLaMA 3.1 70B, Mixtral 8x7B, Gemma 2",
                SignUpMethod = "Google/GitHub Account",
                KeyPrefix = "gsk_"
            },
            new FreeApiProvider {
                Name = "Mistral AI",
                Url = "https://console.mistral.ai/api-keys/",
                FreeLimit = "1 RPM free tier",
                Models = "Mistral Small, Mistral Nemo",
                SignUpMethod = "Google Account",
                KeyPrefix = ""
            },
            new FreeApiProvider {
                Name = "HuggingFace",
                Url = "https://huggingface.co/settings/tokens",
                FreeLimit = "Serverless Inference (rate limited)",
                Models = "Thousands of models",
                SignUpMethod = "Email/Google",
                KeyPrefix = "hf_"
            },
            new FreeApiProvider {
                Name = "OpenRouter (Free models)",
                Url = "https://openrouter.ai/settings/keys",
                FreeLimit = "Free models only (no credit needed)",
                Models = "Meta LLaMA 3.1, Gemma 2, Phi-3",
                SignUpMethod = "Google Account",
                KeyPrefix = "sk-or-"
            },
            new FreeApiProvider {
                Name = "Together AI",
                Url = "https://api.together.xyz/settings/api-keys",
                FreeLimit = "$1 free credit on signup",
                Models = "LLaMA 3.1, Mistral, CodeLlama",
                SignUpMethod = "Google/GitHub",
                KeyPrefix = ""
            },
            new FreeApiProvider {
                Name = "Fireworks AI",
                Url = "https://fireworks.ai/account/api-keys",
                FreeLimit = "$1 free credit on signup",
                Models = "LLaMA 3.1, FireFunction v2",
                SignUpMethod = "Google Account",
                KeyPrefix = "fw_"
            },
            new FreeApiProvider {
                Name = "Cohere",
                Url = "https://dashboard.cohere.com/api-keys",
                FreeLimit = "Trial key: 1000 calls/month",
                Models = "Command R+, Command R, Embed v3",
                SignUpMethod = "Google Account",
                KeyPrefix = ""
            },
            new FreeApiProvider {
                Name = "Anthropic Claude",
                Url = "https://console.anthropic.com/settings/keys",
                FreeLimit = "$5 free credit for new accounts",
                Models = "Claude 3.5 Sonnet, Claude 3 Haiku",
                SignUpMethod = "Google Account + Phone",
                KeyPrefix = "sk-ant-"
            },
            new FreeApiProvider {
                Name = "Replicate",
                Url = "https://replicate.com/account/api-tokens",
                FreeLimit = "Free tier with limited predictions",
                Models = "LLaMA, SDXL, Whisper",
                SignUpMethod = "GitHub Account",
                KeyPrefix = "r8_"
            },
            new FreeApiProvider {
                Name = "DeepSeek",
                Url = "https://platform.deepseek.com/api_keys",
                FreeLimit = "$5 free credit",
                Models = "DeepSeek V3, DeepSeek Coder",
                SignUpMethod = "Email/Google",
                KeyPrefix = "sk-"
            },
            new FreeApiProvider {
                Name = "Perplexity AI",
                Url = "https://www.perplexity.ai/settings/api",
                FreeLimit = "$5 free credit",
                Models = "Sonar Small/Large",
                SignUpMethod = "Google Account",
                KeyPrefix = "pplx-"
            },
            new FreeApiProvider {
                Name = "Cloudflare Workers AI",
                Url = "https://dash.cloudflare.com/",
                FreeLimit = "10,000 neurons/day free",
                Models = "LLaMA 3.1 8B, Mistral 7B, Whisper",
                SignUpMethod = "Email",
                KeyPrefix = ""
            },
            new FreeApiProvider {
                Name = "Cerebras",
                Url = "https://cloud.cerebras.ai/",
                FreeLimit = "Free inference (very fast)",
                Models = "LLaMA 3.1 70B (fastest in world)",
                SignUpMethod = "Google Account",
                KeyPrefix = "csk-"
            },
            new FreeApiProvider {
                Name = "SambaNova",
                Url = "https://cloud.sambanova.ai/apis",
                FreeLimit = "Free (rate limited)",
                Models = "LLaMA 3.1 405B",
                SignUpMethod = "Google Account",
                KeyPrefix = ""
            },
            new FreeApiProvider {
                Name = "NVIDIA NIM",
                Url = "https://build.nvidia.com/",
                FreeLimit = "1000 free API calls",
                Models = "LLaMA 3.1, Mixtral, Nemotron",
                SignUpMethod = "NVIDIA Account",
                KeyPrefix = "nvapi-"
            },
        };

        // ==================== CONSTRUCTOR ====================
        public ApiKeyHunterService(string dataDir)
        {
            _dataDir = dataDir;
            _outputFile = Path.Combine(_dataDir, "hunted_api_keys.json");
        }

        // ==================== MAIN SCAN ====================

        /// <summary>
        /// Kompyuterdagi barcha joylardan API keylarni qidirish
        /// </summary>
        public async Task<List<FoundApiKey>> ScanAllAsync()
        {
            _foundKeys.Clear();
            Log("Starting full system API key scan...");

            var scanPaths = GetScanPaths();
            int totalPaths = scanPaths.Count;
            int scannedCount = 0;

            foreach (var scanPath in scanPaths)
            {
                try
                {
                    if (Directory.Exists(scanPath.Path))
                    {
                        Log($"Scanning: {scanPath.Description} ({scanPath.Path})");
                        await ScanDirectoryAsync(scanPath.Path, scanPath.Description, maxDepth: scanPath.MaxDepth);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error scanning {scanPath.Path}: {ex.Message}");
                }

                scannedCount++;
                OnProgressUpdated?.Invoke((int)((double)scannedCount / totalPaths * 100));
            }

            // Duplicate tozalash
            var uniqueKeys = _foundKeys
                .GroupBy(k => k.ApiKey)
                .Select(g => g.First())
                .ToList();

            Log($"Scan complete! Found {uniqueKeys.Count} unique API keys.");

            // Saqlash
            SaveFoundKeys(uniqueKeys);

            return uniqueKeys;
        }

        /// <summary>
        /// Faqat Antigravity AppData papkasidan qidirish
        /// </summary>
        public async Task<List<FoundApiKey>> ScanAntigravityDataAsync()
        {
            _foundKeys.Clear();
            Log("Scanning Antigravity AppData...");

            string antigravityPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".gemini", "antigravity"
            );

            if (Directory.Exists(antigravityPath))
            {
                await ScanDirectoryAsync(antigravityPath, "Antigravity AppData", maxDepth: 10);
            }

            // Brain papkalardan ham qidirish
            string brainPath = Path.Combine(antigravityPath, "brain");
            if (Directory.Exists(brainPath))
            {
                await ScanDirectoryAsync(brainPath, "Antigravity Brain", maxDepth: 10);
            }

            // Knowledge papkadan
            string knowledgePath = Path.Combine(antigravityPath, "knowledge");
            if (Directory.Exists(knowledgePath))
            {
                await ScanDirectoryAsync(knowledgePath, "Antigravity Knowledge", maxDepth: 5);
            }

            SaveFoundKeys(_foundKeys);
            return _foundKeys;
        }

        // ==================== SCAN PATHS ====================

        private List<ScanPath> GetScanPaths()
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return new List<ScanPath>
            {
                // Antigravity o'zi
                new ScanPath(Path.Combine(userHome, ".gemini", "antigravity"), "Antigravity AppData", 10),
                new ScanPath(Path.Combine(userHome, ".gemini"), "Gemini Config", 5),

                // User Desktop va Documents
                new ScanPath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Desktop", 3),
                new ScanPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents", 3),
                new ScanPath(Path.Combine(userHome, "Downloads"), "Downloads", 2),

                // Claude papkasi (bu loyiha)
                new ScanPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Claude"), "Claude Project", 5),
                new ScanPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Project"), "Project Folder", 5),

                // Env fayllar
                new ScanPath(userHome, "User Home (.env files)", 1),

                // VS Code
                new ScanPath(Path.Combine(appData, "Code", "User"), "VS Code Settings", 3),

                // Git
                new ScanPath(Path.Combine(userHome, ".git"), "Git Config", 2),
                new ScanPath(Path.Combine(userHome, ".gitconfig"), "Git Global Config", 1),

                // Node.js
                new ScanPath(Path.Combine(userHome, ".npmrc"), "NPM Config", 1),

                // Python
                new ScanPath(Path.Combine(userHome, ".config"), "Linux-style Config", 3),
                new ScanPath(Path.Combine(appData, "pip"), "Pip Config", 2),

                // Cloud CLI
                new ScanPath(Path.Combine(userHome, ".aws"), "AWS Credentials", 2),
                new ScanPath(Path.Combine(appData, "gcloud"), "Google Cloud CLI", 3),

                // Browser profiles (cookies/saved passwords dan emas, faqat extension data)
                new ScanPath(Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Extensions"), "Chrome Extensions", 4),
                new ScanPath(Path.Combine(appData, "Mozilla", "Firefox", "Profiles"), "Firefox Profiles", 3),

                // Windows Credential Store
                new ScanPath(Path.Combine(localAppData, "Microsoft", "Credentials"), "Windows Credentials", 2),

                // Other dev tools
                new ScanPath(Path.Combine(userHome, ".cursor"), "Cursor Editor", 3),
                new ScanPath(Path.Combine(userHome, ".vscode"), "VS Code Global", 2),
                new ScanPath(Path.Combine(appData, "JetBrains"), "JetBrains IDEs", 3),
            };
        }

        // ==================== DIRECTORY SCANNER ====================

        private async Task ScanDirectoryAsync(string dirPath, string source, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth > maxDepth) return;
            if (!Directory.Exists(dirPath)) return;

            try
            {
                // Fayllarni skanerlash
                var files = Directory.GetFiles(dirPath).Where(f => IsTargetFile(f)).ToList();
                foreach (var file in files)
                {
                    await ScanFileAsync(file, source);
                }

                // Sub-papkalarga kirib skanerlash
                var dirs = Directory.GetDirectories(dirPath);
                foreach (var dir in dirs)
                {
                    string dirName = Path.GetFileName(dir).ToLower();
                    // Skip heavy/unrelated dirs
                    if (dirName == "node_modules" || dirName == ".git" || dirName == "bin" ||
                        dirName == "obj" || dirName == "__pycache__" || dirName == "cache" ||
                        dirName == "Cache" || dirName == "GPUCache" || dirName == "BlobStorage")
                        continue;

                    await ScanDirectoryAsync(dir, source, maxDepth, currentDepth + 1);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                Log($"Scan error in {dirPath}: {ex.Message}");
            }
        }

        private bool IsTargetFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath).ToLower();
            string ext = Path.GetExtension(filePath).ToLower();

            // Target file names
            if (fileName == ".env" || fileName == ".env.local" || fileName == ".env.production" ||
                fileName == ".env.development" || fileName == "credentials" || fileName == ".npmrc" ||
                fileName == ".pypirc" || fileName == "config.json" || fileName == "settings.json" ||
                fileName == "secrets.json" || fileName == "api_keys.json" || fileName == "keys.json" ||
                fileName == "appsettings.json" || fileName == "metadata.json" ||
                fileName == "config.yaml" || fileName == "config.yml" || fileName == "config.toml" ||
                fileName == "overview.txt" || fileName == "task_config.json")
                return true;

            // Target extensions
            if (ext == ".env" || ext == ".json" || ext == ".yaml" || ext == ".yml" ||
                ext == ".toml" || ext == ".ini" || ext == ".cfg" || ext == ".conf" ||
                ext == ".txt" || ext == ".md" || ext == ".cs" || ext == ".py" ||
                ext == ".js" || ext == ".ts" || ext == ".kt" || ext == ".xml")
                return true;

            return false;
        }

        // ==================== FILE SCANNER ====================

        private async Task ScanFileAsync(string filePath, string source)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 5 * 1024 * 1024) return; // Skip files > 5MB

                string content = await File.ReadAllTextAsync(filePath);

                foreach (var platformEntry in KeyPatterns)
                {
                    foreach (var pattern in platformEntry.Value)
                    {
                        var matches = Regex.Matches(content, pattern.Pattern);
                        foreach (Match match in matches)
                        {
                            string key = match.Value;

                            // False positive filtrlash
                            if (IsFalsePositive(key, platformEntry.Key)) continue;

                            // Dublikat tekshirish
                            if (_foundKeys.Any(k => k.ApiKey == key)) continue;

                            var foundKey = new FoundApiKey
                            {
                                ApiKey = key,
                                Platform = platformEntry.Key,
                                PlatformName = pattern.Description,
                                FoundInFile = filePath,
                                Source = source,
                                FoundAt = DateTime.Now
                            };

                            _foundKeys.Add(foundKey);
                            OnKeyFound?.Invoke(foundKey);
                            Log($"FOUND [{platformEntry.Key}] in {Path.GetFileName(filePath)}: {key[..Math.Min(25, key.Length)]}...");
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsFalsePositive(string key, string platform)
        {
            // Juda qisqa keylar
            if (key.Length < 10) return true;

            // Test/example keylar
            if (key.Contains("test") || key.Contains("example") || key.Contains("demo") ||
                key.Contains("xxx") || key.Contains("your_") || key.Contains("TODO") ||
                key == "AIzaSyA0000000000000000000000000000000000") return true;

            // All same chars
            if (key.Distinct().Count() < 5) return true;

            // Gemini uchun aniq prefix
            if (platform == "gemini" && !key.StartsWith("AIza")) return true;
            if (platform == "groq" && !key.StartsWith("gsk_")) return true;
            if (platform == "huggingface" && !key.StartsWith("hf_")) return true;
            if (platform == "anthropic" && !key.StartsWith("sk-ant-")) return true;
            if (platform == "openrouter" && !key.StartsWith("sk-or-")) return true;
            if (platform == "replicate" && !key.StartsWith("r8_")) return true;
            if (platform == "perplexity" && !key.StartsWith("pplx-")) return true;

            return false;
        }

        // ==================== PERSISTENCE ====================

        private void SaveFoundKeys(List<FoundApiKey> keys)
        {
            try
            {
                string json = JsonConvert.SerializeObject(keys, Formatting.Indented);
                File.WriteAllText(_outputFile, json);

                // Platform bo'yicha alohida fayllar
                foreach (var group in keys.GroupBy(k => k.Platform))
                {
                    string platformFile = Path.Combine(_dataDir, $"found_{group.Key}_keys.txt");
                    var lines = group.Select(k => k.ApiKey);
                    File.WriteAllLines(platformFile, lines);
                }

                Log($"Saved {keys.Count} keys to {_outputFile}");
            }
            catch (Exception ex)
            {
                Log($"Save error: {ex.Message}");
            }
        }

        public List<FoundApiKey> LoadFoundKeys()
        {
            try
            {
                if (File.Exists(_outputFile))
                {
                    string json = File.ReadAllText(_outputFile);
                    return JsonConvert.DeserializeObject<List<FoundApiKey>>(json) ?? new();
                }
            }
            catch { }
            return new();
        }

        private void Log(string msg) => OnLog?.Invoke($"[KeyHunter] {msg}");
    }

    // ==================== MODELS ====================

    public class FoundApiKey
    {
        public string ApiKey { get; set; } = "";
        public string Platform { get; set; } = "";
        public string PlatformName { get; set; } = "";
        public string FoundInFile { get; set; } = "";
        public string Source { get; set; } = "";
        public DateTime FoundAt { get; set; }
    }

    public class FreeApiProvider
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string FreeLimit { get; set; } = "";
        public string Models { get; set; } = "";
        public string SignUpMethod { get; set; } = "";
        public string KeyPrefix { get; set; } = "";
    }

    public class KeyPattern
    {
        public string Pattern { get; set; }
        public string Description { get; set; }
        public KeyPattern(string pattern, string desc) { Pattern = pattern; Description = desc; }
    }

    public class ScanPath
    {
        public string Path { get; set; }
        public string Description { get; set; }
        public int MaxDepth { get; set; }
        public ScanPath(string path, string desc, int depth) { Path = path; Description = desc; MaxDepth = depth; }
    }
}
