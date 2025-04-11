using System.Security.Cryptography;
using System.Text;

namespace data_mover.ColumnProcessors;

public static class ColumnProcessors
{
    public static IColumnProcessor Create(string type)
    {
        return type switch
        {
            "HelloWorld" => new HelloWorldProcessor(),
            "Encryption" => new EncryptionProcessor(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type)
        };
    }
}

public class HelloWorldProcessor : IColumnProcessor
{
    public object ProcessValue(object input) => "Hello, World!";
}

public class EncryptionProcessor : IColumnProcessor
{
    private static readonly byte[] _aesKey = Encoding.UTF8.GetBytes("0123456789ABCDEF"); // 16 bytes for AES-128
    private static readonly byte[] _aesIv = Encoding.UTF8.GetBytes("ABCDEF9876543210"); // 16 bytes for AES block size

    public object ProcessValue(object input)
    {
        var plainText = input?.ToString();
        if (plainText != null)
        {
            var cipherText = EncryptToHexString(plainText);
            return cipherText;
        }
        else
        {
            throw new ArgumentException($"Couldn't ToString an input to the {nameof(EncryptionProcessor)}");
        }
    }

    private static string EncryptToHexString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("The string to be encrypted cannot be null or empty.");

        using var aesAlg = Aes.Create();
        aesAlg.Key = _aesKey;
        aesAlg.IV = _aesIv;

        var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

        var plainTextBytes = Encoding.UTF8.GetBytes(plainText);

        using var msEncrypt = new MemoryStream();
        using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);

        csEncrypt.Write(plainTextBytes, 0, plainTextBytes.Length);
        csEncrypt.FlushFinalBlock();
        var encrypted = msEncrypt.ToArray();
        var builder = new StringBuilder();
        foreach (var t in encrypted)
        {
            builder.AppendFormat("{0:x2}", t);
        }
        return builder.ToString();
    }
}