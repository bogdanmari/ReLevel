using Autodesk.Revit.DB;
using ReLevel.Revit.Logic;

namespace ReLevel.Revit.Transfer;

internal sealed class ViewSheetPlacement(Document document)
{
    public void Place(ElementId sourceViewId, ElementId targetViewId, ViewOperationLog log)
    {
        var stage = L.Get("Размещение на листе");
        List<ElementId> placements;
        try
        {
            placements = new FilteredElementCollector(document).OfClass(typeof(Viewport))
                .Cast<Viewport>().Where(viewport => viewport.ViewId == sourceViewId
                    && viewport.SheetId != ElementId.InvalidElementId)
                .Select(viewport => viewport.Id).ToList();
        }
        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
        catch (Exception ex)
        {
            log.Add(LogSeverity.Error, stage, L.Get("Не удалось собрать размещения исходного вида. Новый вид сохранён без размещения на листах."), ex.ToString());
            return;
        }
        if (placements.Count == 0)
        {
            log.Add(LogSeverity.Info, stage, L.Get("Исходный вид не размещён на листах. Новый вид оставлен без размещения."));
            return;
        }
        foreach (var placementId in placements)
        {
            var createdId = ElementId.InvalidElementId;
            var sheetId = ElementId.InvalidElementId;
            if (!log.Run(stage, () =>
            {
                var source = (Viewport)document.GetElement(placementId);
                sheetId = source.SheetId;
                var center = source.GetBoxCenter();
                if (!Viewport.CanAddViewToSheet(document, sheetId, targetViewId))
                    throw new InvalidOperationException(L.Format($"Новый вид {targetViewId.Value} нельзя разместить на листе {sheetId.Value}."));
                var target = Viewport.Create(document, sheetId, targetViewId, center);
                var replacementId = target.ChangeTypeId(source.GetTypeId());
                if (replacementId != ElementId.InvalidElementId)
                    target = (Viewport)document.GetElement(replacementId);
                target.Rotation = source.Rotation;
                target.LabelOffset = source.LabelOffset;
                target.LabelLineLength = source.LabelLineLength;
                document.Regenerate();
                target.SetBoxCenter(center);
                target.Pinned = source.Pinned;
                createdId = target.Id;
            }, placementId.Value)) continue;
            log.Add(LogSeverity.Info, stage,
                L.Format($"Вид размещён поверх исходного на листе {sheetId.Value}. Новое размещение: {createdId.Value}; исходное: {placementId.Value}."),
                L.Get("Исходный вид и его размещение сохранены. Номер нового видового экрана назначен Revit."), placementId.Value);
        }
    }
}
