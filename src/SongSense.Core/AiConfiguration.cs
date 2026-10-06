using System.Security;
using System.Text.RegularExpressions;

namespace SongSense.Core;

public interface ISecretStore
{
    Task<bool> HasKeyAsync(CancellationToken cancellationToken = default);
    Task SaveKeyAsync(SecureString key, CancellationToken cancellationToken = default);
    Task<string?> ReadKeyAsync(CancellationToken cancellationToken = default);
    Task DeleteKeyAsync(CancellationToken cancellationToken = default);
}

public interface IRequestBudget
{
    Task<bool> TryReserveRequestAsync(DateOnly localDate, CancellationToken cancellationToken = default);
}

public enum AiFailure { Disabled, MissingKey, InvalidSettings, DailyLimit, Busy, Authentication, IncompatibleModel, RateLimited, Timeout, Offline, InvalidResponse, HttpError, LocalStorage, Refused, Incomplete, UnknownLanguage, InvalidLyrics }

// Never attach response bodies, API keys, HTTP headers or original exceptions.
public sealed class AiException(AiFailure failure) : Exception("No se pudo completar la operación de IA.")
{
    public AiFailure Failure { get; } = failure;
}

public static partial class AiSettingsRules
{
    public static bool IsValidModel(string? model) => !string.IsNullOrEmpty(model) &&
        !model.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) && ModelId().IsMatch(model);

    public static void Validate(AppSettings settings)
    {
        if (settings.DailyRequestLimit is < 1 or > 100 ||
            (settings.AiEnabled && !IsValidModel(settings.Model)) ||
            (!string.IsNullOrEmpty(settings.Model) && !IsValidModel(settings.Model)))
            throw new AiException(AiFailure.InvalidSettings);
    }

    [GeneratedRegex(@"\A[a-zA-Z0-9][a-zA-Z0-9._:-]{0,119}\z", RegexOptions.NonBacktracking)]
    private static partial Regex ModelId();
}
