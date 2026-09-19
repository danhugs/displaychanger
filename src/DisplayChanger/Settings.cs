using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisplayChanger;

/// <summary>User preferences persisted to %LocalAppData%\DisplayChanger\settings.json.</summary>
public sealed class Settings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayChanger");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    /// <summary>Set after the first launch has auto-registered the app to start with Windows.</summary>
    public bool FirstRunDone { get; set; }

    /// <summary>
    /// Show the on-screen pane (bottom-right of the primary display) when the primary display or default
    /// audio device changes. Kept under its original JSON name for compatibility with existing settings files.
    /// </summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>Playback endpoint IDs skipped by Win+] cycling. They can still be picked directly from the menu.</summary>
    public HashSet<string> ExcludedOutputIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Recording endpoint IDs skipped by Win+' cycling.</summary>
    public HashSet<string> ExcludedInputIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string? LoadError { get; private set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                if (loaded is not null)
                {
                    // Deserialized sets lose their comparer; endpoint IDs are GUID-based so compare case-insensitively.
                    loaded.ExcludedOutputIds = new HashSet<string>(loaded.ExcludedOutputIds, StringComparer.OrdinalIgnoreCase);
                    loaded.ExcludedInputIds = new HashSet<string>(loaded.ExcludedInputIds, StringComparer.OrdinalIgnoreCase);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            return new Settings { LoadError = ex.Message };
        }
        return new Settings();
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
