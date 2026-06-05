using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModLifecycleManagerTests
{
    [Fact]
    public void RegisterEntity_RecordsOwnership()
    {
        var scope = new ModScope("test-mod");
        var entityId = Guid.NewGuid();
        scope.OwnedEntities.Add(entityId);
        Assert.Single(scope.OwnedEntities);
        Assert.Contains(entityId, scope.OwnedEntities);
    }

    [Fact]
    public void RegisterSubscription_RecordsOwnership()
    {
        var scope = new ModScope("test-mod");
        Action<string> handler = _ => { };
        scope.OwnedSubscriptions.Add((typeof(string), handler));
        Assert.Single(scope.OwnedSubscriptions);
    }

    [Fact]
    public void ModScope_OwnedCollections_AreEmptyByDefault()
    {
        var scope = new ModScope("test-mod");
        Assert.Empty(scope.OwnedEntities);
        Assert.Empty(scope.OwnedSubscriptions);
        Assert.Empty(scope.OwnedProcessors);
        Assert.Empty(scope.OwnedComponents);
        Assert.Empty(scope.CachedReflectionMembers);
        Assert.Empty(scope.StaticReferenceCleanup);
    }

    [Fact]
    public void ModScope_NullModId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ModScope(null!));
    }
}
