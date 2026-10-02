using System.Security.Cryptography;
using System.Text;

namespace Busca_BT.Infrastructure;

/// <summary>
/// Hash do PIN do operador (PBKDF2-SHA256 com sal aleatório). O PIN em si nunca é
/// gravado. Formato: "pbkdf2$&lt;iterações&gt;$&lt;sal base64&gt;$&lt;hash base64&gt;".
/// </summary>
public static class PinHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return true; // operador sem PIN

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>PIN aceito: 4 a 8 dígitos.</summary>
    public static bool IsValid(string? pin) =>
        pin is { Length: >= 4 and <= 8 } && pin.All(char.IsAsciiDigit);
}
