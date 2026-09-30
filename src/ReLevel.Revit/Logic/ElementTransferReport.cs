namespace ReLevel.Revit.Logic;

internal enum ElementTransferStatus { Transferred, Skipped, Failed }
internal sealed record ElementTransferResult(long Id, ElementTransferStatus Status, string Reason);

internal sealed class ElementTransferReport
{
    public List<ElementTransferResult> Items { get; } = [];
    public bool Stopped { get; set; }
    public int Transferred => Items.Count(item => item.Status == ElementTransferStatus.Transferred);
    public int Skipped => Items.Count(item => item.Status == ElementTransferStatus.Skipped);
    public int Failed => Items.Count(item => item.Status == ElementTransferStatus.Failed);
}
