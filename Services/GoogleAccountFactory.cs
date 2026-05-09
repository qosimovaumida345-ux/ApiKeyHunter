using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AntigravityMobile.Models;

namespace AntigravityMobile.Services
{
    /// <summary>
    /// Antigravity Google Account Factory v2026
    /// Telefon orqali avtomatik Google account yaratish va boshqarish tizimi.
    /// Accessibility Service orqali telefonni to'liq boshqaradi.
    /// </summary>
    public class GoogleAccountFactory
    {
        // ==================== FIELDS ====================
        private readonly MobileControllerServer _server;
        private readonly string _deviceId;
        private readonly string _dataDir;
        private readonly string _accountsFile;
        private readonly Random _rng = new();

        // Flow tracking
        private int _totalCreated;
        private int _totalFailed;
        private DateTime _sessionStart;
        private bool _isRunning;

        // Events
        public event Action<string>? OnLog;
        public event Action<GoogleAccountData>? OnAccountCreated;
        public event Action<int, int>? OnProgressUpdated;

        // Google sign-up page coordinates (1080x2400 resolution baseline)
        // Adaptive: These are ratios that get multiplied by actual screen size
        private const double RATIO_CREATE_ACCOUNT_X = 0.25;
        private const double RATIO_CREATE_ACCOUNT_Y = 0.88;
        private const double RATIO_FOR_MYSELF_X = 0.50;
        private const double RATIO_FOR_MYSELF_Y = 0.65;
        private const double RATIO_FIRST_NAME_X = 0.50;
        private const double RATIO_FIRST_NAME_Y = 0.35;
        private const double RATIO_LAST_NAME_X = 0.50;
        private const double RATIO_LAST_NAME_Y = 0.45;
        private const double RATIO_NEXT_BUTTON_X = 0.85;
        private const double RATIO_NEXT_BUTTON_Y = 0.90;
        private const double RATIO_BIRTHDAY_MONTH_X = 0.30;
        private const double RATIO_BIRTHDAY_MONTH_Y = 0.35;
        private const double RATIO_BIRTHDAY_DAY_X = 0.55;
        private const double RATIO_BIRTHDAY_DAY_Y = 0.35;
        private const double RATIO_BIRTHDAY_YEAR_X = 0.80;
        private const double RATIO_BIRTHDAY_YEAR_Y = 0.35;
        private const double RATIO_GENDER_X = 0.50;
        private const double RATIO_GENDER_Y = 0.55;
        private const double RATIO_EMAIL_FIELD_X = 0.50;
        private const double RATIO_EMAIL_FIELD_Y = 0.40;
        private const double RATIO_PASSWORD_FIELD_X = 0.50;
        private const double RATIO_PASSWORD_FIELD_Y = 0.40;
        private const double RATIO_CONFIRM_PASS_X = 0.50;
        private const double RATIO_CONFIRM_PASS_Y = 0.52;
        private const double RATIO_AGREE_BUTTON_X = 0.85;
        private const double RATIO_AGREE_BUTTON_Y = 0.90;
        private const double RATIO_SKIP_BUTTON_X = 0.25;
        private const double RATIO_SKIP_BUTTON_Y = 0.90;

        // Screen dimensions (updated from device info)
        private int _screenW = 1080;
        private int _screenH = 2400;

        // ==================== CONSTRUCTOR ====================
        public GoogleAccountFactory(MobileControllerServer server, string deviceId, string dataDir)
        {
            _server = server;
            _deviceId = deviceId;
            _dataDir = dataDir;
            _accountsFile = Path.Combine(_dataDir, "google_accounts.json");

            // Get screen dimensions from connected device
            var deviceInfo = _server.GetDeviceInfo(deviceId);
            if (deviceInfo != null)
            {
                _screenW = deviceInfo.ScreenWidth;
                _screenH = deviceInfo.ScreenHeight;
            }
        }

        // ==================== MAIN CREATE FLOW ====================

