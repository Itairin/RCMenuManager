namespace RCMenuManager.Services;

public interface IOperationLog
{
    void Append(OperationLogEntry entry);
}
