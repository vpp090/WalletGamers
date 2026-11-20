using System.Security.Cryptography;

namespace Game.Api.Domain;

public sealed class CryptoRandomSource : IRandomSource
{
    public double NextDouble()
    {
        // 53 random bits -> [0, 1)
        var value = RandomNumberGenerator.GetInt32(0, 1 << 26) * (double)(1 << 27)
                    + RandomNumberGenerator.GetInt32(0, 1 << 27);
        return value / (1L << 53);
    }
}
