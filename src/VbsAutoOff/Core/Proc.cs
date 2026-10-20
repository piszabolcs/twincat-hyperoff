using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
namespace VbsAutoOff.Core
{
    public sealed class ProcResult
    {
        public int ExitCode { get; private set; }
        public string Output { get; private set; }
        public ProcResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output ?? string.Empty;
        }
    }
    public sealed class OpResult
    {
        public bool Ok { get; private set; }
        public string Message { get; private set; }
        public OpResult(bool ok, string message)
        {
            Ok = ok;
            Message = message ?? string.Empty;
        }
    }
    public static class Proc
    {
        // The console Windows tools (bcdedit, sc, manage-bde) write in the system OEM code page
        // (852 on a Hungarian machine). Previously we read it as UTF-8, which garbled the log output.
        private static Encoding OemEncoding()
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.Default; }
        }

        public static ProcResult Run(string fileName, string arguments)
        {
            try
            {
                var enc = OemEncoding();
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = enc,
                    StandardErrorEncoding = enc,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return new ProcResult(-1, "process start failed");
                    var errTask = p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                        return new ProcResult(-1, "timeout: " + fileName + " " + arguments);
                    }
                    string e = errTask.Result;
                    string combined = (o + (string.IsNullOrEmpty(e) ? "" : Environment.NewLine + e)).Trim();
                    return new ProcResult(p.ExitCode, combined);
                }
            }
            catch (Exception ex)
            {
                return new ProcResult(-1, ex.Message);
            }
        }
        public static ProcResult Cmd(string command)
        {
            return Run("cmd.exe", "/d /c " + command);
        }
    }
}
