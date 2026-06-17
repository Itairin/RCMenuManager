using System.Collections.Generic;

namespace RCMenuManager.Services;

public interface IOperationLog
{
    void Append(OperationLogEntry entry);
    IReadOnlyList<OperationLogEntry> ReadAll();
}
