using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerPDF.Services
{
    // ============================================================
    // File-type (shell) icons - split out of FileOperations.cs
    // (KillerUI refactor). Same name as the KillerUI kit's
    // Services/ShellIcons.cs, which the family file picker uses, so
    // the picker rollout lands on a familiar shape.
    //
    // Cached per extension. Uses SHGFI_USEFILEATTRIBUTES so the
    // icon resolves from the extension alone - works even when the
    // file is missing, and never touches the file on disk.
    // ============================================================
    internal static class ShellIcons
    {
        private static readonly Dictionary<string, ImageSource?> _shellIconCache = new(System.StringComparer.OrdinalIgnoreCase);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        internal static ImageSource? GetShellIcon(string path)
        {
            string ext = System.IO.Path.GetExtension(path) ?? "";
            if (_shellIconCache.TryGetValue(ext, out var hit)) return hit;

            const uint SHGFI_ICON = 0x000000100, SHGFI_LARGEICON = 0x000000000, SHGFI_USEFILEATTRIBUTES = 0x000000010;
            const uint FILE_ATTRIBUTE_NORMAL = 0x80;
            ImageSource? src = null;
            try
            {
                var info = new SHFILEINFO();
                IntPtr res = SHGetFileInfo("file" + ext, FILE_ATTRIBUTE_NORMAL, ref info,
                    (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
                if (res != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    DestroyIcon(info.hIcon);
                }
            }
            catch { /* no icon available - the row simply shows none */ }
            _shellIconCache[ext] = src;
            return src;
        }
    }
}
