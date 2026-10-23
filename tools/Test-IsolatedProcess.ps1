#Requires -PSEdition Desktop
param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$ArgumentLine = '',
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [int]$TimeoutSeconds = 30,
    [switch]$ExpectMainWindow,
    [string]$ExpectedWindowTitle = 'PCL Communist',
    [string]$ExpectedWindowClassPrefix = 'HwndWrapper'
)
$ErrorActionPreference = 'Stop'
$Executable = [IO.Path]::GetFullPath($Executable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# Run our own test process on an inactive desktop. FormMain moves itself to the
# foreground during startup, so merely setting WindowStyle Hidden is insufficient.
if (-not ('RedIsolatedTest' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class RedIsolatedTest {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo {
        public int cb; public string reserved; public string desktop; public string title;
        public int x, y, width, height, charsX, charsY, fill, flags;
        public short show, reservedSize;
        public IntPtr reservedPointer, input, output, error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public IntPtr process, thread; public uint id, threadId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int left, top, right, bottom; }
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string file, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity,
        bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(IntPtr desktop, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);

    public static int Run(string executable, string arguments, string output, int seconds, bool expectWindow, string expectedTitle, string expectedClassPrefix) {
        string name = "RedTest-" + Guid.NewGuid().ToString("N");
        IntPtr desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01FF, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        ProcessInfo process = new ProcessInfo();
        try {
            var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)), desktop = name };
            var command = new StringBuilder("\"" + executable + "\" " + arguments);
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000000,
                IntPtr.Zero, Path.GetDirectoryName(executable), ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            bool exited = WaitForSingleObject(process.process, (uint)(seconds * 1000)) == 0;
            uint code;
            if (exited) {
                GetExitCodeProcess(process.process, out code);
                File.WriteAllText(Path.Combine(output, "isolated-process.txt"), "Process exited: " + code);
                if (expectWindow) throw new Exception("Launcher exited before main-window inspection: " + code);
                return (int)code;
            }
            var windows = new List<string>();
            IntPtr main = IntPtr.Zero;
            bool errorDialog = false;
            EnumDesktopWindows(desktop, delegate(IntPtr window, IntPtr parameter) {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner != process.id) return true;
                var title = new StringBuilder(512);
                var kind = new StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                GetClassName(window, kind, kind.Capacity);
                windows.Add(kind + " | " + title);
                if (title.ToString() == expectedTitle && kind.ToString().StartsWith(expectedClassPrefix, StringComparison.Ordinal)) main = window;
                if (kind.ToString() == "#32770") {
                    errorDialog = true;
                    EnumChildWindows(window, delegate(IntPtr child, IntPtr unused) {
                        var content = new StringBuilder(8192);
                        GetWindowText(child, content, content.Capacity);
                        if (content.Length > 0) windows.Add("  " + content);
                        return true;
                    }, IntPtr.Zero);
                }
                return true;
            }, IntPtr.Zero);
            File.WriteAllLines(Path.Combine(output, "isolated-process.txt"), windows);
            if (!expectWindow) throw new TimeoutException("Test process exceeded " + seconds + " seconds.");
            if (main == IntPtr.Zero || errorDialog) throw new Exception("Launcher main window missing or native error dialog detected. See isolated-process.txt.");
            Rect bounds;
            GetWindowRect(main, out bounds);
            using (var bitmap = new Bitmap(bounds.right - bounds.left, bounds.bottom - bounds.top))
            using (var graphics = Graphics.FromImage(bitmap)) {
                IntPtr dc = graphics.GetHdc();
                bool captured;
                try { captured = PrintWindow(main, dc, 2); }
                finally { graphics.ReleaseHdc(dc); }
                if (captured) bitmap.Save(Path.Combine(output, "standalone-startup.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            File.AppendAllText(Path.Combine(output, "isolated-process.txt"), Environment.NewLine + "PASS: standalone launcher main window is alive; no native initialization error dialog.");
            return 0;
        } finally {
            if (process.process != IntPtr.Zero) {
                if (WaitForSingleObject(process.process, 0) != 0) {
                    try { Process.GetProcessById((int)process.id).Kill(); } catch (ArgumentException) { }
                    WaitForSingleObject(process.process, 5000);
                }
                CloseHandle(process.thread);
                CloseHandle(process.process);
            }
            CloseDesktop(desktop);
        }
    }
}
'@
}
$testExitCode = [RedIsolatedTest]::Run($Executable, $ArgumentLine, $OutputDirectory, $TimeoutSeconds, $ExpectMainWindow.IsPresent, $ExpectedWindowTitle, $ExpectedWindowClassPrefix)
if ($testExitCode -ne 0) { throw "Isolated test failed with exit code $testExitCode. See $OutputDirectory." }
Write-Output "PASS: isolated process check. Results: $OutputDirectory"
