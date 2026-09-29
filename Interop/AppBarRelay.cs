using System.Runtime.InteropServices;
using Gravitone.Core;
using MsNative = ManagedShell.Interop.NativeMethods;

namespace Gravitone.Interop;

/// <summary>
/// App bar requests (ours and every other app's) reach the menu bar's tray window first, since
/// apps look the taskbar up by class. The tray service forwards them to Explorer unchanged, which
/// works for most of them but not for the three that pass a rectangle through shared memory:
/// Explorer cannot open memory shared with another process. Those are re-shared with Explorer,
/// sent on, and the answer copied back (the same approach as ManagedShell's own app bar manager).
/// </summary>
internal static class AppBarRelay
{
    public static IntPtr Handle(MsNative.APPBARMSGDATAV3 amd, ref bool handled)
    {
        var message = (MsNative.ABMsg)amd.dwMessage;
        if (message is not (MsNative.ABMsg.ABM_QUERYPOS or MsNative.ABMsg.ABM_SETPOS or MsNative.ABMsg.ABM_GETTASKBARPOS))
            return IntPtr.Zero; // forwarded as is

        var explorerTray = TaskbarState.FindExplorerTray();
        if (explorerTray == IntPtr.Zero) return IntPtr.Zero;
        NativeMethods.GetWindowThreadProcessId(explorerTray, out uint explorerPid);

        int size = Marshal.SizeOf<MsNative.APPBARDATAV2>();
        var sourceData = MsNative.SHLockShared((IntPtr)amd.hSharedMemory, (uint)amd.dwSourceProcessId);
        if (sourceData == IntPtr.Zero) return IntPtr.Zero;
        var shared = MsNative.SHAllocShared(IntPtr.Zero, (uint)size, explorerPid);
        var amdBuffer = IntPtr.Zero;
        var copyBuffer = IntPtr.Zero;
        try
        {
            var sharedData = MsNative.SHLockShared(shared, explorerPid);
            if (sharedData == IntPtr.Zero) return IntPtr.Zero;
            Marshal.StructureToPtr(Marshal.PtrToStructure<MsNative.APPBARDATAV2>(sourceData), sharedData, false);
            MsNative.SHUnlockShared(sharedData);

            amd.hSharedMemory = (long)shared;
            amd.dwSourceProcessId = (int)explorerPid;
            amdBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<MsNative.APPBARMSGDATAV3>());
            Marshal.StructureToPtr(amd, amdBuffer, false);
            var copy = new MsNative.COPYDATASTRUCT
            {
                dwData = IntPtr.Zero,
                cbData = Marshal.SizeOf<MsNative.APPBARMSGDATAV3>(),
                lpData = amdBuffer,
            };
            copyBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<MsNative.COPYDATASTRUCT>());
            Marshal.StructureToPtr(copy, copyBuffer, false);

            var result = NativeMethods.SendMessage(explorerTray, WM_COPYDATA, (IntPtr)amd.abd.hWnd, copyBuffer);
            handled = true;

            // Explorer writes its answer (the granted rectangle) into the shared block.
            var answer = MsNative.SHLockShared(shared, explorerPid);
            if (answer != IntPtr.Zero)
            {
                Marshal.StructureToPtr(Marshal.PtrToStructure<MsNative.APPBARDATAV2>(answer), sourceData, false);
                MsNative.SHUnlockShared(answer);
            }
            return result;
        }
        finally
        {
            MsNative.SHUnlockShared(sourceData);
            MsNative.SHFreeShared(shared, explorerPid);
            if (amdBuffer != IntPtr.Zero) Marshal.FreeHGlobal(amdBuffer);
            if (copyBuffer != IntPtr.Zero) Marshal.FreeHGlobal(copyBuffer);
        }
    }

    const int WM_COPYDATA = 0x004A;
}
