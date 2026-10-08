using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Brushes = System.Windows.Media.Brushes;

namespace Pegline
{
    internal sealed class PreparedImage
    {
        public BitmapSource Thumbnail;
        public string Hash;
        public int Width, Height;
    }
    internal static class Images
    {
        public const string OwnClipboardFormat = "Pegline.OwnCopy.v1";
        public const long MaxFileBytes = 256L * 1024 * 1024;
        public const long MaxPixels = 80000000;
        public static bool SupportedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string e; try { e = Path.GetExtension(path).ToLowerInvariant(); } catch (ArgumentException) { return false; }
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp" || e == ".gif" || e == ".tif" || e == ".tiff" || e == ".heic" || e == ".heif" || e == ".webp";
        }
        public static bool CanOverwrite(string path)
        {
            string e = Path.GetExtension(path).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp";
        }
        public static byte[] ReadBytes(string path) { return SafeFiles.ReadBounded(path, MaxFileBytes); }
        public static BitmapSource Load(string path, int maxDimension) { return Decode(ReadBytes(path), maxDimension); }
        public static PreparedImage Prepare(string path, BitmapSource supplied)
        {
            BitmapSource full = supplied ?? Load(path, 0);
            return new PreparedImage { Thumbnail = Thumbnail(full, 480), Hash = Fingerprint(full), Width = full.PixelWidth, Height = full.PixelHeight };
        }
        public static BitmapSource Decode(byte[] bytes, int maxDimension)
        {
            if (bytes == null || bytes.Length == 0 || bytes.LongLength > MaxFileBytes) throw new IOException("This image exceeds the 256 MB safety limit.");
            using (var stream = new MemoryStream(bytes, false))
            {
                // Inspect dimensions before requesting a full OnLoad decode. Codec parsers
                // still run here; the limit is resource hardening, not a codec sandbox.
                var probe = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                if (probe.Frames.Count == 0) throw new IOException("The image contains no decodable frames.");
                int width = probe.Frames[0].PixelWidth, height = probe.Frames[0].PixelHeight;
                if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) throw new IOException("This image exceeds the 80 megapixel safety limit.");
                var decoder = probe;
                BitmapSource source;
                int orientation = 1;
                try
                {
                    var metadata = decoder.Frames[0].Metadata as BitmapMetadata;
                    object value = null;
                    // WIC codecs may reject a query path rather than returning null.
                    // A JPEG-specific query must not prevent the TIFF fallback.
                    if (metadata != null)
                    {
                        try { value = metadata.GetQuery("/app1/ifd/{ushort=274}"); } catch { }
                        if (value == null) try { value = metadata.GetQuery("/ifd/{ushort=274}"); } catch { }
                    }
                    if (value != null) orientation = Convert.ToInt32(value);
                }
                catch { }
                stream.Position = 0;
                var bitmap = new BitmapImage(); bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.StreamSource = stream;
                if (maxDimension > 0 && Math.Max(width, height) > maxDimension)
                { if (width >= height) bitmap.DecodePixelWidth = maxDimension; else bitmap.DecodePixelHeight = maxDimension; }
                bitmap.EndInit(); bitmap.Freeze(); source = bitmap;
                source = ApplyOrientation(source, orientation);
                if (maxDimension > 0 && Math.Max(source.PixelWidth, source.PixelHeight) > maxDimension)
                {
                    double factor = (double)maxDimension / Math.Max(source.PixelWidth, source.PixelHeight);
                    source = new TransformedBitmap(source, new ScaleTransform(factor, factor));
                }
                source.Freeze();
                return maxDimension > 0 ? Thumbnail(source, maxDimension) : source;
            }
        }
        internal static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
        {
            if (orientation >= 2 && orientation <= 8)
            {
                var transform = new TransformGroup();
                if (orientation == 2 || orientation == 4 || orientation == 5 || orientation == 7) transform.Children.Add(new ScaleTransform(-1, 1));
                double angle = orientation == 3 || orientation == 4 ? 180 : orientation == 6 || orientation == 7 ? 90 : orientation == 5 || orientation == 8 ? 270 : 0;
                if (angle != 0) transform.Children.Add(new RotateTransform(angle));
                source = new TransformedBitmap(source, transform);
            }
            source.Freeze(); return source;
        }
        // Used ONLY for the Win32 DIB fallback. PNG alpha must never be repaired.
        internal static bool RepairDibAlpha(byte[] pixels, bool opaqueFormat)
        {
            if (pixels == null || pixels.Length % 4 != 0) throw new ArgumentException("Expected packed BGRA pixels.");
            bool allZero = true;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { allZero = false; break; }
            if (opaqueFormat || allZero) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return opaqueFormat || allZero;
        }
        internal static BitmapSource FromDib(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0 || (long)bitmap.Width * bitmap.Height > MaxPixels)
                throw new IOException("Clipboard image exceeds the pixel safety limit.");
            var format = bitmap.PixelFormat;
            bool native32 = format == System.Drawing.Imaging.PixelFormat.Format32bppArgb || format == System.Drawing.Imaging.PixelFormat.Format32bppPArgb || format == System.Drawing.Imaging.PixelFormat.Format32bppRgb;
            // Do not draw into a new Bitmap before looking at alpha: GDI+ may
            // destroy a producer's nonzero RGB values when all alpha bytes are 0.
            var bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly,
                native32 ? format : System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int stride = checked(bitmap.Width * 4);
            byte[] pixels = new byte[checked(stride * bitmap.Height)];
            try
            {
                for (int y = 0; y < bitmap.Height; y++) Marshal.Copy(IntPtr.Add(bits.Scan0, checked(y * bits.Stride)), pixels, y * stride, stride);
            }
            finally { bitmap.UnlockBits(bits); }
            bool repaired = RepairDibAlpha(pixels, format == System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            var pixelFormat = !repaired && format == System.Drawing.Imaging.PixelFormat.Format32bppPArgb ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, pixelFormat, null, pixels, stride); source.Freeze(); return source;
        }
        public static BitmapSource Thumbnail(BitmapSource image, int size)
        {
            if (Math.Max(image.PixelWidth, image.PixelHeight) > size)
            {
                double scale = (double)size / Math.Max(image.PixelWidth, image.PixelHeight);
                image = new TransformedBitmap(image, new ScaleTransform(scale, scale)); image.Freeze();
            }
            // Copy the small pixels so a thumbnail does not retain a full-resolution parent.
            int stride; byte[] bytes = Pixels(image, out stride);
            var thumb = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null, bytes, stride); thumb.Freeze(); return thumb;
        }
        public static BitmapSource FromGdi(Bitmap bitmap)
        {
            using (var s = new MemoryStream()) { bitmap.Save(s, ImageFormat.Png); return Decode(s.ToArray(), 0); }
        }
        public static Bitmap ToGdi(BitmapSource source)
        {
            using (var s = new MemoryStream(Encode(source, ".png")))
            using (var image = new Bitmap(s)) return new Bitmap(image);
        }
        public static byte[] Pixels(BitmapSource image, out int stride)
        {
            if (image == null || (long)image.PixelWidth * image.PixelHeight > MaxPixels) throw new IOException("The image exceeds the pixel safety limit.");
            if (image.Format != PixelFormats.Bgra32) { image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); image.Freeze(); }
            stride = checked(image.PixelWidth * 4);
            byte[] bytes = new byte[checked(stride * image.PixelHeight)]; image.CopyPixels(bytes, stride, 0); return bytes;
        }
        public static string Fingerprint(BitmapSource image)
        {
            int stride; var pixels = Pixels(image, out stride);
            using (var sha = SHA256.Create())
            {
                byte[] w = BitConverter.GetBytes(image.PixelWidth), h = BitConverter.GetBytes(image.PixelHeight);
                sha.TransformBlock(w, 0, w.Length, w, 0); sha.TransformBlock(h, 0, h.Length, h, 0);
                sha.TransformFinalBlock(pixels, 0, pixels.Length);
                return BitConverter.ToString(sha.Hash).Replace("-", "");
            }
        }
        public static byte[] Encode(BitmapSource image, string extension)
        {
            BitmapEncoder encoder;
            switch (extension.ToLowerInvariant())
            {
                case ".jpg": case ".jpeg":
                    // JPEG has no alpha: composite onto white deliberately, not silently black.
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); }
                    var jpeg = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32); jpeg.Render(visual); jpeg.Freeze(); image = jpeg;
                    encoder = new JpegBitmapEncoder { QualityLevel = 95 }; break;
                case ".bmp": encoder = new BmpBitmapEncoder(); break;
                case ".tif": case ".tiff": encoder = new TiffBitmapEncoder(); break;
                case ".gif": encoder = new GifBitmapEncoder(); break;
                case ".png": encoder = new PngBitmapEncoder(); break;
                default: throw new NotSupportedException("Save this format as PNG instead.");
            }
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
        }
        public static BitmapSource ReadClipboard()
        {
            var data = System.Windows.Clipboard.GetDataObject();
            if (data == null || data.GetDataPresent(OwnClipboardFormat)) return null;
            if (data.GetDataPresent("PNG"))
            {
                object value = data.GetData("PNG"); var stream = value as MemoryStream; var bytes = value as byte[];
                if (stream != null) return Decode(stream.ToArray(), 0);
                if (bytes != null) return Decode(bytes, 0);
            }
            if (!Forms.Clipboard.ContainsImage()) return null;
            using (var image = Forms.Clipboard.GetImage())
            {
                if (image == null) return null;
                if ((long)image.Width * image.Height > MaxPixels) throw new IOException("Clipboard image exceeds the 80 megapixel limit.");
                var bitmap = image as Bitmap;
                if (bitmap != null) return FromDib(bitmap);
                using (var raster = new Bitmap(image)) return FromDib(raster);
            }
        }
        public static void PutClipboard(BitmapSource image, string path)
        {
            var data = new System.Windows.DataObject();
            data.SetData("PNG", new MemoryStream(Encode(image, ".png")));
            data.SetImage(image);
            if (path != null) data.SetFileDropList(new StringCollection { path });
            data.SetData(OwnClipboardFormat, new MemoryStream(new byte[] { 1 }));
            // Explicitly request that Windows not upload this copy to Cloud Clipboard.
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
            System.Windows.Clipboard.SetDataObject(data, true);
        }
    }
}