        /// <summary>
        /// Bitta Google account yaratish (to'liq avtomatik)
        /// </summary>
        public async Task<GoogleAccountData?> CreateAccountAsync(string firstName, string lastName, string password, CancellationToken ct)
        {
            Log($"Starting account creation for {firstName} {lastName}...");

            try
            {
                // Step 0: Har qanday oldingi Google ekranlardan tozalash
                await CleanupPreviousStatesAsync(ct);

                // Step 1: Google Account Settings ochish
                Log("Opening Google Account Settings...");
                await _server.SendOpenGoogleAccountSettingsAsync(_deviceId);
                await SafeDelay(4000, ct);

                // Step 2: "Add another account" yoki "Manage accounts" topish
                Log("Looking for 'Add account' option...");
                await _server.SendFindAndClickAsync(_deviceId, "Add another account");
                await SafeDelay(2000, ct);

                // Ba'zan "Add account" deb ham chiqishi mumkin
                await _server.SendFindAndClickAsync(_deviceId, "Add account");
                await SafeDelay(2000, ct);

                // Step 3: "Google" ni tanlash (account type)
                Log("Selecting Google account type...");
                await _server.SendFindAndClickAsync(_deviceId, "Google");
                await SafeDelay(5000, ct); // Google sign-in sahifa yuklanishi uchun kutish

                // Step 4: "Create account" tugmasini bosish
                Log("Clicking 'Create account'...");
                bool foundCreate = await TryFindAndClickText(new[] { "Create account", "Hisob yaratish", "Create Account" }, ct);
                if (!foundCreate)
                {
                    // Agar matn topilmasa, koordinata bo'yicha bosish
                    await ClickByRatio(RATIO_CREATE_ACCOUNT_X, RATIO_CREATE_ACCOUNT_Y);
                }
                await SafeDelay(2000, ct);

                // Step 5: "For my personal use" ni tanlash
                Log("Selecting 'For my personal use'...");
                bool foundPersonal = await TryFindAndClickText(new[] { "For my personal use", "For myself", "Shaxsiy foydalanish" }, ct);
                if (!foundPersonal)
                {
                    await ClickByRatio(RATIO_FOR_MYSELF_X, RATIO_FOR_MYSELF_Y);
                }
                await SafeDelay(3000, ct);

                // Step 6: Ism kiritish
                Log($"Entering first name: {firstName}");
                bool foundFirstName = await TryFindFieldAndType(new[] { "First name", "First", "Ism" }, firstName, ct);
                if (!foundFirstName)
                {
                    await ClickByRatio(RATIO_FIRST_NAME_X, RATIO_FIRST_NAME_Y);
                    await SafeDelay(300, ct);
                    await _server.SendTextAsync(_deviceId, firstName);
                }
                await SafeDelay(500, ct);

                // Step 7: Familiya kiritish
                Log($"Entering last name: {lastName}");
                bool foundLastName = await TryFindFieldAndType(new[] { "Last name", "Last", "Familiya" }, lastName, ct);
                if (!foundLastName)
                {
                    await ClickByRatio(RATIO_LAST_NAME_X, RATIO_LAST_NAME_Y);
                    await SafeDelay(300, ct);
                    await _server.SendTextAsync(_deviceId, lastName);
                }
                await SafeDelay(500, ct);

                // Step 8: Next tugmasini bosish
                Log("Clicking Next...");
                await ClickNextButton(ct);
                await SafeDelay(3000, ct);

                // Step 9: Tug'ilgan kun kiritish
                Log("Entering birthday...");
                await EnterBirthdayAsync(ct);
                await SafeDelay(500, ct);

                // Step 10: Jins tanlash
                Log("Selecting gender...");
                await SelectGenderAsync(ct);
                await SafeDelay(500, ct);

                // Step 11: Next
                await ClickNextButton(ct);
                await SafeDelay(3000, ct);

                // Step 12: Email ni tanlash yoki yaratish
                string email = await HandleEmailSelectionAsync(firstName, lastName, ct);
                Log($"Email selected/created: {email}");
                await SafeDelay(500, ct);

                // Step 13: Next
                await ClickNextButton(ct);
                await SafeDelay(3000, ct);

                // Step 14: Parol kiritish
                Log("Entering password...");
                await EnterPasswordAsync(password, ct);
                await SafeDelay(500, ct);

                // Step 15: Next
                await ClickNextButton(ct);
                await SafeDelay(3000, ct);

                // Step 16: Phone number — Skip qilish
                Log("Skipping phone verification...");
                await HandlePhoneVerificationSkipAsync(ct);
                await SafeDelay(2000, ct);

                // Step 17: Recovery email — Skip
                await HandleRecoveryEmailSkipAsync(ct);
                await SafeDelay(2000, ct);

                // Step 18: Account review — Next
                await ClickNextButton(ct);
                await SafeDelay(2000, ct);

                // Step 19: Privacy and Terms — Agree
                Log("Accepting Terms of Service...");
                await HandleTermsAcceptAsync(ct);
                await SafeDelay(5000, ct);

                // Step 20: Verify account was created
                Log("Verifying account creation...");
                bool verified = await VerifyAccountCreatedAsync(email, ct);

                if (verified)
                {
                    var account = new GoogleAccountData
                    {
                        Email = email,
                        Password = password,
                        FirstName = firstName,
                        LastName = lastName,
                        CreatedAt = DateTime.Now,
                        DeviceId = _deviceId,
                        Status = "Active"
                    };

                    SaveAccount(account);
                    _totalCreated++;
                    OnAccountCreated?.Invoke(account);
                    OnProgressUpdated?.Invoke(_totalCreated, _totalFailed);
                    Log($"Account created successfully: {email}");
                    return account;
                }
                else
                {
                    _totalFailed++;
                    OnProgressUpdated?.Invoke(_totalCreated, _totalFailed);
                    Log($"Account creation could not be verified for {email}");
                    
                    // Save anyway
                    var account = new GoogleAccountData
                    {
                        Email = email,
                        Password = password,
                        FirstName = firstName,
                        LastName = lastName,
                        CreatedAt = DateTime.Now,
                        DeviceId = _deviceId,
                        Status = "Unverified"
                    };
                    SaveAccount(account);
                    return account;
                }
            }
            catch (OperationCanceledException)
            {
                Log("Account creation canceled.");
                return null;
            }
            catch (Exception ex)
            {
                _totalFailed++;
                Log($"Error creating account: {ex.Message}");
                return null;
            }
        }

