using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class DpapiSecretStore(string path) : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SongSense.OpenAI.Key.v1");
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SongSense", "credentials.dpapi");

    public Task<bool> HasKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }

    public async Task SaveKeyAsync(SecureString key, CancellationToken cancellationToken = default)
    {
        if (key.Length < 1) throw new AiException(AiFailure.MissingKey);
        if (key.Length > 512) throw new AiException(AiFailure.InvalidSettings);
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(key);
        var chars = new char[key.Length];
        byte[]? plain = null;
        byte[]? cipher = null;
        string? temporary = null;
        try
        {
            Marshal.Copy(pointer, chars, 0, chars.Length);
            if (chars.Any(character => character is < '!' or > '~')) throw new AiException(AiFailure.InvalidSettings);
            plain = Encoding.UTF8.GetBytes(chars);
            cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, cipher, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            Array.Clear(chars);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (cipher is not null) CryptographicOperations.ZeroMemory(cipher);
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<string?> ReadKeyAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16_384) throw new AiException(AiFailure.LocalStorage);
        var cipher = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cipher);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public Task DeleteKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
        return Task.CompletedTask;
    }
}
