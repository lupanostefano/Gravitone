using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gravitone.Core;

internal enum DockEdge { Bottom, Left, Right }

internal sealed class DockSettings
{
    /// <summary>Resting icon size, in device-independent pixels.</summary>
    public double IconSize { get; set; } = 48;
    public bool Magnification { get; set; } = true;
    public double MagnifiedSize { get; set; } = 80;
    public bool AutoHide { get; set; }
    public DockEdge Edge { get; set; } = DockEdge.Bottom;

    /// <summary>Hide the Windows taskbar while the dock runs (it always comes back when the dock ends).</summary>
    public bool ReplaceTaskbar { get; set; } = true;

    /// <summary>Shows / hides the Windows taskbar again while the dock replaces it.</summary>
    public string TaskbarHotkey { get; set; } = "Ctrl+Alt+Shift+B";

    /// <summary>Start Gravitone at sign-in (Run key). On by default: the dock replaces the taskbar.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Win+1 … Win+9 start / activate the dock's apps (taken away from Explorer while the dock runs).</summary>
    public bool NumberShortcuts { get; set; } = true;

    /// <summary>The macOS-style menu bar along the top of the screen (clock, tray icons, active app).</summary>
    public bool MenuBar { get; set; } = true;

    /// <summary>Interface language: "" follows Windows, otherwise "en" or "it".</summary>
    public string Language { get; set; } = "";

    /// <summary>The "genie" effect: windows flow into their dock icon when minimized and out of it when restored.</summary>
    public bool Genie { get; set; } = true;

    /// <summary>A menu bar on every monitor, as on macOS (the tray icons stay on the primary one).</summary>
    public bool MenuBarOnAllDisplays { get; set; } = true;

    /// <summary>The monitor the dock lives on (<c>\\.\DISPLAY2</c>); empty = the primary monitor, whichever it is.</summary>
    public string Display { get; set; } = "";

    /// <summary>
    /// As on macOS: pushing the pointer against the dock's edge of another monitor brings the dock there
    /// (until the next start, when it goes back to <see cref="Display"/>).
    /// </summary>
    public bool FollowPointer { get; set; } = true;

    public void Clamp()
    {
        IconSize = Math.Clamp(IconSize, 16, 128);
        MagnifiedSize = Math.Clamp(MagnifiedSize, IconSize, 256);
    }
}

internal sealed class DockItemConfig
{
    public string Name { get; set; } = "";

    /// <summary>A file, folder or shortcut path, or a shell parsing name such as <c>shell:AppsFolder\&lt;AUMID&gt;</c>.</summary>
    public string Target { get; set; } = "";
    public string? Arguments { get; set; }

    /// <summary>Launch the app when Gravitone starts at sign-in ("Open at Login").</summary>
    public bool OpenAtStart { get; set; }
}

internal sealed class DockConfig
{
    const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public DockSettings Settings { get; set; } = new();
    public List<DockItemConfig> Items { get; set; } = [];

    /// <summary>Folders shown as stacks, between the running apps and the Recycle Bin.</summary>
    public List<DockItemConfig> Folders { get; set; } = [new DockItemConfig { Target = KnownFolders.Downloads }];

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gravitone");

    public static string FilePath => Path.Combine(Folder, "config.json");

    /// <summary>
    /// The data folder of the first versions (called WinDock): its configuration is taken over once, so an
    /// existing dock keeps its apps and settings. Nothing is deleted there.
    /// </summary>
    static void MigrateLegacyFolder()
    {
        try
        {
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinDock", "config.json");
            if (File.Exists(FilePath) || !File.Exists(legacy)) return;
            Directory.CreateDirectory(Folder);
            File.Copy(legacy, FilePath);
            Log.Info("Configuration taken over from the first version's data folder");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Migrating the configuration");
        }
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static DockConfig LoadOrCreate()
    {
        MigrateLegacyFolder();
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<DockConfig>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null)
                {
                    loaded.Settings ??= new();
                    loaded.Items ??= [];
                    loaded.Folders ??= [];
                    loaded.Settings.Clamp();
                    if (loaded.Migrate()) loaded.Save();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the configuration");
        }

        var config = new DockConfig { Items = DefaultApps.Discover() };
        config.Save();
        return config;
    }

    /// <summary>Brings a configuration written by an older version up to date; true if it changed.</summary>
    bool Migrate()
    {
        if (Version >= CurrentVersion) return false;
        // v2: the Start item, first in the dock.
        if (Version < 2 && !Items.Any(i => StartMenu.IsTarget(i.Target))) Items.Insert(0, StartMenu.CreateConfig());
        Version = CurrentVersion;
        return true;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the configuration");
        }
    }
}
