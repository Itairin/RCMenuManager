using System.IO;
using RCMenuManager.Models;

namespace RCMenuManager.Services;

public sealed class FileTypeService : IFileTypeService
{
    public DragDropInfo Identify(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new DragDropInfo(path ?? string.Empty, DragDropKind.Unknown);

        if (IsDriveRoot(path)) return new DragDropInfo(path, DragDropKind.Drive);
        if (Directory.Exists(path)) return new DragDropInfo(path, DragDropKind.Folder);
        if (File.Exists(path)) return new DragDropInfo(path, DragDropKind.File);
        return new DragDropInfo(path, DragDropKind.Unknown);
    }

    private static bool IsDriveRoot(string path)
    {
        if (path.Length < 2 || path.Length > 3) return false;
        if (!char.IsLetter(path[0]) || path[1] != ':') return false;
        return path.Length == 2 || path[2] == '\\' || path[2] == '/';
    }
}