using Microsoft.Win32;
using System.Collections.Generic;

namespace RCMenuManager.Services;

public interface IBackupService
{
    /// <summary>
    /// Snapshots a registry sub-tree to a .reg file.
    /// <para>
    /// Returns <c>null</c> when the target key does not exist yet — that is the
    /// normal case for create operations, which have no "previous state" to
    /// preserve. Callers must not treat a null result as a failure.
    /// </para>
    /// </summary>
    string? Export(RegistryHive hive, string subKey, string scopeId, string verbName);

    IReadOnlyList<Models.BackupRecord> List();
    void Import(string filePath);
    void Delete(string filePath);
}
