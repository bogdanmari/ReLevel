namespace ReLevel.Core.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void CoreAssemblyCanBeReferenced()
    {
        Assert.Equal("ReLevel.Core", typeof(Core.AssemblyMarker).Assembly.GetName().Name);
    }
}
