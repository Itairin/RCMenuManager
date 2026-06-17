using System;
using System.IO;
using RCMenuManager.Models;
using RCMenuManager.Services;
using Xunit;

namespace RCMenuManager.Tests;

public class FileTypeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileTypeService _svc = new();

    public FileTypeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RCMenuManagerDragDropTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Identify_drive_root_uppercase() =>
        Assert.Equal(DragDropKind.Drive, _svc.Identify(@"C:\").Kind);

    [Fact]
    public void Identify_drive_root_lowercase_no_backslash() =>
        Assert.Equal(DragDropKind.Drive, _svc.Identify(@"d:").Kind);

    [Fact]
    public void Identify_drive_root_with_forward_slash() =>
        Assert.Equal(DragDropKind.Drive, _svc.Identify(@"E:/").Kind);

    [Fact]
    public void Identify_existing_folder() =>
        Assert.Equal(DragDropKind.Folder, _svc.Identify(_tempDir).Kind);

    [Fact]
    public void Identify_existing_file() =>
        Assert.Equal(DragDropKind.File, _svc.Identify(CreateTestFile("a.txt")).Kind);

    private string CreateTestFile(string name)
    {
        var p = Path.Combine(_tempDir, name);
        File.WriteAllText(p, "hello");
        return p;
    }

    [Fact]
    public void Identify_nonexistent_returns_unknown() =>
        Assert.Equal(DragDropKind.Unknown, _svc.Identify(Path.Combine(_tempDir, "nope.bin")).Kind);

    [Fact]
    public void Identify_empty_string_returns_unknown() =>
        Assert.Equal(DragDropKind.Unknown, _svc.Identify(string.Empty).Kind);

    [Fact]
    public void Identify_null_returns_unknown() =>
        Assert.Equal(DragDropKind.Unknown, _svc.Identify(null!).Kind);

    [Fact]
    public void Identify_preserves_path() =>
        Assert.Equal(@"C:\", _svc.Identify(@"C:\").Path);
}
