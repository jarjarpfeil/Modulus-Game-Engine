// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Stride.Core;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

/// <summary>
/// Exhaustive tests for the "managed C# only" policy — ModLoadContext explicitly
/// blocks unmanaged DLL loading by overriding AssemblyLoadContext.LoadUnmanagedDll.
///
/// This is critical because native DLL file handles cannot be safely released in a
/// running process, which would break hot-reloading architecture on repeated mod reloads.
/// </summary>
public class NativeDllBlockingTests
{
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "ModulusEngine", "NativeBlockTests");

    public NativeDllBlockingTests()
        => Directory.CreateDirectory(TempDir);

    // ── positive tests: DllImport triggers NotSupportedException ──────────

    /// <summary>
    /// Loading a mod and invoking a P/Invoke method should throw NotSupportedException.
    /// The [DllImport] is resolved lazily when the method is called, which fires
    /// AssemblyLoadContext.LoadUnmanagedDll on this ALC.
    /// </summary>
    [Fact]
    public void LoadUnmanagedDll_WhenPInvokeCalled_ThrowsNotSupportedException()
    {
        var dllPath = EmitModWithPInvokes(Path.Combine(TempDir, "PInvokeMod.dll"));

        using var package = CreatePackage("pinvoke-mod");

        var asm = package.LoadContext!.LoadFromModPath(dllPath);
        Assert.NotNull(asm);

        var modType = asm.GetType("PInvokeMod.PInvokeMod");
        Assert.NotNull(modType);

        var ex = Assert.Throws<NotSupportedException>(() =>
            Activator.CreateInstance(modType));

        Assert.Contains("native library", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(package.Manifest.Id, ex.Message);
    }

    /// <summary>
    /// The P/Invoke library name reported in the exception should match what was requested.
    /// </summary>
    [Fact]
    public void LoadUnmanagedDll_ExceptionContainsRequestedLibraryName()
    {
        var dllPath = EmitModWithPInvokes(Path.Combine(TempDir, "PInvokeMod2.dll"));

        using var package = CreatePackage("pinvoke-libname-test");

        var asm = package.LoadContext!.LoadFromModPath(dllPath);
        var modType = asm.GetType("PInvokeMod.PInvokeMod");

        var ex = Assert.Throws<NotSupportedException>(() =>
            Activator.CreateInstance(modType));

        Assert.Contains("nonexistent-native-lib", ex.Message);
    }

    /// <summary>
    /// Verifying that [DllImport(..., SetLastError = true)] doesn't bypass the block.
    /// </summary>
    [Fact]
    public void LoadUnmanagedDll_DllImportWithSetLastError_StillBlocks()
    {
        var dllPath = EmitModWithSetLastError(Path.Combine(TempDir, "SetLastErrorMod.dll"));

        using var package = CreatePackage("seterror-mod");

        var asm = package.LoadContext!.LoadFromModPath(dllPath);
        Assert.NotNull(asm);

        var modType = asm.GetType("SetLastErrorMod.SetLastErrorMod");
        Assert.NotNull(modType);

        var ex = Assert.Throws<NotSupportedException>(() =>
            Activator.CreateInstance(modType));

        Assert.Contains("native library", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A mod with multiple [DllImport] references — the first call should still throw.
    /// </summary>
    [Fact]
    public void LoadUnmanagedDll_MultiplePInvokes_FirstOneBlocks()
    {
        var dllPath = EmitModWithMultiplePInvokes(Path.Combine(TempDir, "MultiNativeMod.dll"));

        using var package = CreatePackage("multi-native-mod");

        var asm = package.LoadContext!.LoadFromModPath(dllPath);
        Assert.NotNull(asm);

        var modType = asm.GetType("MultiNativeMod.MultiNativeMod");
        Assert.NotNull(modType);

        var ex = Assert.Throws<NotSupportedException>(() =>
            Activator.CreateInstance(modType));

        Assert.True(
            ex.Message.Contains("nonexistent-native-lib", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("fakelib", StringComparison.OrdinalIgnoreCase),
            $"Exception message should reference a native library name: {ex.Message}");
    }

    // ── isolation: multiple ALCs each block independently ───────────────────

    /// <summary>
    /// Each mod's ALC should independently enforce the native library policy.
    /// </summary>
    [Fact]
    public void LoadUnmanagedDll_MultipleMods_EachAlcBlocks()
    {
        var services = new ServiceRegistry();
        var host = new ModHost(services);

        // Use a test subclass to access the protected method directly
        var ctxA = new TestModLoadContext(host, "multi-alc-a", TempDir);
        var ctxB = new TestModLoadContext(host, "multi-alc-b", TempDir);

        var modPathA = EmitModWithPInvokes(Path.Combine(TempDir, "MultiAlcModA.dll"));
        var modPathB = EmitModWithPInvokes(Path.Combine(TempDir, "MultiAlcModB.dll"));

        var asmA = ctxA.LoadFromModPath(modPathA);
        var asmB = ctxB.LoadFromModPath(modPathB);
        Assert.NotNull(asmA);
        Assert.NotNull(asmB);

        // Both ALCs must refuse native loading independently.
        var typeA = asmA.GetType("PInvokeMod.PInvokeMod");
        var typeB = asmB.GetType("PInvokeMod.PInvokeMod");

        Assert.Throws<NotSupportedException>(() => Activator.CreateInstance(typeA!));
        Assert.Throws<NotSupportedException>(() => Activator.CreateInstance(typeB!));

        // Both must also reject direct LoadUnmanagedDll invocation with any name.
        const string libName = "some-other-lib";
        Assert.Throws<NotSupportedException>(
            () => ctxA.DirectCallLoadUnmanagedDll(libName));
        Assert.Throws<NotSupportedException>(
            () => ctxB.DirectCallLoadUnmanagedDll(libName));

        ctxA.Unload();
        ctxB.Unload();
    }

    // ── negative tests: managed-only mods work fine ─────────────────────────

    /// <summary>
    /// A pure-managed mod (no P/Invoke) loads and instantiates without errors.
    /// This ensures the block is not a false-positive for normal mod usage.
    /// </summary>
    [Fact]
    public void LoadUnmanagedDll_PureManagedMod_LoadsCorrectly()
    {
        var dllPath = EmitPureManagedMod(Path.Combine(TempDir, "PureManagedMod.dll"));

        using var package = CreatePackage("pure-managed");

        var asm = package.LoadContext!.LoadFromModPath(dllPath);
        Assert.NotNull(asm);

        var modType = asm.GetType("PureManaged.PureManagedMod");
        var instance = Activator.CreateInstance(modType!);
        Assert.NotNull(instance);
    }

    /// <summary>
    /// A modular class with a method that doesn't call P/Invoke should work fine.
    /// </summary>
    [Fact]
    public void LoadUnmanagedDll_ManagedMethodCalls_DoesNotThrow()
    {
        var dllPath = EmitModWithManagedCall(Path.Combine(TempDir, "ManagedCallMod.dll"));

        using var package = CreatePackage("managed-call");

        var asm = package.LoadContext!.LoadFromModPath(dllPath);
        var modType = asm.GetType("ManagedCall.ModWithManagedMethod");
        var instance = Activator.CreateInstance(modType!);

        // Calling a managed method should not trigger LoadUnmanagedDll.
        Assert.DoesNotThrow(() =>
            modType!.GetMethod("DoWork")!.Invoke(instance, new object[0]));
    }

    // ── direct LoadUnmanagedDll calls via test subclass ─────────────────────

    /// <summary>
    /// Directly calling LoadUnmanagedDll with various name formats should all throw.
    /// </summary>
    [Theory]
    [InlineData("foo")]
    [InlineData("foo.dll")]
    [InlineData("libbar.so")]
    [InlineData("test.dylib")]
    [InlineData("kernel32")]
    [InlineData("user32.dll")]
    public void LoadUnmanagedDll_DirectCall_ThrowsForAllFormats(string libraryName)
    {
        var services = new ServiceRegistry();
        var host = new ModHost(services);
        var ctx = new TestModLoadContext(host, "direct-call-test", TempDir);

        Assert.Throws<NotSupportedException>(
            () => ctx.DirectCallLoadUnmanagedDll(libraryName));

        ctx.Unload();
    }

    /// <summary>
    /// The exception message should be actionable — include mod ID and library name.
    /// </summary>
    [Fact]
    public void LoadUnmanagedDll_ExceptionMessage_IsActionable()
    {
        var dllPath = EmitModWithPInvokes(Path.Combine(TempDir, "MsgTestMod.dll"));

        using var package = CreatePackage("msg-test-mod");

        var asm = package.LoadContext!.LoadFromModPath(dllPath);
        var modType = asm.GetType("PInvokeMod.PInvokeMod");

        var ex = Assert.Throws<NotSupportedException>(() =>
            Activator.CreateInstance(modType!));

        // The message should contain enough info for a modder to understand:
        // 1. That native libraries are not allowed
        // 2. Which library triggered it
        // 3. Which mod triggered it
        Assert.Contains("managed C#", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nonexistent-native-lib", ex.Message);
        Assert.Contains(" msg-test-mod ", ex.Message);
    }

    /// <summary>
    /// Accessing LoadUnmanagedDll directly with different library names.
    /// </summary>
    [Theory]
    [InlineData("mylib.Native")]
    [InlineData("libnative")]
    [InlineData("SomeNativeLibrary123")]
    public void LoadUnmanagedDll_DirectCallViaSubclass_ThrowsForAnyName(string libraryName)
    {
        var services = new ServiceRegistry();
        var host = new ModHost(services);
        var ctx = new TestModLoadContext(host, "subclass-direct-test", TempDir);

        var ex = Assert.Throws<NotSupportedException>(
            () => ctx.DirectCallLoadUnmanagedDll(libraryName));

        // Should still contain the library name in the exception message.
        Assert.Contains(libraryName, ex.Message);

        ctx.Unload();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static readonly Lock _pkgLock = new();

    private ModPackage CreatePackage(string modId) => new(
        new ModManifest { Id = modId, Name = $"{modId}-test", Version = "1.0.0" },
        TempDir);

    /// <summary>
    /// Extends ModLoadContext to expose the protected LoadUnmanagedDll for direct testing.
    /// </summary>
    public class TestModLoadContext : ModLoadContext
    {
        public TestModLoadContext(ModHost host, string modId, string modDirectory)
            : base(host, modId, modDirectory)
        {
        }

        public IntPtr DirectCallLoadUnmanagedDll(string unmanagedDllName)
            => LoadUnmanagedDll(unmanagedDllName);
    }

    /// <summary>
    /// Emits a mod DLL that imports a native function via [DllImport].
    /// The static constructor immediately calls the P/Invoke to force eager resolution.
    /// </summary>
    private static string EmitModWithPInvokes(string outputPath)
    {
        var sourceCode = @"
using System.Runtime.InteropServices;

namespace PInvokeMod;

public class PInvokeMod : IMod
{
    static PInvokeMod() { TryLoad(); }

    [DllImport(""nonexistent-native-lib"", CallingConvention = CallingConvention.Cdecl)]
    private static extern void TryLoad();

    public string GetIdentity() => ""pinvoke-mod"";
}";
        return EmitAssembly(outputPath, sourceCode);
    }

    /// <summary>
    /// Emits a mod with [DllImport(..., SetLastError = true)].
    /// </summary>
    private static string EmitModWithSetLastError(string outputPath)
    {
        var sourceCode = @"
using System.Runtime.InteropServices;

namespace SetLastErrorMod;

public class SetLastErrorMod : IMod
{
    static SetLastErrorMod() { TryLoad(); }

    [DllImport(""nonexistent-native-lib"", SetLastError = true)]
    private static extern void TryLoad();

    public string GetIdentity() => ""seterror-mod"";
}";
        return EmitAssembly(outputPath, sourceCode);
    }

    /// <summary>
    /// Emits a mod with two [DllImport] references; the first call should fire LoadUnmanagedDll.
    /// </summary>
    private static string EmitModWithMultiplePInvokes(string outputPath)
    {
        var sourceCode = @"
using System.Runtime.InteropServices;

namespace MultiNativeMod;

public class MultiNativeMod : IMod
{
    static MultiNativeMod() { TryLoad(); }

    [DllImport(""nonexistent-native-lib"")]
    private static extern void TryLoad();

    [DllImport(""fakelib"")]
    private static extern void FakeLibLoad();

    public string GetIdentity() => ""multi-native"";
}";
        return EmitAssembly(outputPath, sourceCode);
    }

    /// <summary>
    /// Emits a pure-managed mod with zero native imports.
    /// </summary>
    private static string EmitPureManagedMod(string outputPath)
    {
        var sourceCode = @"
namespace PureManaged;

public class PureManagedMod : IMod
{
    public string GetIdentity() => ""pure-managed"";
}";
        return EmitAssembly(outputPath, sourceCode);
    }

    /// <summary>
    /// Emits a mod with managed-only method calls (no P/Invoke).
    /// </summary>
    private static string EmitModWithManagedCall(string outputPath)
    {
        var sourceCode = @"
namespace ManagedCall;

public class ModWithManagedMethod : IMod
{
    public int DoWork() => 42;

    public string GetIdentity() => ""managed-call"";
}";
        return EmitAssembly(outputPath, sourceCode);
    }

    /// <summary>
    /// Use Roslyn to compile a .modpkg entry DLL from inline C# source.
    /// </summary>
    private static string EmitAssembly(string outputPath, string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        // Core references for a valid assembly targeting the engine's ALC environment.
        var coreReference = MetadataReference.CreateFromFile(
            typeof(ModHost).Assembly.Location,
            documentation: null,
            isEmbeddedResource: false,
            fileName: nameof(ModHost),
            apiCompatEmitLicenseComment: false);

        var moddingApiRef = MetadataReference.CreateFromFile(
            typeof(IMod).Assembly.Location,
            documentation: null,
            isEmbeddedResource: false,
            fileName: Path.GetFileNameWithoutExtension(typeof(IMod).Assembly.Location),
            apiCompatEmitLicenseComment: false);

        var references = new List<MetadataReference>
        {
            coreReference,
            moddingApiRef,
        };

        // Also pull in all currently loaded assemblies for completeness.
        foreach (var refAssembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (refAssembly.Location is not null && File.Exists(refAssembly.Location))
                    references.Add(MetadataReference.CreateFromFile(refAssembly.Location));
            }
            catch { /* ignore problematic assemblies */ }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: Path.GetFileNameWithoutExtension(outputPath),
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity >= DiagnosticSeverity.Warning)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Roslyn emit failed:\n{errors}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, ms.ToArray());
        return outputPath;
    }
}
