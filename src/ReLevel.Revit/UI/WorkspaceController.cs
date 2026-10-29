using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ReLevel.Revit.Transfer;
using ReLevel.Revit.Logic;
using ReLevel.Revit.UI.Prototype;

namespace ReLevel.Revit.UI;

internal sealed class WorkspaceController(UIApplication application, IReadOnlyList<Level> levels,
    IReadOnlyList<PrototypeLevel> snapshot, ModelElementIndex index)
    : IPrototypeWorkspace, IDisposable
{
    private Window owner = null!;
    private TransferPanel transfer = null!;
    private LevelViewGraphics? graphics;
    private readonly Dictionary<long, LevelViewGraphics> viewGraphics = [];
    private bool busy;
    public bool InspectElementRequested => transfer.InspectElementRequested;

    public IPrototypeTransferPage CreateTransfer(Window window, bool english)
    {
        owner = window;
        L.UseEnglish = english;
        UpdateCounts();
        index.Changed += UpdateCounts;
        owner.Closing += (_, e) => { if (busy) e.Cancel = true; };
        return transfer = new TransferPanel(owner, application, levels, modern: true, index: index, refreshIndex: RefreshIndex);
    }

    private void UpdateCounts()
    {
        foreach (var row in snapshot)
        {
            row.ModelElementCount = index.Count(row.Id);
            row.AnalysisIncomplete = index.Errors.Count > 0;
        }
    }

    private void RefreshIndex()
    {
        using var loader = new ModelAnalysisLoader();
        index.Refresh(transfer.AdditionalElementIds, loader.Report);
    }

    public void RunDebug(WorkspaceDebugTool tool) => transfer.RunDebug(tool);

    public void Distribute(IReadOnlyList<PrototypeLevel> choices, Action<int, int, long?> progress, Action<bool> finished, Func<bool> cancelled)
    {
        if (busy) return;
        var retained = choices.Where(row => row.Keep).Select(row => new RetainedLevel(row.Id, row.Elevation)).ToList();
        if (retained.Count == 0) return;
        var protectedIds = choices.Where(row => row.Keep && row.Protect).Select(row => row.Id).ToHashSet();
        busy = true;
        ElementTransferReport? report = null;
        Exception? failure = null;
        try
        {
            report = new DistributionService(application.ActiveUIDocument.Document, levels)
                .Execute(retained, protectedIds, transfer.AdditionalElementIds, progress, index.ElementIds.ToList(), cancelled);
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            busy = false;
            finished(report is null || report.Stopped);
        }
        if (failure is not null)
        {
            OperationDialogs.Show(owner, L.Get("Операция отменена"), failure.Message);
            owner.Close(); return;
        }
        var summary = L.Format($"Перенесено: {report!.Transferred}. Пропущено: {report.Skipped}. Ошибки: {report.Failed}.");
        new ElementTransferResultsWindow(owner, summary, report.Items.Select(ElementTransferResultRow.From).ToList(), report.Stopped).ShowDialog();
        if (report.Stopped) { owner.Close(); return; }
        try { RefreshIndex(); }
        catch (Exception ex)
        {
            OperationDialogs.Show(owner, L.Get("Операция отменена"), ex.Message);
            owner.Close(); return;
        }
        transfer.RefreshAfterDistribution();
    }

    public ColorPageState PrepareColors()
    {
        try
        {
            var view = application.ActiveUIDocument.ActiveView;
            graphics = null;
            if (viewGraphics.TryGetValue(view.Id.Value, out var existing))
            {
                existing.Refresh(levels);
                graphics = existing;
            }
            else
            {
                graphics = new LevelViewGraphics(view, levels, index.Case);
                viewGraphics.Add(view.Id.Value, graphics);
            }
            return new(true, Diagnostics(), levels.ToDictionary(level => level.Id.Value, level => graphics.Count(level.Id.Value)));
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException ex)
        {
            Stop(ex);
            return new(false, ex.Message, new Dictionary<long, int>());
        }
        catch (Exception ex) { return new(false, ex.Message, new Dictionary<long, int>()); }
    }

    public string ApplyColors(IReadOnlyList<PrototypeLevel> choices, bool reset)
    {
        if (busy || graphics is null) return "";
        var controls = (UIElement)owner.Content;
        busy = true; controls.IsEnabled = false;
        Exception? failure = null;
        string message = "";
        try
        {
            graphics.Refresh(levels);
            var actions = choices.ToDictionary(row => row.Id, row => new LevelGraphicsChoice(
                reset || !row.Selected ? LevelGraphicsAction.Unchanged : row.Hidden ? LevelGraphicsAction.Hide : LevelGraphicsAction.Color,
                new Autodesk.Revit.DB.Color(row.Color.R, row.Color.G, row.Color.B)));
            message = graphics.Apply(actions);
            application.ActiveUIDocument.RefreshActiveView();
            message += "\n" + Diagnostics();
        }
        catch (Exception ex) { failure = ex; }
        finally { busy = false; controls.IsEnabled = true; }
        if (failure is not null) Stop(failure);
        return message;
    }

    private string Diagnostics()
    {
        var message = L.Get("Вид") + ": " + application.ActiveUIDocument.ActiveView.Name;
        if (graphics!.Diagnostics.Count > 0)
            message += "\n" + L.Format($"Ошибок чтения элементов: {graphics.Diagnostics.Count}.")
                + "\n" + string.Join("\n", graphics.Diagnostics);
        return message;
    }

    private void Stop(Exception exception)
    {
        OperationDialogs.Show(owner, L.Get("Цвета по уровням"), exception.Message);
        owner.Close();
    }

    public void Dispose()
    {
        index.Changed -= UpdateCounts;
        foreach (var session in viewGraphics.Values) session.Dispose();
        viewGraphics.Clear(); graphics = null;
    }
}
