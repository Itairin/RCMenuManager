using RCMenuManager.Models;

namespace RCMenuManager.Services;

public interface IFileTypeService
{
    DragDropInfo Identify(string path);
}