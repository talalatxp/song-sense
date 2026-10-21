using System.Text.Json;
using SongSense.Core;

namespace SongSense.Infrastructure;

// Only window geometry and display preferences; no song, identity or credentials.
public sealed class WindowPreferenceStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SongSense", "window-preferences.json");
    public WindowPreferences Read()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16384) return new();
            return WindowPlacement.Normalize(JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(path)) ?? new());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public bool Save(WindowPreferences preferences)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(WindowPlacement.Normalize(preferences)));
            File.Move(temporary, path, true); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
