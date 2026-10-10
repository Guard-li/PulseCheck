using System.Diagnostics;

namespace PulseCheck.Agent.Reporting;

internal static class EdgePdfRenderer
{
    public static bool TryRender(string htmlPath, string pdfPath, out string? error)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        var edge = candidates.FirstOrDefault(File.Exists);
        if (edge is null)
        {
            error = "未找到 Microsoft Edge。";
            return false;
        }
        try
        {
            var profile = Path.Combine(Path.GetTempPath(), "PulseCheckEdge", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            var start = new ProcessStartInfo
            {
                FileName = edge,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add("--headless=new");
            start.ArgumentList.Add("--disable-gpu");
            start.ArgumentList.Add("--no-first-run");
            start.ArgumentList.Add("--disable-extensions");
            start.ArgumentList.Add($"--user-data-dir={profile}");
            start.ArgumentList.Add($"--print-to-pdf={pdfPath}");
            start.ArgumentList.Add(new Uri(htmlPath).AbsoluteUri);
            using var process = Process.Start(start);
            if (process is null)
            {
                error = "无法启动 Microsoft Edge。";
                return false;
            }
            if (!process.WaitForExit(30000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                error = "Microsoft Edge 生成 PDF 超时。";
                return false;
            }
            if (process.ExitCode == 0 && File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0)
            {
                error = null;
                return true;
            }
            error = $"Microsoft Edge 退出码 {process.ExitCode}。{process.StandardError.ReadToEnd()}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}