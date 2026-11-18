using System;
using Microsoft.Win32;

namespace RCMenuManager.Services;

/// <summary>
/// One logical registry transaction: pre-write backup, the mutation itself, the
/// shell notification and the audit line. Exposed as an interface so features that
/// touch keys outside "a verb under a scope" (the Win11 menu toggle, the Block
/// list) still go through the same funnel instead of writing to the registry
/// directly — which is how they used to bypass backup and logging entirely.
/// </summary>
public interface IRegistryTransactor
{
    bool CanWrite(RegistryHive hive);

    /// <summary>
    /// Runs <paramref name="mutate"/> as a single audited transaction.
    /// <c>op</c> is the label recorded in the operation log.
    /// </summary>
    void Run(RegistryHive hive, string subKey, string scopeId, string verbName, string op, Action<IRegistryWriter> mutate);
}
