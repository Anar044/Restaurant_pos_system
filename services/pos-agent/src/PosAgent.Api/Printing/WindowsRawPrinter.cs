using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PosAgent.Api.Printing;

public sealed class WindowsRawPrinter
{
    public void Send(string queueName, byte[] payload, string documentName)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows printer queues are only available on Windows.");

        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Windows printer queue name is required.", nameof(queueName));

        if (payload.Length == 0)
            throw new ArgumentException("Print payload is empty.", nameof(payload));

        if (!OpenPrinter(queueName, out var printerHandle, IntPtr.Zero))
            ThrowWin32("Windows could not open the printer queue.");

        try
        {
            var docInfo = new DocInfo1
            {
                DocumentName = string.IsNullOrWhiteSpace(documentName) ? "Restaurant POS" : documentName,
                OutputFile = null,
                DataType = "RAW"
            };

            if (StartDocPrinter(printerHandle, 1, ref docInfo) == 0)
                ThrowWin32("Windows could not start the print document.");

            try
            {
                if (!StartPagePrinter(printerHandle))
                    ThrowWin32("Windows could not start the printer page.");

                try
                {
                    if (!WritePrinter(printerHandle, payload, payload.Length, out var written))
                        ThrowWin32("Windows could not write data to the printer.");

                    if (written != payload.Length)
                        throw new IOException($"Windows wrote only {written} of {payload.Length} bytes to the printer.");
                }
                finally
                {
                    _ = EndPagePrinter(printerHandle);
                }
            }
            finally
            {
                _ = EndDocPrinter(printerHandle);
            }
        }
        finally
        {
            _ = ClosePrinter(printerHandle);
        }
    }

    private static void ThrowWin32(string message) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), message);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string DocumentName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? OutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printerHandle, IntPtr printerDefaults);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint StartDocPrinter(IntPtr printerHandle, uint level, ref DocInfo1 docInfo);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(
        IntPtr printerHandle,
        byte[] bytes,
        int count,
        out int written);
}
