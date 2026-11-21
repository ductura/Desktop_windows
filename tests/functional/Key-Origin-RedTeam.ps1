# Red-team of P-3 (PROPOSAL, docs/ductus/USTAV-DOPUNA-P3.md in ductura/.github): a program sends keys to the
# Ductura window through SendInput, as an automation tool or an AI typist would; the window must count every one
# as a key from a program. Keys from a real keyboard cannot be produced by a script, so the device count is
# checked only to be zero here; a person checks it by hand (README).
#
# Safety: only F24 is sent (no app acts on it), and only while Ductura is the foreground window; if another window
# takes the foreground, the script stops sending and fails.
#
#   ./tests/functional/Key-Origin-RedTeam.ps1 [-Exe publish/Ductura.exe] [-Keys 60]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\publish\Ductura.exe'),
    [int]$Keys = 60
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class RedTeamInput {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    const ushort VK_F24 = 0x87; const uint KEYUP = 0x0002;
    public static uint PressF24() {
        var down = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = VK_F24 } };
        var up = new INPUT { type = 1, ki = new KEYBDINPUT { wVk = VK_F24, dwFlags = KEYUP } };
        return SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
    }
}
'@

$out = Join-Path ([IO.Path]::GetTempPath()) "ductura-key-tally-$PID.json"
Remove-Item $out -ErrorAction SilentlyContinue
$app = Start-Process -FilePath $Exe -ArgumentList '--key-tally-out', $out -PassThru
try {
    $deadline = (Get-Date).AddSeconds(30)
    while ($app.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200; $app.Refresh() }
    if ($app.MainWindowHandle -eq 0) { throw 'Ductura window did not appear' }
    [RedTeamInput]::SetForegroundWindow($app.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 500

    for ($i = 0; $i -lt $Keys; $i++) {
        if ([RedTeamInput]::GetForegroundWindow() -ne $app.MainWindowHandle) {
            throw "Ductura is no longer the foreground window after $i keys; stopped sending"
        }
        if ([RedTeamInput]::PressF24() -ne 2) { throw 'SendInput was refused' }
        Start-Sleep -Milliseconds 20
    }
    Start-Sleep -Milliseconds 500
    $app.CloseMainWindow() | Out-Null
    if (-not $app.WaitForExit(15000)) { throw 'Ductura did not close' }
}
finally {
    if (-not $app.HasExited) { $app.Kill() }
}

if (-not (Test-Path $out)) { throw 'no tally was written' }
$tally = Get-Content $out -Raw | ConvertFrom-Json
Remove-Item $out
Write-Host "Tally: device $($tally.deviceKeys), program $($tally.programKeys) (sent $Keys)"
if ($tally.programKeys -ne $Keys) { throw "expected $Keys keys from a program, got $($tally.programKeys)" }
if ($tally.deviceKeys -ne 0) { throw "expected no keys from a device, got $($tally.deviceKeys)" }
Write-Host 'OK: every key sent by a program was counted as a program''s'
