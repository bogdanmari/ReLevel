using System.Windows;
using System.Windows.Controls;

namespace ReLevel.Revit.UI.Prototype;

internal sealed partial class PrototypeWindow
{
    private void DistributionFooter(DockPanel content)
    {
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var progress = new TransferProgress();
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 0, 0, 6) };
        Bind(button, ContentProperty, "RunDistribution");
        Bind(button, ToolTipProperty, "DistributeHelp");
        ToolTipService.SetShowOnDisabled(button, true);
        void Update() => button.IsEnabled = levels.Any(level => level.Keep);
        foreach (var level in levels) level.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PrototypeLevel.Keep)) Update(); };
        button.Click += (_, _) =>
        {
            using var controls = new OperationControls(this, progress);
            progress.Begin(0);
            workspace!.Distribute(levels, progress.Report,
                stopped => { progress.Finish(stopped, text[stopped ? "DistributionStopped" : "DistributionComplete"]); controls.Dispose(); },
                () => progress.CancellationRequested);
        };
        footer.Children.Add(button); footer.Children.Add(progress);
        DockPanel.SetDock(footer, Dock.Bottom); content.Children.Add(footer);
        Update();
    }
}
