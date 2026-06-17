using System;
using RCMenuManager.Services;

namespace RCMenuManager.Models;

public sealed class OperationLogEntryViewModel
{
    public DateTime Timestamp { get; }
    public string Op { get; }
    public string ScopeId { get; }
    public string Verb { get; }
    public string Hive { get; }
    public string SubKey { get; }
    public string SuccessText { get; }
    public string? Error { get; }

    public OperationLogEntryViewModel(OperationLogEntry e)
    {
        Timestamp = e.timestamp;
        Op = e.op;
        ScopeId = e.scopeId;
        Verb = e.verb;
        Hive = e.hive.ToString();
        SubKey = e.subKey;
        SuccessText = e.success ? "成功" : "失败";
        Error = e.error;
    }
}
