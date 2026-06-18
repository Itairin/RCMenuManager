using System;

namespace RCMenuManager.Services;

public sealed class PresetConflictException : Exception
{
    public string VerbName { get; }
    public string Scope { get; }

    public PresetConflictException(string scope, string verbName)
        : base($"预设 {scope}/{verbName} 已存在，请勾选「覆盖」后再应用。")
    {
        Scope = scope;
        VerbName = verbName;
    }
}
