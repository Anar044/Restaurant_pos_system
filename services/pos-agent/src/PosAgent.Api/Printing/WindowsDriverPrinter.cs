using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PosAgent.Api.Printing;

public sealed class WindowsDriverPrinter
{
    private const int HorzRes = 8;
    private const int VertRes = 10;
    private const int LogPixelsX = 88;
    private const int LogPixelsY = 90;
    private const int Transparent = 1;
    private const int DefaultCharset = 1;
    private const int OutDefaultPrecis = 0;
    private const int ClipDefaultPrecis = 0;
    private const int ClearTypeQuality = 5;
    private const int DefaultPitch = 0;
    private const int FwNormal = 400;

    private const uint DtLeft = 0x00000000;
    private const uint DtTop = 0x00000000;
    private const uint DtWordBreak = 0x00000010;
    private const uint DtNoPrefix = 0x00000800;

    public void PrintText(string queueName, string text, string documentName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows printer queues are only available on Windows.");

        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Windows printer queue name is required.", nameof(queueName));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Print text is empty.", nameof(text));

        var printerDc = CreateDC("WINSPOOL", queueName, null, IntPtr.Zero);
        if (printerDc == IntPtr.Zero)
            ThrowWin32($"Windows could not create a printer device context for '{queueName}'.");

        IntPtr font = IntPtr.Zero;
        IntPtr previousFont = IntPtr.Zero;
        var documentStarted = false;
        var pageStarted = false;
        var completed = false;

        try
        {
            var dpiX = Math.Max(GetDeviceCaps(printerDc, LogPixelsX), 96);
            var dpiY = Math.Max(GetDeviceCaps(printerDc, LogPixelsY), 96);
            var printableWidth = Math.Max(GetDeviceCaps(printerDc, HorzRes), dpiX * 2);
            var printableHeight = Math.Max(GetDeviceCaps(printerDc, VertRes), dpiY * 2);

            var fontHeight = -Math.Max(1, (int)Math.Round(9.0 * dpiY / 72.0));
            font = CreateFont(
                fontHeight,
                0,
                0,
                0,
                FwNormal,
                0,
                0,
                0,
                DefaultCharset,
                OutDefaultPrecis,
                ClipDefaultPrecis,
                ClearTypeQuality,
                DefaultPitch,
                "Segoe UI");

            if (font == IntPtr.Zero)
                ThrowWin32("Windows could not create a font for the print job.");

            previousFont = SelectObject(printerDc, font);
            _ = SetBkMode(printerDc, Transparent);

            var docInfo = new DocInfo
            {
                Size = Marshal.SizeOf<DocInfo>(),
                DocName = string.IsNullOrWhiteSpace(documentName) ? "Restaurant POS" : documentName,
                Output = null,
                DataType = null,
                Type = 0
            };

            if (StartDoc(printerDc, ref docInfo) <= 0)
                ThrowWin32("Windows could not start the printer document through the installed driver.");
            documentStarted = true;

            if (StartPage(printerDc) <= 0)
                ThrowWin32("Windows could not start the printer page.");
            pageStarted = true;

            var marginX = Math.Max(1, (int)Math.Round(dpiX * 5.0 / 25.4));
            var marginY = Math.Max(1, (int)Math.Round(dpiY * 5.0 / 25.4));
            var rect = new Rect
            {
                Left = marginX,
                Top = marginY,
                Right = Math.Max(marginX + 1, printableWidth - marginX),
                Bottom = Math.Max(marginY + 1, printableHeight - marginY)
            };

            var drawResult = DrawText(
                printerDc,
                text,
                text.Length,
                ref rect,
                DtLeft | DtTop | DtWordBreak | DtNoPrefix);

            if (drawResult <= 0)
                ThrowWin32("Windows printer driver could not render the receipt text.");

            if (EndPage(printerDc) <= 0)
                ThrowWin32("Windows could not finish the printer page.");
            pageStarted = false;

            if (EndDoc(printerDc) <= 0)
                ThrowWin32("Windows could not finish the printer document.");
            documentStarted = false;
            completed = true;
        }
        finally
        {
            if (!completed && (pageStarted || documentStarted))
                _ = AbortDoc(printerDc);

            if (previousFont != IntPtr.Zero)
                _ = SelectObject(printerDc, previousFont);

            if (font != IntPtr.Zero)
                _ = DeleteObject(font);

            _ = DeleteDC(printerDc);
        }
    }

    private static void ThrowWin32(string message) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), message);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        public int Size;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string DocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Output;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? DataType;

        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr devMode);

    [DllImport("gdi32.dll", EntryPoint = "DeleteDC", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "GetDeviceCaps")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("gdi32.dll", EntryPoint = "StartDocW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDoc(IntPtr hdc, ref DocInfo docInfo);

    [DllImport("gdi32.dll", EntryPoint = "EndDoc", SetLastError = true)]
    private static extern int EndDoc(IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "AbortDoc", SetLastError = true)]
    private static extern int AbortDoc(IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "StartPage", SetLastError = true)]
    private static extern int StartPage(IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "EndPage", SetLastError = true)]
    private static extern int EndPage(IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        int charSet,
        int outPrecision,
        int clipPrecision,
        int quality,
        int pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll", EntryPoint = "SelectObject", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr objectHandle);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("gdi32.dll", EntryPoint = "SetBkMode", SetLastError = true)]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DrawText(
        IntPtr hdc,
        string text,
        int textLength,
        ref Rect rect,
        uint format);
}
