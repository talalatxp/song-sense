namespace SongSense.App;

public sealed record ReadingBlock(string Original, string Translation)
{
    public bool HasTranslation => !string.IsNullOrWhiteSpace(Translation);
}
public sealed partial class MainViewModel
{
    public IReadOnlyList<ReadingBlock> ReadingBlocks
    {
        get
        {
            if (string.IsNullOrWhiteSpace(OriginalLyrics)) return [];
            var originals = Stanzas(OriginalLyrics);
            var translations = Insight is not null && Insight.Language != Insight.TargetLanguage ? Stanzas(TranslatedLyrics) : [];
            return originals.Select((text, index) => new ReadingBlock(text, index < translations.Length ? translations[index] : "")).ToArray();
        }
    }
    private static string[] Stanzas(string text) => System.Text.RegularExpressions.Regex.Split(text.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n[\t ]*\n+");
}
