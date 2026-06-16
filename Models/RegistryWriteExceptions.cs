using System;
using Microsoft.Win32;

namespace RCMenuManager.Models;

/// <summary>Base for all expected registry-write failures we want UI to recognise.</summary>
public class RegistryWriteException : Exception
{
    public RegistryWriteException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Thrown when a write requires admin and the current process is not elevated.</summary>
public sealed class ElevationRequiredException : RegistryWriteException
{
    public RegistryHive Hive { get; }
    public string SubKey { get; }
    public ElevationRequiredException(RegistryHive hive, string subKey)
        : base($"Write to {hive}\\{subKey} requires administrator privileges.")
    {
        Hive = hive;
        SubKey = subKey;
    }
}

/// <summary>Thrown when a target key already exists and we refuse to overwrite.</summary>
public sealed class RegistryConflictException : RegistryWriteException
{
    public RegistryHive Hive { get; }
    public string SubKey { get; }
    public RegistryConflictException(RegistryHive hive, string subKey)
        : base($"目标键已存在：{hive}\\{subKey}")
    {
        Hive = hive;
        SubKey = subKey;
    }
}
