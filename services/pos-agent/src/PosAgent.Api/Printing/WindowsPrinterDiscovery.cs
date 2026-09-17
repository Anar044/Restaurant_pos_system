using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PosAgent.Api.Printing;

public sealed record LocalPrinterInfo(string QueueName, bool IsDefault);

public sealed class WindowsPrinterDiscovery
{
    private const uint PrinterEnumLocal = 0x00000002;
    private const uint PrinterEnumConnections = 0x00000004;

    public IReadOnlyList<LocalPrinterInfo> GetInstalledPrinters()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var flags = PrinterEnumLocal | PrinterEnumConnections;
        _ = EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out var bytesNeeded, out _);
        if (bytesNeeded == 0)
            return [];

        var buffer = Marshal.AllocHGlobal(checked((int)bytesNeeded));
        try
        {
            if (!EnumPrinters(flags, null, 4, buffer, bytesNeeded, out _, out var returned))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enumerate installed printers.");

            var defaultPrinter = GetDefaultPrinterName();
            var structSize = Marshal.SizeOf<PrinterInfo4>();
            var printers = new List<LocalPrinterInfo>(checked((int)returned));

            for (var index = 0; index < returned; index++)
            {
                var itemPointer = IntPtr.Add(buffer, checked((int)index * structSize));
                var item = Marshal.PtrToStructure<PrinterInfo4>(itemPointer);
                var queueName = Marshal.PtrToStringUni(item.PrinterName)?.Trim();
                if (string.IsNullOrWhiteSpace(queueName))
                    continue;

                printers.Add(new LocalPrinterInfo(
                    queueName,
                    string.Equals(queueName, defaultPrinter, StringComparison.OrdinalIgnoreCase)));
            }

            return printers
                .GroupBy(x => x.QueueName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.QueueName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? GetDefaultPrinterName()
    {
        var charsNeeded = 0;
        _ = GetDefaultPrinter(null, ref charsNeeded);
        if (charsNeeded <= 0)
            return null;

        var buffer = new StringBuilder(charsNeeded);
        return GetDefaultPrinter(buffer, ref charsNeeded)
            ? buffer.ToString()
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr PrinterName;
        public IntPtr ServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr printerEnum,
        uint bufferSize,
        out uint bytesNeeded,
        out uint returned);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? buffer, ref int charsNeeded);
}