        // ==================== SUB-STEPS ====================

        private async Task CleanupPreviousStatesAsync(CancellationToken ct)
        {
            // Home ga qaytish
            await _server.SendHomeAsync(_deviceId);
            await SafeDelay(1000, ct);
            // Recent apps tozalash
            await _server.SendHomeAsync(_deviceId);
            await SafeDelay(500, ct);
        }

        private async Task EnterBirthdayAsync(CancellationToken ct)
        {
            // Month
            int month = _rng.Next(1, 13);
            string[] months = { "January", "February", "March", "April", "May", "June",
                               "July", "August", "September", "October", "November", "December" };

            // Click month dropdown
            await ClickByRatio(RATIO_BIRTHDAY_MONTH_X, RATIO_BIRTHDAY_MONTH_Y);
            await SafeDelay(500, ct);
            await _server.SendFindAndClickAsync(_deviceId, months[month - 1]);
            await SafeDelay(500, ct);

            // Day
            int day = _rng.Next(1, 29);
            bool foundDay = await TryFindFieldAndType(new[] { "Day", "Kun" }, day.ToString(), ct);
            if (!foundDay)
            {
                await ClickByRatio(RATIO_BIRTHDAY_DAY_X, RATIO_BIRTHDAY_DAY_Y);
                await SafeDelay(200, ct);
                await _server.SendTextAsync(_deviceId, day.ToString());
            }
            await SafeDelay(300, ct);

            // Year (1990-2004)
            int year = _rng.Next(1990, 2005);
            bool foundYear = await TryFindFieldAndType(new[] { "Year", "Yil" }, year.ToString(), ct);
            if (!foundYear)
            {
                await ClickByRatio(RATIO_BIRTHDAY_YEAR_X, RATIO_BIRTHDAY_YEAR_Y);
                await SafeDelay(200, ct);
                await _server.SendTextAsync(_deviceId, year.ToString());
            }
            await SafeDelay(300, ct);

            Log($"Birthday set: {months[month - 1]} {day}, {year}");
        }

