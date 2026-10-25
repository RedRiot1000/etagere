using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Etagere
{
    // Grandes icônes Windows (256 px quand le programme en fournit) et images de jaquettes
    public static class ShellIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("shell32.dll", EntryPoint = "#727")]
        static extern int SHGetImageList(int list, ref Guid riid, out IntPtr ppv);

        [DllImport("comctl32.dll")]
        static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, int flags);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        const uint SHGFI_SYSICONINDEX = 0x4000;
        const int SHIL_EXTRALARGE = 2, SHIL_JUMBO = 4;
        static Guid IID_IImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");

        public static ImageSource Load(string source)
        {
            if (string.IsNullOrEmpty(source) || source.Contains("://")) return null;
            try
            {
                var ext = Path.GetExtension(source).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp") return LoadImage(source, 128);
                if (ext == ".ico" && File.Exists(source)) return LoadIco(source);
                if (File.Exists(source) || Directory.Exists(source)) return FromShell(source);
            }
            catch { }
            return null;
        }

        public static ImageSource LoadImage(string path, int width)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = width;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        static ImageSource LoadIco(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
                best.Freeze();
                return best;
            }
        }

        static ImageSource FromShell(string path)
        {
            var info = new SHFILEINFO();
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_SYSICONINDEX) == IntPtr.Zero) return null;
            var jumbo = FromList(SHIL_JUMBO, info.iIcon);
            // Les vieux programmes n'ont qu'une petite icône collée dans un coin du 256 px : on prend alors le 48 px
            if (jumbo != null && HasContentBeyond(jumbo, 48)) return jumbo;
            return FromList(SHIL_EXTRALARGE, info.iIcon) ?? jumbo;
        }

        static BitmapSource FromList(int list, int index)
        {
            IntPtr himl;
            if (SHGetImageList(list, ref IID_IImageList, out himl) != 0 || himl == IntPtr.Zero) return null;
            var hIcon = ImageList_GetIcon(himl, index, 1);
            if (hIcon == IntPtr.Zero) return null;
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
            finally { DestroyIcon(hIcon); }
        }

        static bool HasContentBeyond(BitmapSource bmp, int size)
        {
            var fc = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = fc.PixelWidth, h = fc.PixelHeight;
            if (w <= size && h <= size) return false;
            var px = new byte[w * h * 4];
            fc.CopyPixels(px, w * 4, 0);
            for (int y = 0; y < h; y++)
                for (int x = (y < size ? size : 0); x < w; x++)
                    if (px[(y * w + x) * 4 + 3] > 8) return true;
            return false;
        }
    }
}
