// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org & https://stride3d.net) and Silicon Studio Corp. (https://siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Stride.Engine.Modding;
using Xunit;

namespace Stride.Engine.Modding.Tests;

public class ModPackageManagerTests : IDisposable
{
    private readonly string _tempRoot;

    public ModPackageManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ModulusTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private string CreateModSourceDirectory(string id, string version = "1.0.0",
        string type = "standard", bool withAssembly = true, string? depId = null, string? depMinVersion = null)
    {
        var dir = Path.Combine(_tempRoot, "src", id);
        Directory.CreateDirectory(dir);

        var manifest = new Dictionary<string, object>
        {
            ["id"] = id,
            ["name"] = $"Test Mod {id}",
            ["version"] = version,
            ["apiVersion"] = "1.0",
            ["type"] = type,
        };

        if (depId != null)
        {
            manifest["dependencies"] = new[]
            {
                new { id = depId, minVersion = depMinVersion ?? "1.0.0" }
            };
        }

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, "mod.json"), json);

        if (withAssembly && type is "standard" or "patch")
        {
            var asmDir = Path.Combine(dir, "assemblies");
            Directory.CreateDirectory(asmDir);
            // Create a dummy DLL (just needs to exist for validation)
            File.WriteAllText(Path.Combine(asmDir, "Dummy.dll"), "not-a-real-dll");
        }

        return dir;
    }

    private string GetModsDir() => Path.Combine(_tempRoot, "mods");

    // ──────────────────────────────────────────────
    //  CreatePackage
    // ──────────────────────────────────────────────

    [Fact]
    public void CreatePackage_ValidMod_CreatesZip()
    {
        var sourceDir = CreateModSourceDirectory("com.example.test-mod");
        var outputPath = Path.Combine(_tempRoot, "test-mod.modpkg");
        var manager = new ModPackageManager(GetModsDir());

        var package = manager.CreatePackage(sourceDir, outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.Equal("com.example.test-mod", package.Manifest.Id);

        // Verify it's a valid ZIP
        using var zip = ZipFile.OpenRead(outputPath);
        Assert.NotNull(zip.GetEntry("mod.json"));
        Assert.NotNull(zip.GetEntry("assemblies/Dummy.dll"));
    }

    [Fact]
    public void CreatePackage_MissingModJson_Throws()
    {
        var dir = Path.Combine(_tempRoot, "src", "no-manifest");
        Directory.CreateDirectory(dir);
        var manager = new ModPackageManager(GetModsDir());

        Assert.Throws<FileNotFoundException>(() =>
            manager.CreatePackage(dir, Path.Combine(_tempRoot, "output.modpkg")));
    }

    [Fact]
    public void CreatePackage_InvalidManifest_Throws()
    {
        var dir = Path.Combine(_tempRoot, "src", "invalid");
        Directory.CreateDirectory(dir);
        // Missing required fields
        File.WriteAllText(Path.Combine(dir, "mod.json"), """{"name": "No ID"}""");
        var manager = new ModPackageManager(GetModsDir());

        Assert.Throws<InvalidOperationException>(() =>
            manager.CreatePackage(dir, Path.Combine(_tempRoot, "output.modpkg")));
    }

    [Fact]
    public void CreatePackage_DataMod_NoAssemblyRequired()
    {
        var sourceDir = CreateModSourceDirectory("com.example.data-mod", type: "data", withAssembly: false);
        var outputPath = Path.Combine(_tempRoot, "data-mod.modpkg");
        var manager = new ModPackageManager(GetModsDir());

        var package = manager.CreatePackage(sourceDir, outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.Equal("data", package.Manifest.Type);
    }

    [Fact]
    public void CreatePackage_StandardModWithoutAssembly_Throws()
    {
        var sourceDir = CreateModSourceDirectory("com.example.no-asm", type: "standard", withAssembly: false);
        var outputPath = Path.Combine(_tempRoot, "no-asm.modpkg");
        var manager = new ModPackageManager(GetModsDir());

        Assert.Throws<InvalidOperationException>(() =>
            manager.CreatePackage(sourceDir, outputPath));
    }

    // ──────────────────────────────────────────────
    //  Install
    // ──────────────────────────────────────────────

    [Fact]
    public void Install_ValidPackage_ExtractsToDirectory()
    {
        var sourceDir = CreateModSourceDirectory("com.example.installable");
        var pkgPath = Path.Combine(_tempRoot, "installable.modpkg");
        var manager = new ModPackageManager(GetModsDir());

        manager.CreatePackage(sourceDir, pkgPath);
        var installedDir = manager.Install(pkgPath);

        Assert.True(Directory.Exists(installedDir));
        Assert.True(File.Exists(Path.Combine(installedDir, "mod.json")));
        Assert.True(File.Exists(Path.Combine(installedDir, "assemblies", "Dummy.dll")));
    }

    [Fact]
    public void Install_MissingFile_Throws()
    {
        var manager = new ModPackageManager(GetModsDir());

        Assert.Throws<FileNotFoundException>(() =>
            manager.Install(Path.Combine(_tempRoot, "nonexistent.modpkg")));
    }

    [Fact]
    public void Install_InvalidZip_ThrowsInvalidData()
    {
        var badZip = Path.Combine(_tempRoot, "bad.modpkg");
        File.WriteAllText(badZip, "not a zip file");
        var manager = new ModPackageManager(GetModsDir());

        Assert.Throws<InvalidDataException>(() => manager.Install(badZip));
    }

    [Fact]
    public void Install_ZipMissingModJson_ThrowsInvalidData()
    {
        // Create a valid zip but without mod.json
        var zipPath = Path.Combine(_tempRoot, "no-manifest.modpkg");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("some-file.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hello");
        }

        var manager = new ModPackageManager(GetModsDir());

        Assert.Throws<InvalidDataException>(() => manager.Install(zipPath));
    }

    [Fact]
    public void Install_DuplicateId_UpgradesExisting()
    {
        var sourceDir = CreateModSourceDirectory("com.example.upgrade", version: "1.0.0");
        var pkgPath = Path.Combine(_tempRoot, "upgrade.modpkg");
        var modsDir = GetModsDir();
        var manager = new ModPackageManager(modsDir);

        // Install v1
        manager.CreatePackage(sourceDir, pkgPath);
        manager.Install(pkgPath);

        // Create v2 and install
        var sourceDir2 = CreateModSourceDirectory("com.example.upgrade", version: "2.0.0");
        var pkgPath2 = Path.Combine(_tempRoot, "upgrade-v2.modpkg");
        manager.CreatePackage(sourceDir2, pkgPath2);
        manager.Install(pkgPath2);

        // Should have the v2 manifest
        var installedManifest = Path.Combine(modsDir, "com.example.upgrade", "mod.json");
        Assert.True(File.Exists(installedManifest));
        var manifest = ModManifest.FromJson(File.ReadAllText(installedManifest));
        Assert.Equal("2.0.0", manifest.Version);
    }

    // ──────────────────────────────────────────────
    //  Uninstall
    // ──────────────────────────────────────────────

    [Fact]
    public void Uninstall_RemovesDirectoryAndPackage()
    {
        var sourceDir = CreateModSourceDirectory("com.example.removable");
        var pkgPath = Path.Combine(_tempRoot, "removable.modpkg");
        var modsDir = GetModsDir();
        var manager = new ModPackageManager(modsDir);

        manager.CreatePackage(sourceDir, pkgPath);
        manager.Install(pkgPath);

        Assert.True(manager.IsInstalled("com.example.removable"));

        var removed = manager.Uninstall("com.example.removable");

        Assert.True(removed);
        Assert.False(manager.IsInstalled("com.example.removable"));
    }

    [Fact]
    public void Uninstall_Nonexistent_ReturnsFalse()
    {
        var manager = new ModPackageManager(GetModsDir());
        var removed = manager.Uninstall("nonexistent-mod");
        Assert.False(removed);
    }

    // ──────────────────────────────────────────────
    //  ListInstalled
    // ──────────────────────────────────────────────

    [Fact]
    public void ListInstalled_FindsExtractedMods()
    {
        var modsDir = GetModsDir();
        var manager = new ModPackageManager(modsDir);

        // Install two mods
        var src1 = CreateModSourceDirectory("com.example.mod-one");
        var pkg1 = Path.Combine(_tempRoot, "mod-one.modpkg");
        manager.CreatePackage(src1, pkg1);
        manager.Install(pkg1);

        var src2 = CreateModSourceDirectory("com.example.mod-two");
        var pkg2 = Path.Combine(_tempRoot, "mod-two.modpkg");
        manager.CreatePackage(src2, pkg2);
        manager.Install(pkg2);

        var installed = manager.ListInstalled();

        Assert.Equal(2, installed.Count);
        Assert.Contains(installed, m => m.Id == "com.example.mod-one");
        Assert.Contains(installed, m => m.Id == "com.example.mod-two");
    }

    [Fact]
    public void ListInstalled_EmptyDirectory_ReturnsEmpty()
    {
        var manager = new ModPackageManager(Path.Combine(_tempRoot, "empty-mods"));
        var installed = manager.ListInstalled();
        Assert.Empty(installed);
    }

    // ──────────────────────────────────────────────
    //  IsInstalled
    // ──────────────────────────────────────────────

    [Fact]
    public void IsInstalled_AfterInstall_ReturnsTrue()
    {
        var sourceDir = CreateModSourceDirectory("com.example.check-me");
        var pkgPath = Path.Combine(_tempRoot, "check-me.modpkg");
        var manager = new ModPackageManager(GetModsDir());

        manager.CreatePackage(sourceDir, pkgPath);
        manager.Install(pkgPath);

        Assert.True(manager.IsInstalled("com.example.check-me"));
    }

    [Fact]
    public void IsInstalled_BeforeInstall_ReturnsFalse()
    {
        var manager = new ModPackageManager(GetModsDir());
        Assert.False(manager.IsInstalled("com.example.not-installed"));
    }

    // ──────────────────────────────────────────────
    //  ValidatePackage (static)
    // ──────────────────────────────────────────────

    [Fact]
    public void ValidatePackage_ValidPackage_NoErrors()
    {
        var sourceDir = CreateModSourceDirectory("com.example.valid");
        var pkgPath = Path.Combine(_tempRoot, "valid.modpkg");
        var manager = new ModPackageManager(GetModsDir());
        manager.CreatePackage(sourceDir, pkgPath);

        var errors = ModPackageManager.ValidatePackage(pkgPath);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidatePackage_MissingFile_ReturnsError()
    {
        var errors = ModPackageManager.ValidatePackage(Path.Combine(_tempRoot, "nonexistent.modpkg"));
        Assert.Single(errors);
        Assert.Contains("not found", errors[0]);
    }

    [Fact]
    public void ValidatePackage_InvalidZip_ReturnsError()
    {
        var badFile = Path.Combine(_tempRoot, "not-a-zip.modpkg");
        File.WriteAllText(badFile, "garbage");
        var errors = ModPackageManager.ValidatePackage(badFile);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidatePackage_MissingModJson_ReturnsError()
    {
        var zipPath = Path.Combine(_tempRoot, "no-json.modpkg");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("some-file.txt");
        }

        var errors = ModPackageManager.ValidatePackage(zipPath);
        Assert.Contains(errors, e => e.Contains("mod.json"));
    }

    [Fact]
    public void ValidatePackage_StandardModMissingAssemblies_ReturnsError()
    {
        var sourceDir = CreateModSourceDirectory("com.example.no-asm-pkg", type: "standard", withAssembly: false);
        // Manually create a ZIP with mod.json but no assemblies
        var zipPath = Path.Combine(_tempRoot, "no-asm.modpkg");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("mod.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(File.ReadAllText(Path.Combine(sourceDir, "mod.json")));
        }

        var errors = ModPackageManager.ValidatePackage(zipPath);
        Assert.Contains(errors, e => e.Contains("assemblies"));
    }

    // ──────────────────────────────────────────────
    //  Full lifecycle
    // ──────────────────────────────────────────────

    [Fact]
    public void FullLifecycle_PackageInstallListUninstall()
    {
        var sourceDir = CreateModSourceDirectory("com.example.lifecycle", version: "3.2.1");
        var pkgPath = Path.Combine(_tempRoot, "lifecycle.modpkg");
        var modsDir = GetModsDir();
        var manager = new ModPackageManager(modsDir);

        // Create
        var package = manager.CreatePackage(sourceDir, pkgPath);
        Assert.Equal("com.example.lifecycle", package.Manifest.Id);
        Assert.True(File.Exists(pkgPath));

        // Install
        var installedDir = manager.Install(pkgPath);
        Assert.True(Directory.Exists(installedDir));
        Assert.True(manager.IsInstalled("com.example.lifecycle"));

        // List
        var installed = manager.ListInstalled();
        Assert.Single(installed);
        Assert.Equal("3.2.1", installed[0].Version);

        // Validate the package
        var validationErrors = ModPackageManager.ValidatePackage(pkgPath);
        Assert.Empty(validationErrors);

        // Uninstall
        var removed = manager.Uninstall("com.example.lifecycle");
        Assert.True(removed);
        Assert.False(manager.IsInstalled("com.example.lifecycle"));

        // List empty
        installed = manager.ListInstalled();
        Assert.Empty(installed);
    }
}
