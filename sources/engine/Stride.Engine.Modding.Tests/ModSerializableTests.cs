using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModSerializableTests
{
    [Fact]
    public void ModStateStore_SaveAndLoad_RoundTrips()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid():N}");
        try
        {
            var store = new ModStateStore(storeDir);
            var data = new byte[] { 1, 2, 3, 4, 5 };
            store.SaveState("test-mod", data, new Version(1, 0, 0));
            Assert.True(store.HasState("test-mod"));
            var loaded = store.LoadState("test-mod");
            Assert.NotNull(loaded);
            Assert.Equal(data, loaded.Data);
            Assert.Equal(new Version(1, 0, 0), loaded.Version);
        }
        finally { if (Directory.Exists(storeDir)) Directory.Delete(storeDir, recursive: true); }
    }

    [Fact]
    public void ModStateStore_HasState_ReturnsFalseForMissing()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid():N}");
        try
        {
            var store = new ModStateStore(storeDir);
            Assert.False(store.HasState("nonexistent-mod"));
        }
        finally { if (Directory.Exists(storeDir)) Directory.Delete(storeDir, recursive: true); }
    }

    [Fact]
    public void ModStateStore_LoadState_ReturnsNullForMissing()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid():N}");
        try
        {
            var store = new ModStateStore(storeDir);
            Assert.Null(store.LoadState("nonexistent-mod"));
        }
        finally { if (Directory.Exists(storeDir)) Directory.Delete(storeDir, recursive: true); }
    }

    [Fact]
    public void ModStateStore_Uninstall_DoesNotDeleteState()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), $"modulus-test-{Guid.NewGuid():N}");
        try
        {
            var store = new ModStateStore(storeDir);
            store.SaveState("test-mod", new byte[] { 1 }, new Version(1, 0, 0));
            Assert.True(store.HasState("test-mod"));
        }
        finally { if (Directory.Exists(storeDir)) Directory.Delete(storeDir, recursive: true); }
    }
}
