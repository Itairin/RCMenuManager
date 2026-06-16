namespace RCMenuManager.Models;

/// <summary>
/// Where a registry-backed entry lives. Used purely for display / merging,
/// since HKCR is a virtual merge of HKLM and HKCU.
/// </summary>
public enum RegistryHiveOrigin
{
    /// <summary>Origin unknown / not relevant.</summary>
    Unknown,

    /// <summary>HKCU\Software\Classes - per-user override.</summary>
    CurrentUser,

    /// <summary>HKLM\SOFTWARE\Classes - machine-wide.</summary>
    LocalMachine,

    /// <summary>HKCR (merged view).</summary>
    ClassesRoot,
}
