using System.Windows;
using System.Windows.Documents;
using System.Security.Cryptography;
using System.Text;

namespace SongSense.App;

public sealed record ReadingTextAnchor(string Key, int Character);
public sealed record ReadingBookmark(ReadingTextAnchor View, double Top, IReadOnlyList<string> Order,
    ReadingTextAnchor? SelectionStart, ReadingTextAnchor? SelectionEnd, string SelectionText);

/// <summary>Logical paragraph identities survive reflow; offsets only locate text inside a paragraph.</summary>
public static class ReadingAnchors
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached("Key", typeof(string), typeof(ReadingAnchors), new PropertyMetadata(""));
    public static void SetKey(DependencyObject target, string value) => target.SetValue(KeyProperty, value);
    public static string GetKey(DependencyObject target) => (string)target.GetValue(KeyProperty);
    public static IReadOnlyList<(string Key, Paragraph Paragraph)> Index(FlowDocument document)
    {
        var occurrences = new Dictionary<string, int>(); var result = new List<(string, Paragraph)>();
        foreach (var paragraph in Paragraphs(document.Blocks))
        {
            var key = GetKey(paragraph);
            if (key.Length == 0) key = "text:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text)));
            occurrences.TryGetValue(key, out var count); occurrences[key] = count + 1;
            result.Add((key + ":" + count, paragraph));
        }
        return result;
    }
    private static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph) yield return paragraph;
            if (block is Section section) foreach (var nested in Paragraphs(section.Blocks)) yield return nested;
            if (block is List list) foreach (var item in list.ListItems) foreach (var nested in Paragraphs(item.Blocks)) yield return nested;
        }
    }
    public static ReadingTextAnchor? Locate(TextPointer pointer, IReadOnlyList<(string Key, Paragraph Paragraph)> index)
    {
        var paragraph = pointer.Paragraph;
        foreach (var entry in index)
            if (ReferenceEquals(entry.Paragraph, paragraph)) return new(entry.Key, paragraph!.ContentStart.GetOffsetToPosition(pointer));
        return null;
    }
    public static TextPointer? Resolve(ReadingTextAnchor anchor, IReadOnlyList<(string Key, Paragraph Paragraph)> index)
    {
        var paragraph = index.FirstOrDefault(entry => entry.Key == anchor.Key).Paragraph;
        return paragraph?.ContentStart.GetPositionAtOffset(Math.Clamp(anchor.Character, 0, paragraph.ContentStart.GetOffsetToPosition(paragraph.ContentEnd)));
    }
    public static TextPointer? ResolveView(ReadingBookmark bookmark, IReadOnlyList<(string Key, Paragraph Paragraph)> index)
    {
        if (Resolve(bookmark.View, index) is { } exact) return exact;
        var otherRole = bookmark.View.Key.Replace("lyrics:original:", "lyrics:translation:");
        if (otherRole == bookmark.View.Key) otherRole = bookmark.View.Key.Replace("lyrics:translation:", "lyrics:original:");
        if (otherRole != bookmark.View.Key && Resolve(bookmark.View with { Key = otherRole }, index) is { } equivalent) return equivalent;
        var original = bookmark.Order.ToList().IndexOf(bookmark.View.Key);
        foreach (var key in bookmark.Order.Select((key, i) => (key, distance: Math.Abs(i - original))).OrderBy(item => item.distance))
            if (Resolve(new(key.key, 0), index) is { } nearby) return nearby;
        return index.FirstOrDefault().Paragraph?.ContentStart;
    }
}
