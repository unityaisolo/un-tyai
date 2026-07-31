using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// PANODAN GÖRSEL OKUMA (Ctrl+V).
    ///
    /// Unity'nin API'si panodan sadece METİN verir (EditorGUIUtility.systemCopyBuffer).
    /// Ekran görüntüsü aldığında (PrtScn, Win+Shift+S, Snipping Tool) pano bir BİTMAP tutar.
    /// Burada Windows panosunu doğrudan okuyup Texture2D'ye çeviriyoruz.
    ///
    /// Güvenlik: pano belleği yalnızca KİLİTLENİP okunur, asla serbest bırakılmaz
    /// (sahibi pano); her yol try/finally ile kapatılır. Windows dışında sessizce false döner.
    /// </summary>
    public static class NovaClipboard
    {
        private const uint CF_DIB = 8;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr GlobalSize(IntPtr hMem);

        /// <summary>Panoda görsel varsa Texture2D olarak verir. Görsel yoksa false.</summary>
        public static bool TryGetImage(out Texture2D tex)
        {
            tex = null;
            // 1) Panoda dosya YOLU olabilir (Explorer'da kopyalanmış .png gibi)
            if (TryFromPathInClipboard(out tex)) return true;

#if UNITY_EDITOR_WIN
            try { return TryFromDib(out tex); }
            catch (Exception e) { Debug.LogWarning("[Nova] Pano görseli okunamadı: " + e.Message); return false; }
#else
            return false;
#endif
        }

        private static bool TryFromPathInClipboard(out Texture2D tex)
        {
            tex = null;
            string s = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim().Trim('"');
            if (s.Length > 400 || s.IndexOf('\n') >= 0) return false;

            string ext = Path.GetExtension(s).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return false;
            if (!File.Exists(s)) return false;

            var t = new Texture2D(2, 2);
            if (!t.LoadImage(File.ReadAllBytes(s))) return false;
            tex = t;
            return true;
        }

#if UNITY_EDITOR_WIN
        private static bool TryFromDib(out Texture2D tex)
        {
            tex = null;
            if (!IsClipboardFormatAvailable(CF_DIB)) return false;
            if (!OpenClipboard(IntPtr.Zero)) return false;

            IntPtr hMem = IntPtr.Zero, ptr = IntPtr.Zero;
            try
            {
                hMem = GetClipboardData(CF_DIB);
                if (hMem == IntPtr.Zero) return false;

                ptr = GlobalLock(hMem);
                if (ptr == IntPtr.Zero) return false;

                int size = (int)GlobalSize(hMem).ToUInt64();
                if (size < 40) return false;
                var buf = new byte[size];
                Marshal.Copy(ptr, buf, 0, size);

                // BITMAPINFOHEADER
                int headerSize = BitConverter.ToInt32(buf, 0);
                int width = BitConverter.ToInt32(buf, 4);
                int height = BitConverter.ToInt32(buf, 8);
                short bitCount = BitConverter.ToInt16(buf, 14);
                int compression = BitConverter.ToInt32(buf, 16);

                if (headerSize < 40 || width <= 0 || width > 8192 || height == 0 || Math.Abs(height) > 8192)
                    return false;
                if (bitCount != 24 && bitCount != 32) return false;   // yalnızca yaygın ekran görüntüsü formatları
                if (compression != 0 && compression != 3) return false;

                bool bottomUp = height > 0;
                int h = Math.Abs(height);
                int dataOffset = headerSize + (compression == 3 ? 12 : 0);
                int bytesPerPixel = bitCount / 8;
                int stride = ((width * bitCount + 31) / 32) * 4;       // 4 bayta hizalı satır
                if (dataOffset + stride * h > size) return false;

                var pixels = new Color32[width * h];
                for (int y = 0; y < h; y++)
                {
                    int srcRow = dataOffset + (bottomUp ? (h - 1 - y) : y) * stride;
                    int dstRow = (h - 1 - y) * width;                  // Unity alttan yukarı
                    for (int x = 0; x < width; x++)
                    {
                        int i = srcRow + x * bytesPerPixel;
                        byte b = buf[i], g = buf[i + 1], r = buf[i + 2];
                        byte a = bytesPerPixel == 4 ? buf[i + 3] : (byte)255;
                        if (bytesPerPixel == 4 && a == 0) a = 255;     // bazı kaynaklar alfayı 0 bırakır
                        pixels[dstRow + x] = new Color32(r, g, b, a);
                    }
                }

                var t = new Texture2D(width, h, TextureFormat.RGBA32, false);
                t.SetPixels32(pixels);
                t.Apply();
                tex = t;
                return true;
            }
            finally
            {
                if (ptr != IntPtr.Zero) GlobalUnlock(hMem);
                CloseClipboard();
            }
        }
#endif
    }
}
