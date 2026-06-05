using System.Reflection;
using Stride.Core.Serialization;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class DataSerializerFactoryCacheTests
{
    [Fact]
    public void ClearAssemblySerializers_DoesNotThrow_ForNonRegisteredAssembly()
    {
        // Use an assembly that has no registered serializers (our test assembly)
        var assembly = typeof(DataSerializerFactoryCacheTests).Assembly;

        // Should not throw — clearing an assembly that was never registered is a no-op
        var exception = Record.Exception(() => DataSerializerFactory.ClearAssemblySerializers(assembly));
        Assert.Null(exception);
    }

    [Fact]
    public void ClearAssemblySerializers_NullAssembly_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DataSerializerFactory.ClearAssemblySerializers(null!));
    }

    [Fact]
    public void ClearAssemblySerializers_CalledTwice_DoesNotThrow()
    {
        // Calling clear twice on the same assembly should be idempotent
        var assembly = typeof(DataSerializerFactoryCacheTests).Assembly;
        DataSerializerFactory.ClearAssemblySerializers(assembly);
        var exception = Record.Exception(() => DataSerializerFactory.ClearAssemblySerializers(assembly));
        Assert.Null(exception);
    }
}
