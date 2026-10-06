using System.Text.Json;
using System.Text.RegularExpressions;

namespace SongSense.Core;

public sealed record LyricLine(int Id, string Text);

public static partial class InsightValidation
{
    public static IReadOnlyList<LyricLine> Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n')
        .Split('\n').Select((line, index) => new LyricLine(index + 1, line)).ToArray();

    public static SongInsight Parse(long trackRevision, string lyrics, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Object(root, "language", "translation", "summary", "themes", "metaphors", "alternatives", "warnings");
            var language = Text(root.GetProperty("language"), 10);
            if (language == "unknown") throw new AiException(AiFailure.UnknownLanguage);
            if (language != "mixed" && !LanguageCode().IsMatch(language)) Invalid();
            var summary = Text(root.GetProperty("summary"), 3000);
            if (Words().Matches(summary).Count is < 80 or > 150) Invalid();
            var themes = Strings(root.GetProperty("themes"), 1, 5, 200);
            var alternatives = Strings(root.GetProperty("alternatives"), 0, 3, 1500);
            var warnings = Strings(root.GetProperty("warnings"), 0, 10, 800);
            var metaphors = new List<ExplainedMetaphor>();
            var metaphorArray = Array(root.GetProperty("metaphors"), 0, 6);
            foreach (var metaphor in metaphorArray.EnumerateArray())
            {
                Object(metaphor, "text", "explanation");
                var quote = Text(metaphor.GetProperty("text"), 400);
                if (!lyrics.Contains(quote, StringComparison.Ordinal)) Invalid();
                metaphors.Add(new(quote, Text(metaphor.GetProperty("explanation"), 2500)));
            }
            var lines = Lines(lyrics).Where(line => !string.IsNullOrWhiteSpace(line.Text)).ToArray();
            if (lines.Length == 0) throw new AiException(AiFailure.InvalidLyrics);
            var translation = new List<TranslatedLine>();
            var translated = Array(root.GetProperty("translation"), 0, lines.Length);
            foreach (var entry in translated.EnumerateArray())
            {
                Object(entry, "id", "text", "source_language");
                if (!entry.GetProperty("id").TryGetInt32(out var id)) Invalid();
                var sourceLanguage = Text(entry.GetProperty("source_language"), 3);
                if (!LanguageCode().IsMatch(sourceLanguage)) Invalid();
                var translatedText = Text(entry.GetProperty("text"), 20_000);
                if (translatedText.Contains('\n') || translatedText.Contains('\r')) Invalid();
                translation.Add(new(id, translatedText, sourceLanguage));
            }
            if (language == "es")
            {
                if (translation.Count != 0) Invalid();
            }
            else
            {
                if (!translation.Select(line => line.Id).SequenceEqual(lines.Select(line => line.Id))) Invalid();
                if (language == "mixed")
                {
                    if (!translation.Any(line => line.SourceLanguage == "es") || !translation.Any(line => line.SourceLanguage != "es")) Invalid();
                    for (var index = 0; index < lines.Length; index++)
                        if (translation[index].SourceLanguage == "es" && translation[index].Text != lines[index].Text) Invalid();
                }
                else if (translation.Any(line => line.SourceLanguage != language)) Invalid();
            }
            return new(trackRevision, language, translation, summary, themes, metaphors, alternatives, warnings);
        }
        catch (JsonException) { throw new AiException(AiFailure.InvalidResponse); }
        catch (InvalidOperationException) { throw new AiException(AiFailure.InvalidResponse); }
        catch (KeyNotFoundException) { throw new AiException(AiFailure.InvalidResponse); }
    }

    public static string TranslationText(string lyrics, SongInsight insight)
    {
        if (insight.Language == "es") return "";
        var byId = insight.Translation.ToDictionary(line => line.Id, line => line.Text);
        return string.Join('\n', Lines(lyrics).Select(line => byId.TryGetValue(line.Id, out var text) ? text : line.Text));
    }

    private static void Object(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) Invalid();
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != expected.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            !names.ToHashSet(StringComparer.Ordinal).SetEquals(expected)) Invalid();
    }
    private static JsonElement Array(JsonElement value, int minimum, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < minimum || value.GetArrayLength() > maximum) Invalid();
        return value;
    }
    private static string Text(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String) Invalid();
        var text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum) Invalid();
        return text;
    }
    private static IReadOnlyList<string> Strings(JsonElement value, int minimum, int maximum, int textMaximum) =>
        Array(value, minimum, maximum).EnumerateArray().Select(item => Text(item, textMaximum)).ToArray();
    private static void Invalid() => throw new AiException(AiFailure.InvalidResponse);
    [GeneratedRegex(@"\A[a-z]{2,3}\z", RegexOptions.NonBacktracking)]
    private static partial Regex LanguageCode();
    [GeneratedRegex(@"\S+", RegexOptions.NonBacktracking)]
    private static partial Regex Words();
}
