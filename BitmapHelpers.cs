using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using KillerPDF.Services;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace KillerPDF
{
    public partial class MainWindow
    {
        // ============================================================
        // Bitmap rotation helper
        // ============================================================

        /// <summary>
        /// Rotates a raw BGRA (4 bytes/pixel) bitmap clockwise by degrees.
        /// Used because Docnet's FPDF_RenderPageBitmapWithMatrix uses a pure-scaling
        /// matrix, so PDFium renders the page in its MediaBox orientation (no rotation).
        /// We strip /Rotate from the temp file so content is never clipped, then rotate
        /// the pixel buffer here to match the intended visual orientation.
        /// </summary>
        internal static (byte[] bytes, int w, int h) RotateBitmapStatic(byte[] src, int w, int h, int degrees)
            => RotateBitmap(src, w, h, degrees);

        // ============================================================
        // Document color inversion (#135, "dark mode")
        // ============================================================

        // True = the document pane renders with inverted colors (dark-mode reading). DISPLAY
        // ONLY: saves, prints, exports, OCR, thumbnails, and tool previews all keep the
        // document's true colors. Loaded from the "DocInvert" setting at startup; toggled from
        // the Settings panel, which flushes the render caches (the state is baked into pixels).
        internal static bool DocInvert;

        /// <summary>In-place inversion for the display dark mode, called at the Viewport render
        /// sites right after rotation. PDF pages usually paint NO background - the "paper" is
        /// transparent pixels compositing over the white page slot - so a plain RGB flip left
        /// the page white and merely faded the ink. Composite over white and invert in one
        /// step: out = a*(255-c)/255 with alpha forced opaque. White (or unpainted) paper
        /// becomes black, dark ink becomes light, and opaque images get a true negative.</summary>
        internal static void InvertBgraInPlace(byte[] bgra)
        {
            for (int i = 0; i + 3 < bgra.Length; i += 4)
            {
                int a = bgra[i + 3];
                bgra[i]     = (byte)(a * (255 - bgra[i])     / 255);
                bgra[i + 1] = (byte)(a * (255 - bgra[i + 1]) / 255);
                bgra[i + 2] = (byte)(a * (255 - bgra[i + 2]) / 255);
                bgra[i + 3] = 255;
            }
        }

        private static (byte[] bytes, int w, int h) RotateBitmap(byte[] src, int w, int h, int degrees)
        {
            degrees = ((degrees % 360) + 360) % 360;
            if (degrees == 0) return (src, w, h);
            int newW = (degrees == 90 || degrees == 270) ? h : w;
            int newH = (degrees == 90 || degrees == 270) ? w : h;
            byte[] dst = new byte[newW * newH * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int srcIdx = (y * w + x) * 4;
                    int dstX, dstY;
                    switch (degrees)
                    {
                        case 90: dstX = h - 1 - y; dstY = x; break; // CW
                        case 180: dstX = w - 1 - x; dstY = h - 1 - y; break;
                        default: dstX = y; dstY = w - 1 - x; break; // 270 CW
                    }
                    int dstIdx = (dstY * newW + dstX) * 4;
                    dst[dstIdx] = src[srcIdx];
                    dst[dstIdx + 1] = src[srcIdx + 1];
                    dst[dstIdx + 2] = src[srcIdx + 2];
                    dst[dstIdx + 3] = src[srcIdx + 3];
                }
            }
            return (dst, newW, newH);
        }

    }
}
