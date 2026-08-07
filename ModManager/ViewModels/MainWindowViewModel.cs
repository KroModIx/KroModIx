using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ModManager.Services;
using ModManager.Views;

namespace ModManager.ViewModels;

/// <summary>
/// Haupt-VM. In M1 nur Plumbing (Settings/About öffnen). Sidebar-Content,
/// GameDiscovery und Plugin-TabHost kommen ab M2.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;

    public MainWindowViewModel(IServiceProvider services)
    {
        _services = services;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = _services.GetRequiredService<SettingsWindowViewModel>();
        var window = new SettingsWindow { DataContext = vm };
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            window.ShowDialog(desktop.MainWindow);
        }
        else
        {
            window.Show();
        }
    }

    [RelayCommand]
    private void OpenAbout()
    {
        var updateService = _services.GetRequiredService<HostUpdateService>();
        var window = new AboutWindow(updateService);
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            window.ShowDialog(desktop.MainWindow);
        }
        else
        {
            window.Show();
        }
    }
}
