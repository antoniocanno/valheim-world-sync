using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ValheimWorldSync.Core.Localization;

namespace ValheimWorldSync.Platform.Windows.Credentials;

public sealed class WindowsCredentialVault : ICredentialVault
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;
    private const int NotFound = 1168;

    public Task<R2Credentials?> ReadAsync(string target, CancellationToken token = default)
    {
        EnsureWindowsAndTarget(target);
        token.ThrowIfCancellationRequested();
        if (!CredRead(target, GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound) return Task.FromResult<R2Credentials?>(null);
            throw new Win32Exception(error, Strings.Get("Vault_ReadFailed"));
        }

        try
        {
            var native = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (native.CredentialBlob == IntPtr.Zero || native.CredentialBlobSize == 0)
                throw new InvalidDataException(Strings.Get("Vault_Empty"));
            var bytes = new byte[native.CredentialBlobSize];
            Marshal.Copy(native.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                var credentials = JsonSerializer.Deserialize<R2Credentials>(bytes)
                    ?? throw new InvalidDataException(Strings.Get("Vault_Invalid"));
                credentials.Validate();
                return Task.FromResult<R2Credentials?>(credentials);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CredFree(pointer); }
    }

    public Task WriteAsync(string target, R2Credentials credentials, CancellationToken token = default)
    {
        EnsureWindowsAndTarget(target);
        credentials.Validate();
        token.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var native = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = checked((uint)bytes.Length),
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = "Cloudflare R2"
            };
            if (!CredWrite(ref native, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, Strings.Format("Vault_ProtectFailed", error));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string target, CancellationToken token = default)
    {
        EnsureWindowsAndTarget(target);
        token.ThrowIfCancellationRequested();
        if (!CredDelete(target, GenericCredential, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound)
                throw new Win32Exception(error, Strings.Get("Vault_DeleteFailed"));
        }
        return Task.CompletedTask;
    }

    public static string TargetFor(string profileId)
    {
        if (!Guid.TryParse(profileId, out _)) throw new ArgumentException(Strings.Get("Vault_BadProfileId"), nameof(profileId));
        return $"ValheimWorldSync/profile/{profileId}/r2";
    }

    private static void EnsureWindowsAndTarget(string target)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(Strings.Get("Vault_WindowsOnly"));
        if (string.IsNullOrWhiteSpace(target) || target.Length > 32767 || target.Contains('\0'))
            throw new ArgumentException(Strings.Get("Vault_BadTarget"), nameof(target));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
