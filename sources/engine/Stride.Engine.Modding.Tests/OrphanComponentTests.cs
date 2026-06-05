using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class OrphanComponentTests
{
    [Fact]
    public void OrphanComponent_PreservesOriginalTypeName()
    {
        var orphan = new OrphanComponent
        {
            OriginalTypeName = "MyMod.RotatingComponent",
            ModId = "com.example.my-mod",
            RawData = [0x01, 0x02, 0x03]
        };
        Assert.Equal("MyMod.RotatingComponent", orphan.OriginalTypeName);
        Assert.Equal("com.example.my-mod", orphan.ModId);
        Assert.Equal([0x01, 0x02, 0x03], orphan.RawData);
    }

    [Fact]
    public void OrphanComponent_DisplayName_ShowsMissingInfo()
    {
        var orphan = new OrphanComponent
        {
            OriginalTypeName = "MyMod.RotatingComponent",
            ModId = "com.example.my-mod"
        };
        Assert.Contains("RotatingComponent", orphan.DisplayName);
        Assert.Contains("com.example.my-mod", orphan.DisplayName);
    }

    [Fact]
    public void OrphanComponent_CanRehydrate_WhenModAvailable()
    {
        var orphan = new OrphanComponent
        {
            OriginalTypeName = "MyMod.RotatingComponent",
            ModId = "com.example.my-mod",
            RawData = [0x01, 0x02, 0x03]
        };
        Assert.True(orphan.CanRehydrate);
    }

    [Fact]
    public void OrphanComponent_CanRehydrate_FalseWithoutData()
    {
        var orphan = new OrphanComponent
        {
            OriginalTypeName = "MyMod.RotatingComponent",
            ModId = "com.example.my-mod",
            RawData = []
        };
        Assert.False(orphan.CanRehydrate);
    }
}
