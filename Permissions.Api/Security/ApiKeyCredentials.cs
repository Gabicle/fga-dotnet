using System.Security.Cryptography;

namespace Permissions.Api.Security;

public sealed class ApiKeyCredentials
{
  public const int KeySizeBytes = 32;

  private ApiKeyCredentials(byte[] checkKey, byte[] adminKey)
  {
    CheckKey = checkKey;
    AdminKey = adminKey;
  }

  public byte[] CheckKey { get; }
  public byte[] AdminKey { get; }

  public static ApiKeyCredentials FromConfiguration(IConfiguration configuration)
  {
    var checkKey = ReadKey(configuration, "Permissions:ApiKeys:Check");
    var adminKey = ReadKey(configuration, "Permissions:ApiKeys:Admin");

    if (CryptographicOperations.FixedTimeEquals(checkKey, adminKey))
      throw new InvalidOperationException("Check and admin API keys must differ.");

    return new ApiKeyCredentials(checkKey, adminKey);
  }

  private static byte[] ReadKey(IConfiguration configuration, string name)
  {
    var value = configuration[name];
    if (string.IsNullOrWhiteSpace(value))
      throw new InvalidOperationException($"Required API-key configuration '{name}' is missing.");

    var key = new byte[KeySizeBytes];
    if (!Convert.TryFromBase64String(value, key, out var written) || written != KeySizeBytes)
      throw new InvalidOperationException($"API-key configuration '{name}' must be a Base64-encoded 32-byte key.");

    if (key.All(byteValue => byteValue == key[0]))
      throw new InvalidOperationException($"API-key configuration '{name}' contains degenerate key material.");

    return key;
  }
}
