using System.ComponentModel;
using SongSense.Core;

namespace SongSense.App;

// Session-only activity. No retrospective history and no credentials or lyrics.
public sealed class AiActivityViewModel : INotifyPropertyChanged
{
    public AiUsagePresentation? Last { get; private set; }
    private DateOnly? counterDate;
    private int? count;
    private int limit;
    public string LastActivity => Last is not { } last ? "Sin actividad conocida en esta sesión." :
        $"Última actividad · {last.At:dd/MM/yyyy HH:mm:ss zzz}\n{last.Context?.Operation} · {last.Context?.Subject}\n" +
        $"{(last.Context?.Mode == AiConnectionMode.ApiKey ? "API key · de pago" : "ChatGPT")} · {last.Context?.Model} · {last.Context?.Language}\n{last.Description}";
    public string Counter => counterDate is not { } date || count is null
        ? "Contador local no disponible."
        : $"Contador local · {date:dd/MM/yyyy}: {count} / {limit} solicitudes reservadas. Fecha local de Windows; no representa los límites del plan ni euros.";
    public void Record(AiUsagePresentation activity)
    {
        // A late completion may update its own activity, but cannot replace a newer operation.
        if (Last?.Sequence > activity.Sequence) return;
        Last = activity;
        PropertyChanged?.Invoke(this, new(nameof(Last)));
        PropertyChanged?.Invoke(this, new(nameof(LastActivity)));
    }
    public void UpdateCounter(DateOnly date, int? value, int dailyLimit)
    {
        counterDate = date; count = value; limit = dailyLimit;
        PropertyChanged?.Invoke(this, new(nameof(Counter)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
