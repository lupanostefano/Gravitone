# Reads "Shell IDList Array" from Explorer's own data object for a shell item and decodes it like ShellItems.FromIdListArray.
param([string]$Item = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms @"
using System; using System.Collections.Generic; using System.IO; using System.Runtime.InteropServices; using System.Windows.Forms;
[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItem { void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv); }
public static class CI {
  [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
  static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
  [DllImport("shell32.dll")] static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
  [DllImport("shell32.dll")] static extern void ILFree(IntPtr pidl);
  [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdn, out IntPtr name);
  public static string Run(string item) {
    Guid iidItem = typeof(IShellItem).GUID; object o;
    SHCreateItemFromParsingName(item, IntPtr.Zero, ref iidItem, out o);
    Guid bhid = new Guid("B8C0BD9F-ED24-455c-83E6-D5390C4FE8C4"); Guid iidData = new Guid("0000010e-0000-0000-C000-000000000046");
    IntPtr p; ((IShellItem)o).BindToHandler(IntPtr.Zero, ref bhid, ref iidData, out p);
    var data = new DataObject((System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(p));
    var formats = string.Join(", ", data.GetFormats());
    var ms = data.GetData("Shell IDList Array") as MemoryStream;
    if (ms == null) return "formati: " + formats + " -> nessun Shell IDList Array";
    var cida = ms.ToArray(); var names = new List<string>();
    IntPtr mem = Marshal.AllocHGlobal(cida.Length); Marshal.Copy(cida, 0, mem, cida.Length);
    int count = Marshal.ReadInt32(mem); IntPtr parent = mem + Marshal.ReadInt32(mem, 4);
    for (int i = 0; i < count; i++) {
      IntPtr abs = ILCombine(parent, mem + Marshal.ReadInt32(mem, 8 + i * 4)); IntPtr name;
      if (SHGetNameFromIDList(abs, 0x80028000, out name) == 0) { names.Add(Marshal.PtrToStringUni(name)); Marshal.FreeCoTaskMem(name); }
      ILFree(abs);
    }
    Marshal.FreeHGlobal(mem);
    return "formati: " + formats + " -> " + string.Join(" | ", names);
  }
}
"@
[CI]::Run($Item)
