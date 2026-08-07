using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ModManager.ViewModels;

namespace ModManager;

/// <summary>
/// MVVM-Standard: erzeugt den View zu einem ViewModel per Namenskonvention
/// (…ViewModel → …View im Views-Namespace). Beim ModManager haben wir
/// aktuell nur Fenster mit explizitem <c>DataContext</c>, aber der Locator
/// bleibt für zukünftige eingebettete VMs im TabHost bereit.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;
        var vmName = data.GetType().FullName!;
        var viewName = vmName.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", string.Empty, StringComparison.Ordinal);
        var type = Type.GetType(viewName);
        if (type is null)
            return new TextBlock { Text = $"Not found: {viewName}" };
        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
