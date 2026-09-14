using System.ComponentModel;
using ValheimWorldSync.Platform.Windows.Credentials;
using Xunit;

namespace ValheimWorldSync.Windows.Tests;

public sealed class CredentialVaultTests
{
    [Fact]
    public async Task WritesReadsReplacesAndDeletesCredential()
    {
        if (!OperatingSystem.IsWindows()) return;
        var vault = new WindowsCredentialVault();
        var target = WindowsCredentialVault.TargetFor(Guid.NewGuid().ToString());
        var written = false;
        try
        {
            var first = new R2Credentials("first", "secret-one");
            try { await vault.WriteAsync(target, first, TestContext.Current.CancellationToken); }
            catch (Win32Exception e) when (e.NativeErrorCode == 1312)
            {
                Assert.Skip("O host de testes não possui uma sessão interativa do Credential Manager.");
            }
            written = true;
            Assert.Equal(first, await vault.ReadAsync(target, TestContext.Current.CancellationToken));

            var replacement = new R2Credentials("second", "secret-two");
            await vault.WriteAsync(target, replacement, TestContext.Current.CancellationToken);
            Assert.Equal(replacement, await vault.ReadAsync(target, TestContext.Current.CancellationToken));
        }
        finally { if (written) await vault.DeleteAsync(target); }

        Assert.Null(await vault.ReadAsync(target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void BuildsOnlyNamespacedTargets()
    {
        var id = Guid.NewGuid().ToString();
        Assert.Equal($"ValheimWorldSync/profile/{id}/r2", WindowsCredentialVault.TargetFor(id));
        Assert.Throws<ArgumentException>(() => WindowsCredentialVault.TargetFor("not-a-guid"));
    }
}
