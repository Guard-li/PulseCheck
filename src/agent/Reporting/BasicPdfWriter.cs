using System.Globalization;
using System.Text;

namespace PulseCheck.Agent.Reporting;

internal static class BasicPdfWriter
{
    public static void Write(string path, Contracts.HealthReport report)
    {
        var lines = new List<string>
        {
            "PulseCheck Health Report",
            $"Device: {Ascii(report.Device.DisplayName)}",
            $"Report ID: {report.ReportId}",
            $"Generated UTC: {report.GeneratedAtUtc:O}",
            $"Scan mode: {report.ScanMode}",
            $"Health score: {report.Health.OverallScore:F1}/100",
            $"Risk: {Ascii(report.RiskLevel)}",
            $"Findings: {report.Findings.Length}",
            $"Report SHA256: {report.ReportHash}",
            string.Empty,
            "This fallback PDF uses a standard PDF font and may not display Chinese.",
            "Use the HTML report for full Chinese formatting."
        };
        var content = new StringBuilder();
        content.AppendLine("BT /F1 11 Tf 42 792 Td 16 TL");
        foreach (var line in lines)
        {
            content.Append('(').Append(Escape(line)).AppendLine(") Tj T*");
            if (line.Length > 0) content.AppendLine("T*");
        }
        content.AppendLine("ET");
        var stream = Encoding.ASCII.GetBytes(content.ToString());
        var objects = new List<byte[]>
        {
            AsciiObject("<< /Type /Catalog /Pages 2 0 R >>"),
            AsciiObject("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            AsciiObject("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>"),
            AsciiObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
            StreamObject(stream)
        };
        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write(output, $"{i + 1} 0 obj\n");
            output.Write(objects[i]);
            Write(output, "\nendobj\n");
        }
        var xref = output.Position;
        Write(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(output, $"{offset:0000000000} 00000 n \n");
        Write(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, output.ToArray());
    }

    private static byte[] AsciiObject(string value) => Encoding.ASCII.GetBytes(value);
    private static byte[] StreamObject(byte[] stream)
    {
        var header = Encoding.ASCII.GetBytes($"<< /Length {stream.Length} >>\nstream\n");
        var footer = Encoding.ASCII.GetBytes("\nendstream");
        return [.. header, .. stream, .. footer];
    }
    private static void Write(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);
    private static string Ascii(string value) => string.Concat(value.Select(c => c <= 126 ? c : '?'));
}