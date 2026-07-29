using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using NetSearch.Core.Native;

namespace NetSearch.App.Services;

public static class ElevationService
{
    public static bool IsElevated =>
        OperatingSystem.IsWindows() && new WindowsEnvironmentProbe().IsWindowsElevated;

    /// <summary>
    /// Relaunches this executable elevated (UAC prompt) and shuts the current instance down.
    /// No-ops if not running from a real exe path or if the user declines the prompt.
    /// Works for the published self-contained exe; under `dotnet run` the host path may differ.
    /// </summary>
    public static void RelaunchAsAdmin()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception) { /* user cancelled the UAC prompt — stay running unelevated */ }
    }
}
