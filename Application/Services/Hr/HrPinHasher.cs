using System.Security.Cryptography;
using System.Text;

namespace Application.Services.Hr
{
    /// <summary>PIN hashing for till attendance: SHA-256(pin + salt), hex encoded. PINs are 4-6 digits.</summary>
    public static class HrPinHasher
    {
        public static bool IsValidPin(string? pin)
            => !string.IsNullOrEmpty(pin) && pin.Length >= 4 && pin.Length <= 6 && pin.All(char.IsDigit);

        public static string NewSalt()
            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        public static string Hash(string pin, string salt)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin + salt));
            return Convert.ToHexString(bytes);
        }

        public static bool Verify(string? pin, string? hash, string? salt)
        {
            if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt)) return false;
            var computed = Hash(pin, salt);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(hash.ToUpperInvariant()));
        }
    }
}
