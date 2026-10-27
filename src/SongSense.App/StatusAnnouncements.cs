namespace SongSense.App;

/// <summary>Deduplicates semantic states; progress and changing song metadata are not live speech.</summary>
public sealed class StatusAnnouncements
{
    private string? last;
    public void Reset() => last = null;
    public bool Observe(NoticeKind kind, string text, string scope = "")
    {
        if (kind == NoticeKind.Progress) { last = null; return false; }
        if (string.IsNullOrWhiteSpace(text) || kind == NoticeKind.Information && scope.Length == 0) return false;
        var key = $"{kind}:{(scope.Length == 0 ? text : scope)}";
        if (last == key) return false;
        last = key; return true;
    }
}
