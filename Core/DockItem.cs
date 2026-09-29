using System.Diagnostics;
using System.Windows.Media;

namespace Gravitone.Core;

internal enum DockItemKind { App, Separator }

internal sealed class DockItem
{
    DockItem()
    {
        Kind = DockItemKind.Separator;
        Config = new DockItemConfig();
        Identity = AppIdentity.None;
    }

    /// <summary>A pinned item from the configuration.</summary>
    public DockItem(DockItemConfig config)
    {
        Kind = DockItemKind.App;
        Pinned = true;
        Config = config;
        if (StartMenu.IsTarget(config.Target))
        {
            IsStart = true;
            Icon = StartMenu.Icon;
            Identity = AppIdentity.None; // no window ever belongs to it
            if (string.IsNullOrWhiteSpace(config.Name)) config.Name = "Start";
            return;
        }
        var expanded = Environment.ExpandEnvironmentVariables(config.Target);
        IsFolder = Directory.Exists(expanded);
        Icon = ShellItems.Icon(config.Target);
        Identity = IsFolder ? AppIdentity.None : AppIdentity.FromTarget(config.Target); // a folder owns no windows
        if (IsFolder && string.IsNullOrWhiteSpace(config.Name))
        {
            config.Name = ShellItems.DisplayName(config.Target) ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(expanded));
        }
        else if (string.IsNullOrWhiteSpace(config.Name))
        {
            // A bare program is named by its file description ("Paint"), not by its file name ("mspaint").
            config.Name = config.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(config.Target)
                ? ResolveName(Identity, Path.GetFileNameWithoutExtension(config.Target))
                : ShellItems.DisplayName(config.Target) ?? Path.GetFileNameWithoutExtension(config.Target);
        }
    }

    /// <summary>A running app that is not pinned: shown after the separator while it has windows.</summary>
    public DockItem(TrackedWindow window)
    {
        Kind = DockItemKind.App;
        Identity = window.Identity;
        var appsFolderTarget = window.Identity.Aumid is { } aumid ? ShellItems.AppsFolder + aumid : null;
        bool registered = appsFolderTarget is not null && ShellItems.Exists(appsFolderTarget);
        var target = registered ? appsFolderTarget! : window.Identity.ExePath ?? "";

        Config = new DockItemConfig { Target = target, Name = ResolveName(window.Identity, window.Title) };
        Icon = target.Length > 0 ? ShellItems.Icon(target) : null;
        Presence = 0; // grows in
    }

    public static DockItem CreateSeparator() => new() { Presence = 0, TargetPresence = 0 };

    /// <summary>The Recycle Bin at the end of the dock.</summary>
    public static DockItem CreateTrash()
    {
        var item = new DockItem(trash: true);
        item.Icon = ShellItems.Icon(RecycleBin.ShellPath);
        return item;
    }

    DockItem(bool trash)
    {
        Kind = DockItemKind.App;
        IsTrash = trash;
        Config = new DockItemConfig { Name = Loc.T("Recycle Bin"), Target = RecycleBin.ShellPath };
        Identity = AppIdentity.None;
    }

    public DockItemKind Kind { get; }
    public bool Pinned { get; }
    /// <summary>A placeholder that opens a gap while something is dragged over the dock.</summary>
    public bool IsGhost { get; set; }
    /// <summary>The Start menu button rather than an app.</summary>
    public bool IsStart { get; }
    /// <summary>A folder shown as a stack, right of the running apps.</summary>
    public bool IsFolder { get; }
    /// <summary>The Recycle Bin.</summary>
    public bool IsTrash { get; }
    public DockItemConfig Config { get; }
    public AppIdentity Identity { get; }
    public string Name => Config.Name;
    public string Target => Config.Target;
    public ImageSource? Icon { get; set; }

    /// <summary>
    /// Loads the icon again if it is missing: at sign-in the shell may not answer yet, and an icon
    /// that failed once would stay empty. Returns true when one was found.
    /// </summary>
    public bool ReloadIcon()
    {
        if (Icon is not null) return false;
        if (Kind != DockItemKind.App || IsStart || Config.Target.Length == 0) return false;
        Icon = ShellItems.Icon(Config.Target);
        return Icon is not null;
    }

    /// <summary>Open windows of this app, most recently used first.</summary>
    public List<TrackedWindow> Windows { get; } = [];
    public bool IsRunning => Windows.Count > 0;

    /// <summary>0 = absent .. 1 = fully in the dock (animated when apps appear / disappear).</summary>
    public double Presence { get; set; } = 1;
    public double TargetPresence { get; set; } = 1;

    public Bouncer Bounce { get; } = new();

    /// <summary>The name an app goes by: its Start menu name, else its file description, else <paramref name="fallback"/>.</summary>
    public static string ResolveName(AppIdentity identity, string fallback)
    {
        if (identity.Aumid is { } aumid && ShellItems.DisplayName(ShellItems.AppsFolder + aumid) is { Length: > 0 } shellName)
            return shellName;
        if (identity.ExePath is { } exe && File.Exists(exe))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName.Trim();
            }
            catch (Exception)
            {
                // Fall back to the file name.
            }
            return Path.GetFileNameWithoutExtension(exe);
        }
        return fallback;
    }
}
