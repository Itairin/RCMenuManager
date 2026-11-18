using System;
using System.IO;
using System.Text.Json;

namespace RCMenuManager.Services;

/// <summary>
/// Holds the one operation that may survive an elevation relaunch.
/// <para>
/// The file is only consumed when the new process was started with <c>--resume</c>,
/// so a copy left behind by a crash can never fire on an unrelated launch. It also
/// expires: replaying "delete this verb" twenty minutes later is not the same
/// request the user made.
/// </para>
/// </summary>
public sealed class PendingActionStore
{
    /// <summary>How long a recorded request stays valid.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Func<DateTime> _now;

    public PendingActionStore() : this(DefaultPath(), () => DateTime.UtcNow) { }

    public PendingActionStore(string path, Func<DateTime>? now = null)
    {
        _path = path;
        _now = now ?? (() => DateTime.UtcNow);
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "pending-action.json");

    /// <summary>Where the record lives; exposed for diagnostics and tests.</summary>
    public string FilePath => _path;

    public void Save(PendingAction action)
    {
        action.RecordedUtc = _now();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(action, JsonOpts));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Reads the recorded action without consuming it, returning null when there is
    /// none, it is unreadable, or it is older than <see cref="MaxAge"/>.
    /// </summary>
    public PendingAction? Peek()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var action = JsonSerializer.Deserialize<PendingAction>(File.ReadAllText(_path), JsonOpts);
            if (action is null || string.IsNullOrEmpty(action.Kind)) return null;
            if (_now() - action.RecordedUtc > MaxAge) return null;
            return action;
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
