using System;

namespace RCMenuManager.Services;

public class WinVersionService
{
    public virtual int Build => Environment.OSVersion.Version.Build;
    public bool IsWindows11 => Build >= 22000;
}