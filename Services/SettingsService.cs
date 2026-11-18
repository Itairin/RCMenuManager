using System;
using System.IO;
using System.Text.Json;

namespace RCMenuManager.Services;

/// <summary>
/// Persists <see cref="AppSettings"/> next to the backups and the operation log
/// (%LocalAppData%\RCMenuManager\settings.json).
/// <para>
/// Every failure path is non-fatal: a corrupt, locked or read-only file falls back
/// to defaults instead of stopping the app from starting.
/// </para>
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    public AppSettings Current { get; }

    public SettingsService() : this(DefaultPath()) { }

    public SettingsService(string path)
    {
        _path = path;
        Current = Load(path);
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "settings.json");

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch (IOException) { /* another instance or AV holds the file; keep going */ }
        catch (UnauthorizedAccessException) { }
    }

    internal static AppSettings Load(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpts);
                if (loaded is not null) return Sanitize(loaded);
            }
        }
        catch
        {
            // Corrupt JSON: behave like a first run rather than crashing on startup.
        }
        return new AppSettings();
    }

    /// <summary>Clamps values a hand-edited file (or a different DPI) could push out of range.</summary>
    internal static AppSettings Sanitize(AppSettings s)
    {
        if (s.WindowWidth < 480 || s.WindowWidth > 20000) s.WindowWidth = 1180;
        if (s.WindowHeight < 320 || s.WindowHeight > 20000) s.WindowHeight = 720;
        if (s.ListColumnRatio < 0.2 || s.ListColumnRatio > 0.85) s.ListColumnRatio = 0.606;
        return s;
    }
}
