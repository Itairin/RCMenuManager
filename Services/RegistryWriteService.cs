using System;
using Microsoft.Win32;
using RCMenuManager.Helpers;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Single funnel for every verb-key write. Each method follows the same flow:
/// elevation check -> backup -> in-memory/registry write -> SHChangeNotify ->
/// log append. On failure the exception is logged with success=false and rethrown.
/// </summary>
public sealed class RegistryWriteService
{
    private readonly IRegistryWriter _writer;
    private readonly IBackupService _backup;
    private readonly IOperationLog _log;
    private readonly Func<bool> _isAdmin;
    private readonly Action _notifyShell;

    public RegistryWriteService(IRegistryWriter writer, IBackupService backup, IOperationLog log, Func<bool> isAdmin, Action? notifyShell = null)
    {
        _writer = writer;
        _backup = backup;
        _log = log;
        _isAdmin = isAdmin;
        _notifyShell = notifyShell ?? ShellNotifyHelper.NotifyAssociationsChanged;
    }

    /// <summary>True when the current process can safely write to <paramref name="hive"/>.</summary>
    public bool CanWrite(RegistryHive hive) => hive == RegistryHive.CurrentUser || _isAdmin();

    public void Disable(RegistryHive hive, string subKey, string scopeId, string verbName) =>
        Run(hive, subKey, scopeId, verbName, "Disable", () =>
            _writer.SetStringValue(hive, subKey, "ProgrammaticAccessOnly", string.Empty));

    public void Enable(RegistryHive hive, string subKey, string scopeId, string verbName) =>
        Run(hive, subKey, scopeId, verbName, "Enable", () =>
            _writer.DeleteValue(hive, subKey, "ProgrammaticAccessOnly"));

    public void Delete(RegistryHive hive, string subKey, string scopeId, string verbName) =>
        Run(hive, subKey, scopeId, verbName, "Delete", () =>
            _writer.DeleteSubKeyTree(hive, subKey));

    public void UpdateDisplayName(RegistryHive hive, string subKey, string scopeId, string verbName, string displayName) =>
        Run(hive, subKey, scopeId, verbName, "UpdateDisplayName", () =>
            _writer.SetStringValue(hive, subKey, string.Empty, displayName));

    public void UpdateCommand(RegistryHive hive, string subKey, string scopeId, string verbName, string command) =>
        Run(hive, subKey, scopeId, verbName, "UpdateCommand", () =>
            _writer.SetStringValue(hive, subKey + @"\command", string.Empty, command));

    public void UpdateIcon(RegistryHive hive, string subKey, string scopeId, string verbName, string iconExpression) =>
        Run(hive, subKey, scopeId, verbName, "UpdateIcon", () =>
        {
            if (string.IsNullOrEmpty(iconExpression))
                _writer.DeleteValue(hive, subKey, "Icon");
            else
                _writer.SetStringValue(hive, subKey, "Icon", iconExpression);
        });

    public void SetExtended(RegistryHive hive, string subKey, string scopeId, string verbName, bool extended) =>
        Run(hive, subKey, scopeId, verbName, "SetExtended", () =>
        {
            if (extended) _writer.SetStringValue(hive, subKey, "Extended", string.Empty);
            else _writer.DeleteValue(hive, subKey, "Extended");
        });

    public void SetPosition(RegistryHive hive, string subKey, string scopeId, string verbName, string position) =>
        Run(hive, subKey, scopeId, verbName, "SetPosition", () =>
        {
            if (string.Equals(position, "Top", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(position, "Bottom", StringComparison.OrdinalIgnoreCase))
                _writer.SetStringValue(hive, subKey, "Position", position);
            else
                _writer.DeleteValue(hive, subKey, "Position");
        });

    private void Run(RegistryHive hive, string subKey, string scopeId, string verbName, string op, Action mutate)
    {
        if (!CanWrite(hive))
            throw new ElevationRequiredException(hive, subKey);

        string? backupPath = null;
        try
        {
            backupPath = _backup.Export(hive, subKey, scopeId, verbName);
            mutate();
            _notifyShell();
            _log.Append(new OperationLogEntry(DateTime.UtcNow, scopeId, verbName, op, hive, subKey, backupPath, success: true, error: null));
        }
        catch (Exception ex)
        {
            _log.Append(new OperationLogEntry(DateTime.UtcNow, scopeId, verbName, op, hive, subKey, backupPath, success: false, error: ex.Message));
            throw;
        }
    }
}
