Add-Type @"
using System; using System.Runtime.InteropServices;
public static class S {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  // POWERBROADCAST_SETTING for GUID_CONSOLE_DISPLAY_STATE with the given state
  public static IntPtr Display(int state) {
    IntPtr p = Marshal.AllocHGlobal(24);
    Marshal.Copy(new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47").ToByteArray(), 0, p, 16);
    Marshal.WriteInt32(p, 16, 4); Marshal.WriteInt32(p, 20, state); return p;
  }
}
"@
$h = [S]::FindWindow([NullString]::Value, "Gravitone")
"dock hwnd $h"
function Send($m, $w, $l = [IntPtr]::Zero) { [void][S]::SendMessage($h, $m, [IntPtr]$w, $l); Start-Sleep -Milliseconds 400 }
Send 0x2B1 7            # lock
Send 0x218 0x8013 ([S]::Display(0))   # screen off
Send 0x218 4            # suspend
Send 0x218 0x12         # resume automatic
Send 0x218 0x8013 ([S]::Display(1))   # screen on
Send 0x2B1 8            # unlock
Send 0x2B1 2            # console disconnect (user switch)
Send 0x2B1 1            # console connect
Send 0x1E 0             # time change

