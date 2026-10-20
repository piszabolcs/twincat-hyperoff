using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;

namespace VbsAutoOff.Core
{
    public static class ServiceControl
    {
        public const string ServiceName = "VbsAutoOff";
        public const string DisplayName = "VBS Auto Off (TwinCAT)";
        public const string WatchdogTaskName = "VbsAutoOff-Watchdog";

        public static string InstallDir { get { return Logger.LogDir; } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "VbsAutoOff.exe"); } }

        public static bool IsInstalled()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    var st = sc.Status;
                    return true;
                }
            }
            catch { return false; }
        }

        public static string CurrentStatus()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    return sc.Status.ToString();
                }
            }
            catch { return null; }
        }

        public static OpResult InstallAndStart()
        {
            try
            {
                Directory.CreateDirectory(InstallDir);

                // Stop FIRST so the running exe is not locked when we overwrite it.
                if (IsInstalled())
                {
                    StopIfRunning();
                }

                string self = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.Equals(self, InstalledExe, StringComparison.OrdinalIgnoreCase))
                {
                    CopyWithRetry(self, InstalledExe);
                    Logger.Log("Exe copied to: " + InstalledExe);
                }

                string binPathArg = "\\\"" + InstalledExe + "\\\" --service";

                if (IsInstalled())
                {
                    var cfg = Proc.Run("sc.exe", "config " + ServiceName + " binPath= \"" + binPathArg + "\" start= auto obj= LocalSystem");
                    if (cfg.ExitCode != 0)
                        Logger.Log("sc config warning: " + OneLine(cfg.Output));
                }
                else
                {
                    var c = Proc.Run("sc.exe", "create " + ServiceName + " binPath= \"" + binPathArg + "\" start= auto obj= LocalSystem DisplayName= \"" + DisplayName + "\"");
                    if (c.ExitCode != 0)
                        return new OpResult(false, "sc create error: " + OneLine(c.Output));
                }

                Proc.Run("sc.exe", "description " + ServiceName + " \"TwinCAT Win11 compatibility: keeps VBS/Hyper-V/CredentialGuard continuously switched off.\"");
                Proc.Run("sc.exe", "failure " + ServiceName + " reset= 86400 actions= restart/30000/restart/30000/restart/30000");

                // Clean up the old tasks removed by a feature update.
                Proc.Run("schtasks.exe", "/Delete /TN \"DisableVBSDrift-Startup\" /F");
                Proc.Run("schtasks.exe", "/Delete /TN \"DisableVBSDrift-Hourly\" /F");

                EnsureWatchdogTask();

                var s = Proc.Run("sc.exe", "start " + ServiceName);
                if (s.ExitCode != 0 && s.ExitCode != 1056)
                {
                    Logger.Log("sc start warning exit=" + s.ExitCode + " " + OneLine(s.Output));
                }

                Logger.Log("Service installed and started.");
                return new OpResult(true, "The background service and the self-healing protection have been installed and started.");
            }
            catch (Exception ex)
            {
                Logger.Log("Service install error: " + ex.Message);
                return new OpResult(false, "Install error: " + ex.Message);
            }
        }

        public static void EnsureWatchdogTask()
        {
            try
            {
                if (!File.Exists(InstalledExe)) return;
                string tr = "\\\"" + InstalledExe + "\\\" --watchdog";
                Proc.Run("schtasks.exe", "/Create /F /TN \"" + WatchdogTaskName + "\" /RU \"SYSTEM\" /RL HIGHEST /SC ONSTART /DELAY 0001:00 /TR \"" + tr + "\"");
            }
            catch { }
        }

        public static OpResult Uninstall()
        {
            try
            {
                Proc.Run("schtasks.exe", "/Delete /TN \"" + WatchdogTaskName + "\" /F");
                StopIfRunning();
                var d = Proc.Run("sc.exe", "delete " + ServiceName);
                if (d.ExitCode != 0 && d.ExitCode != 1060)
                    return new OpResult(false, "sc delete error: " + OneLine(d.Output));

                Logger.Log("Service and Watchdog removed.");
                return new OpResult(true, "The background service has been removed.");
            }
            catch (Exception ex)
            {
                return new OpResult(false, ex.Message);
            }
        }

        private static void CopyWithRetry(string src, string dst)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    File.Copy(src, dst, true);
                    return;
                }
                catch (IOException)
                {
                    if (i == 9) throw;
                    Thread.Sleep(500);
                }
            }
        }

        private static void StopIfRunning()
        {
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                    }
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }
            }
            catch { }
            Thread.Sleep(300);
        }

        private static string OneLine(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("\r", " ").Replace("\n", " ");
        }
    }
}

