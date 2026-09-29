using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Gravitone.Core;

/// <summary>
/// Win+1 … Win+9 for the dock's apps, like the taskbar's. Explorer owns those keys, so they cannot be
/// registered as hotkeys: a low-level keyboard hook sees them first and swallows them.
/// </summary>
internal sealed class NumberShortcuts : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    const ushort VK_UNASSIGNED = 0xE8;

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public UIntPtr extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public ushort vk, scan;
        public uint flags, time;
        public UIntPtr extra;
        public uint pad1, pad2; // the union is as large as MOUSEINPUT
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam, lParam;
        public uint time;
        public int x, y;
    }

    const int WM_QUIT = 0x0012;

    [DllImport("user32.dll")]
    static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    static extern bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    readonly HookProc _proc; // kept alive: native code calls it
    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly Action<int> _pressed;
    readonly HashSet<uint> _swallowed = []; // only touched on the hook thread
    readonly Thread _thread;
    uint _threadId;
    IntPtr _hook;

    /// <param name="pressed">Called (on the UI thread) with 1..9.</param>
    public NumberShortcuts(Action<int> pressed)
    {
        _pressed = pressed;
        _proc = OnKey;
        // Every key press of the whole system waits for this hook. On the UI thread, anything slow there
        // (loading an icon, a shell call) would make typing lag everywhere, and Windows silently drops a
        // hook that is too slow. So it gets a thread that does nothing else.
        var ready = new ManualResetEventSlim(); // not disposed: the thread may still set it after a timed-out wait
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            ready.Set();
            if (_hook == IntPtr.Zero)
            {
                Log.Info("Win+1…9: keyboard hook not installed");
                return;
            }
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { } // the hook runs inside GetMessage
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "Gravitone Win+number",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        // Runs on every key press system-wide: keep it tiny (Windows drops slow hooks).
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (key.vkCode != VK_UNASSIGNED) // not the key sent below
            {
                int message = (int)wParam;
                if ((message == WM_KEYDOWN || message == WM_SYSKEYDOWN) && key.vkCode is >= '1' and <= '9' && WinOnly())
                {
                    _swallowed.Add(key.vkCode);
                    // Without another key in between, releasing Win would open the Start menu.
                    SendUnassignedKey();
                    int number = (int)key.vkCode - '0';
                    _dispatcher.BeginInvoke(() => _pressed(number));
                    return (IntPtr)1;
                }
                if ((message == WM_KEYUP || message == WM_SYSKEYUP) && _swallowed.Remove(key.vkCode)) return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    static bool WinOnly() =>
        ((GetAsyncKeyState(VK_LWIN) | GetAsyncKeyState(VK_RWIN)) & 0x8000) != 0
        && (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0
        && (GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0
        && (GetAsyncKeyState(VK_MENU) & 0x8000) == 0;

    static void SendUnassignedKey()
    {
        var inputs = new INPUT[2];
        inputs[0] = new INPUT { type = 1, vk = VK_UNASSIGNED };
        inputs[1] = new INPUT { type = 1, vk = VK_UNASSIGNED, flags = 2 /* KEYEVENTF_KEYUP */ };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(1));
    }
}