        private async Task SelectGenderAsync(CancellationToken ct)
        {
            // Gender dropdown
            await ClickByRatio(RATIO_GENDER_X, RATIO_GENDER_Y);
            await SafeDelay(500, ct);

            // Random select (Male yoki Female)
            string gender = _rng.Next(2) == 0 ? "Male" : "Female";
            await _server.SendFindAndClickAsync(_deviceId, gender);
            await SafeDelay(500, ct);
            Log($"Gender selected: {gender}");
        }

        private async Task<string> HandleEmailSelectionAsync(string firstName, string lastName, CancellationToken ct)
        {
            // Google odatda email taklif qiladi, yoki "Create your own Gmail address" degan tugma bo'ladi
            await SafeDelay(1000, ct);

            // Custom email yaratish
            string customEmail = GenerateEmail(firstName, lastName);

            // First try: "Create your own Gmail address" ni topish
            bool foundCustom = await TryFindAndClickText(new[] {
                "Create your own Gmail address",
                "Create your own",
                "custom address",
                "Gmail manzilini yarating"
            }, ct);

            if (foundCustom)
            {
                await SafeDelay(1500, ct);
            }

            // Email field ni topish va yozish
            bool foundEmailField = await TryFindFieldAndType(new[] {
                "Create a Gmail address",
                "Username",
                "Email",
                "Gmail address"
            }, customEmail.Replace("@gmail.com", ""), ct);

            if (!foundEmailField)
            {
                await ClickByRatio(RATIO_EMAIL_FIELD_X, RATIO_EMAIL_FIELD_Y);
                await SafeDelay(300, ct);
                await _server.SendClearAndTypeAsync(_deviceId, customEmail.Replace("@gmail.com", ""));
            }

            await SafeDelay(500, ct);
            return customEmail;
        }

        private async Task EnterPasswordAsync(string password, CancellationToken ct)
        {
            // Password field
            bool foundPass = await TryFindFieldAndType(new[] {
                "Create a password", "Password",
                "Create password", "Parol yarating"
            }, password, ct);

            if (!foundPass)
            {
                await ClickByRatio(RATIO_PASSWORD_FIELD_X, RATIO_PASSWORD_FIELD_Y);
                await SafeDelay(300, ct);
                await _server.SendTextAsync(_deviceId, password);
            }
            await SafeDelay(500, ct);

            // Confirm password
            bool foundConfirm = await TryFindFieldAndType(new[] { "Confirm", "Re-enter", "Confirm password", "Tasdiqlang" }, password, ct);
            if (!foundConfirm)
            {
                await ClickByRatio(RATIO_CONFIRM_PASS_X, RATIO_CONFIRM_PASS_Y);
                await SafeDelay(300, ct);
                await _server.SendTextAsync(_deviceId, password);
            }
            await SafeDelay(300, ct);
        }

        private async Task HandlePhoneVerificationSkipAsync(CancellationToken ct)
        {
            // Skip tugmasini izlash
            bool skipped = await TryFindAndClickText(new[] {
                "Skip", "No thanks", "O'tkazib yuborish",
                "Not now", "Maybe later"
            }, ct);

            if (!skipped)
            {
                // Agar Skip bo'lmasa, coordinate bo'yicha
                await ClickByRatio(RATIO_SKIP_BUTTON_X, RATIO_SKIP_BUTTON_Y);
            }
            await SafeDelay(1000, ct);
        }

        private async Task HandleRecoveryEmailSkipAsync(CancellationToken ct)
        {
            bool skipped = await TryFindAndClickText(new[] {
                "Skip", "No thanks", "O'tkazib yuborish",
                "Not now", "Maybe later"
            }, ct);

            if (!skipped)
            {
                await ClickByRatio(RATIO_SKIP_BUTTON_X, RATIO_SKIP_BUTTON_Y);
            }
            await SafeDelay(1000, ct);
        }

