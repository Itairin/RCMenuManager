using System;
using System.Diagnostics;
using System.Security.Principal;

namespace RCMenuManager.Helpers;

/// <summary>
/// Small UAC helper. M1 is read-only, but later milestones will use
/// <see cref="RelaunchAsAdmin"/> when the user opts to write to HKCR/HKLM.
/// </summary>
public static class UacHelper
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Relaunches the current process with the runas verb. Returns true when the
    /// new process started; the caller is then expected to shut the current
    /// instance down. Returns false when the user declined the UAC prompt.
    /// </summary>
    public static bool RelaunchAsAdmin(string? arguments = null)
    {
        // Environment.ProcessPath gives the host .exe (works under PublishSingleFile
        // unlike Assembly.Location, which returns empty for embedded assemblies).
        var entry = Environment.ProcessPath;
        if (string.IsNullOrEmpty(entry))
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = entry,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = arguments ?? string.Empty,
        };

        try
        {
            return Process.Start(psi) is not null;
        }
        catch
        {
            return false;
        }
    }
}
