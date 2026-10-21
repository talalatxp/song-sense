namespace SongSense.Core;

public sealed record OutputLanguage(string Code, string Name);
public static class OutputLanguages
{
    public const string Default = "es";
    public static IReadOnlyList<OutputLanguage> All { get; } = Array.AsReadOnly(new[] {
        new OutputLanguage("es", "Español"), new("en", "Inglés"), new("fr", "Francés"),
        new("de", "Alemán"), new("it", "Italiano"), new("pt", "Portugués") });
    public static bool IsSupported(string? code) => All.Any(language => language.Code == code);
    public static OutputLanguage Get(string code) => All.FirstOrDefault(language => language.Code == code)
        ?? throw new AiException(AiFailure.InvalidSettings);
}
public interface IOutputLanguageSettings
{
    Task<string> ReadOutputLanguageAsync(CancellationToken token = default);
    Task SaveOutputLanguageAsync(string language, CancellationToken token = default);
}
