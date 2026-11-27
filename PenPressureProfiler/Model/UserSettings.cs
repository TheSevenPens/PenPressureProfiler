using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenPressureProfiler.Model;

/// <summary>
/// Small per-user preferences that persist across app restarts, stored as JSON
/// in <c>%LOCALAPPDATA%\PenPressureProfiler\settings.json</c>. Loading and
/// saving never throw: a missing, unreadable or corrupt file just yields
/// defaults, and a failed save is ignored (a preference is not worth a crash).
/// </summary>
public sealed class UserSettings
{
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PenPressureProfiler", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The COM port the user last picked for the scale, e.g. "COM3".
    /// May name a port that no longer exists (device unplugged).</summary>
    [JsonPropertyName("lastComPort")]
    public string? LastComPort { get; set; }

    public static UserSettings Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new UserSettings();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new UserSettings();
        }
        catch (Exception)
        {
            return new UserSettings();
        }
    }

    public void Save(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Best effort: losing a remembered preference is harmless.
        }
    }
}
