using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Hunuh;

public static class PasswordHasher
{
    const int Iterations = 100_000;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string hash, string salt)
    {
        var expected = Convert.FromBase64String(hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt),
            Iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

public sealed record TokenClaims(Guid UserId, string Username, Role Role, DateTimeOffset Expires, string Jti);

/// <summary>Issues and validates compact HMAC-SHA256 signed tokens (header.payload.signature, JWT-style).</summary>
public sealed class TokenService
{
    // Per-process key: tokens are valid only for this app session.
    readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    readonly HashSet<string> _revoked = new();

    public string Issue(AppUser user, TimeSpan lifetime)
    {
        var payload = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["name"] = user.Username,
            ["role"] = user.Role.ToString(),
            ["exp"] = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };
        var h = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var p = B64(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{h}.{p}.{B64(Sign($"{h}.{p}"))}";
    }

    public TokenClaims? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(Sign($"{parts[0]}.{parts[1]}"), FromB64(parts[2])))
                return null;

            using var doc = JsonDocument.Parse(FromB64(parts[1]));
            var r = doc.RootElement;
            var exp = DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("exp").GetInt64());
            var jti = r.GetProperty("jti").GetString() ?? "";
            if (exp < DateTimeOffset.UtcNow) return null;
            lock (_revoked) { if (_revoked.Contains(jti)) return null; }

            return new TokenClaims(
                Guid.Parse(r.GetProperty("sub").GetString()!),
                r.GetProperty("name").GetString() ?? "",
                Enum.Parse<Role>(r.GetProperty("role").GetString()!),
                exp, jti);
        }
        catch { return null; }
    }

    public void Revoke(string token)
    {
        var c = Validate(token);
        if (c is not null) lock (_revoked) _revoked.Add(c.Jti);
    }

    byte[] Sign(string data) => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(data));
    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static byte[] FromB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
