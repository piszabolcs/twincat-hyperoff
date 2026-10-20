[CmdletBinding()]
param(
    [switch]$SkipPhase2,
    [string]$LogPath = "$env:ProgramData\VbsAutoOff\VbsAutoOff.log"
)
# Phase 1 (PowerShell version) - the same logic as the VbsAutoOff service.
# Win11-compatible: sets an EXPLICIT 0 under CurrentControlSet/ControlSetNNN (not just deleting).
# Fast Startup (HiberbootEnabled) is turned off + vsmlaunchtype is set on {default} too.
$ErrorActionPreference = "Continue"
$dir = Split-Path -Parent $LogPath
if (-not (Test-Path $dir)) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
function Log([string]$m) {
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    Add-Content -Path $LogPath -Value "[$ts] [PS] $m"
}
Log "Phase 1 (PS) starting..."
$policyPath = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard"
$policyVals = @("RequirePlatformSecurityFeatures","ConfigureKernelShadowStacksLaunch","ConfigureSystemGuardLaunch","MachineIdentityIsolation","LsaCfgFlags","HVCIMATRequired","HypervisorEnforcedCodeIntegrity","EnableVirtualizationBasedSecurity","Locked")
if (Test-Path $policyPath) {
    foreach ($v in $policyVals) {
        Remove-ItemProperty -Path $policyPath -Name $v -Force -ErrorAction SilentlyContinue
    }
}
$sets = @("CurrentControlSet") + (Get-ChildItem "HKLM:\SYSTEM" -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^ControlSet\d{3}$' } | Select-Object -ExpandProperty PSChildName) | Select-Object -Unique
foreach ($cs in $sets) {
    $dg   = "HKLM:\SYSTEM\$cs\Control\DeviceGuard"
    $hvci = "$dg\Scenarios\HypervisorEnforcedCodeIntegrity"
    $kss  = "$dg\Scenarios\KernelShadowStacks"
    $sg   = "$dg\Scenarios\SystemGuard"
    $cg   = "$dg\Scenarios\CredentialGuard"
    $lsa  = "HKLM:\SYSTEM\$cs\Control\Lsa"
    foreach ($p in @($dg, $hvci, $kss, $sg, $cg, $lsa)) {
        if (-not (Test-Path $p)) { New-Item -Path $p -Force | Out-Null }
    }
    Remove-ItemProperty -Path $dg -Name "Locked" -Force -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $dg -Name "EnableVirtualizationBasedSecurity" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $dg -Name "RequirePlatformSecurityFeatures" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $dg -Name "Mandatory" -Value 0 -Type DWord -Force
    Remove-ItemProperty -Path $hvci -Name "Locked" -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $hvci -Name "HVCIMATRequired" -Force -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $hvci -Name "Enabled" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $kss -Name "Enabled" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $sg  -Name "Enabled" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $cg  -Name "Enabled" -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $lsa -Name "LsaCfgFlags" -Value 0 -Type DWord -Force
    Remove-ItemProperty -Path $lsa -Name "IsolatedUserMode" -Force -ErrorAction SilentlyContinue
}
# Fast Startup OFF (otherwise "Shut down" is a hibernation and the settings do not take effect)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power" -Name "HiberbootEnabled" -Value 0 -Type DWord -Force
bcdedit /set "{current}" hypervisorlaunchtype off | Out-Null
bcdedit /set "{default}" hypervisorlaunchtype off | Out-Null
bcdedit /set "{current}" vsmlaunchtype Off | Out-Null
bcdedit /set "{default}" vsmlaunchtype Off | Out-Null
bcdedit /set "{current}" useplatformtick yes | Out-Null
bcdedit /set "{current}" disabledynamictick yes | Out-Null
Log "Phase 1 (PS) done."

