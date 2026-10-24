namespace SongSense.App;

public sealed record ReadingBlock(string Original, string Translation)
{
    public bool HasTranslation => !string.IsNullOrWhiteSpace(Translation);
}
public sealed partial class MainViewModel
{
    private SongSense.Core.ResolvedLyrics? readingLyrics;
    private SongSense.Core.SongInsight? readingInsight;
    private IReadOnlyList<ReadingBlock> readingBlocks = [];
    public IReadOnlyList<ReadingBlock> ReadingBlocks
    {
        get
        {
            if (ReferenceEquals(readingLyrics, PreparedLyrics) && ReferenceEquals(readingInsight, Insight)) return readingBlocks;
            readingLyrics = PreparedLyrics; readingInsight = Insight;
            if (string.IsNullOrWhiteSpace(OriginalLyrics)) return readingBlocks = [];
            var originals = Stanzas(OriginalLyrics);
            var translations = Insight is not null && Insight.Language != Insight.TargetLanguage ? Stanzas(TranslatedLyrics) : [];
            return readingBlocks = originals.Select((text, index) => new ReadingBlock(text, index < translations.Length ? translations[index] : "")).ToArray();
        }
    }
    private static string[] Stanzas(string text) => System.Text.RegularExpressions.Regex.Split(text.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n[\t ]*\n+");
}
