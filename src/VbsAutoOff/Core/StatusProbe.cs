using System;
using System.IO;
using System.Management;
using Microsoft.Win32;

namespace VbsAutoOff.Core
{
    public sealed class VbsSnapshot
    {
        public int VbsStatus { get; set; }
        public string VbsStatusText { get; set; }
        public string HypervisorLaunch { get; set; }
        public bool UefiLocked { get; set; }
        public bool PolicyPresent { get; set; }
        public bool Phase2Queued { get; set; }

        public VbsSnapshot()
        {
            VbsStatus = -1;
            VbsStatusText = "Unknown";
            HypervisorLaunch = "?";
        }
    }

    public static class StatusProbe
    {
        public static string UefiLockMarkerPath
        {
            get { return Path.Combine(Logger.LogDir, "uefi_lock_detected.flag"); }
        }

        public static VbsSnapshot Read()
        {
            var s = new VbsSnapshot();
            s.VbsStatus = ReadVbsStatus();
            switch (s.VbsStatus)
            {
                case 0: s.VbsStatusText = "Disabled (OK)"; break;
                case 1: s.VbsStatusText = "Enabled (not running)"; break;
                case 2: s.VbsStatusText = "Active and running (blocks TwinCAT!)"; break;
                default: s.VbsStatusText = "Unknown"; break;
            }

            s.HypervisorLaunch = ReadHypervisorLaunch();
            ReadRegistryFlags(s);
            s.Phase2Queued = Phase2Setup.IsBootSequenceQueued();
            return s;
        }

        public static void MarkUefiLockDetected()
        {
            try
            {
                Directory.CreateDirectory(Logger.LogDir);
                File.WriteAllText(UefiLockMarkerPath, DateTime.Now.ToString("s"));
            }
            catch { }
        }

        public static void ClearUefiLockMarker()
        {
            try
            {
                if (File.Exists(UefiLockMarkerPath)) File.Delete(UefiLockMarkerPath);
            }
            catch { }
        }

        private static int ReadVbsStatus()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\DeviceGuard",
                    "SELECT VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard"))
                {
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        object val = mo["VirtualizationBasedSecurityStatus"];
                        if (val != null) return Convert.ToInt32(val);
                    }
                }
            }
            catch { }
            return -1;
        }

        private static string ReadHypervisorLaunch()
        {
            try
            {
                var r = Proc.Run("bcdedit.exe", "/enum {current}");
                foreach (var raw in r.Output.Split('\n'))
                {
                    if (raw.IndexOf("hypervisorlaunchtype", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) return parts[parts.Length - 1];
                    }
                }
            }
            catch { }
            return "?";
        }

        private static void ReadRegistryFlags(VbsSnapshot s)
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var dg = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard"))
                    {
                        if (dg != null)
                        {
                            object l = dg.GetValue("Locked");
                            if (l is int && (int)l != 0) { s.UefiLocked = true; MarkUefiLockDetected(); }
                        }
                    }
                    using (var hp = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"))
                    {
                        if (hp != null)
                        {
                            object l2 = hp.GetValue("Locked");
                            if (l2 is int && (int)l2 != 0) { s.UefiLocked = true; MarkUefiLockDetected(); }
                        }
                    }
                    using (var pol = hklm.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard"))
                    {
                        if (pol != null && pol.GetValueNames().Length > 0) s.PolicyPresent = true;
                    }
                }

                if (s.VbsStatus == 0)
                {
                    ClearUefiLockMarker();
                }
                else if (File.Exists(UefiLockMarkerPath))
                {
                    s.UefiLocked = true;
                }
            }
            catch { }
        }

        public static string ShortSummary()
        {
            var s = Read();
            return "VBS=" + s.VbsStatusText
                 + " | hypervisorlaunchtype=" + s.HypervisorLaunch
                 + " | UEFI-lock=" + (s.UefiLocked ? "yes" : "no");
        }
    }
}

