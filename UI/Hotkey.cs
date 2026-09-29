using System.Windows.Input;
using Gravitone.Core;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.UI;

/// <summary>A system-wide shortcut such as "Ctrl+Alt+Shift+B", delivered as WM_HOTKEY to a window.</summary>
internal sealed class Hotkey : IDisposable
{
    readonly IntPtr _hwnd;

    Hotkey(IntPtr hwnd, int id, string text)
    {
        _hwnd = hwnd;
        Id = id;
        Text = text;
    }

    public int Id { get; }
    public string Text { get; }

    /// <summary>Registers the shortcut, or returns null (and logs why) if it is invalid or taken.</summary>
    public static Hotkey? Register(IntPtr hwnd, int id, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint modifiers = MOD_NOREPEAT;
        Key key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": modifiers |= MOD_WIN; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key)) key = Key.None;
                    break;
            }
        }

        int vk = key == Key.None ? 0 : KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            Log.Info($"Invalid shortcut: {text}");
            return null;
        }
        if (!RegisterHotKey(hwnd, id, modifiers, (uint)vk))
        {
            Log.Info($"Shortcut {text} is already used by another app");
            return null;
        }
        return new Hotkey(hwnd, id, text);
    }

    public void Dispose() => UnregisterHotKey(_hwnd, Id);
}
