using System;
using System.Runtime.InteropServices;

namespace RCMenuManager.Helpers;

/// <summary>
/// Wraps SHChangeNotify so Explorer reloads the shell associations after
/// we've mutated registry verbs.
/// </summary>
public static class ShellNotifyHelper
{
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>Tells Explorer that file associations changed.</summary>
    public static void NotifyAssociationsChanged()
    {
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }
}
