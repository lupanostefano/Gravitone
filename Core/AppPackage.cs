using System.Runtime.InteropServices;
using System.Text;

namespace Gravitone.Core;

/// <summary>Whether Gravitone runs from its MSIX package (Microsoft Store) rather than from a normal install.</summary>
internal static class AppPackage
{
    const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

    public static bool IsPackaged { get; } = Check();

    static bool Check()
    {
        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
        catch (Exception)
        {
            return false; // the function is missing: an old Windows, so certainly not packaged
        }
    }
}
