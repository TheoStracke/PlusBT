using System.IO;
using System.Security.Cryptography;

namespace Busca_BT.Infrastructure;

/// <summary>Hash SHA-256 (hex minúsculo) usado para versionar os templates.</summary>
public static class HashArquivo
{
    public static string Sha256(byte[] conteudo) => Convert.ToHexStringLower(SHA256.HashData(conteudo));

    public static string Sha256(string caminho)
    {
        // FileShare.ReadWrite: o BarTender pode estar com o arquivo aberto.
        using var stream = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
