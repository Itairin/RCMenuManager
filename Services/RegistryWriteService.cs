using System;
using System.Collections.Generic;
using Microsoft.Win32;
using RCMenuManager.Helpers;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Single funnel for every verb-key write. Each public operation is one
/// transaction: elevation check -> backup -> one or more mutations ->
/// SHChangeNotify -> one log entry. Batching matters because a single "save"
/// edits up to five values; issuing five separate transactions produced five
/// backups of the same key, five shell notifications and five log lines.
/// </summary>
public sealed class RegistryWriteService : IRegistryTransactor
{
    private readonly IRegistryWriter _writer;
    private readonly IBackupService _backup;
    private readonly IOperationLog _log;
    private readonly Func<bool> _isAdmin;
    private readonly Action _notifyShell;
    private readonly Action<string>? _importFile;

    public RegistryWriteService(IRegistryWriter writer, IBackupService backup, IOperationLog log,
        Func<bool> isAdmin, Action? notifyShell = null, Action<string>? importFile = null)
    {
        _writer = writer;
        _backup = backup;
        _log = log;
        _isAdmin = isAdmin;
        _notifyShell = notifyShell ?? ShellNotifyHelper.NotifyAssociationsChanged;
        // Importing a .reg cannot go through IRegistryWriter, so the capability is
        // injected. It exists so restore is a first-class audited transaction instead
        // of a call that silently bypassed snapshot + log.
        _importFile = importFile;
    }

    /// <summary>
    /// Rolls a key back to a previous snapshot as one audited transaction: the
    /// current state is snapshotted first (so a restore can itself be undone), the
    /// file is imported, the shell is notified and the log records it.
    /// </summary>
    public void RestoreFromBackup(RegistryHive hive, string subKey, string scopeId, string verbName, string filePath)
    {
        if (_importFile is null)
            throw new InvalidOperationException("未配置导入能力，无法恢复备份");
        if (!CanWrite(hive))
            throw new ElevationRequiredException(hive, subKey);

        string? snapshot = null;
        try
        {
            // "-pre-restore" keeps this file distinguishable from the one being
            // restored when both carry the same verb name.
            snapshot = _backup.Export(hive, subKey, scopeId, verbName + "-pre-restore");
            _importFile(filePath);
            _notifyShell();
            _log.Append(new OperationLogEntry(DateTime.UtcNow, scopeId, verbName, "Restore",
                hive, subKey, snapshot, success: true, error: null));
        }
        catch (Exception ex)
        {
            _log.Append(new OperationLogEntry(DateTime.UtcNow, scopeId, verbName, "Restore",
                hive, subKey, snapshot, success: false, error: ex.Message));
            throw;
        }
    }

    /// <summary>True when the current process can safely write to <paramref name="hive"/>.</summary>
    public bool CanWrite(RegistryHive hive) => hive == RegistryHive.CurrentUser || _isAdmin();

    /// <summary>
    /// Escape hatch for callers that own a key this service has no dedicated method
    /// for (the Win11 CLSID toggle, the Block list). Going through here is what keeps
    /// those writes backed up and logged; writing to the registry directly skips both.
    /// </summary>
    public void Run(RegistryHive hive, string subKey, string scopeId, string verbName, string op,
        Action<IRegistryWriter> mutate) => Execute(hive, subKey, scopeId, verbName, op, mutate);

    public void Disable(RegistryHive hive, string subKey, string scopeId, string verbName) =>
        Execute(hive, subKey, scopeId, verbName, "Disable", w =>
            w.SetStringValue(hive, subKey, "ProgrammaticAccessOnly", string.Empty));

    public void Enable(RegistryHive hive, string subKey, string scopeId, string verbName) =>
        Execute(hive, subKey, scopeId, verbName, "Enable", w =>
        {
            // Clear both "hidden from Explorer" mechanisms. The legacy
            // LegacyDisable flag is ignored by modern Explorers but still
            // respected by some shell extensions, so it has to go too.
            w.DeleteValue(hive, subKey, "ProgrammaticAccessOnly");
            w.DeleteValue(hive, subKey, "LegacyDisable");
        });

