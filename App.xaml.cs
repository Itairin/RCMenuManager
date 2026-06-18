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
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IOperationLog, OperationLogService>();
        services.AddSingleton<WinVersionService>();
        services.AddSingleton<IWin11MenuService, Win11MenuService>();
        services.AddSingleton<IFileTypeService, FileTypeService>();
        services.AddSingleton<IPresetService, PresetService>();
        services.AddSingleton<RegistryWriteService>(sp =>
            new RegistryWriteService(
                sp.GetRequiredService<IRegistryWriter>(),
                sp.GetRequiredService<IBackupService>(),
                sp.GetRequiredService<IOperationLog>(),
                () => UacHelper.IsAdministrator()));
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

        var window = Services.GetRequiredService<MainWindow>();
        window.DataContext = vm;
        window.Show();
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
        var scope = MenuScope.FromScopeId(vm.PendingScopeId);
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