        private async Task HandleTermsAcceptAsync(CancellationToken ct)
        {
            // Pastga scroll qilish (terms ni ko'rish uchun)
            for (int i = 0; i < 5; i++)
            {
                await _server.SendScrollAsync(_deviceId, "down", 600);
                await SafeDelay(800, ct);
            }

            // "I agree" bosish
            bool agreed = await TryFindAndClickText(new[] {
                "I agree", "Agree", "Accept",
                "Roziman", "Qabul qilaman"
            }, ct);

            if (!agreed)
            {
                await ClickByRatio(RATIO_AGREE_BUTTON_X, RATIO_AGREE_BUTTON_Y);
            }
            await SafeDelay(3000, ct);

            // Ba'zida ikkinchi "I agree" ham bo'ladi
            await TryFindAndClickText(new[] { "I agree", "Agree", "Accept" }, ct);
            await SafeDelay(2000, ct);
        }

        private async Task<bool> VerifyAccountCreatedAsync(string email, CancellationToken ct)
        {
            // Ekrendagi matnni o'qib tekshirish
            bool foundWelcome = await _server.WaitForTextOnScreenAsync(_deviceId, "Welcome", 10000, 2000);
            if (foundWelcome) return true;

            // Yoki account settings da yangi email bor-yo'qligini tekshirish
            await _server.SendOpenGoogleAccountSettingsAsync(_deviceId);
            await SafeDelay(3000, ct);

            bool foundEmail = await _server.WaitForTextOnScreenAsync(_deviceId, email.Replace("@gmail.com", ""), 5000, 1000);
            return foundEmail;
        }

        // ==================== BATCH CREATION ====================

        /// <summary>
        /// Bir nechta account yaratish (batch)
        /// </summary>
        public async Task CreateMultipleAccountsAsync(int count, string password, CancellationToken ct)
        {
            _sessionStart = DateTime.Now;
            _isRunning = true;
            _totalCreated = 0;
            _totalFailed = 0;

            Log($"Starting batch creation of {count} accounts...");

            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested || !_isRunning) break;

                string firstName = NameGenerator.RandomFirstName();
                string lastName = NameGenerator.RandomLastName();

                Log($"=== Account {i + 1}/{count} ===");

                var account = await CreateAccountAsync(firstName, lastName, password, ct);

                if (account != null)
                {
                    Log($"Account {i + 1} created: {account.Email}");
                }
                else
                {
                    Log($"Account {i + 1} failed");
                }

                // Accounts orasida 3-7 soniya kutish (Google cheklov o'tmasligi uchun)
                int delay = _rng.Next(3000, 7000);
                Log($"Waiting {delay}ms before next account...");
                await SafeDelay(delay, ct);
            }