    public void Delete(RegistryHive hive, string subKey, string scopeId, string verbName) =>
        Execute(hive, subKey, scopeId, verbName, "Delete", w => w.DeleteSubKeyTree(hive, subKey));

    public void UpdateDisplayName(RegistryHive hive, string subKey, string scopeId, string verbName, string displayName) =>
        Execute(hive, subKey, scopeId, verbName, "UpdateDisplayName", w =>
            w.SetStringValue(hive, subKey, string.Empty, displayName));

    public void UpdateCommand(RegistryHive hive, string subKey, string scopeId, string verbName, string command) =>
        Execute(hive, subKey, scopeId, verbName, "UpdateCommand", w =>
            w.SetStringValue(hive, subKey + @"\command", string.Empty, command));

    public void UpdateIcon(RegistryHive hive, string subKey, string scopeId, string verbName, string iconExpression) =>
        Execute(hive, subKey, scopeId, verbName, "UpdateIcon", w =>
        {
            if (string.IsNullOrEmpty(iconExpression))
                w.DeleteValue(hive, subKey, "Icon");
            else
                w.SetStringValue(hive, subKey, "Icon", iconExpression);
        });

    public void SetExtended(RegistryHive hive, string subKey, string scopeId, string verbName, bool extended) =>
        Execute(hive, subKey, scopeId, verbName, "SetExtended", w =>
        {
            if (extended) w.SetStringValue(hive, subKey, "Extended", string.Empty);
            else w.DeleteValue(hive, subKey, "Extended");
        });

    public void SetPosition(RegistryHive hive, string subKey, string scopeId, string verbName, string position) =>
        Execute(hive, subKey, scopeId, verbName, "SetPosition", w =>
        {
            if (IsTopOrBottom(position)) w.SetStringValue(hive, subKey, "Position", position);
            else w.DeleteValue(hive, subKey, "Position");
        });

    /// <summary>
    /// Applies a whole edit-panel draft as ONE transaction: a single backup,
    /// a single shell notification and a single log entry. Command is skipped
    /// when <paramref name="includeCommand"/> is false (DelegateExecute verbs
    /// own their command through COM; writing one would override that).
    /// </summary>
    public void SaveEdits(RegistryHive hive, string subKey, string scopeId, string verbName,
        EditableVerbDraft draft, bool includeCommand)
    {
        Execute(hive, subKey, scopeId, verbName, "Save", w =>
        {
            // An inferred name is only persisted when the user typed a different one;
            // saving an icon change must not turn a guessed label into stored data.
            var nameChanged = !draft.HasInferredName
                || !string.Equals(draft.DisplayName, draft.OriginalDisplayName, StringComparison.Ordinal);
            if (nameChanged)
                w.SetStringValue(hive, subKey, string.Empty, draft.DisplayName);

            // MUIVerb takes precedence over the default value when the label is read
            // back, so renaming a verb that has one has to move MUIVerb as well —
            // otherwise the edit "succeeds" and the list shows the old name.
            if (nameChanged && draft.HasMuiVerb && !string.IsNullOrEmpty(draft.DisplayName))
                w.SetStringValue(hive, subKey, "MUIVerb", draft.DisplayName);
            if (includeCommand)
                w.SetStringValue(hive, subKey + @"\command", string.Empty, draft.Command);
            if (string.IsNullOrEmpty(draft.IconExpression))
                w.DeleteValue(hive, subKey, "Icon");
            else
                w.SetStringValue(hive, subKey, "Icon", draft.IconExpression);
            if (draft.IsExtended) w.SetStringValue(hive, subKey, "Extended", string.Empty);
            else w.DeleteValue(hive, subKey, "Extended");
            if (IsTopOrBottom(draft.Position)) w.SetStringValue(hive, subKey, "Position", draft.Position);
            else w.DeleteValue(hive, subKey, "Position");
        });
    }

