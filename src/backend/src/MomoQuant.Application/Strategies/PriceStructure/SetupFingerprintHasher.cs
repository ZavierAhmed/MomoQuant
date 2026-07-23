using System.Security.Cryptography;
using System.Text;

namespace MomoQuant.Application.Strategies.PriceStructure;

public static class SetupFingerprintHasher
{
    public static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16];
    }
}
