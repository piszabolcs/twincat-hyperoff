using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace VbsAutoOff.Core
{
    // Phase 1: sets the VBS / Device Guard / Credential Guard settings to an EXPLICIT 0 in a
    // Win11-compatible way (not just deleting them), because on Win11 a missing key means VBS is on.
    // The GPO-enforcing policy values are removed from the SOFTWARE\Policies branch.
    //
    // Design notes:
    //  - Fast Startup (HiberbootEnabled) is turned off. With Fast Startup on, "Shut down" is actually
    //    a hibernation: the service's OnShutdown does NOT run, and on the next power-on the
    //    kernel/hypervisor state is reloaded, so the registry/BCD changes have no effect.
    //  - RunRegistryOnlyFast(): on shutdown, registry only, no bcdedit/WMI (Windows gives about 5 s).
    //  - vsmlaunchtype is set on the {default} entry too.
    //  - Thread-safe (the worker thread and OnShutdown/OnStop may call it at the same time).
    public static class VbsCleaner
    {
        private static readonly object Gate = new object();
        private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\DeviceGuard";
        private const string PowerPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
        private static readonly string[] PolicyValues =
        {
            "RequirePlatformSecurityFeatures",
            "ConfigureKernelShadowStacksLaunch",
            "ConfigureSystemGuardLaunch",
            "MachineIdentityIsolation",
            "LsaCfgFlags",
            "HVCIMATRequired",
            "HypervisorEnforcedCodeIntegrity",
            "EnableVirtualizationBasedSecurity",
            "Locked"
        };

        public static int RunPhase1(bool forceBcdAndLog)
        {
            return RunCore(forceBcdAndLog, false);
        }

        // Call on shutdown: registry only, fast, does not start an external process.
        public static int RunRegistryOnlyFast()
        {
            return RunCore(false, true);
        }

        private static int RunCore(bool forceBcdAndLog, bool fast)
        {
            lock (Gate)
            {
                int changes = CleanRegistry();
                if (fast)
                {
                    if (changes > 0) Logger.Log("Fast (shutdown) registry cleanup. Modified values: " + changes);
                    return changes;
                }
                if (forceBcdAndLog || changes > 0)
                {
                    var b1 = Proc.Run("bcdedit.exe", "/set {current} hypervisorlaunchtype off");
                    Proc.Run("bcdedit.exe", "/set {default} hypervisorlaunchtype off");
                    Proc.Run("bcdedit.exe", "/set {current} vsmlaunchtype Off");
                    Proc.Run("bcdedit.exe", "/set {default} vsmlaunchtype Off");
                    Proc.Run("bcdedit.exe", "/set {current} useplatformtick yes");
                    Proc.Run("bcdedit.exe", "/set {current} disabledynamictick yes");
                    Logger.Log("Phase1 completed. Modified registry values: " + changes
                        + " | bcdedit exit=" + b1.ExitCode + " | " + StatusProbe.ShortSummary());
                }
                return changes;
            }
        }

        private static int CleanRegistry()
        {
            int changes = 0;
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                foreach (var v in PolicyValues)
                {
                    if (RemoveValue(hklm, PolicyPath, v)) changes++;
                }
                foreach (var cs in GetControlSets(hklm))
                {
                    string dgPath = @"SYSTEM\" + cs + @"\Control\DeviceGuard";
                    string hvciPath = dgPath + @"\Scenarios\HypervisorEnforcedCodeIntegrity";
                    string kssPath = dgPath + @"\Scenarios\KernelShadowStacks";
                    string sgPath = dgPath + @"\Scenarios\SystemGuard";
                    string cgPath = dgPath + @"\Scenarios\CredentialGuard";
                    string lsaPath = @"SYSTEM\" + cs + @"\Control\Lsa";
                    if (CheckLockedValue(hklm, dgPath, "Locked") || CheckLockedValue(hklm, hvciPath, "Locked"))
                    {
                        StatusProbe.MarkUefiLockDetected();
                    }
                    if (RemoveValue(hklm, dgPath, "Locked")) changes++;
                    if (EnsureDwordZero(hklm, dgPath, "EnableVirtualizationBasedSecurity")) changes++;
                    if (EnsureDwordZero(hklm, dgPath, "RequirePlatformSecurityFeatures")) changes++;
                    if (EnsureDwordZero(hklm, dgPath, "Mandatory")) changes++;
                    if (RemoveValue(hklm, hvciPath, "Locked")) changes++;
                    if (RemoveValue(hklm, hvciPath, "HVCIMATRequired")) changes++;
                    if (EnsureDwordZero(hklm, hvciPath, "Enabled")) changes++;
                    if (EnsureDwordZero(hklm, kssPath, "Enabled")) changes++;
                    if (EnsureDwordZero(hklm, sgPath, "Enabled")) changes++;
                    if (EnsureDwordZero(hklm, cgPath, "Enabled")) changes++;
                    if (EnsureDwordZero(hklm, lsaPath, "LsaCfgFlags")) changes++;
                    if (RemoveValue(hklm, lsaPath, "IsolatedUserMode")) changes++;
                }
                // Fast Startup OFF: this way "Shut down" is also a real, clean boot.
                if (EnsureDwordZero(hklm, PowerPath, "HiberbootEnabled")) changes++;
            }
            return changes;
        }

        private static IEnumerable<string> GetControlSets(RegistryKey hklm)
        {
            var sets = new List<string> { "CurrentControlSet" };
            try
            {
                using (var sys = hklm.OpenSubKey("SYSTEM"))
                {
                    if (sys != null)
                    {
                        foreach (var name in sys.GetSubKeyNames())
                        {
                            if (Regex.IsMatch(name, @"^ControlSet\d{3}$", RegexOptions.IgnoreCase))
                                sets.Add(name);
                        }
                    }
                }
            }
            catch { }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var s in sets)
            {
                if (seen.Add(s)) result.Add(s);
            }
            return result;
        }

        private static bool CheckLockedValue(RegistryKey hklm, string subPath, string name)
        {
            try
            {
                using (var k = hklm.OpenSubKey(subPath, false))
                {
                    if (k == null) return false;
                    object val = k.GetValue(name);
                    return (val is int && (int)val != 0);
                }
            }
            catch { return false; }
        }

        private static bool RemoveValue(RegistryKey hklm, string subPath, string name)
        {
            try
            {
                using (var k = hklm.OpenSubKey(subPath, true))
                {
                    if (k == null) return false;
                    if (k.GetValue(name) == null) return false;
                    k.DeleteValue(name, false);
                    Logger.Log("Deleted: HKLM\\" + subPath + " -> " + name);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error while deleting: HKLM\\" + subPath + " -> " + name + " | " + ex.Message);
                return false;
            }
        }

        private static bool EnsureDwordZero(RegistryKey hklm, string subPath, string name)
        {
            try
            {
                using (var k = hklm.CreateSubKey(subPath))
                {
                    if (k == null) return false;
                    object cur = k.GetValue(name);
                    if (cur is int && (int)cur == 0) return false;
                    k.SetValue(name, 0, RegistryValueKind.DWord);
                    Logger.Log("Set (0): HKLM\\" + subPath + " -> " + name);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error while setting to 0: HKLM\\" + subPath + " -> " + name + " | " + ex.Message);
                return false;
            }
        }
    }
}

