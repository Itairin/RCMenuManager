namespace RCMenuManager.Models;

public enum DragDropKind { Unknown, File, Folder, Drive }

public sealed record DragDropInfo(string Path, DragDropKind Kind);