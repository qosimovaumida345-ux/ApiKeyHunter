using System;

namespace AntigravityMobile.Models
{
    public class GoogleAccountData
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string DeviceId { get; set; } = "";
        public string Status { get; set; } = "Active";
    }

    public class ApiKeyData
    {
        public string Email { get; set; } = "";
        public string Platform { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public DateTime ObtainedAt { get; set; } = DateTime.Now;
    }

    public class MobileCommand
    {
        public string Action { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public string Text { get; set; } = "";
        public string Url { get; set; } = "";
        public string PackageName { get; set; } = "";
        public int DelayMs { get; set; }
    }
}
