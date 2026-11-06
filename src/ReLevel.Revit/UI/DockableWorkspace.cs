using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using ReLevel.Revit.Transfer;
using ReLevel.Revit.UI.Prototype;

namespace ReLevel.Revit.UI;

internal sealed class DockableWorkspace : IDockablePaneProvider, IExternalEventHandler, IDisposable
{
    internal static readonly DockablePaneId Id = new(new Guid("66D5E805-AB2A-45EB-957A-F57678BFEB12"));
    private readonly DockPanel host = new() { Background = System.Windows.Media.Brushes.WhiteSmoke };
    private readonly ContentControl content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Button refresh = new() { Margin = new Thickness(6), Padding = new Thickness(8) };
    private readonly ExternalEvent externalEvent;
    private Window? dialogOwner;
    private WorkspaceController? workspace;
    private Document? document;
    private UIApplication? application;
    private Action? pending;
    private bool running, stale = true, closeRequested;
    private bool refreshRequested;

    public DockableWorkspace()
    {
        externalEvent = ExternalEvent.Create(this);
        DockPanel.SetDock(refresh, Dock.Bottom);
        host.Children.Add(refresh);
        host.Children.Add(content);
        refresh.Click += (_, _) => QueueRefresh();
        UpdateRefreshLabel();
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = host;
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
        data.VisibleByDefault = false;
    }

    public void Show(UIApplication ui)
    {
        application = ui;
        if (!SameDocument(document, ui.ActiveUIDocument?.Document)) stale = true;
        if (stale || workspace is null) Rebuild(ui);
        ui.GetDockablePane(Id).Show();
    }

    private void QueueRefresh()
    {
        if (pending is not null || running) return;
        refreshRequested = true;
        Submit(() => Rebuild(application!));
    }

    private void Run(Action action)
    {
        if (running) { action(); return; }
        if (stale || pending is not null) return;
        Submit(action);
    }

    private void Submit(Action action)
    {
        pending = action;
        content.IsEnabled = refresh.IsEnabled = false;
        var result = externalEvent.Raise();
        if (result != ExternalEventRequest.Accepted)
        {
            pending = null;
            refreshRequested = false;
            content.IsEnabled = !stale;
            refresh.IsEnabled = true;
            MessageBox.Show($"ReLevel: ExternalEvent — {result}", "ReLevel");
        }
    }

    public void Execute(UIApplication ui)
    {
        application = ui;
        var action = pending;
        pending = null;
        var rebuilding = refreshRequested;
        refreshRequested = false;
        // Never apply a queued operation to a different document or invalidated snapshot.
        if (!rebuilding && (stale || !SameDocument(document, ui.ActiveUIDocument?.Document)))
        {
            Invalidate();
            return;
        }
        running = true;
        content.IsEnabled = true;
        try
        {
            if (!rebuilding && (document is null || !document.IsValidObject || document.IsReadOnly || document.IsFamilyDocument))
            {
                Invalidate();
                return;
            }
            action?.Invoke();
            if (workspace?.InspectElementRequested == true) ElementInspectorAction.Run(ui);
        }
        catch (Exception ex)
        {
            closeRequested = true;
            TaskDialog.Show("ReLevel", ex.ToString());
        }
        finally
        {
            running = false;
            if (closeRequested)
            {
                ui.GetDockablePane(Id).Hide();
                Clear();
                stale = true;
                closeRequested = false;
            }
            content.IsEnabled = !stale;
            refresh.IsEnabled = true;
            UpdateRefreshLabel();
        }
    }

    private void Rebuild(UIApplication ui)
    {
        Clear();
        stale = true;
        document = ui.ActiveUIDocument?.Document;
        L.Initialize(ui.Application.Language);
        if (document is null || document.IsFamilyDocument || document.IsReadOnly)
        {
            content.Content = new TextBlock { Text = L.Get("Откройте доступный для изменения документ проекта Revit."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
            UpdateRefreshLabel();
            return;
        }
        var wasRunning = running;
        running = true;
        try
        {
            var levels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(level => level.ProjectElevation).ThenBy(level => level.Name).ThenBy(level => level.Id.Value).ToList();
            var snapshot = levels.Select(level => new PrototypeLevel(level.Id.Value, level.Name, level.ProjectElevation)
            {
                Color = System.Windows.Media.Color.FromRgb((byte)Random.Shared.Next(40, 231),
                    (byte)Random.Shared.Next(40, 231), (byte)Random.Shared.Next(40, 231))
            }).ToList();
            var index = new ModelElementIndex(document, levels);
            using (var loader = new ModelAnalysisLoader()) index.Refresh(progress: loader.Report);
            // Modal tools retain a WPF owner whose native parent is Revit.
            dialogOwner = new Window { ShowInTaskbar = false, Width = 968, Height = 686 };
            new WindowInteropHelper(dialogOwner) { Owner = ui.MainWindowHandle }.EnsureHandle();
            workspace = new WorkspaceController(ui, levels, snapshot, index, () => Run(() => closeRequested = true),
                () => (UIElement)content.Content, Run);
            content.Content = new PrototypeWindow(snapshot, L.UseEnglish, workspace.CreateTransfer, workspace, dialogOwner, Run);
            stale = false;
            content.IsEnabled = true;
        }
        catch
        {
            Clear();
            throw;
        }
        finally { running = wasRunning; UpdateRefreshLabel(); }
    }

    internal void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (!running && SameDocument(e.GetDocument(), document)) Invalidate();
    }

    internal void OnViewActivated(object? sender, ViewActivatedEventArgs e)
    {
        if (!running) Invalidate();
    }

    internal void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (running || sender is not UIApplication ui) return;
        application = ui;
        if (!SameDocument(document, ui.ActiveUIDocument?.Document))
        {
            Clear();
            document = ui.ActiveUIDocument?.Document;
            Invalidate();
        }
    }

    private void Invalidate()
    {
        stale = true;
        pending = null;
        refreshRequested = false;
        content.IsEnabled = false;
        refresh.IsEnabled = true;
        UpdateRefreshLabel();
    }

    // Revit can return distinct managed wrappers for the same native document.
    // Reference equality would clear the workspace on every Idling callback.
    private static bool SameDocument(Document? first, Document? second) =>
        first is null ? second is null : second is not null
            && first.IsValidObject && second.IsValidObject && first.Equals(second);

    private void UpdateRefreshLabel() => refresh.Content = L.UseEnglish
        ? stale ? "Refresh model data" : "Refresh"
        : stale ? "Обновить данные модели" : "Обновить";

    private void Clear()
    {
        workspace?.Dispose();
        workspace = null;
        content.Content = null;
        dialogOwner?.Close();
        dialogOwner = null;
    }

    public string GetName() => "ReLevel workspace";
    public void Dispose() { Clear(); externalEvent.Dispose(); }
}
