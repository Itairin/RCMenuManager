namespace RCMenuManager.Services;

/// <summary>
/// Reads and writes the small set of preferences in <see cref="AppSettings"/>.
/// Implementations must never throw: a read-only or corrupt settings file cannot
/// be allowed to block registry edits.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
}
