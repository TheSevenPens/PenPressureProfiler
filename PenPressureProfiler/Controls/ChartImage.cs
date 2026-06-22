using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace PenPressureProfiler.Controls;

/// <summary>
/// Exports a chart — or any Avalonia visual — to a PNG file or the Windows
/// clipboard. Rendering is WYSIWYG: it snapshots the live visual at its
/// on-screen pixel size, so the current theme, axis range, and live overlays
/// (e.g. the accumulator force line) are preserved exactly as displayed.
///
/// <para>Passing a container visual (such as the stacked Time-series grid)
/// captures every chart inside it in one image.</para>
/// </summary>
public static class ChartImage
{
    private static readonly FilePickerFileType PngFilter =
        new("PNG image") { Patterns = ["*.png"] };

    /// <summary>Renders <paramref name="visual"/> to a device-pixel bitmap.</summary>
    public static RenderTargetBitmap Render(Control visual)
    {
        double scale = TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1.0;
        var dip = visual.Bounds.Size;
        int w = Math.Max(1, (int)Math.Ceiling(dip.Width  * scale));
        int h = Math.Max(1, (int)Math.Ceiling(dip.Height * scale));

        var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96 * scale, 96 * scale));
        rtb.Render(visual);
        return rtb;
    }

    /// <summary>Renders the visual and encodes it as PNG bytes.</summary>
    public static byte[] RenderPng(Control visual)
    {
        using var rtb = Render(visual);
        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Shows a Save-as dialog and writes a PNG of the visual. No-op if the user
    /// cancels.
    /// </summary>
    public static async Task SavePngAsync(Control visual, TopLevel topLevel, string suggestedFileName)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = "Save chart image",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices   = [PngFilter],
            DefaultExtension  = "png",
        });
        if (file is null) return;

        var png = RenderPng(visual);
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(png);
    }

    /// <summary>
    /// Copies the visual to the Windows clipboard as both CF_DIB (so Paint,
    /// Office, etc. can paste it) and a "PNG" format (preferred by browsers and
    /// chat apps). Must be called on the UI thread.
    /// </summary>
    public static void CopyToClipboard(Control visual)
    {
        using var rtb = Render(visual);

        byte[] dib = ToDib(rtb);
        byte[] png;
        using (var ms = new MemoryStream())
        {
            rtb.Save(ms);
            png = ms.ToArray();
        }

        ClipboardImage.SetImage(dib, png);
    }

    /// <summary>
    /// Packs a render-target bitmap into a CF_DIB byte block: a
    /// BITMAPINFOHEADER followed by bottom-up 32bpp BGRA pixels. Pixels are
    /// flattened over white and forced opaque, so any uncovered (transparent)
    /// border pixels paste as white rather than black.
    /// </summary>
    private static byte[] ToDib(RenderTargetBitmap rtb)
    {
        var size = rtb.PixelSize;
        int w = size.Width, h = size.Height;
        int stride   = w * 4;
        int pixBytes = stride * h;

        var src = new byte[pixBytes];
        var handle = GCHandle.Alloc(src, GCHandleType.Pinned);
        try
        {
            rtb.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixBytes, stride);
        }
        finally { handle.Free(); }

        const int HEADER = 40; // sizeof(BITMAPINFOHEADER)
        var dib = new byte[HEADER + pixBytes];

        BitConverter.GetBytes(HEADER).CopyTo(dib, 0);
        BitConverter.GetBytes(w).CopyTo(dib, 4);
        BitConverter.GetBytes(h).CopyTo(dib, 8);            // positive height → bottom-up
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);    // planes
        BitConverter.GetBytes((short)32).CopyTo(dib, 14);   // bpp
        BitConverter.GetBytes(0).CopyTo(dib, 16);           // BI_RGB
        BitConverter.GetBytes(pixBytes).CopyTo(dib, 20);    // image size

        // Avalonia render targets are premultiplied BGRA. Flatten over white —
        // out = premulComponent + 255*(1-alpha) — and flip rows for bottom-up.
        for (int y = 0; y < h; y++)
        {
            int srcRow = y * stride;
            int dstRow = HEADER + (h - 1 - y) * stride;
            for (int x = 0; x < stride; x += 4)
            {
                int inv = 255 - src[srcRow + x + 3];
                dib[dstRow + x + 0] = (byte)(src[srcRow + x + 0] + inv);
                dib[dstRow + x + 1] = (byte)(src[srcRow + x + 1] + inv);
                dib[dstRow + x + 2] = (byte)(src[srcRow + x + 2] + inv);
                dib[dstRow + x + 3] = 255;
            }
        }

        return dib;
    }
}

/// <summary>
/// Minimal Win32 clipboard image writer. Avalonia's <c>IClipboard</c> only
/// handles text and opaque data objects, so image copy goes through the native
/// clipboard API directly.
/// </summary>
internal static class ClipboardImage
{
    private const uint CF_DIB        = 8;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>Places the same image on the clipboard under CF_DIB and "PNG".</summary>
    public static void SetImage(byte[] dib, byte[] png)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            Place(CF_DIB, dib);
            uint cfPng = RegisterClipboardFormat("PNG");
            if (cfPng != 0) Place(cfPng, png);
        }
        finally { CloseClipboard(); }
    }

    private static void Place(uint format, byte[] data)
    {
        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(uint)data.Length);
        if (hMem == IntPtr.Zero) return;

        IntPtr ptr = GlobalLock(hMem);
        if (ptr == IntPtr.Zero) { GlobalFree(hMem); return; }

        Marshal.Copy(data, 0, ptr, data.Length);
        GlobalUnlock(hMem);

        // On success the system owns hMem; only free it if the call failed.
        if (SetClipboardData(format, hMem) == IntPtr.Zero)
            GlobalFree(hMem);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
}
