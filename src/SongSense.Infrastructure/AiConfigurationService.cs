using System.Security;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class AiConfigurationService(ISettingsStore settings, ISecretStore secrets, IRequestBudget budget,
    OpenAiConnectionProbe probe, TimeProvider? timeProvider = null, string? operationLockPath = null)
{
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public DateOnly LocalDate => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<(AppSettings Settings, bool HasKey, int Count)> ReadAsync(CancellationToken token)
    {
        try
        {
            return (await settings.ReadSettingsAsync(token), await secrets.HasKeyAsync(token), await settings.ReadRequestCountAsync(LocalDate, token));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AiException(AiFailure.LocalStorage); }
    }

    public async Task SaveAsync(AppSettings value, SecureString? replacement, bool noticeAccepted, CancellationToken token)
    {
        if (!await operation.WaitAsync(0, token)) throw new AiException(AiFailure.Busy);
        try
        {
            using var diskLock = AcquireDiskLock();
            AiSettingsRules.Validate(value);
            var previous = await settings.ReadSettingsAsync(token);
            if (value.AiEnabled && !previous.AiEnabled && !noticeAccepted) throw new AiException(AiFailure.InvalidSettings);
            if (value.AiEnabled && replacement is not { Length: > 0 } && !await secrets.HasKeyAsync(token)) throw new AiException(AiFailure.MissingKey);
            if (replacement is { Length: > 0 }) await secrets.SaveKeyAsync(replacement, token);
            await settings.SaveSettingsAsync(value, token);
        }
        catch (AiException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AiException(AiFailure.LocalStorage); }
        finally { operation.Release(); }
    }

    public async Task DeleteKeyAsync(CancellationToken token)
    {
        if (!await operation.WaitAsync(0, token)) throw new AiException(AiFailure.Busy);
        try
        {
            using var diskLock = AcquireDiskLock();
            var previous = await settings.ReadSettingsAsync(token);
            // Disable first; a failed delete can never leave generation enabled.
            await settings.SaveSettingsAsync(previous with { AiEnabled = false }, token);
            await secrets.DeleteKeyAsync(token);
        }
        catch (AiException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AiException(AiFailure.LocalStorage); }
        finally { operation.Release(); }
    }

    public Task TestConnectionAsync(CancellationToken token) => RunRequestAsync<object?>(async (model, key, request) =>
    {
        await probe.SendAsync(model, key, request);
        return null;
    }, token);

    public async Task<T> RunRequestAsync<T>(Func<string, string, CancellationToken, Task<T>> send, CancellationToken token)
    {
        if (!await operation.WaitAsync(0, token)) throw new AiException(AiFailure.Busy);
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(60), clock);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        try
        {
            using var diskLock = AcquireDiskLock();
            var value = await settings.ReadSettingsAsync(request.Token);
            AiSettingsRules.Validate(value);
            if (!value.AiEnabled) throw new AiException(AiFailure.Disabled);
            var key = await secrets.ReadKeyAsync(request.Token);
            if (string.IsNullOrEmpty(key)) throw new AiException(AiFailure.MissingKey);
            request.Token.ThrowIfCancellationRequested();
            if (!await budget.TryReserveRequestAsync(LocalDate, request.Token)) throw new AiException(AiFailure.DailyLimit);
            // Reservation is durable before any bytes are sent. Never refund uncertain,
            // failed or cancelled attempts and never retry a paid request automatically.
            return await send(value.Model!, key, request.Token);
        }
        catch (AiException) { throw; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timer.IsCancellationRequested) { throw new AiException(AiFailure.Timeout); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AiException(AiFailure.LocalStorage); }
        finally { operation.Release(); }
    }

    private FileStream? AcquireDiskLock()
    {
        if (operationLockPath is null) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(operationLockPath))!);
        try { return new FileStream(operationLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new AiException(AiFailure.Busy); }
    }
}
