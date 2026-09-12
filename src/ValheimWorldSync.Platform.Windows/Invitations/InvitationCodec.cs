using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValheimWorldSync.Platform.Windows.Credentials;

namespace ValheimWorldSync.Platform.Windows.Invitations;

public sealed record InvitationPayload(string Endpoint, string Bucket, string RemotePrefix, string WorldId,
    string WorldDisplayName, string WorldFolderName, int RetentionCount, R2Credentials Credentials);
public sealed record InvitationEnvelope(int SchemaVersion, int Iterations, string Salt, string Nonce, string Ciphertext, string Tag);

public sealed class InvitationCodec(int iterations = 600_000)
{
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public async Task WriteAsync(string path, InvitationPayload payload, string password, CancellationToken token = default)
    {
        Validate(payload, password);
        var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions); var cipher = new byte[plain.Length]; var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, plain, cipher, tag);
            var envelope = new InvitationEnvelope(SchemaVersion, iterations, Convert.ToBase64String(salt),
                Convert.ToBase64String(nonce), Convert.ToBase64String(cipher), Convert.ToBase64String(tag));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(envelope, JsonOptions), token);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public async Task<InvitationPayload> ReadAsync(string path, string password, CancellationToken token = default)
    {
        if (password.Length < 12) throw new InvalidDataException("A senha do convite deve ter pelo menos 12 caracteres.");
        var envelope = JsonSerializer.Deserialize<InvitationEnvelope>(await File.ReadAllTextAsync(path, token), JsonOptions)
            ?? throw new InvalidDataException("Convite vazio.");
        if (envelope.SchemaVersion != SchemaVersion || envelope.Iterations < 10_000 || envelope.Iterations > 2_000_000)
            throw new InvalidDataException("Versão de convite incompatível.");
        if (envelope.Salt is null || envelope.Nonce is null || envelope.Ciphertext is null || envelope.Tag is null)
            throw new InvalidDataException("Convite inválido.");
        try
        {
            var salt = Convert.FromBase64String(envelope.Salt); var nonce = Convert.FromBase64String(envelope.Nonce);
            var cipher = Convert.FromBase64String(envelope.Ciphertext); var tag = Convert.FromBase64String(envelope.Tag);
            if (salt.Length != 16 || nonce.Length != 12 || tag.Length != 16) throw new InvalidDataException("Convite inválido.");
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, envelope.Iterations, HashAlgorithmName.SHA256, 32);
            var plain = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, cipher, tag, plain);
                var payload = JsonSerializer.Deserialize<InvitationPayload>(plain, JsonOptions) ?? throw new InvalidDataException("Convite vazio.");
                Validate(payload, password); return payload;
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
        }
        catch (CryptographicException) { throw new InvalidDataException("Senha incorreta ou convite alterado."); }
        catch (FormatException) { throw new InvalidDataException("Convite inválido."); }
    }
    private static void Validate(InvitationPayload p, string password)
    {
        if (password.Length < 12 || string.IsNullOrWhiteSpace(p.Endpoint) || string.IsNullOrWhiteSpace(p.Bucket) ||
            string.IsNullOrWhiteSpace(p.WorldId) || p.RemotePrefix != $"worlds/{p.WorldId}/" ||
            string.IsNullOrWhiteSpace(p.WorldDisplayName) || string.IsNullOrWhiteSpace(p.WorldFolderName) || p.RetentionCount is < 0 or > 1000)
            throw new InvalidDataException("Dados do convite inválidos.");
        p.Credentials.Validate();
    }
}
