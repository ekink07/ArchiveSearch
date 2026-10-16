using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArsivAra.App;

/// <summary>
/// Windows file type icons (as in Explorer: folder, Word, Photos…). The file does not need to exist on disk;
/// the icon is looked up from the extension (SHGFI_USEFILEATTRIBUTES). For large sizes, the 48 px (ExtraLarge) and
/// 256 px (Jumbo) versions are taken from the system image list. Results are cached per extension + size.
/// Must be called from the UI thread.
/// </summary>
public static class IconProvider
{
    static readonly Dictionary<(string Key, int List), ImageSource?> Cache = new();

    const uint SHGFI_SYSICONINDEX = 0x4000;
    const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    const int SHIL_LARGE = 0, SHIL_SMALL = 1, SHIL_EXTRALARGE = 2, SHIL_JUMBO = 4;
    const int ILD_TRANSPARENT = 0x1;

    /// <summary>The icon best suited to the requested pixel size (WPF scales it if needed).</summary>
    public static ImageSource? For(string fileName, bool isDirectory, double pixelSize)
    {
        string key = isDirectory ? "<folder>" : System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        if (key.Length == 0) key = "<no extension>";
        int list = pixelSize <= 16 ? SHIL_SMALL : pixelSize <= 32 ? SHIL_LARGE : pixelSize <= 48 ? SHIL_EXTRALARGE : SHIL_JUMBO;

        if (Cache.TryGetValue((key, list), out var cached)) return cached;
        var icon = Load(key, isDirectory, list);
        Cache[(key, list)] = icon;
        return icon;
    }

    static ImageSource? Load(string key, bool isDirectory, int list)
    {
        try
        {
            var info = new SHFILEINFO();
            string probe = isDirectory ? "klasor" : "dosya" + (key.StartsWith('<') ? "" : key);
            uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            if (SHGetFileInfo(probe, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES) == 0)
                return null;

            var iid = typeof(IImageList).GUID;
            if (SHGetImageList(list, ref iid, out var imageList) != 0 || imageList == null) return null;
            nint hIcon = 0;
            imageList.GetIcon(info.iIcon, ILD_TRANSPARENT, ref hIcon);
            if (hIcon == 0) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DestroyIcon(hIcon); }
        }
        catch (Exception) { return null; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    static extern int SHGetImageList(int list, ref Guid iid, out IImageList? imageList);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(nint hIcon);

    // Only GetIcon is used; the preceding methods are declared to preserve the vtable order.
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageList
    {
        [PreserveSig] int Add(nint hbmImage, nint hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, nint hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, nint hbmImage, nint hbmMask);
        [PreserveSig] int AddMasked(nint hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(nint pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref nint picon);
    }
}
