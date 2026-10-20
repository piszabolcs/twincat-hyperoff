using System;
using System.IO;
namespace VbsAutoOff.Core
{
    // Phase 2: one-time preparation to release VBS that is locked in the UEFI firmware, using
    // Microsoft's SecConfig.efi tool. At the next reboot the user has to confirm at the console
    // (F3). The bootsequence can be verified from bcdedit -> IsBootSequenceQueued.
    //
    // BitLocker is suspended for 2 restarts before the boot chain is modified.
    // Fix for the "An error occurred while attempting to reference the specified entry" error:
    // "bcdedit /enum {GUID}" returns exit code 0 even when the entry does NOT exist (it only
    // prints that there is no match). Because of this the code assumed the DebugTool entry was
    // already present, skipped the /create step, and the first /set failed. Now we check for the
    // actual presence of the GUID in the output, and if there is a leftover half-finished entry,
    // we delete it and recreate it cleanly.
    public static class Phase2Setup
    {
        private const string Guid = "{0cb3b571-2f2e-4343-a879-d86a476d7215}";
        private const string GuidCore = "0cb3b571-2f2e-4343-a879-d86a476d7215";

        public static bool IsBootSequenceQueued()
        {
            try
            {
                var r = Proc.Run("bcdedit.exe", "/enum {bootmgr}");
                if (r.ExitCode != 0) return false;
                return r.Output.IndexOf(GuidCore, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static bool EntryExists()
        {
            var r = Proc.Run("bcdedit.exe", "/enum " + Guid);
            return r.ExitCode == 0 && r.Output.IndexOf(GuidCore, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static OpResult Prepare()
        {
            string letter = FreeDriveLetter();
            string efiPath = letter + @":\EFI\Microsoft\Boot\SecConfig.efi";
            bool mounted = false;
            try
            {
                VbsCleaner.RunPhase1(true);

                string windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
                string src = Path.Combine(windir, "System32", "SecConfig.efi");
                if (!File.Exists(src))
                    return new OpResult(false, "SecConfig.efi was not found on this machine: " + src);

                string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                var bl = Proc.Run("manage-bde.exe", "-protectors -disable " + sysDrive + " -RebootCount 2");
                Logger.Log("Phase2: BitLocker suspend (for 2 restarts) exit=" + bl.ExitCode + " | " + OneLine(bl.Output));

                // Clean up a previous (e.g. half-finished) entry so we start from a clean state.
                if (EntryExists())
                {
                    var del = Proc.Run("bcdedit.exe", "/delete " + Guid + " /f");
                    Logger.Log("Phase2: previous DebugTool entry deleted, exit=" + del.ExitCode + " | " + OneLine(del.Output));
                }

                var m = Proc.Run("mountvol.exe", letter + ": /s");
                if (m.ExitCode != 0)
                    return new OpResult(false, "Could not mount the EFI partition: " + OneLine(m.Output));
                mounted = true;

                string dir = Path.GetDirectoryName(efiPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(src, efiPath, true);
                Logger.Log("Phase2: SecConfig.efi copied -> " + efiPath);

                var c = Proc.Run("bcdedit.exe", "/create " + Guid + " /d \"DebugTool\" /application osloader");
                if (c.ExitCode != 0 && !EntryExists())
                    return new OpResult(false, "bcdedit /create error: " + OneLine(c.Output));
                Logger.Log("Phase2: BCD entry created: " + Guid);

                Must(Proc.Run("bcdedit.exe", "/set " + Guid + " path \\EFI\\Microsoft\\Boot\\SecConfig.efi"), "path");
                Must(Proc.Run("bcdedit.exe", "/set " + Guid + " loadoptions DISABLE-LSA-ISO,DISABLE-VBS"), "loadoptions");
                Must(Proc.Run("bcdedit.exe", "/set " + Guid + " device partition=" + letter + ":"), "device");
                // The bootsequence last of all: schedule it only once the entry is complete.
                Must(Proc.Run("bcdedit.exe", "/set {bootmgr} bootsequence " + Guid), "bootsequence");

                if (!IsBootSequenceQueued())
                    return new OpResult(false, "bcdedit ran, but the bootsequence is not visible under {bootmgr}. Check the log.");

                Logger.Log("Phase2 successfully configured for the next boot (bootsequence active).");
                return new OpResult(true,
                    "Phase 2 prepared.\n\n"
                    + "Restart the machine. Before Windows starts, a Microsoft screen appears; "
                    + "press the F3 key there (or the key shown on screen) to confirm turning off "
                    + "VBS / Credential Guard. It may ask twice.\n\n"
                    + "BitLocker is suspended for 2 restarts. To be safe, keep the recovery key at hand.");
            }
            catch (Exception ex)
            {
                Logger.Log("Phase2 error: " + ex.Message);
                return new OpResult(false, "Phase 2 error: " + ex.Message);
            }
            finally
            {
                if (mounted) Proc.Run("mountvol.exe", letter + ": /d");
            }
        }

        private static void Must(ProcResult r, string step)
        {
            if (r.ExitCode != 0)
                throw new Exception("bcdedit error (" + step + "): " + OneLine(r.Output));
        }

        private static string FreeDriveLetter()
        {
            var used = new System.Collections.Generic.HashSet<char>();
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!string.IsNullOrEmpty(d.Name))
                    used.Add(char.ToUpperInvariant(d.Name[0]));
            }
            foreach (char l in "ZYXWVUTSRQPONMLKJIHGFED")
            {
                if (!used.Contains(l)) return l.ToString();
            }
            throw new Exception("No free drive letter available for mounting the EFI partition.");
        }

        private static string OneLine(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
