using System.Runtime.InteropServices;
using Gravitone.Interop;

namespace Gravitone.Core;

internal readonly record struct RecentItem(string Name, string Path);

/// <summary>
/// The recent documents of an app (the "Recent" part of its jump list), through the documented
/// <c>IApplicationDocumentLists</c>. Custom tasks such as "New incognito window" are stored in an
/// undocumented format and are not available.
/// </summary>
internal static class JumpList
{
    const uint SIGDN_FILESYSPATH = 0x80058000;
    const int ADLT_RECENT = 0;

    [ComImport, Guid("3c594f9f-9f30-47a1-979a-c9e83d3d0a06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationDocumentLists
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [return: MarshalAs(UnmanagedType.Interface)]
        object GetList(int listType, uint itemsDesired, ref Guid riid);
    }

    [ComImport, Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    static readonly Guid ClsidApplicationDocumentLists = new("86bec222-30f2-47e0-9f25-60d11cd75c28");

    /// <summary>Up to <paramref name="max"/> recent documents of the app with this AppUserModelID.</summary>
    public static List<RecentItem> Recent(string? aumid, int max = 5)
    {
        var result = new List<RecentItem>();
        if (string.IsNullOrEmpty(aumid)) return result;
        object? lists = null, array = null;
        try
        {
            lists = Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidApplicationDocumentLists)!);
            var documentLists = (IApplicationDocumentLists)lists!;
            documentLists.SetAppID(aumid);
            var riid = typeof(IObjectArray).GUID;
            array = documentLists.GetList(ADLT_RECENT, (uint)max * 2, ref riid);
            var objects = (IObjectArray)array;
            objects.GetCount(out uint count);
            var itemGuid = typeof(IShellItem).GUID;
            for (uint i = 0; i < count && result.Count < max; i++)
            {
                objects.GetAt(i, ref itemGuid, out var obj);
                try
                {
                    if (obj is not IShellItem item) continue;
                    item.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                    if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path))) continue;
                    item.GetDisplayName(NativeMethods.SIGDN_NORMALDISPLAY, out var name);
                    result.Add(new RecentItem(string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(path) : name, path));
                }
                catch (Exception)
                {
                    // Items without a file system path (or a broken entry) are skipped.
                }
                finally
                {
                    Marshal.ReleaseComObject(obj);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Recent items of {aumid}");
        }
        finally
        {
            if (array is not null) Marshal.ReleaseComObject(array);
            if (lists is not null) Marshal.ReleaseComObject(lists);
        }
        return result;
    }

    public static void Open(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Opening {path}");
        }
    }
}
