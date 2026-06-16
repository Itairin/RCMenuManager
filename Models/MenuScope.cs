using System;

namespace RCMenuManager.Models;

/// <summary>
/// A concrete scope the user is browsing right now. Wraps <see cref="ScopeType"/>
/// with optional file-extension info, plus the registry sub-paths to query.
/// </summary>
public sealed class MenuScope : IEquatable<MenuScope>
{
    public ScopeType Type { get; }

    /// <summary>
    /// Lower-case extension including the dot, e.g. ".txt". Only set when
    /// <see cref="Type"/> is <see cref="ScopeType.FileExtension"/>.
    /// </summary>
    public string? Extension { get; }

    /// <summary>
    /// ProgID resolved for the extension (e.g. "txtfile"). May be null when
    /// the extension has no associated ProgID.
    /// </summary>
    public string? ProgId { get; }

    public string DisplayName { get; }

    private MenuScope(ScopeType type, string displayName, string? extension = null, string? progId = null)
    {
        Type = type;
        DisplayName = displayName;
        Extension = extension;
        ProgId = progId;
    }

    public static MenuScope AllFiles { get; } = new(ScopeType.AllFiles, "文件 (所有文件)");
    public static MenuScope AllFilesystemObjects { get; } = new(ScopeType.AllFilesystemObjects, "文件与文件夹");
    public static MenuScope Folder { get; } = new(ScopeType.Folder, "文件夹");
    public static MenuScope FolderBackground { get; } = new(ScopeType.FolderBackground, "文件夹背景");
    public static MenuScope Drive { get; } = new(ScopeType.Drive, "驱动器");
    public static MenuScope Desktop { get; } = new(ScopeType.Desktop, "桌面");

    public static MenuScope ForExtension(string extension, string? progId)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("Extension must be provided.", nameof(extension));
        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        var label = string.IsNullOrEmpty(progId) ? $"{ext} 文件" : $"{ext} 文件 ({progId})";
        return new MenuScope(ScopeType.FileExtension, label, ext, progId);
    }

    public bool Equals(MenuScope? other)
    {
        if (other is null) return false;
        return Type == other.Type
            && string.Equals(Extension, other.Extension, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ProgId, other.ProgId, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is MenuScope s && Equals(s);

    public override int GetHashCode() => HashCode.Combine(Type, Extension?.ToLowerInvariant(), ProgId?.ToLowerInvariant());

    public override string ToString() => DisplayName;

    /// <summary>
    /// Stable identifier used by the <c>--scope=</c> command-line protocol.
    /// FileExtension scopes are encoded as <c>FileExt:.txt</c>.
    /// </summary>
    public string ScopeId => Type switch
    {
        ScopeType.AllFiles => "AllFiles",
        ScopeType.AllFilesystemObjects => "AllFilesystemObjects",
        ScopeType.Folder => "Folder",
        ScopeType.FolderBackground => "FolderBackground",
        ScopeType.Drive => "Drive",
        ScopeType.Desktop => "Desktop",
        ScopeType.FileExtension => $"FileExt:{Extension}",
        _ => Type.ToString(),
    };

    /// <summary>
    /// Returns the registry sub-path used as the parent of any verb in this
    /// scope (without the hive). For example, Folder -> "Directory\\shell".
    /// FileExtension scopes prefer the ProgID's shell key when one is known,
    /// otherwise fall back to the per-extension shell key.
    /// </summary>
    public string ShellSubKey => Type switch
    {
        ScopeType.AllFiles => @"*\shell",
        ScopeType.AllFilesystemObjects => @"AllFilesystemObjects\shell",
        ScopeType.Folder => @"Directory\shell",
        ScopeType.FolderBackground => @"Directory\Background\shell",
        ScopeType.Drive => @"Drive\shell",
        ScopeType.Desktop => @"DesktopBackground\Shell",
        ScopeType.FileExtension => string.IsNullOrEmpty(ProgId)
            ? $@"{Extension}\shell"
            : $@"{ProgId}\shell",
        _ => @"*\shell",
    };

    /// <summary>HKCU prefix that maps to the same scope (under Software\\Classes).</summary>
    public string HkcuShellSubKey => $@"Software\Classes\{ShellSubKey}";

    /// <summary>Parses an id back to a MenuScope. Throws when the id is unknown.</summary>
    public static MenuScope FromScopeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return AllFiles;
        if (id.StartsWith("FileExt:", StringComparison.OrdinalIgnoreCase))
        {
            var ext = id.Substring("FileExt:".Length);
            return ForExtension(ext, progId: null);
        }
        return id switch
        {
            "AllFiles" => AllFiles,
            "AllFilesystemObjects" => AllFilesystemObjects,
            "Folder" => Folder,
            "FolderBackground" => FolderBackground,
            "Drive" => Drive,
            "Desktop" => Desktop,
            _ => AllFiles,
        };
    }
}