            Log($"Batch complete: {_totalCreated} created, {_totalFailed} failed");
            _isRunning = false;
        }

        // ==================== LOGIN/LOGOUT MANAGEMENT ====================

        /// <summary>
        /// Mavjud Google accountga login qilish (telefonda)
        /// </summary>
        public async Task<bool> LoginToGoogleAccountAsync(string email, string password, CancellationToken ct)
        {
            Log($"Logging into Google account: {email}");

            await _server.SendOpenGoogleAccountSettingsAsync(_deviceId);
            await SafeDelay(3000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Add another account");
            await SafeDelay(2000, ct);

            await _server.SendFindAndClickAsync(_deviceId, "Google");
            await SafeDelay(5000, ct);

            // Email kiritish
            bool foundEmail = await TryFindFieldAndType(new[] { "Email or phone", "Email", "Phone" }, email, ct);
            if (!foundEmail)
            {
                await _server.SendTextAsync(_deviceId, email);
            }
            await SafeDelay(500, ct);

            await ClickNextButton(ct);
            await SafeDelay(4000, ct);

            // Parol kiritish
            bool foundPass = await TryFindFieldAndType(new[] { "Enter your password", "Password" }, password, ct);
            if (!foundPass)
            {
                await _server.SendTextAsync(_deviceId, password);
            }
            await SafeDelay(500, ct);

            await ClickNextButton(ct);
            await SafeDelay(5000, ct);

            // Terms agree (agar chiqsa)
            await TryFindAndClickText(new[] { "I agree", "Agree", "Accept" }, ct);
            await SafeDelay(3000, ct);

            bool success = await _server.WaitForTextOnScreenAsync(_deviceId, "Welcome", 10000, 2000);
            Log(success ? $"Login successful: {email}" : $"Login may have failed: {email}");
            return success;
        }

        /// <summary>
        /// Google accountdan chiqish (logout)
        /// </summary>
        public async Task<bool> LogoutFromGoogleAccountAsync(string email, CancellationToken ct)
        {
            Log($"Logging out from: {email}");

            await _server.SendOpenGoogleAccountSettingsAsync(_deviceId);
            await SafeDelay(3000, ct);

            // Accountni topish va bosish
            await _server.SendFindAndClickAsync(_deviceId, email.Replace("@gmail.com", ""));
            await SafeDelay(2000, ct);

            // Remove account
            await _server.SendFindAndClickAsync(_deviceId, "Remove account");
            await SafeDelay(2000, ct);

            // Confirm
            await _server.SendFindAndClickAsync(_deviceId, "Remove account");
            await SafeDelay(3000, ct);

            Log($"Logged out from: {email}");
            return true;
        }

        // ==================== QR CODE VERIFICATION ====================

        /// <summary>
        /// PC dagi QR kodni telefon bilan skanerlash (login/verify uchun)
        /// </summary>
        public async Task<bool> ScanQRCodeForVerificationAsync(CancellationToken ct)
        {
            Log("Activating QR scanner on phone...");
            await _server.SendScanQRCodeAsync(_deviceId);
            await SafeDelay(5000, ct);

            bool scanned = await _server.WaitForTextOnScreenAsync(_deviceId, "scanned", 30000, 2000);
            if (scanned)
            {
                Log("QR code successfully scanned!");

                // "Yes, it's me" yoki "Confirm" bosish
                await TryFindAndClickText(new[] {
                    "Yes, it's me", "Confirm", "Yes",
                    "Allow", "Approve", "Ha, men"
                }, ct);
                
                await SafeDelay(2000, ct);
                
                // "Sign in again" bosish (agar chiqsa)
                await TryFindAndClickText(new[] {
                    "Sign in again", "Sign in", "Continue"
                }, ct);
                await SafeDelay(2000, ct);

                return true;
            }

            Log("QR code scan timeout");
            return false;
        }

        // ==================== HELPERS ====================

        private async Task ClickNextButton(CancellationToken ct)
        {
            bool found = await TryFindAndClickText(new[] { "Next", "Keyingi", "Davom" }, ct);
            if (!found)
            {
                await ClickByRatio(RATIO_NEXT_BUTTON_X, RATIO_NEXT_BUTTON_Y);
            }
        }

        private async Task ClickByRatio(double ratioX, double ratioY)
        {
            int x = (int)(_screenW * ratioX);
            int y = (int)(_screenH * ratioY);
            await _server.SendClickAsync(_deviceId, x, y);
        }

        private async Task<bool> TryFindAndClickText(string[] texts, CancellationToken ct)
        {
            foreach (string text in texts)
            {
                await _server.SendFindAndClickAsync(_deviceId, text);
                await SafeDelay(300, ct);
            }
            return true; // Actual verification would need response from device
        }

        private async Task<bool> TryFindFieldAndType(string[] hints, string value, CancellationToken ct)
        {
            foreach (string hint in hints)
            {
                await _server.SendFindAndTypeAsync(_deviceId, hint, value);
                await SafeDelay(300, ct);
            }
            return true;
        }

        private string GenerateEmail(string firstName, string lastName)
        {
            string baseEmail = $"{firstName.ToLower()}.{lastName.ToLower()}";
            baseEmail = baseEmail.Replace(" ", "");
            int randomNum = _rng.Next(100, 9999);
            return $"{baseEmail}{randomNum}@gmail.com";
        }

        private async Task SafeDelay(int ms, CancellationToken ct)
        {
            try { await Task.Delay(ms, ct); }
            catch (OperationCanceledException) { }
        }

        // ==================== PERSISTENCE ====================

        private void SaveAccount(GoogleAccountData account)
        {
            try
            {
                var accounts = LoadAllAccounts();
                accounts.Add(account);
                string json = JsonConvert.SerializeObject(accounts, Formatting.Indented);
                File.WriteAllText(_accountsFile, json);
            }
            catch (Exception ex)
            {
                Log($"Error saving account: {ex.Message}");
            }
        }

        public List<GoogleAccountData> LoadAllAccounts()
        {
            try
            {
                if (File.Exists(_accountsFile))
                {
                    string json = File.ReadAllText(_accountsFile);
                    return JsonConvert.DeserializeObject<List<GoogleAccountData>>(json) ?? new List<GoogleAccountData>();
                }
            }
            catch { }
            return new List<GoogleAccountData>();
        }

        private void Log(string msg) => OnLog?.Invoke($"[GoogleFactory] {msg}");
    }

    // ==================== NAME GENERATOR ====================

    public static class NameGenerator
    {
        private static readonly Random Rng = new();

        private static readonly string[] FirstNames = {
            "James", "Emma", "Liam", "Olivia", "Noah", "Ava", "William", "Sophia",
            "Oliver", "Isabella", "Elijah", "Mia", "Lucas", "Charlotte", "Mason",
            "Amelia", "Logan", "Harper", "Alexander", "Evelyn", "Ethan", "Abigail",
            "Jacob", "Emily", "Michael", "Elizabeth", "Daniel", "Avery", "Henry",
            "Sofia", "Jackson", "Ella", "Sebastian", "Scarlett", "Aiden", "Grace",
            "Matthew", "Victoria", "Samuel", "Riley", "David", "Aria", "Joseph",
            "Lily", "Carter", "Aurora", "Owen", "Chloe", "Wyatt", "Layla",
            "John", "Penelope", "Jack", "Camila", "Luke", "Hannah", "Jayden",
            "Nora", "Dylan", "Zoe", "Ryan", "Stella", "Nathan", "Hazel",
            "Caleb", "Ellie", "Andrew", "Paisley", "Isaac", "Audrey", "Joshua",
            "Brooklyn", "Adam", "Bella", "Leo", "Claire", "Julian", "Skylar",
            "Aaron", "Lucy", "Robert", "Savannah", "Thomas", "Anna", "Evan",
            "Caroline", "Hunter", "Genesis", "Finn", "Aaliyah", "Miles", "Kennedy",
            "Max", "Kinsley", "Theo", "Allison", "Xavier", "Maya", "Jason"
        };

        private static readonly string[] LastNames = {
            "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller",
            "Davis", "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez",
            "Wilson", "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin",
            "Lee", "Perez", "Thompson", "White", "Harris", "Sanchez", "Clark",
            "Ramirez", "Lewis", "Robinson", "Walker", "Young", "Allen", "King",
            "Wright", "Scott", "Torres", "Nguyen", "Hill", "Flores", "Green",
            "Adams", "Nelson", "Baker", "Hall", "Rivera", "Campbell", "Mitchell",
            "Carter", "Roberts", "Collins", "Stewart", "Morris", "Reed", "Cook",
            "Morgan", "Bell", "Murphy", "Bailey", "Cooper", "Richardson", "Cox",
            "Howard", "Ward", "Peterson", "Gray", "James", "Watson", "Brooks",
            "Kelly", "Sanders", "Price", "Bennett", "Wood", "Barnes", "Ross",
            "Henderson", "Coleman", "Jenkins", "Perry", "Powell", "Long", "Patterson",
            "Hughes", "Butler", "Simmons", "Foster", "Gonzales", "Bryant", "Russell",
            "Griffin", "Diaz", "Hayes", "Myers", "Ford", "Hamilton", "Graham"
        };

        public static string RandomFirstName() => FirstNames[Rng.Next(FirstNames.Length)];
        public static string RandomLastName() => LastNames[Rng.Next(LastNames.Length)];
    }
}
