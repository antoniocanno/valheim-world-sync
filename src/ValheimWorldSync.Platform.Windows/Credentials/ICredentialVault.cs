namespace ValheimWorldSync.Platform.Windows.Credentials;

using ValheimWorldSync.Core.Localization;

public sealed record R2Credentials(string AccessKeyId, string SecretAccessKey)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessKeyId) || string.IsNullOrWhiteSpace(SecretAccessKey))
            throw new InvalidDataException(Strings.Get("Vault_Incomplete"));
    }
}

public interface ICredentialVault
{
    Task<R2Credentials?> ReadAsync(string target, CancellationToken token = default);
    Task WriteAsync(string target, R2Credentials credentials, CancellationToken token = default);
    Task DeleteAsync(string target, CancellationToken token = default);
}
