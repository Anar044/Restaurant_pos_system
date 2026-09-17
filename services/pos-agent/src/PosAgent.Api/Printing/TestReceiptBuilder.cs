using System.Text;

namespace PosAgent.Api.Printing;

public static class TestReceiptBuilder
{
    public static string BuildWindowsText(string? deviceName, string? printerName)
    {
        return string.Join(Environment.NewLine,
        [
            "RESTAURANT POS TEST",
            "WINDOWS DRIVER PRINT OK",
            "------------------------------",
            $"POS: {deviceName ?? "Unknown POS"}",
            $"Printer: {printerName ?? "Unknown printer"}",
            $"Machine: {Environment.MachineName}",
            $"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}",
            "------------------------------",
            "Unicode test:",
            "Azərbaycan: Ə ə Ş ş Ç ç Ğ ğ İ ı Ö ö Ü ü",
            "Русский: Тест печати через Windows",
            "------------------------------",
            "Printed by POS Agent locally.",
            "Windows queue uses the installed printer driver.",
            "Restaurant Node is not needed for this local print step.",
            ""
        ]);
    }

    public static byte[] BuildEscPos(string? deviceName, string? printerName)
    {
        var lines = new List<byte>();

        // ESC/POS initialize.
        lines.AddRange([0x1B, 0x40]);

        // Center + bold title.
        lines.AddRange([0x1B, 0x61, 0x01]);
        lines.AddRange([0x1B, 0x45, 0x01]);
        AppendAscii(lines, "RESTAURANT POS TEST\n");
        lines.AddRange([0x1B, 0x45, 0x00]);
        AppendAscii(lines, "NETWORK RAW PRINT OK\n");
        AppendAscii(lines, "------------------------------\n");

        // Left aligned details.
        lines.AddRange([0x1B, 0x61, 0x00]);
        AppendAscii(lines, $"POS: {ToAscii(deviceName ?? "Unknown POS")}\n");
        AppendAscii(lines, $"Printer: {ToAscii(printerName ?? "Unknown printer")}\n");
        AppendAscii(lines, $"Machine: {ToAscii(Environment.MachineName)}\n");
        AppendAscii(lines, $"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}\n");
        AppendAscii(lines, "------------------------------\n");
        AppendAscii(lines, "Printed by POS Agent locally.\n");
        AppendAscii(lines, "Network mode sends ESC/POS RAW.\n");
        AppendAscii(lines, "Restaurant Node is not needed\nfor this local print step.\n");
        AppendAscii(lines, "\n\n\n");

        return lines.ToArray();
    }

    private static void AppendAscii(List<byte> target, string text) =>
        target.AddRange(Encoding.ASCII.GetBytes(text));

    private static string ToAscii(string text) =>
        new(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray());
}
