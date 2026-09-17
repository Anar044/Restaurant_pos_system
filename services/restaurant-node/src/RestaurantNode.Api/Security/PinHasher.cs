using System.Security.Cryptography;
using System.Text;

namespace RestaurantNode.Api.Security;

public sealed class PinHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string V2Prefix = "pin-v2$";
    private readonly byte[] lookupKey;

    public PinHasher(IConfiguration configuration)
    {
        var configuredKey = configuration["PinLookup:Key"] ?? configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("PinLookup:Key or Jwt:Key is required.");

        lookupKey = SHA256.HashData(Encoding.UTF8.GetBytes($"restaurant-pin-lookup|{configuredKey}"));
    }

    public string Hash(Guid restaurantId, string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        var lookup = ComputeLookup(restaurantId, pin);
        return $"{V2Prefix}{lookup}$pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public string LookupPrefix(Guid restaurantId, string pin) =>
        $"{V2Prefix}{ComputeLookup(restaurantId, pin)}$";

    public bool IsLookupOptimized(string encoded) =>
        encoded.StartsWith(V2Prefix, StringComparison.Ordinal);

    public bool Verify(string pin, string encoded)
    {
        try
        {
            if (encoded.StartsWith(V2Prefix, StringComparison.Ordinal))
            {
                var parts = encoded.Split('$');
                if (parts.Length != 6 || parts[0] != "pin-v2" || parts[2] != "pbkdf2-sha256") return false;
                return VerifyPbkdf2(pin, parts[3], parts[4], parts[5]);
            }

            var legacyParts = encoded.Split('$');
            if (legacyParts.Length != 4 || legacyParts[0] != "pbkdf2-sha256") return false;
            return VerifyPbkdf2(pin, legacyParts[1], legacyParts[2], legacyParts[3]);
        }
        catch
        {
            return false;
        }
    }

    private string ComputeLookup(Guid restaurantId, string pin)
    {
        using var hmac = new HMACSHA256(lookupKey);
        var payload = Encoding.UTF8.GetBytes($"{restaurantId:N}:{pin}");
        return Convert.ToHexString(hmac.ComputeHash(payload));
    }

    private static bool VerifyPbkdf2(string pin, string iterationsText, string saltText, string hashText)
    {
        var iterations = int.Parse(iterationsText);
        var salt = Convert.FromBase64String(saltText);
        var expected = Convert.FromBase64String(hashText);
        var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
