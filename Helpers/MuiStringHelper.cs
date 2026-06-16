using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RCMenuManager.Helpers;

/// <summary>
/// Resolves indirect string resources of the form "@C:\\Windows\\System32\\foo.dll,-123"
/// into a real localized string via SHLoadIndirectString. The CLR has no managed
/// equivalent, and we want to surface user-facing names (MUIVerb / LocalizedString)
/// in the UI just like Explorer does.
/// </summary>
public static class MuiStringHelper
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        StringBuilder pszOutBuf,
        int cchOutBuf,
        IntPtr ppvReserved);

    /// <summary>
    /// Tries to resolve <paramref name="indirect"/> to a localized string. When the
    /// input does not look like an indirect reference it is returned unchanged.
    /// Returns null when the resource could not be loaded.
    /// </summary>
    public static string? Resolve(string? indirect)
    {
        if (string.IsNullOrWhiteSpace(indirect))
            return null;

        // Not an indirect reference - hand it back as a literal string.
        if (!indirect.StartsWith('@'))
            return indirect;

        // SHLoadIndirectString needs absolute paths. Allow callers to pass
        // "@%SystemRoot%\\..." style references and expand them first.
        var expanded = Environment.ExpandEnvironmentVariables(indirect);

        // Some entries omit the directory and rely on the search path; try to
        // anchor those to System32 so the loader can find them.
        expanded = TryAnchorToSystem32(expanded);

        var buffer = new StringBuilder(1024);
        var hr = SHLoadIndirectString(expanded, buffer, buffer.Capacity, IntPtr.Zero);
        if (hr == 0 && buffer.Length > 0)
            return buffer.ToString();
        return null;
    }

    private static string TryAnchorToSystem32(string indirect)
    {
        // Format: @<file>,<resource-id>[;<fallback>]
        if (!indirect.StartsWith('@'))
            return indirect;
        var commaIndex = indirect.IndexOf(',');
        if (commaIndex <= 1)
            return indirect;

        var filePart = indirect.Substring(1, commaIndex - 1).Trim();
        if (Path.IsPathRooted(filePart) || filePart.Contains(Path.DirectorySeparatorChar))
            return indirect;

        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var anchored = Path.Combine(system32, filePart);
        if (!File.Exists(anchored))
            return indirect;

        return "@" + anchored + indirect.Substring(commaIndex);
    }
}
