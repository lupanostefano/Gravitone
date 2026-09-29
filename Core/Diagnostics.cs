using System.Runtime.InteropServices;

namespace Gravitone.Core;

/// <summary>What the process holds (with <c>--diag</c>, logged every few minutes to spot leaks over long runs).</summary>
internal static class Diagnostics
{
    [DllImport("user32.dll")]
    static extern uint GetGuiResources(IntPtr process, uint flags);

    public static string Resources()
    {
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        return $"privata {p.PrivateMemorySize64 / 1048576} MB, gestita {GC.GetTotalMemory(false) / 1048576} MB, " +
               $"handle {p.HandleCount}, GDI {GetGuiResources(p.Handle, 0)}, USER {GetGuiResources(p.Handle, 1)}, " +
               $"thread {p.Threads.Count}, CPU {p.TotalProcessorTime.TotalSeconds:0.0} s";
    }
}
