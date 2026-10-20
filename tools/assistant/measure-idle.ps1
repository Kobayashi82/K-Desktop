param(
    [Parameter(Mandatory)] [string[]]$Case,
    [int]$Warmup = 180,
    [int]$Seconds = 120,
    [switch]$Keep
)
# Measures idle K-Desktop instances (and their assistant processes, if the module starts one) without touching the
# real data. Each case is "label|exe|data template": the template is copied to a fresh folder and an isolated
# instance starts on it (no hooks, UDP or web; its config.json decides whether the module is on). All cases run at
# the same time, so they share the conditions; after -Warmup seconds (startup leaves threads finishing their work
# for a couple of minutes) both processes of each are measured for -Seconds. Reported: CPU as % of one core, memory
# at the end, threads, handles, whether the assistant assemblies were loaded into K-Desktop, and how much of the time
# the PC was in use (window events wake the assistant).
# CPU comes from cycle counts (QueryProcessCycleTime): the processor time Windows reports is charged in 15.6 ms
# clock ticks, and threads that wake on a timer are running right at the tick, so idle ones look several times
# busier than they are. Use framework-dependent builds (bin\Release), so each assembly shows up as its own module.
# -Keep leaves the data folders (and their logs) in %TEMP%.
#   .\measure-idle.ps1 -Case 'off|src\KDesktop\bin\Release\net10.0-windows\win-x64\KDesktop.exe|<template>', ...
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MeasureNative {
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("kernel32.dll")] private static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);
    public static double IdleSeconds() {
        var info = new LastInput { Size = 8 };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) / 1000.0 : 0;
    }
    public static double Cycles(IntPtr process) { ulong c; return QueryProcessCycleTime(process, out c) ? c : 0; }
}
'@
$hz = (Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0').'~MHz' * 1e6

function Sample([System.Diagnostics.Process]$p) {
    if (-not $p -or $p.HasExited) { return $null }
    $p.Refresh()
    [pscustomobject]@{ Cycles = [MeasureNative]::Cycles($p.Handle); WS = $p.WorkingSet64; Private = $p.PrivateMemorySize64; Threads = $p.Threads.Count; Handles = $p.HandleCount }
}

function FindAssistant([string]$folder) {
    $row = Get-CimInstance Win32_Process -Filter "Name = 'KDesktop.exe'" |
        Where-Object { $_.CommandLine -like '*--assistant*' -and $_.CommandLine -like "*$folder*" } | Select-Object -First 1
    if ($row) { Get-Process -Id $row.ProcessId -ErrorAction SilentlyContinue }
}

$mb = { param($bytes) [math]::Round($bytes / 1MB, 1) }
$cpu = { param($a, $b, $wall) [math]::Round(($b.Cycles - $a.Cycles) / $hz / $wall * 100, 3) }
$runs = foreach ($c in $Case) {
    $label, $exe, $data = $c -split '\|'
    $folder = Join-Path $env:TEMP ('kd-measure-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    Copy-Item (Resolve-Path $data).Path $folder -Recurse
    $main = Start-Process (Resolve-Path $exe).Path -ArgumentList @('--data', "`"$folder`"", '--no-elevate', '--no-hooks', '--no-udp', '--no-web') -PassThru
    [pscustomobject]@{ Label = $label; Folder = $folder; Main = $main; Assistant = $null; M0 = $null; A0 = $null }
}
try {
    Start-Sleep -Seconds $Warmup
    foreach ($r in $runs) {
        $r.Assistant = FindAssistant $r.Folder
        $r.M0 = Sample $r.Main
        $r.A0 = Sample $r.Assistant
    }
    $start = Get-Date
    # In use: share of 5 s slices with keyboard or mouse input.
    $slices = 0; $used = 0
    while (((Get-Date) - $start).TotalSeconds -lt $Seconds) {
        Start-Sleep -Seconds 5
        $slices++
        if ([MeasureNative]::IdleSeconds() -lt 5) { $used++ }
    }
    $wall = ((Get-Date) - $start).TotalSeconds
    foreach ($r in $runs) {
        $m1 = Sample $r.Main
        $a1 = Sample $r.Assistant
        $modules = @($r.Main.Modules | ForEach-Object ModuleName)
        [pscustomobject]@{
            Label           = $r.Label
            'In use %'      = [math]::Round($used / [math]::Max(1, $slices) * 100)
            'CPU %'         = & $cpu $r.M0 $m1 $wall
            'WS MB'         = & $mb $m1.WS
            'Private MB'    = & $mb $m1.Private
            Threads         = $m1.Threads
            Handles         = $m1.Handles
            'Assistant dll' = [bool]($modules -match '^KDesktop\.Assistant\.')
            'A CPU %'       = $(if ($a1 -and $r.A0) { & $cpu $r.A0 $a1 $wall })
            'A WS MB'       = $(if ($a1) { & $mb $a1.WS })
            'A Private MB'  = $(if ($a1) { & $mb $a1.Private })
            'A Threads'     = $(if ($a1) { $a1.Threads })
            'A Handles'     = $(if ($a1) { $a1.Handles })
        }
    }
}
finally {
    foreach ($r in $runs) {
        $assistant = FindAssistant $r.Folder
        Stop-Process -Id $r.Main.Id -Force -ErrorAction SilentlyContinue
        if ($assistant) { Stop-Process -Id $assistant.Id -Force -ErrorAction SilentlyContinue }
    }
    Start-Sleep -Seconds 2
    if (-not $Keep) { foreach ($r in $runs) { Remove-Item $r.Folder -Recurse -Force -ErrorAction SilentlyContinue } }
    else { foreach ($r in $runs) { Write-Host "$($r.Label): $($r.Folder)" } }
}
