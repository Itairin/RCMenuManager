using System;
using System.Diagnostics;

namespace RCMenuManager.Services;

/// <summary>
/// Snapshot side of the backup pipeline (export / import a registry sub-tree).
/// Abstracted so <see cref="BackupService"/> can be tested without spawning
/// reg.exe, and so an alternative implementation can be plugged in on machines
/// where external processes are restricted.
/// </summary>
public interface IRegistrySnapshotTool
{
    /// <summary>Exports <paramref name="fullKeyPath"/> (e.g. <c>HKCU\Software\Classes\*\shell\x</c>) to a .reg file.</summary>
    void Export(string fullKeyPath, string filePath);

    /// <summary>Imports a .reg file, overwriting the keys it contains.</summary>
    void Import(string filePath);
}

/// <summary>
/// Default implementation backed by reg.exe. A timeout is applied because a
/// hung child process would otherwise freeze the caller forever.
/// </summary>
public sealed class RegExeSnapshotTool : IRegistrySnapshotTool
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public void Export(string fullKeyPath, string filePath) =>
        Run(new[] { "export", fullKeyPath, filePath, "/y" }, "export");

    public void Import(string filePath) =>
        Run(new[] { "import", filePath }, "import");

    private static void Run(string[] arguments, string operation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 reg.exe");
        if (!proc.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException($"reg.exe {operation} 超时（>{Timeout.TotalSeconds:0} 秒）");
        }

        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"reg.exe {operation} 失败 (exit={proc.ExitCode}): {err}");
        }
    }
}
