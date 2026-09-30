using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace ReLevel.Revit.UI;

internal sealed class ReLevelPaneController : IExternalEventHandler, IDisposable
{
    public static DockablePaneId PaneId { get; } = new(new Guid("9F4BD94A-4E33-4E68-8D4F-B8F08B34E70B"));
    public ReLevelDockPane Pane { get; } = new();
    private readonly ExternalEvent externalEvent;
    private Document? document;
    private TransferPanel? panel;
    private PendingAction? pending;
    private bool executing;
    private bool dirty = true;
    private bool suspended;
    private long revision;

    public ReLevelPaneController() => externalEvent = ExternalEvent.Create(this);
    public string GetName() => "ReLevel panel actions";

    public void Show(UIApplication application)
    {
        L.Initialize(application.Application.Language);
        suspended = false;
        application.GetDockablePane(PaneId).Show();
        Synchronize(application);
    }

    public void OnDocumentChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (!ReferenceEquals(args.GetDocument(), document)) return;
        revision++;
        dirty = true;
    }

    public void OnIdling(object? sender, IdlingEventArgs args)
    {
        if (sender is not UIApplication application || executing || pending is not null) return;
        // Release closed documents while hidden, but defer collection until the pane is shown.
        try
        {
            var visible = DockablePane.PaneExists(PaneId) && application.GetDockablePane(PaneId).IsShown();
            Synchronize(application, visible);
        }
        catch (Exception ex)
        {
            Suspend();
            Pane.ShowMessage(L.Format($"Не удалось обновить панель: {ex.Message}"));
        }
    }

    private void Synchronize(UIApplication application, bool refresh = true)
    {
        var active = application.ActiveUIDocument?.Document;
        if (!ReferenceEquals(active, document))
        {
            document = active;
            panel = null;
            suspended = false;
            revision++;
            dirty = true;
            Pane.ShowMessage(L.Get("Откройте доступный для изменения документ проекта Revit."));
        }
        if (!refresh || suspended) return; // No further model reads after a critical operation failure.
        if (active is null || active.IsFamilyDocument || active.IsReadOnly)
        {
            panel = null;
            Pane.ShowMessage(L.Get("Откройте доступный для изменения документ проекта Revit."));
            return;
        }
        L.Initialize(application.Application.Language);
        if (panel is null)
        {
            var created = new TransferPanel(application, this);
            if (suspended) return;
            panel = created;
            Pane.Content = panel;
        }
        else if (dirty) panel.Reload();
        dirty = false;
    }

    // Invoked by WPF; neither the document nor its elements are read here.
    public void Request(TransferPanel origin, Action action)
    {
        if (suspended || executing || pending is not null || !ReferenceEquals(origin, panel)) return;
        pending = new(origin, document, revision, action);
        origin.IsEnabled = false;
        try
        {
            if (externalEvent.Raise() == ExternalEventRequest.Accepted) return;
            origin.ShowMessage(L.Get("Revit занят. Повторите действие после завершения текущей команды."));
        }
        catch (Exception ex) { origin.ShowMessage(L.Format($"Не удалось запустить действие: {ex.Message}")); }
        pending = null;
        origin.IsEnabled = true;
    }

    public void Execute(UIApplication application)
    {
        var request = pending;
        pending = null;
        if (request is null) return;
        executing = true;
        try
        {
            if (!ReferenceEquals(application.ActiveUIDocument?.Document, request.Document)
                || request.Revision != revision || dirty || suspended)
            {
                Synchronize(application);
                panel?.ShowMessage(L.Get("Документ изменился. Таблица обновлена; повторите действие."));
                return;
            }
            if (request.Document is null || request.Document.IsReadOnly || request.Document.IsModifiable)
            {
                request.Panel.ShowMessage(L.Get("Revit занят. Повторите действие после завершения текущей команды."));
                return;
            }
            request.Action();
        }
        catch (Exception ex)
        {
            Suspend();
            TaskDialog.Show("ReLevel", L.Format($"Операция остановлена: {ex.Message}"));
        }
        finally
        {
            executing = false;
            request.Panel.IsEnabled = !suspended;
        }
    }

    public void Suspend()
    {
        suspended = true;
        dirty = false;
        panel = null;
        Pane.ShowMessage(L.Get("Панель остановлена. Проверьте документ и откройте ReLevel заново."));
    }

    public void Dispose()
    {
        pending = null;
        panel = null;
        document = null;
        Pane.Content = null;
        externalEvent.Dispose();
    }

    private sealed record PendingAction(TransferPanel Panel, Document? Document, long Revision, Action Action);
}
