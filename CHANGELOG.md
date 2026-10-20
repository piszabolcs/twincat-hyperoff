# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versioning: [SemVer](https://semver.org/).

## [1.0.0] - 2026-10-20

### Added
- WPF status panel with one-click activation and uninstall, and a live log viewer.
- Background SYSTEM service (`CleanupWorker`) that keeps VBS, Device Guard, Credential Guard and HVCI off on boot, on shutdown and on a timer.
- Registry and BCD cleanup (`VbsCleaner`): clears enforcing GPO policy values, sets `hypervisorlaunchtype off` and the TwinCAT real-time clock switches, turns off Fast Startup.
- UEFI-lock unlock (Phase 2) via Microsoft `SecConfig.efi`, with BitLocker suspended for two reboots.
- Watchdog: the service and a scheduled task rebuild each other if an update removes either one.
- Read-only status probe for VBS, the service, `hypervisorlaunchtype` and the UEFI lock.
