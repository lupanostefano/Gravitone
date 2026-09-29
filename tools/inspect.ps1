# Prints the work area and Gravitone's visible windows (physical pixels).
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class WI {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Ri, B; }
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int a, int b, ref R r, int c);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool EnumWindows(P p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public static List<string> List(uint pid) { var res = new List<string>(); EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==pid && IsWindowVisible(h)){ var t=new StringBuilder(256); GetWindowText(h,t,256); var c=new StringBuilder(256); GetClassName(h,c,256); R r; GetWindowRect(h,out r); res.Add("'"+t+"' ["+c+"] "+r.L+","+r.T+","+r.Ri+","+r.B);} return true;}, IntPtr.Zero); return res; }
}
"@
[WI]::SetProcessDPIAware() | Out-Null
$r = New-Object WI+R
[WI]::SystemParametersInfo(0x30, 0, [ref]$r, 0) | Out-Null
"work area: $($r.L),$($r.T) - $($r.Ri),$($r.B)"
$p = Get-Process Gravitone -ErrorAction SilentlyContinue
if ($p) { [WI]::List($p.Id) } else { "Gravitone not running" }
