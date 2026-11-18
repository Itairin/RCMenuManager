using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RCMenuManager.Helpers;
using RCMenuManager.Models;
using RCMenuManager.Services;
using RCMenuManager.ViewModels;

namespace RCMenuManager;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = default!;

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RCMenuManager", "crash.log");

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledDomainException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<RegistryService>();
        services.AddSingleton<MenuParserService>();
        services.AddSingleton<IconService>();
        services.AddSingleton<IRegistryWriter, Win32RegistryWriter>();
        services.AddSingleton<IRegistrySnapshotTool, RegExeSnapshotTool>();
        services.AddSingleton<IBackupService>(sp => new BackupService(
            BackupService.DefaultBackupDir(),
            sp.GetRequiredService<IRegistrySnapshotTool>()));
        services.AddSingleton<IOperationLog, OperationLogService>();
        services.AddSingleton<WinVersionService>();
        services.AddSingleton<IWin11MenuService>(sp => new Win11MenuService(
            sp.GetRequiredService<RegistryWriteService>()));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton(sp => new MenuItemOriginService(
            sp.GetRequiredService<IOperationLog>(),
            sp.GetRequiredService<IPresetService>()));
        services.AddSingleton<PendingActionStore>();
        services.AddSingleton<IFileTypeService, FileTypeService>();
        services.AddSingleton<IPresetService>(sp => new PresetService(
            sp.GetRequiredService<IRegistryWriter>(),
            sp.GetRequiredService<RegistryWriteService>(),
            sp.GetRequiredService<RegistryService>()));
        services.AddSingleton<IShellContextMenuReader>(ShellContextMenuReader.Instance);
        services.AddSingleton<RegistryWriteService>(sp =>
            new RegistryWriteService(
                sp.GetRequiredService<IRegistryWriter>(),
                sp.GetRequiredService<IBackupService>(),
                sp.GetRequiredService<IOperationLog>(),
                () => UacHelper.IsAdministrator(),
                notifyShell: null,
                importFile: sp.GetRequiredService<IBackupService>().Import));
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var vm = Services.GetRequiredService<MainViewModel>();
        var pendingScope = ParseScopeArg(e.Args);
        if (!string.IsNullOrEmpty(pendingScope))
        {
            // MainViewModel constructor has already run; PendingScopeId is read
            // by ResolvePendingScope on next SelectedScope assignment, so we
            // re-trigger the resolution explicitly.
            vm.PendingScopeId = pendingScope;
            ApplyPendingScope(vm);
        }

        // ShellNew/SendTo enumeration is machine-dependent and slow on first
        // use; warm it off the UI thread so the Preview tab's first right-click
        // does not stall while HKCR is walked.
        _ = System.Threading.Tasks.Task.Run(SystemMenuCatalog.Prewarm);

        var window = Services.GetRequiredService<MainWindow>();
        window.DataContext = vm;
        window.Show();

        if (HasResumeFlag(e.Args))
        {
            // Deferred until the window exists so the replay's status / error banner is
            // visible; the action itself is deleted from disk before it runs.
            window.Dispatcher.BeginInvoke(new Action(() => _ = vm.RunPendingActionAsync()));
        }
    }

    private static bool HasResumeFlag(string[] args)
    {
        foreach (var a in args)
            if (string.Equals(a, "--resume", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string? ParseScopeArg(string[] args)
    {
        foreach (var a in args)
        {
            if (a.StartsWith("--scope=", StringComparison.OrdinalIgnoreCase))
                return a.Substring("--scope=".Length);
        }
        return null;
    }

    private static void ApplyPendingScope(MainViewModel vm)
    {
        if (string.IsNullOrEmpty(vm.PendingScopeId)) return;
        // The id does not carry a ProgID; resolve it again or the extension scope
        // comes back with only half of its registry paths.
        var probe = MenuScope.FromScopeId(vm.PendingScopeId);
        var scope = probe;
        if (probe.Type == ScopeType.FileExtension && !string.IsNullOrEmpty(probe.Extension))
        {
            var progId = vm.ResolveProgIdFor(probe.Extension!);
            scope = MenuScope.FromScopeId(vm.PendingScopeId, progId);
        }
        foreach (var s in vm.Scopes)
            if (s.Scope.Equals(scope))
            {
                vm.SelectedScope = s;
                return;
            }
        if (scope.Type == ScopeType.FileExtension)
        {
            var opt = new ScopeOption(scope.DisplayName, scope);
            vm.Scopes.Add(opt);
            vm.SelectedScope = opt;
        }
    }

    private static void OnUnhandledDomainException(object sender, UnhandledExceptionEventArgs e)
        => WriteCrash(e.ExceptionObject as Exception);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        => WriteCrash(e.Exception);

    private static void WriteCrash(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
