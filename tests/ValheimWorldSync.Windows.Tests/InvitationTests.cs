using ValheimWorldSync.Platform.Windows.Credentials;
using ValheimWorldSync.Platform.Windows.Invitations;
using Xunit;

namespace ValheimWorldSync.Windows.Tests;

public sealed class InvitationTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "invite-" + Guid.NewGuid().ToString("N") + ".vwsinvite");
    private static InvitationPayload Payload => new("https://account.r2.cloudflarestorage.com", "bucket", "worlds/world/", "world", "Midgard", "Midgard", 10, new("access", "secret"));
    [Fact] public async Task RoundTripsWithoutPlaintextSecrets()
    {
        var codec = new InvitationCodec(10_000); await codec.WriteAsync(path, Payload, "a-strong-password");
        Assert.Equal(Payload, await codec.ReadAsync(path, "a-strong-password"));
        Assert.DoesNotContain("secret", await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task RejectsWrongPasswordAndTampering()
    {
        var codec = new InvitationCodec(10_000); await codec.WriteAsync(path, Payload, "a-strong-password");
        await Assert.ThrowsAsync<InvalidDataException>(() => codec.ReadAsync(path, "another-password"));
        var text = await File.ReadAllTextAsync(path); await File.WriteAllTextAsync(path, text.Replace("ciphertext", "ciphertexu"));
        await Assert.ThrowsAsync<InvalidDataException>(() => codec.ReadAsync(path, "a-strong-password"));
    }
    [Fact] public async Task SupportsLegacyRootPrefix()
    {
        var codec = new InvitationCodec(10_000); var payload = Payload with { RemotePrefix = "" };
        await codec.WriteAsync(path, payload, "a-strong-password");
        Assert.Equal("", (await codec.ReadAsync(path, "a-strong-password")).RemotePrefix);
    }
    public void Dispose() { if (File.Exists(path)) File.Delete(path); }
}
