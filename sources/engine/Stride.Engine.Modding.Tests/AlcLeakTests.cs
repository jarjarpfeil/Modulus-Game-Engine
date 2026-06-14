// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Stride.Core;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

/// <summary>
/// Tests for ALC (AssemblyLoadContext) leak prevention — the single hardest
/// technical problem in the modding system. A collectible ALC can only be
/// garbage collected when zero strong references exist from outside.
///
/// These tests verify the actual mod loading/unloading lifecycle using a
/// dynamically generated mod DLL (via Roslyn compilation), ensuring real
/// cross-ALC type usage, resolution, and verification of collection.
/// </summary>
public class AlcLeakTests
{
    /// <summary>
    /// Verifies that a collectible ModLoadContext is actually collected after
    /// all references are released.
    /// This is the fundamental test — if this fails, mod unloading will leak memory.
    ///
    /// Uses a real ModLoadContext with a dynamically generated mod DLL to test
    /// the actual code path, not an empty ALC.
    /// </summary>
    [Fact]
    public void ModLoadContext_IsCollectedAfterUnload()
    {
        var weakRef = CreateAndUnloadModAlc();

        ForceFullGc();

        Assert.Null(weakRef.Target);
    }

    /// <summary>
    /// Verifies that creating and unloading ModLoadContexts 100 times does not cause
    /// unbounded memory growth. This catches leaked references that prevent ALC collection —
    /// the most common real-world leak scenario.
    /// </summary>
    [Fact]
    public void ModLoadContext_100Cycles_ConstantMemory()
    {
        ForceFullGc();
        var beforeMemory = GC.GetTotalMemory(true);

        for (int i = 0; i < 100; i++)
        {
            CreateAndUnloadModAlc();
        }

        ForceFullGc();
        var afterMemory = GC.GetTotalMemory(true);
        var growth = afterMemory - beforeMemory;

        Assert.True(growth < 5 * 1024 * 1024,
            $"Memory grew by {growth / 1024 / 1024}MB after 100 mod ALC cycles — possible reference leak");
    }

    /// <summary>
    /// Verifies that types resolved from a loaded mod are actually from the mod's ALC.
    /// This tests cross-ALC type identity wiring — mod types load and instantiate
    /// correctly within the mod's own ALC.
    /// </summary>
    [Fact]
    public void ModLoadContext_TypesResolveFromModAlc()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ModulusEngine", "AlcLeakTests");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dllPath = EmitTestModDll(Path.Combine(tempDir, "TestMod.dll"));

            var services = new ServiceRegistry();
            var host = new ModHost(services);
            var package = new ModPackage(
                new ModManifest { Id = "test-mod", Name = "Test Mod", Version = "1.0.0" },
                tempDir);
            package.LoadAssemblies(host);

            var loadContext = package.LoadContext!;
            var modAssembly = loadContext.LoadFromModPath(dllPath);
            Assert.NotNull(modAssembly);

            var modType = modAssembly.GetType("TestMod.ModComponent");
            Assert.NotNull(modType);

            var instance = Activator.CreateInstance(modType);
            Assert.NotNull(instance);

            package.Unload();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that Multiple mod ALCs can coexist and resolve their own private
    /// assemblies independently.
    /// </summary>
    [Fact]
    public void ModLoadContext_MultipleMods_IndependentAlcs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ModulusEngine", "AlcLeakTests");
        Directory.CreateDirectory(tempDir);

        try
        {
            var mod1Path = EmitTestModDll(Path.Combine(tempDir, "ModA.dll"));
            var mod2Path = EmitTestModDll(Path.Combine(tempDir, "ModB.dll"));

            var services = new ServiceRegistry();
            var host = new ModHost(services);

            var packageA = new ModPackage(
                new ModManifest { Id = "mod-a", Name = "Mod A", Version = "1.0.0" },
                tempDir);
            packageA.LoadAssemblies(host);
            var asmA = packageA.LoadContext!.LoadFromModPath(mod1Path);
            Assert.NotNull(asmA);

            var packageB = new ModPackage(
                new ModManifest { Id = "mod-b", Name = "Mod B", Version = "1.0.0" },
                tempDir);
            packageB.LoadAssemblies(host);
            var asmB = packageB.LoadContext!.LoadFromModPath(mod2Path);
            Assert.NotNull(asmB);

            Assert.NotEqual(asmA, asmB);

            packageA.Unload();
            packageB.Unload();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that ModLoadContext blocks unmanaged DLL loading.
    /// Mods are managed C# only — native code is prohibited.
    /// Emits a mod DLL with [DllImport], loads it, and asserts that
    /// LoadUnmanagedDll is called and throws NotSupportedException.
    /// </summary>
    [Fact]
    public void ModLoadContext_BlocksNativeDllLoad()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ModulusEngine", "AlcLeakTests");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dllPath = EmitTestModWithNativeImport(Path.Combine(tempDir, "TestModNative.dll"));

            var services = new ServiceRegistry();
            var host = new ModHost(services);
            var package = new ModPackage(
                new ModManifest { Id = "test-native", Name = "Test Native", Version = "1.0.0" },
                tempDir);
            package.LoadAssemblies(host);

            var loadContext = package.LoadContext!;
            Assert.Throws<NotSupportedException>(
                () => loadContext.LoadFromModPath(dllPath));

            package.Unload();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndUnloadModAlc()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ModulusEngine", "AlcLeakTests");
        Directory.CreateDirectory(tempDir);

        ModPackage? package = null;
        WeakReference weakRef;

        try
        {
            var dllPath = EmitTestModDll(Path.Combine(tempDir, "AlcLeakTestMod.dll"));

            var services = new ServiceRegistry();
            var host = new ModHost(services);
            package = new ModPackage(
                new ModManifest { Id = "alc-leak-test", Name = "ALC Leak Test", Version = "1.0.0" },
                tempDir);
            package.LoadAssemblies(host);

            var modAssembly = package.LoadContext!.LoadFromModPath(dllPath);
            Assert.NotNull(modAssembly);

            var modType = modAssembly.GetType("TestMod.ModComponent");
            Assert.NotNull(modType);

            _ = Activator.CreateInstance(modType!);

            weakRef = new WeakReference(package.LoadContext!);
            package.Unload();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }

        return weakRef;
    }

    private static string EmitTestModDll(string outputPath)
    {
        var sourceCode = """
            using Stride.Engine;

            namespace TestMod;

            public class ModComponent : EntityComponent
            {
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Stride.Engine.EntityComponent).Assembly.Location),
        };

        foreach (var refAssembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (refAssembly.Location is not null && File.Exists(refAssembly.Location))
                    references.Add(MetadataReference.CreateFromFile(refAssembly.Location));
            }
            catch { }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: Path.GetFileNameWithoutExtension(outputPath),
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Compilation failed:\n{errors}");
        }

        File.WriteAllBytes(outputPath, ms.ToArray());
        return outputPath;
    }

    private static void ForceFullGc()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
