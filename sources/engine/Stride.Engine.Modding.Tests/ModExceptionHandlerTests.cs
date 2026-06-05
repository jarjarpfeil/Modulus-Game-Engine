using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModExceptionHandlerTests
{
    [Fact]
    public void ExecuteModCode_ActionDoesNotThrow_ExecutesNormally()
    {
        var handler = new ModExceptionHandler();
        int executed = 0;
        handler.ExecuteModCode("test-mod", () => executed++, onDisable: () => { });
        Assert.Equal(1, executed);
    }

    [Fact]
    public void ExecuteModCode_ActionThrows_DisablesMod()
    {
        var handler = new ModExceptionHandler();
        bool disableCalled = false;
        handler.ExecuteModCode("test-mod",
            () => throw new InvalidOperationException("mod error"),
            onDisable: () => disableCalled = true);
        Assert.True(disableCalled);
    }

    [Fact]
    public void ExecuteModCode_ActionThrows_DoesNotPropagate()
    {
        var handler = new ModExceptionHandler();
        var exception = Record.Exception(() =>
            handler.ExecuteModCode("test-mod",
                () => throw new InvalidOperationException("mod error"),
                onDisable: () => { }));
        Assert.Null(exception);
    }

    [Fact]
    public void ExecuteModCode_ReturnsValue_OnSuccess()
    {
        var handler = new ModExceptionHandler();
        var result = handler.ExecuteModCode("test-mod", () => 42, onDisable: () => { });
        Assert.Equal(42, result);
    }

    [Fact]
    public void ExecuteModCode_ReturnsDefault_OnException()
    {
        var handler = new ModExceptionHandler();
        var result = handler.ExecuteModCode<int>("test-mod",
            () => throw new InvalidOperationException("mod error"),
            onDisable: () => { });
        Assert.Equal(0, result);
    }
}
