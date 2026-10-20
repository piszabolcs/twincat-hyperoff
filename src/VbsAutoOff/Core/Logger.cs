using System;
using System.IO;
using System.Text;
namespace VbsAutoOff.Core
{
    public static class Logger
    {
        public static string LogDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VbsAutoOff"); }
        }
        public static string LogPath
        {
            get { return Path.Combine(LogDir, "VbsAutoOff.log"); }
        }
        private static readonly object Gate = new object();
        private const long MaxBytes = 5 * 1024 * 1024;
        public static void Log(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDir);
                    RotateIfNeeded();
                    string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine;
                    File.AppendAllText(LogPath, line, new UTF8Encoding(false));
                }
            }
            catch { }
        }
        private static void RotateIfNeeded()
        {
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > MaxBytes)
            {
                string bak = LogPath + ".1";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(LogPath, bak);
            }
        }
        public static string[] Tail(int lines)
        {
            try
            {
                if (!File.Exists(LogPath)) return new string[0];
                string[] all;
                // FileShare.ReadWrite: the service may be writing the log at the same time.
                using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    all = sr.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                }
                if (all.Length <= lines) return all;
                var res = new string[lines];
                Array.Copy(all, all.Length - lines, res, 0, lines);
                return res;
            }
            catch
            {
                return new string[0];
            }
        }
    }
}
