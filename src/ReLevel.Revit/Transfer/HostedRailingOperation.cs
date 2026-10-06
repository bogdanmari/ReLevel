using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace ReLevel.Revit.Transfer;

internal sealed class HostedRailingOperation(Element element) : IElementTransferOperation
{
    private readonly ElementId id = element.Id;
    private readonly ElementId hostId = ((Railing)element).HostId;
    private readonly double offset = LevelOffsetBinding.Require(element,
        RailingCase.Binding.OffsetParameter, StorageType.Double).AsDouble();

    public string SuccessMessage => L.Get("Уровень ограждения изменён с восстановлением хоста.");

    public void Apply(Document document, ElementId target)
    {
        // The caller owns one transaction for detaching, changing level and rehosting.
        var railing = (Railing)document.GetElement(id);
        railing.RemoveHost();
        document.Regenerate();

        railing = (Railing)document.GetElement(id);
        var level = LevelOffsetBinding.Require(railing, RailingCase.Binding.LevelParameter, StorageType.ElementId);
        if (level.IsReadOnly)
            throw new InvalidOperationException(L.Get("Кейс 10: Base Level остался заблокированным после отсоединения."));
        if (!level.Set(target))
            throw new InvalidOperationException(L.Get("Revit отклонил запись целевого уровня."));
        document.Regenerate();

        railing = (Railing)document.GetElement(id);
        railing.HostId = hostId;
        document.Regenerate();

        // Restore the host-relative offset; do not compensate the temporary Z displacement.
        railing = (Railing)document.GetElement(id);
        var baseOffset = LevelOffsetBinding.Require(railing, RailingCase.Binding.OffsetParameter, StorageType.Double);
        if (Math.Abs(baseOffset.AsDouble() - offset) > 1e-9 && (baseOffset.IsReadOnly || !baseOffset.Set(offset)))
            throw new InvalidOperationException(L.Get("Кейс 10: не удалось восстановить Base Offset после возврата хоста."));
        document.Regenerate();
    }
}
