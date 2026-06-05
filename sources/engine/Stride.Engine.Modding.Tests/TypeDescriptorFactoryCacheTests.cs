using System.Reflection;
using Stride.Core.Reflection;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class TypeDescriptorFactoryCacheTests
{
    [Fact]
    public void ClearAssemblyCache_RemovesDescriptorsForAssembly()
    {
        var factory = new TypeDescriptorFactory();
        var descriptor = factory.Find(typeof(string));
        Assert.NotNull(descriptor);
        var descriptor2 = factory.Find(typeof(string));
        Assert.Same(descriptor, descriptor2);
        factory.ClearAssemblyCache(typeof(string).Assembly);
        var descriptor3 = factory.Find(typeof(string));
        Assert.NotNull(descriptor3);
        Assert.NotSame(descriptor, descriptor3);
    }

    [Fact]
    public void ClearAssemblyCache_DoesNotAffectOtherAssemblies()
    {
        var factory = new TypeDescriptorFactory();
        var stringDesc = factory.Find(typeof(string));
        var intDesc = factory.Find(typeof(int));
        factory.ClearAssemblyCache(typeof(TypeDescriptorFactoryCacheTests).Assembly);
        var stringDesc2 = factory.Find(typeof(string));
        var intDesc2 = factory.Find(typeof(int));
        Assert.Same(stringDesc, stringDesc2);
        Assert.Same(intDesc, intDesc2);
    }
}