    /// <summary>Creates a brand-new verb at the scope root.</summary>
    public void CreateRootVerb(RegistryHive hive, string scopeShellSubKey, string scopeId, EditableVerbDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.VerbName))
            throw new ArgumentException("VerbName 不能为空", nameof(draft));

        var verbKey = scopeShellSubKey + "\\" + draft.VerbName;
        if (!CanWrite(hive))
            throw new ElevationRequiredException(hive, verbKey);
        if (_writer.KeyExists(hive, verbKey))
            throw new RegistryConflictException(hive, verbKey);

        // The key does not exist yet, so the pre-write snapshot is legitimately
        // absent -- BackupService.Export returns null instead of throwing.
        Execute(hive, verbKey, scopeId, draft.VerbName, draft.IsParentOnly ? "CreateParent" : "CreateRoot", w =>
            ApplyDraft(w, hive, verbKey, draft, includeShellSubKey: draft.IsParentOnly));
    }

    /// <summary>Creates a child verb under <paramref name="parentVerbSubKey"/>'s \shell sub-tree.</summary>
    public void CreateChildVerb(RegistryHive hive, string parentVerbSubKey, string scopeId, EditableVerbDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.VerbName))
            throw new ArgumentException("VerbName 不能为空", nameof(draft));

        var parentShell = parentVerbSubKey + "\\shell";
        var verbKey = parentShell + "\\" + draft.VerbName;
        if (!CanWrite(hive))
            throw new ElevationRequiredException(hive, verbKey);
        if (_writer.KeyExists(hive, verbKey))
            throw new RegistryConflictException(hive, verbKey);

        Execute(hive, verbKey, scopeId, draft.VerbName, "CreateChild", w =>
        {
            if (!w.KeyExists(hive, parentShell))
                w.CreateSubKey(hive, parentShell);
            ApplyDraft(w, hive, verbKey, draft, includeShellSubKey: draft.IsParentOnly);
        });
    }

    private static bool IsTopOrBottom(string? position) =>
        string.Equals(position, "Top", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(position, "Bottom", StringComparison.OrdinalIgnoreCase);

    private void ApplyDraft(IRegistryWriter w, RegistryHive hive, string verbKey, EditableVerbDraft draft, bool includeShellSubKey)
    {
        w.CreateSubKey(hive, verbKey);
        if (!string.IsNullOrEmpty(draft.DisplayName))
            w.SetStringValue(hive, verbKey, string.Empty, draft.DisplayName);
        if (!string.IsNullOrEmpty(draft.IconExpression))
            w.SetStringValue(hive, verbKey, "Icon", draft.IconExpression);
        if (draft.IsExtended)
            w.SetStringValue(hive, verbKey, "Extended", string.Empty);
        if (IsTopOrBottom(draft.Position))
            w.SetStringValue(hive, verbKey, "Position", draft.Position);

        if (includeShellSubKey)
        {
            w.CreateSubKey(hive, verbKey + "\\shell");
        }
        else if (!string.IsNullOrEmpty(draft.Command))
        {
            w.CreateSubKey(hive, verbKey + "\\command");
            w.SetStringValue(hive, verbKey + "\\command", string.Empty, draft.Command);
        }
    }

    /// <summary>
    /// Runs one logical write: backup, mutations, shell notify, log. The backup
    /// returns null for create operations (nothing to snapshot) and that is not
    /// treated as a failure.
    /// </summary>
    private void Execute(RegistryHive hive, string subKey, string scopeId, string verbName, string op, Action<IRegistryWriter> mutate)
    {
        if (!CanWrite(hive))
            throw new ElevationRequiredException(hive, subKey);

        string? backupPath = null;
        try
        {
            backupPath = _backup.Export(hive, subKey, scopeId, verbName);
            mutate(_writer);
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
