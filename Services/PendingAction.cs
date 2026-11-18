using System;
using Microsoft.Win32;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

/// <summary>
/// Describes the operation a command was about to perform when the process had to
/// relaunch elevated. Elevation restarts the whole app, so without this the user
/// fills in a dialog, presses 确定, accepts the UAC prompt — and lands on a fresh
/// window with nothing done.
/// </summary>
public sealed class PendingAction
{
    public string Kind { get; set; } = PendingActionKinds.SaveEdit;

    public RegistryHive Hive { get; set; } = RegistryHive.CurrentUser;

    /// <summary>Target verb key (SaveEdit / Toggle / Delete) or the parent key (AddChild).</summary>
    public string SubKey { get; set; } = string.Empty;

    public string ScopeId { get; set; } = string.Empty;

    public string VerbName { get; set; } = string.Empty;

    /// <summary>Draft as the user last saw it (SaveEdit / AddRoot / AddChild).</summary>
    public EditableVerbDraft? Draft { get; set; }

    /// <summary>SaveEdit: whether the command value may be written (DelegateExecute verbs own it).</summary>
    public bool IncludeCommand { get; set; }

    /// <summary>Toggle: target state; true re-enables the verb.</summary>
    public bool Enable { get; set; }

    /// <summary>Restore: the .reg file to import.</summary>
    public string? BackupPath { get; set; }

    /// <summary>When the request was recorded; a stale one is discarded rather than replayed.</summary>
    public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;
}

public static class PendingActionKinds
{
    public const string SaveEdit = "SaveEdit";
    public const string Toggle = "Toggle";
    public const string Delete = "Delete";
    public const string AddRoot = "AddRoot";
    public const string AddChild = "AddChild";
    public const string Restore = "Restore";
}
