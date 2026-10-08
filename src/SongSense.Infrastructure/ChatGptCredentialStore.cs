using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SongSense.Core;

namespace SongSense.Infrastructure;

public enum ChatGptLoginOutcome { None, Waiting, Connected, Failed, Cancelled }

// Classes intentionally have no generated ToString containing credentials.
public sealed class ChatGptRegistration
{
    public string ClientId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Model { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? IdToken { get; set; }
    public string[] Scopes { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? EarliestRefreshAt { get; set; }
    public bool HasSession => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken);
    public void ClearTokens() { AccessToken = RefreshToken = IdToken = null; Scopes = []; ExpiresAt = default; EarliestRefreshAt = null; }
}

public sealed class ChatGptCredentials
{
    public int Version { get; set; } = 1;
    public string HostId { get; set; } = "urn:uuid:" + Guid.NewGuid();
    public AiConnectionMode Mode { get; set; } = AiConnectionMode.ChatGptIncluded;
    public string? ActiveClientId { get; set; }
    public string? PendingClientId { get; set; }
    // Only fixed diagnostic codes; never callback URLs, codes, tokens or provider text.
    public ChatGptLoginOutcome LoginOutcome { get; set; }
    public AiFailure LoginFailure { get; set; }
    public AuthenticationStep LoginStep { get; set; }
    public OAuthFailureCode LoginCode { get; set; }
    public int? LoginHttpStatus { get; set; }
    public List<ChatGptRegistration> Registrations { get; set; } = [];
}

public sealed class ChatGptCredentialStore(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SongSense.ChatGPT.OAuth.v1");
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SongSense", "ChatGptAuth", "session.dpapi");
    public string LockPath => path + ".lock";

    public async Task<ChatGptCredentials> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(path)) return new ChatGptCredentials();
        if (new FileInfo(path).Length > 524_288) throw new AiException(AiFailure.LocalStorage);
        byte[] cipher = await File.ReadAllBytesAsync(path, token);
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            var value = JsonSerializer.Deserialize<ChatGptCredentials>(plain) ?? throw new AiException(AiFailure.LocalStorage);
            if (value.Version != 1 || !Enum.IsDefined(value.Mode) || !Enum.IsDefined(value.LoginOutcome) ||
                !Enum.IsDefined(value.LoginFailure) || !Enum.IsDefined(value.LoginStep) || !Enum.IsDefined(value.LoginCode) ||
                value.LoginHttpStatus is < 100 or > 599 || !Uri.TryCreate(value.HostId, UriKind.Absolute, out _) ||
                !value.HostId.StartsWith("urn:uuid:", StringComparison.Ordinal) || !Guid.TryParse(value.HostId[9..], out _) ||
                value.Registrations.Count > 20 || value.Registrations.Select(x => x.ClientId).Distinct().Count() != value.Registrations.Count ||
                value.Registrations.Any(x => !IsClientId(x.ClientId) || string.IsNullOrWhiteSpace(x.Subject) || x.Subject.Length > 512) ||
                value.PendingClientId is not null && !IsClientId(value.PendingClientId) ||
                value.ActiveClientId is not null && value.Registrations.All(x => x.ClientId != value.ActiveClientId))
                throw new AiException(AiFailure.LocalStorage);
            return value;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or NullReferenceException) { throw new AiException(AiFailure.LocalStorage); }
        finally { CryptographicOperations.ZeroMemory(cipher); if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
    }

    public async Task SaveAsync(ChatGptCredentials value, CancellationToken token)
    {
        EnsurePrivateDirectory();
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[]? cipher = null;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (plain.Length > 500_000) throw new AiException(AiFailure.LocalStorage);
            cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(temporary, cipher, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain); if (cipher is not null) CryptographicOperations.ZeroMemory(cipher);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void EnsurePrivateDirectory()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
        directory.Create();
        // Dedicated credential directory only; never change the application's data directory ACL.
        var owner = WindowsIdentity.GetCurrent().User ?? throw new AiException(AiFailure.LocalStorage);
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    public static bool IsClientId(string? value) => value is { Length: > 7 and <= 200 } &&
        value.StartsWith("oaiapp_", StringComparison.Ordinal) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
