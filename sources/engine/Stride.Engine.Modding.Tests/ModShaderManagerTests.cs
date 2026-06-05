using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModShaderManagerTests
{
    [Fact]
    public void RegisterModShaders_EmptyManifest_DoesNotThrow()
    {
        var manager = new ModShaderManager();
        var manifest = new ModManifest { Id = "test-mod", Name = "Test", Version = "1.0.0", ApiVersion = "1.0" };
        var exception = Record.Exception(() => manager.RegisterModShaders("test-mod", manifest));
        Assert.Null(exception);
    }

    [Fact]
    public void UnregisterModShaders_NoRegistration_DoesNotThrow()
    {
        var manager = new ModShaderManager();
        var exception = Record.Exception(() => manager.UnregisterModShaders("test-mod"));
        Assert.Null(exception);
    }

    [Fact]
    public void RegisterModShaders_TracksRegisteredShaders()
    {
        var manager = new ModShaderManager();
        var manifest = new ModManifest
        {
            Id = "test-mod", Name = "Test", Version = "1.0.0", ApiVersion = "1.0",
            Shaders = [new ModShaderDeclaration { Name = "CustomPBR", Path = "shaders/custom-pbr.sdbundle" }]
        };
        manager.RegisterModShaders("test-mod", manifest);
        Assert.True(manager.HasModShaders("test-mod"));
        manager.UnregisterModShaders("test-mod");
        Assert.False(manager.HasModShaders("test-mod"));
    }
}
