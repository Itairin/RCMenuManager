using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Describes a registry sub-path we should consult when listing verbs for a
/// scope. The hive together with the sub-key yields a canonical lookup. We
/// keep an <see cref="Origin"/> tag so the UI can show where each verb came
/// from after the merge.
/// </summary>
public sealed record RegistryQueryEntry(RegistryHive Hive, string SubKey, RegistryHiveOrigin Origin)
{
    public string FullPath => Hive switch
    {
        RegistryHive.ClassesRoot => $@"HKEY_CLASSES_ROOT\{SubKey}",
        RegistryHive.CurrentUser => $@"HKEY_CURRENT_USER\{SubKey}",
        RegistryHive.LocalMachine => $@"HKEY_LOCAL_MACHINE\{SubKey}",
        _ => SubKey,
    };
}
