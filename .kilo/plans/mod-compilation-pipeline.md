# Mod Compilation Pipeline Plan

## Problem

Currently, mod authors must manually:
1. Write a `.csproj` with the 4-part recipe (PrivateAssets, RemoveStrideDlls, etc.)
2. Manually copy Stride source assets (`.sdscene`, `.sdmat`, `.sdpromodel`) with no way to compile them into the ObjectDatabase format the engine expects at runtime
3. Manually construct `asset-guids.json` by hand
4. Manually ZIP into `.modpkg` format
5. There is no verification that the mod will actually work at runtime

Only engine developers can produce working mods. The pipeline needs to be automated so any C# developer can create, build, verify, and package a mod.

## Agent Trap Acknowledgment

Seven failure modes were identified during design review. Each has a concrete, implemented fix:

**Trap 1: Post-build DLL deletion breaks incremental compilation.** Deleting `Stride.*.dll` from `bin/Debug/` after build causes MSBuild's Fast Up-To-Date Check to see missing files and force a full recompile every time. Fix: intercept *before* the copy step by removing from `ReferenceCopyLocalPaths` (files are never written, so there's nothing to be "missing" on the next build).

**Trap 2: `<ProjectReference>` copies the entire game assembly into the mod output.** A ProjectReference to the game project copies the game `.exe` and all its dependencies into the mod's output — the entire game leaks into the `.modpkg`. Fix: enforce `Private=false` on all ProjectReferences in mod projects + rely on Trap 1's `ReferenceCopyLocalPaths` filtering as the final safety net. The game assembly is available for compile-time type resolution (normal MSBuild behavior) but never lands in the output dir. Verified by the existing test mods (`mod-assets/ModAssets.csproj` uses plain ProjectReference with no explicit metadata).

**Trap 3: `<Exec>` spawns an isolated OS process, not an in-process call.** Running `dotnet PackTool.dll gen-guids` via `<Exec>` spawns a completely standalone OS process. It does NOT inherit MSBuild's loaded assemblies. If `PackTool` tries to deserialize the binary `ContentIndexMap` without all core Stride serializers present in its own directory, it throws type initialization exceptions. Fix: `Modulus.Mod.PackTool.csproj` must target `net10.0-windows7.0` (matching Stride's runtime) and reference `Stride.Core.Storage` + `Stride.Core.IO` with `CopyLocal=true`. When packaged into the SDK tools directory, all dependent Stride DLLs are bundled directly alongside `Modulus.Mod.PackTool.dll`. Long-term: custom `IAssetBuildStep` inside the AssetCompiler emits JSON natively.

**Trap 4: `$(PkgStride_Core_Assets_CompilerApp)` is not generated automatically.** MSBuild only generates `Pkg[Package_Identity]` properties if the package reference includes `GeneratePathProperty="true"`. Furthermore, if the SDK marks the compiler package as `PrivateAssets="all"`, it won't transitively restore into the mod author's build environment. Fix: the SDK's `Modulus.Mod.Sdk.targets` must explicitly add a `PackageReference` to `Stride.Core.Assets.CompilerApp` with `GeneratePathProperty="true"` so the path property is available and the package restores.

**Trap 5: Asset URL collision is path-based, not namespace-based (VERIFIED).** Stride's `CompilerApp.dll` does NOT use `RootNamespace` or `AssemblyName` as a URL prefix. Virtual URLs are derived from the file path relative to the asset source folder: `Assets/Scene.sdscene` → URL `Scene`. `RootNamespace` is stored on the `Package` object but is NOT used for URL generation (confirmed by reading `PackageLoadingAssetFile.AssetLocation` in `PackageLoadingAssetFile.cs:28`: `(Link ?? FilePath).MakeRelative(SourceFolder).GetDirectoryAndFileNameWithoutExtension()`). This means a mod with `Assets/Scene.sdscene` produces URL `Scene`, which collides with the game's `Scene` asset. The `CompositeFileProviderService` resolves collisions by provider priority (last added = highest priority, see `CompositeFileProviderService.cs:64-66`), so mod assets **silently override** game assets with the same URL. The `explicitOverrides` array makes this intentional and auditable. But for **namespacing** (preventing unintentional collisions), mod authors should place assets in subdirectories: `Assets/com.example.mymod/Scene.sdscene` → URL `com.example.mymod/Scene`. The SDK template creates this subdirectory by default.

**Trap 6: Zero-change builds still pay 2–4 second AssetCompiler lag per platform.** The `ModCompileAssets` target invokes three `<Exec>` tasks (Vulkan + DX11 + DX12) on every build, even for code-only changes. MSBuild runs the target every time because it lacks `Inputs`/`Outputs` declarations. Fix: add `Inputs` and `Outputs` attributes so MSBuild skips the target entirely when no asset files changed since the last build.

**Trap 7: Dual AssetCompiler invocations double cold-start time.** Two separate `dotnet` process spawns each pay the JIT/assembly-loading cold-start cost. For large mods, this compounds. Acceptable for now (incremental builds are fast due to Trap 6 fix), but a future optimization could pass both platform targets in a single invocation or use the AssetCompiler's server mode (`--server=` pipe).

## What Stride's AssetCompiler Does

Stride's `Stride.Core.Assets.CompilerApp` is invoked by MSBuild during `dotnet build`:

```
dotnet Stride.Core.Assets.CompilerApp.dll \
  --package-file "MyGame.csproj" \
  --output-path "bin/Debug/data" \
  --build-path "obj/stride/assets" \
  --platform Windows \
  --compile-property:StrideGraphicsApi=Vulkan
```

It produces:
- `data/db/index` — binary ContentIndexMap (virtual URL → ObjectId mapping)
- `data/db/bundles/default.bundle` — LZ4-compressed compiled assets
- Effect bytecode for all referenced shaders (DXIL for Direct3D11, SPIR-V for Vulkan)
- Serializer registrations for all asset types

For mod compilation, we need the EXACT same pipeline but scoped to a mod project, with multi-platform output and virtualized URLs.

## Architecture Overview

```
Mod Author's Workflow:
  dotnet new modulus-mod -n MyMod
  cd MyMod
  <add assets, scenes, scripts>
  dotnet build                    ← builds DLL + compiles assets (Vulkan+DX11) + verifies + packs .modpkg
  MyMod/bin/Debug/MyMod.modpkg   ← ready to install

Engine Runtime:
  ModHost.LoadAllMods()
    → ModContentManager.RegisterModContent()
      → DatabaseFileProvider reads mod's assets/{platform}/index + bundles
      → ContentManager.Load<Scene>("com.example.mymod/Scene") resolves via ContentIndexMap
```

## Strategic Decision: GraphicsCompositor Inheritance

**Mods inherit the game's GraphicsCompositor by default.** The compositor defines the rendering pipeline (camera slots, render features, swap chain, UI layer). This is the correct design because:

1. `SceneSystem.LoadContent()` loads the compositor **before** the scene — we fixed this ordering specifically to avoid the dummy-compositor binding issue during our scene debugging
2. `ModAutoLoadSystem` assigns the mod scene's camera to the game compositor's "Main" slot
3. A mod that replaces the scene uses the game's render pipeline — it doesn't need its own compositor

A mod *can* optionally ship its own `GraphicsCompositor.sdgfxcomp` (explicit opt-in via the scene manifest), but it must define compatible camera slots and render features. If the mod's compositor doesn't have a "Main" camera slot matching what the game expects, geometry silently won't render — same failure mode as `Material.New()`.

## Implementation Phases

### Phase 1: Modulus.Mod.PackTool CLI (verification + packaging)

**Goal:** A CLI tool for verification, packaging, and GUID index generation.

**Trap 3 fix:** The PackTool is invoked via `<Exec>` which spawns a standalone OS process — it does NOT inherit MSBuild's loaded assemblies. Therefore `Modulus.Mod.PackTool.csproj` must target `net10.0-windows7.0` (matching Stride's runtime) and reference `Stride.Core.Serialization` + `Stride.Core.Storage` + `Stride.Core.IO` with `CopyLocal=true`. When packaged into the SDK tools directory, all dependent Stride DLLs are bundled directly alongside `Modulus.Mod.PackTool.dll`. This ensures `gen-guids` can deserialize the binary `ContentIndexMap` without type initialization exceptions.

**Rationale for this phase first:** Verification and packaging have zero engine runtime deps. Building them first means Phase 2's MSBuild targets have a tested, working tool to call into.

**Files to create:**

```
sources/tools/Modulus.Mod.PackTool/
├── Modulus.Mod.PackTool.csproj    (net10.0-windows7.0, refs Stride.Core.Storage + Stride.Core.IO + Stride.Core.Serialization)
├── Program.cs                     (CLI: gen-guids, verify, pack)
├── GuidGeneration/
│   └── GuidGenerator.cs          (reads binary ContentIndexMap using Stride types, emits JSON)
├── Verification/
│   ├── ModVerifier.cs             (orchestrator: runs checks, returns exit code 0/1/2)
│   ├── ModStructureValidator.cs   (file layout: mod.json, required dirs/files)
│   ├── ModManifestValidator.cs    (mod.json schema, required fields, semver, kebab-case id)
│   ├── ModAssemblyValidator.cs    (Stride DLL contamination, no system dlls)
│   └── ModDependencyValidator.cs  (circular deps, version constraints)
└── Packaging/
    └── ModPacker.cs               (ZIP assembly from staged directory)
```

**`gen-guids` command:**

Reads the binary ContentIndexMap using `Stride.Core.Storage.ObjectDatabase` and `Stride.Core.IO.ContentIndexMap`. Because the PackTool is invoked via `<Exec>` (a separate OS process), it MUST bundle the required Stride DLLs alongside itself (Trap 3 fix — see PackTool csproj notes above). The tool deserializes the index, converts to JSON, and writes `asset-guids.json`:

```json
{
  "mappings": [
    { "virtualPath": "com.example.mymod/Scene", "guid": "49BCFF41-EB79-4884-A4B7-BCD4C41DC4AF" },
    { "virtualPath": "com.example.mymod/GraphicsCompositor", "guid": "2C917432-6246-4AD0-96F0-62CE467C695D" }
  ]
}
```

Long-term: replace with a custom `IAssetBuildStep` inside the AssetCompiler so the JSON is emitted natively during compilation.

**`verify` command:**

Runs all validators. Exit code 0 = valid, 1 = errors, 2 = warnings only.

| Check | Severity | Description |
|---|---|---|
| `mod.json` exists | Error | Required manifest |
| `mod.json` valid JSON | Error | Must parse |
| `id` is kebab-case | Error | e.g., `com.example.mymod` |
| `version` is valid semver | Error | e.g., `1.0.0` |
| `apiVersion` is `"1.0"` | Error | Must match engine |
| `type` is valid | Error | `standard`, `data`, `patch` |
| `entryPoint` format valid | Warning | `Namespace.Class, Assembly` if present |
| Assemblies exist (standard/patch) | Error | ≥1 `.dll` in `assemblies/` |
| No `Stride.*.dll` in assemblies | Error | Type identity failure |
| No `Modulus.Modding.Api.dll` bundled | Warning | Should resolve from engine |
| `asset-guids.json` present if assets | Warning | Needed for URL resolution |
| URL collision with game DB (not in explicitOverrides) | Error | Silently overrides game asset — must be declared |
| URL collision with game DB (in explicitOverrides) | Info | Intentional patch, allowed |
| GUID collision with game DB (different URL) | Warning | Possible reference conflict |
| `explicitOverrides` entry not found in game DB | Warning | Listed override target doesn't exist |
| Scene URLs resolve in asset DB | Warning | Referenced scenes should exist |
| Circular dependencies | Error | All mods in cycle won't load |
| `apiVersion` mismatch | Warning | Mod compiled against different API version |

**`pack` command:**

ZIPs the staged directory with `CompressionLevel.Optimal`.

### Phase 2: Modulus.Mod.Sdk (MSBuild targets + AssetCompiler integration)

**Goal:** `dotnet build` on a mod project compiles assets for both Vulkan and DX11, creates the staging directory, runs verification, and produces `.modpkg`.

**NuGet package:** `Modulus.Mod.Sdk` (build-only, `PackAsBuildSource`).
Declares a **development dependency** on **`Stride.Core.Assets.CompilerApp`** NuGet package. The SDK does NOT bundle the compiler directly — the `Stride.Core.Assets.CompilerApp` package distributes the compiler + native deps (assimp, squish/nvtt, dxcompiler, d3dcompiler) via RID-specific `runtimes/{rid}/native/` folders. Mod authors with no Stride source tree get a working build with zero extra steps.

**Files to create:**

```
sources/tools/Modulus.Mod.Sdk/
├── Modulus.Mod.Sdk.csproj          (PackAsBuildSource, dev-dep on Stride.Core.Assets.CompilerApp)
├── build/
│   ├── Modulus.Mod.Sdk.props      (default properties, auto-imported)
│   └── Modulus.Mod.Sdk.targets    (build pipeline, auto-imported)
└── README.md
```

**`Modulus.Mod.Sdk.props`:**

```xml
<Project>
  <PropertyGroup>
    <ModType Condition="'$(ModType)' == ''">standard</ModType>
    <ModManifest Condition="'$(ModManifest)' == ''">mod.json</ModManifest>
    <ModPkgStaging Condition="'$(ModPkgStaging)' == ''">$(IntermediateOutputPath)ModPkg\</ModPkgStaging>
    <!-- TRAP 4 FOLLOW-UP: The CompilerApp version must be bound before the
         targets file evaluates. This reads from a centralized Version.props
         file bundled inside the SDK NuGet package. Must NOT be left unbound. -->
    <ModulusEngineVersion Condition="'$(ModulusEngineVersion)' == '' AND Exists('$(MSBuildThisFileDirectory)..\build\Version.props')>$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)..\build\Version.props').Trim())</ModulusEngineVersion>
  </PropertyGroup>
  <!-- Import the centralized version so $(ModulusEngineVersion) is bound.
       This file is shipped inside the SDK NuGet package and contains a single
       version string (e.g., "4.4.0.2"). The Import runs before targets evaluate. -->
  <Import Project="$(MSBuildThisFileDirectory)..\build\Version.props" Condition="Exists('$(MSBuildThisFileDirectory)..\build\Version.props')" />
</Project>
```

**NOTE:** The previous version of this plan contained a `<ModulusEngineVersion>` assignment that set the property to a *file path* instead of the version string — a typo that would break the NuGet restore pass. The corrected approach uses `<Import>` to load the version, which sets the property correctly, with a fallback inline read using MSBuild property functions.

**`Modulus.Mod.Sdk.targets` — with ALL trap fixes:**

```xml
<Project>
  <!-- ═══════════════════════════════════════════════════════════════
       TRAP 1 FIX: Intercept BEFORE copy, never delete after build.
       Remove Stride engine assemblies from ReferenceCopyLocalPaths so
       they're never written. MSBuild's Fast Up-To-Date Check stays green
       because the files were never expected in the output.
       ═══════════════════════════════════════════════════════════════ -->
  <Target Name="ExcludeEngineAssembliesFromOutput"
          BeforeTargets="GetReferenceAssemblyPaths;CopyFilesToOutputDirectory">
    <ItemGroup>
      <ReferenceCopyLocalPaths Remove="@(ReferenceCopyLocalPaths)"
        Condition="$([System.String]::Copy('%(Filename)').StartsWith('Stride.'))" />
      <ReferenceCopyLocalPaths Remove="@(ReferenceCopyLocalPaths)"
        Condition="$([System.String]::Copy('%(Filename)').StartsWith('Modulus.Engine'))" />
    </ItemGroup>
  </Target>

  <!-- ═══════════════════════════════════════════════════════════════
       TRAP 2 FIX: Prevent game assembly + downstream deps from being
       copied into the mod's output directory.

       VERIFIED APPROACH: The existing test mods use plain ProjectReference
       with Private=false. The ReferenceCopyLocalPaths removal in Trap 1's
       ExcludeEngineAssembliesFromOutput target handles the cleanup.
       ReferenceOutputAssembly=false + OutputItemType=Analyzer was considered
       but REJECTED — Analyzer items are loaded by Roslyn as diagnostic
       assemblies, NOT as metadata references. Mod code cannot compile
       against game types via Analyzer references. The correct approach is:
       ReferenceOutputAssembly=true (normal compilation reference) + Private=false
       (don't copy to output) + Trap 1's ReferenceCopyLocalPaths filtering.
       ═══════════════════════════════════════════════════════════════ -->
  <Target Name="EnforceModProjectReferenceIsolation"
          BeforeTargets="ResolveProjectReferences">
    <ItemDefinitionGroup>
      <ProjectReference>
        <Private>false</Private>
      </ProjectReference>
    </ItemDefinitionGroup>
  </Target>

  <!-- ═══════════════════════════════════════════════════════════════
       TRAP 4 FIX: Force the CompilerApp package to restore into the
       mod's build environment AND generate the Pkg path property.
       Without GeneratePathProperty="true", $(PkgStride_Core_Assets_CompilerApp)
       is never defined. Without the explicit PackageReference here,
       PrivateAssets="all" on the SDK's own .csproj prevents the
       package from transitively restoring.
       ═══════════════════════════════════════════════════════════════ -->
   <ItemGroup>
    <PackageReference Include="Stride.Core.Assets.CompilerApp" Version="$(ModulusEngineVersion)" PrivateAssets="all" GeneratePathProperty="true" />
  </ItemGroup>

  <!-- NOTE: $(ModulusEngineVersion) MUST be defined before this targets file is
       evaluated. It is set by the Modulus.Mod.Sdk.props file, which reads the
       version from a centralized Version.props file bundled inside the SDK package.
       If left unbound, the PackageReference attempts to restore an unversioned
       package and the entire bootstrap pass fails. -->

  <!-- 1. Compile assets for BOTH platforms (Vulkan + DX11) -->
  <!-- ═══════════════════════════════════════════════════════════════
       TRAP 6 FIX: Inputs/Outputs so MSBuild skips this target when
       no asset files changed since the last build. Without this,
       every code-only change still pays ~6 sec for three dotnet spawns (Vulkan + DX11 + DX12).
       
       OPTIMIZATION: Pure code mods (no .sdscene/.sdmat/.sdsl files)
       skip the AssetCompiler entirely. The condition checks for at
       least one Stride source asset file type before invoking.
       
       Input item group uses a dedicated StrideAssets pattern instead of
       @(None) which contains far more than just asset files (generated
       files, non-asset resources, etc.). This ensures changing a referenced
       shader or material actually invalidates the build.
       ═══════════════════════════════════════════════════════════════ -->
  <ItemGroup>
    <StrideAssets Include="Assets\**\*.sdscene" />
    <StrideAssets Include="Assets\**\*.sdmat" />
    <StrideAssets Include="Assets\**\*.sdpromodel" />
    <StrideAssets Include="Assets\**\*.sdsl" />
    <StrideAssets Include="Assets\**\*.sdtex" />
    <StrideAssets Include="Assets\**\*.sdsky" />
    <StrideAssets Include="Assets\**\*.sdgfxcomp" />
    <StrideAssets Include="Assets\**\*.sdgamesettings" />
    <StrideAssets Include="Assets\**\*.sdfnt" />
    <StrideAssets Include="Assets\**\*.sdanim" />
    <StrideAssets Include="$(ModManifest)" />
  </ItemGroup>
  <Target Name="ModCompileAssets"
          AfterTargets="Build"
          Inputs="@(StrideAssets)"
          Outputs="$(ModPkgStaging)assets\windows-vulkan\index;$(ModPkgStaging)assets\windows-dx11\index;$(ModPkgStaging)assets\windows-dx12\index"
          Condition="Exists('$(ModManifest)') AND '@(StrideAssets)' != ''">
    <PropertyGroup>
      <!-- Resolve the AssetCompiler from the Stride.Core.Assets.CompilerApp NuGet package.
           $(PkgStride_Core_Assets_CompilerApp) is available because of GeneratePathProperty="true" above. -->
      <StrideCompileAssetCommand Condition="'$(StrideCompileAssetCommand)' == ''">
        $(PkgStride_Core_Assets_CompilerApp)\tools\net10.0-windows7.0\Stride.Core.Assets.CompilerApp.dll
      </StrideCompileAssetCommand>
    </PropertyGroup>

    <!-- Stage directory: per-platform subdirs (future-proof naming for D3D12/Metal/WebGPU) -->
    <MakeDir Directories="$(ModPkgStaging)assets\windows-vulkan" />
    <MakeDir Directories="$(ModPkgStaging)assets\windows-dx11" />
    <MakeDir Directories="$(ModPkgStaging)assets\windows-dx12" />

    <!-- Platform 1: Vulkan (SPIR-V) — primary target for Windows/Linux -->
    <Exec Command="dotnet &quot;$(StrideCompileAssetCommand)&quot; --package-file &quot;$(MSBuildProjectFullPath)&quot; --output-path &quot;$(ModPkgStaging)assets\windows-vulkan&quot; --build-path &quot;$(IntermediateOutputPath)stride-assets-vulkan&quot; --platform Windows --compile-property:StrideGraphicsApi=Vulkan" />

    <!-- Platform 2: Direct3D11 (DXIL) — fallback for Windows -->
    <Exec Command="dotnet &quot;$(StrideCompileAssetCommand)&quot; --package-file &quot;$(MSBuildProjectFullName)&quot; --output-path &quot;$(ModPkgStaging)assets\windows-dx11&quot; --build-path &quot;$(IntermediateOutputPath)stride-assets-dx11&quot; --platform Windows --compile-property:StrideGraphicsApi=Direct3D11" />

    <!-- Platform 3: Direct3D12 (DXIL) — modern Windows standard, required for future ray tracing -->
    <Exec Command="dotnet &quot;$(StrideCompileAssetCommand)&quot; --package-file &quot;$(MSBuildProjectFullPath)&quot; --output-path &quot;$(ModPkgStaging)assets\windows-dx12&quot; --build-path &quot;$(IntermediateOutputPath)stride-assets-dx12&quot; --platform Windows --compile-property:StrideGraphicsApi=Direct3D12" />
  </Target>

  <!-- ═══════════════════════════════════════════════════════════════
       TRAP 3 MITIGATION: gen-guids runs as an <Exec> (separate process),
       so the PackTool MUST bundle Stride.Core.Storage + Stride.Core.IO
       DLLs alongside itself in the SDK tools directory. The PackTool
       reads the binary ContentIndexMap using Stride's own deserialization.
       Long-term: custom IAssetBuildStep in the AssetCompiler emits
       the JSON natively as part of the compilation pass.
       ═══════════════════════════════════════════════════════════════ -->
  <Target Name="ModEmitAssetGuidJson"
          AfterTargets="ModCompileAssets"
          Condition="Exists('$(ModPkgStaging)assets\windows-vulkan\index')">
    <Exec Command="dotnet &quot;$(MSBuildThisFileDirectory)..\tools\Modulus.Mod.PackTool\Modulus.Mod.PackTool.dll&quot; gen-guids --db-path &quot;$(ModPkgStaging)assets\windows-vulkan&quot; --output &quot;$(ModPkgStaging)asset-guids.json&quot;" />
  </Target>

  <!-- 2. Stage mod.json + (optional) assemblies + asset-guids into staging dir -->
  <Target Name="ModStage" AfterTargets="ModEmitAssetGuidJson" Condition="Exists('$(ModManifest)')">
    <Copy SourceFiles="$(ModManifest)" DestinationFolder="$(ModPkgStaging)" />
    <!-- Copy mod assemblies (already cleaned by ExcludeEngineAssembliesFromOutput) -->
    <ItemGroup Condition="'$(ModType)' != 'data'">
      <ModAssemblies Include="$(TargetDir)$(AssemblyName).dll" />
      <ModAssemblies Include="$(TargetDir)$(AssemblyName).pdb" Condition="Exists('$(TargetDir)$(AssemblyName).pdb')" />
    </ItemGroup>
    <Copy SourceFiles="@(ModAssemblies)" DestinationFolder="$(ModPkgStaging)assemblies\" SkipUnchangedFiles="true" />
  </Target>

  <!-- 3. Verify mod package -->
  <!-- NOTE: $(GameAssetDbPath) is optional for local development. If not set,
       the verifier skips URL/GUID collision checking and emits [Warning].
       For release builds, set <ModStrictVerification>true</ModStrictVerification>
       which treats missing --game-db-path as an error instead of a warning.
       The mod author sets it in their .csproj:
       <GameAssetDbPath>path\to\game\data\db</GameAssetDbPath> -->
  <Target Name="ModVerify" AfterTargets="ModStage" Condition="Exists('$(ModManifest)')">
    <PropertyGroup>
      <GameAssetDbPath Condition="'$(GameAssetDbPath)' == ''"></GameAssetDbPath>
    </PropertyGroup>
    <Exec Command="dotnet &quot;$(MSBuildThisFileDirectory)..\tools\Modulus.Mod.PackTool\Modulus.Mod.PackTool.dll&quot; verify --mod-dir &quot;$(ModPkgStaging)&quot; --game-db-path &quot;$(GameAssetDbPath)&quot;">
      <ExitCode PropertyName="ModVerifyExitCode" />
    </Exec>
    <Error Condition="'$(ModVerifyExitCode)' == '1'" Text="[Modulus.Mod.Sdk] Mod verification failed — see output above." />
    <Warning Condition="'$(ModVerifyExitCode)' == '2'" Text="[Modulus.Mod.Sdk] Mod verification passed with warnings." />
  </Target>

  <!-- 4. Package into .modpkg -->
  <Target Name="ModPack" AfterTargets="ModVerify" Condition="Exists('$(ModManifest)')">
    <PropertyGroup>
      <ModPkgOutput>$(BaseOutputPath)$(AssemblyName).modpkg</ModPkgOutput>
    </PropertyGroup>
    <Exec Command="dotnet &quot;$(MSBuildThisFileDirectory)..\tools\Modulus.Mod.PackTool\Modulus.Mod.PackTool.dll&quot; pack --source &quot;$(ModPkgStaging)&quot; --output &quot;$(ModPkgOutput)&quot;" />
  </Target>
</Project>
```

**Asset URL virtualization (including Trap 5 correction — VERIFIED in source):**

Stride's AssetCompiler does NOT use `RootNamespace` or `AssemblyName` as a URL prefix. Virtual URLs are derived from the file path relative to the asset source folder (`PackageLoadingAssetFile.AssetLocation` in `PackageLoadingAssetFile.cs:28`). `Assets/Scene.sdscene` → URL `Scene`. The `RootNamespace` is stored on the `Package` object but is NOT used for URL generation.

This means:
- `Assets/Scene.sdscene` → URL `Scene` (not `com.example.mymod/Scene`)
- A mod's `Scene` URL **silently overrides** the game's `Scene` asset via `CompositeFileProviderService` priority (last added = highest priority)

**Two-part fix for URL safety:**

1. **Namespacing via subdirectories (default):** The SDK template creates assets under `Assets/{ModId}/` so `Assets/com.example.mymod/Scene.sdscene` → URL `com.example.mymod/Scene`. This prevents accidental collision.

2. **`explicitOverrides` for intentional patching:** When a mod author deliberately wants to override a game asset at the same URL (e.g., replacing `Scene`), they add the URL to the `explicitOverrides` array in `mod.json`. The verifier checks: collision detected → is URL in explicitOverrides? → error or info.

The `RootNamespace` enforcement from the previous plan revision is **dropped** — it doesn't affect URLs. Mod authors are free to choose their own `RootNamespace`.

**GUID collision detection:**

The `verify` command cross-references the mod's `asset-guids.json` against the game's own asset database (passed via `--game-db-path`). **URL collisions are the primary override detector** — since Stride's `CompositeFileProviderService` resolves by URL, a mod asset with the same URL as a game asset will silently override it regardless of GUID. GUID collisions are a secondary concern (comparatively rare, often accidental).

The verifier runs a two-tier collision check:

**Tier 1 — URL collision (primary):** Does a mod asset's virtual URL match a game asset's virtual URL?
- **NOT in explicitOverrides** → **Error**, build halts. Message: `[Error] URL collision: mod asset 'com.example.mymod/Scene' overrides game asset 'Scene'. If intentional, add 'Scene' to 'explicitOverrides' in mod.json.`
- **In explicitOverrides** → **Info log**, build continues.

**Tier 2 — GUID collision (secondary):** Does a mod asset's ObjectId match a game asset's ObjectId (different URL)?
- **Warning** — possible asset reference conflict. Mod authors should verify their asset references.

When `--game-db-path` is not provided or doesn't exist, collision checking is skipped with a **Warning** (not a silent pass or hard error). For release builds, the SDK can be configured to treat missing `--game-db-path` as an error (see `$(ModStrictVerification)` property).

The `mod.json` `explicitOverrides` schema:

```json
{
  "id": "com.example.mymod",
  "version": "1.0.0",
  "apiVersion": "1.0",
  "type": "standard",
  "explicitOverrides": [
    "Scene",
    "Ground Material"
  ]
}
```

Each entry is the virtual URL of a game asset that the mod intentionally replaces. This is self-documenting and auditable — when users download a mod, they can inspect its `mod.json` and see exactly what core game assets it modifies before installing.

**Deterministic triple-platform shading:**

The AssetCompiler runs three times — once for Vulkan (SPIR-V), once for DX11 (DXIL), and once for DX12 (DXIL). Mod authors never specify platform flags. At runtime, `ModContentManager` selects the correct platform based on `GraphicsDevice.Platform`.

### Phase 3: ModContentManager + ModShaderManager runtime improvements

**Goal:** The engine loads the mod's compiled asset databases and resolves shader bytecode per platform.

**Files to modify:**
- `sources/engine/Stride.Engine/Modding/ModContentManager.cs` — platform-aware provider selection
- `sources/engine/Stride.Engine/Modding/ModShaderManager.cs` — register mod shader bytecode with `EffectSystem`

**Changes:**

1. **Platform selection at registration time:** `ModContentManager.RegisterModContent()` reads `GraphicsDevice.Platform` and mounts only the matching platform's `DatabaseFileProvider`. A Vulkan game loads `assets/windows-vulkan/`; a DX11 game loads `assets/windows-dx11/`; a DX12 game loads `assets/windows-dx12/`. Falls back to whichever platform directory exists if the exact match isn't present.

2. **Virtual URL compliance:** The `CompositeFileProviderService` handles URL prefix isolation via namespace. The mod's `DatabaseFileProvider` is registered under its modId namespace. `ContentManager.Load<Scene>("com.example.mymod/Scene")` resolves through the composite provider. The `ContentIndexMap` entries already include the virtual path prefix.

3. **Shader bytecode registration:** `ModShaderManager.RegisterModShaders()` scans the mod's `shaders/` directory for `.sdbundle` files and registers them with `EffectSystem`. This fixes the known `Material.New()` silent-failure bug by ensuring all mod shader permutations are available as pre-compiled bytecode.

### Phase 4: `dotnet new` template + Game Studio integration

**Goal:** `dotnet new modulus-mod -n MyMod` produces a complete, buildable mod project.

**Files to create:**

```
sources/templates/Modulus.Templates.Mod/
├── Modulus.Templates.Mod.csproj    (PackAsTemplate)
└── content/
    ├── .template.config/
    │   └── template.json
    ├── MyMod.csproj.template
    ├── mod.json.template
    ├── ModEntry.cs.template
    └── Assets/
        └── com.example.mymod/      ← namespaced subdirectory (Trap 5 fix)
            └── .gitkeep
```

**`MyMod.csproj.template`** — inherits the 4-part recipe from the SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>MyMod</AssemblyName>
    <ModId>com.example.mymod</ModId>
    <ModType>standard</ModType>
  </PropertyGroup>
  <!-- NOTE: RootNamespace does NOT affect asset URLs. URL virtualization
       is achieved by placing assets under Assets/{ModId}/ subdirectories.
       See Trap 5 correction. -->
  <ItemGroup>
    <!-- Modulus.Mod.Sdk: AssetCompiler (via NuGet) + MSBuild targets + pre-copy DLL filtering -->
    <PackageReference Include="Modulus.Mod.Sdk" Version="*" PrivateAssets="all" />
    <!-- Modulus.Engine: engine API. PrivateAssets prevents transitive Stride DLLs. -->
    <PackageReference Include="Modulus.Engine" Version="*" PrivateAssets="all" />
    <!-- Modulus.Modding.Api: the ONLY stable ABI mods should reference at runtime -->
    <PackageReference Include="Modulus.Modding.Api" Version="*" />
  </ItemGroup>
  <!-- Optional: reference the game project for custom asset type resolution -->
  <!-- <ProjectReference Include="..\MyGame\MyGame.csproj" /> -->
</Project>
```

**Three automatic protections from the SDK:**
1. `ExcludeEngineAssembliesFromOutput` — `Stride.*.dll` never written to output (Trap 1)
2. `EnforceModProjectReferenceIsolation` — game assembly never copied (Trap 2)
3. `ModCompileAssets` + `ModEmitAssetGuidJson` — triple-platform compilation + JSON index (Trap 3 mitigated)

**Game Studio integration:** The mod project is opened as part of the game solution. Game Studio's asset editor opens `.sdscene`/`.sdmat` files normally. The "Build" button in the mod management panel triggers `dotnet build` on the mod project.

### Phase 5: Verification test harness

**Goal:** Automated tests covering the full build→pack→install→load pipeline.

**Files to create:**

```
tests/stride.engine.tests/Modding/ModPipelineTests/
├── ModPackTests.cs               (package creation, validation)
├── ModAssetCompilationTests.cs    (asset compiler invocation from mod project)
├── ModGuidMappingTests.cs        (asset-guids.json generation and injection)
├── ModVerificationTests.cs       (all validators)
└── ModEndToEndTests.cs           (full build → pack → install → load pipeline)
```

**Key test scenarios:**
1. `CreatePackage_ValidMod_ProducesModPkg` — standard mod with code + assets
2. `CreatePackage_DataOnlyMod_ProducesModPkg` — no assemblies, just assets
3. `VerifyMod_StrideDllsInAssemblyDir_Fails` — catches type identity failures
4. `VerifyMod_MissingManifest_Fails` — mod.json required
5. `VerifyMod_InvalidManifestFields_Fails` — bad id, version, etc.
6. `VerifyMod_GuidCollisionWithoutExplicitOverride_Fails` — unlisted collision halts build
7. `VerifyMod_GuidCollisionWithExplicitOverride_Passes` — listed collision allows build with info log
8. `GenGuids_WithAssetDb_ProducesValidJson` — reads ObjectDatabase index
9. `GenGuids_NoAssetDb_Skips` — data-only mod without compiled assets
10. `EndToEnd_StandardMod_LoadsSuccessfully` — build, pack, install, load, verify scene renders
11. `MSBuild_NoStrideDllsInOutput` — `dotnet build` on test mod produces no `Stride.*.dll` (Trap 1 regression)
12. `MSBuild_AssetUrlMatchesSubdirectory` — asset at `Assets/com.example.mymod/Scene.sdscene` → URL `com.example.mymod/Scene` (Trap 5 regression)
13. `MSBuild_SecondBuildSkipsAssetCompilation` — second `dotnet build` with no asset changes skips `ModCompileAssets` (Trap 6 regression)
14. `MSBuild_PureCodeMod_SkipsAssetCompiler` — mod with no assets pays no AssetCompiler cost
7. `GenGuids_WithAssetDb_ProducesValidJson` — reads ObjectDatabase index
8. `GenGuids_NoAssetDb_Skips` — data-only mod without compiled assets
9. `EndToEnd_StandardMod_LoadsSuccessfully` — build, pack, install, load, verify scene renders

## .modpkg Final Directory Layout

Inside the `.modpkg` ZIP (root-level contents):

```
modpkg root/
├── mod.json                    (manifest)
├── asset-guids.json            (GUID → virtual URL mappings)
├── assemblies/
│   └── MyMod.dll               (cleaned — no Stride.*.dll)
├── assets/                     (compiled ObjectDatabase, multi-platform, future-proofed naming)
│   ├── windows-vulkan/
│   │   ├── index
│   │   └── bundles/
│   │       └── default.bundle
│   ├── windows-dx11/
│   │   ├── index
│   │   └── bundles/
│   │       └── default.bundle
│   └── windows-dx12/
│       ├── index
│       └── bundles/
│           └── default.bundle
├── data/                       (optional: mod configuration)
│   └── config.json
└── shaders/                    (optional: extra effect bytecode)
    └── CustomEffect.sdbundle
```

Inside the mod's `bin/Debug/` (before packaging — the staging dir is cleaned after pack):

```
MyMod/bin/Debug/
├── MyMod.dll                   (mod assembly — NO Stride.*.dll contamination)
└── MyMod.modpkg                (ZIP, ready to install)
```

## Build Flow Diagram

```
dotnet build MyMod.csproj
│
├─ 1. C# compilation (standard dotnet)
│     → MyMod.dll in bin/Debug/
│     → ProjectReference game assembly available for type resolution (Analyzer only)
│
├─ 2. ExcludeEngineAssembliesFromOutput (PRE-copy, incremental-safe)
│     → Stride.*.dll / Modulus.Engine.dll never written to output
│     → Game assembly NOT written (ReferenceOutputAssembly=false)
│     → MSBuild Fast Up-To-Date Check stays green ✓
│
├─ 3. ModCompileAssets — THREE times (Vulkan + DX11 + DX12)
│     → Invokes Stride.Core.Assets.CompilerApp.dll (from NuGet package)
│     → Reads Assets/*.sdscene, *.sdmat, *.sdpromodel, *.sdsl
│     → Writes assets/windows-vulkan/{index, bundles/}
│     → Writes assets/windows-dx11/{index, bundles/}
│     → Writes assets/windows-dx12/{index, bundles/}
│     → Asset URLs virtualized under ModNamespace (e.g., com.example.mymod/Scene)
│     → Compiles all shader permutations for both platforms
│
├─ 4. ModEmitAssetGuidJson (parallel JSON index)
│     → Reads binary ContentIndexMap using Stride.Core.Storage
│     → Writes asset-guids.json (virtual URL → ObjectId)
│
├─ 5. ModStage (copy manifest + assemblies into staging)
│
├─ 6. ModVerify (all checks, exit code 0/1/2)
│     → No Stride DLLs in assemblies/ ✓
│     → No GUID collisions with game DB ✓
│     → No circular deps ✓
│
└─ 7. ModPack (staged dir → MyMod.modpkg ZIP)
```

## Data-Only Mod Flow

For mods with no code (`"type": "data"` in `mod.json`):

1. `ExcludeEngineAssembliesFromOutput` — nothing to exclude, target is a no-op
2. `ModCompileAssets` — runs normally (both platforms)
3. `ModEmitAssetGuidJson` — runs normally
4. `ModStage` — skips assembly copy (no `assemblies/` directory)
5. `ModVerify` — skips assembly checks
6. `ModPack` — produces `.modpkg` with no assemblies section

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ModType>data</ModType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Modulus.Mod.Sdk" Version="*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

## Shader Compilation Strategy

**Problem:** Runtime shader compilation doesn't work. `Material.New()` with `MaterialDiffuseLambertModelFeature` creates `RenderMesh` objects that appear in the `VisibilityGroup` but silently fail at `MeshRenderFeature.PrepareEffectPermutationsImpl()`. Four root causes: (1) shader sources stripped by dead-code eliminator, (2) no runtime compiler shipped, (3) silent skip on failed effects, (4) ALC reflection prevents cross-boundary type resolution.

**Solution: Build-time compilation for all platforms.**

The triple-platform AssetCompiler pass handles everything automatically:

1. Mod author creates `.sdmat` in Game Studio referencing `StrideForwardShadingEffect`
2. AssetCompiler compiles ALL permutations for Vulkan (SPIR-V) AND DX11 (DXIL) during the build
3. Compiled bytecode stored in `assets/{platform}/bundles/default.bundle`
4. At runtime, `EffectSystem` finds bytecode via `DatabaseFileProvider` → uses it directly. Zero compilation needed.

For **custom shaders** (`.sdsl` files):
1. `.sdsl` placed in `Assets/Shaders/` of the mod project
2. AssetCompiler compiles them into bytecode for both platforms during the build
3. Bytecode in the platform-specific bundles, resolved at runtime

For **completely new shader effects** not in the game's bundle:
1. Mod ships `shaders/*.sdbundle` with standalone compiled bytecode
2. `ModShaderManager` registers these with `EffectSystem` at mod load time

Mod authors never think about shader platforms or compilation. They create materials in Game Studio, and the SDK handles the rest.

## Execution Order

```
┌──────────────────────────────────────────────────────────────┐
│ Phase 1: Modulus.Mod.PackTool CLI                            │
│ (verification + ZIP packaging + gen-guids — testable now)   │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ Phase 2: Modulus.Mod.Sdk (MSBuild targets)                  │
│ (pre-copy DLL filtering, triple-platform AssetCompiler build, │
│  JSON index emission, staging, verification, packaging)     │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ Phase 3: ModContentManager + ModShaderManager runtime       │
│ (platform-aware provider selection, shader registration)    │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ Phase 4: Templates + Game Studio integration                 │
│ (dotnet new, asset editing workflow)                         │
└──────────────────────────────────────────────────────────────┘
```

Each phase has a clear testable boundary. Phase 1 has zero engine deps (except gen-guids which needs Stride.Core.Storage). Phase 2 depends on Phase 1's tool. Phase 3 depends on Phase 2's directory layout. Phase 4 is the UX layer.

## Resolved Design Decisions

1. **AssetCompiler distribution** → **Development dependency on `Stride.Core.Assets.CompilerApp` NuGet package.** The SDK declares it as `PackageReference` with `PrivateAssets="all"` AND `GeneratePathProperty="true"` (Trap 4 fix). The `$(ModulusEngineVersion)` property is bound via a centralized `Version.props` file inside the SDK package, imported by `Modulus.Mod.Sdk.props` before targets evaluate (Trap 4 follow-up). The compiler + native deps (assimp, squish/nvtt, dxcompiler, d3dcompiler) come from the package's RID-specific `runtimes/` folders.

2. **Binary index parsing** → **PackTool bundles Stride.Core.Storage + Stride.Core.IO DLLs alongside itself** (Trap 3 fix). The `<Exec>` task spawns a separate OS process, not an in-process call. The PackTool targets `net10.0-windows7.0` and bundles all dependent Stride DLLs in the SDK tools directory. Long-term: custom `IAssetBuildStep` in the AssetCompiler emits JSON natively.

3. **DLL cleanup** → **Pre-copy interception via `ReferenceCopyLocalPaths` removal** (Trap 1 fix). Never delete after build. Incremental compilation stays green.

4. **ProjectReference game assembly leakage** → **Enforced `Private=false`** + Trap 1's `ReferenceCopyLocalPaths` filtering (Trap 2 fix, corrected after review). The previous plan used `ReferenceOutputAssembly=false` + `OutputItemType=Analyzer` but this was rejected — Analyzer references are loaded as Roslyn diagnostic assemblies, NOT as metadata references. Mod code cannot compile against game types via Analyzer. Verified approach: normal compilation reference + Private=false + ReferenceCopyLocalPaths filtering.

5. **Asset URL collisions** → **Two-part safety net** (Trap 5 corrected after source verification). Stride's AssetCompiler uses file-path-relative URLs, NOT `RootNamespace`. `Assets/Scene.sdscene` → URL `Scene`. The `CompositeFileProviderService` resolves by provider priority (mod overrides game silently). Fix: (a) SDK template creates `Assets/{ModId}/` subdirectories so mod URLs are automatically prefixed (e.g., `com.example.mymod/Scene`), and (b) `explicitOverrides` array in `mod.json` makes intentional game-asset patching auditable.

6. **GUID collisions** → **Explicit per-asset override manifest.** Replaced blanket `overwriteGameAssets: true` with `explicitOverrides` array in `mod.json`. Each entry names the exact virtual path of the game asset being patched. Verifier runs three-tier check: collision detected → is it in explicitOverrides? → error or info. Self-documenting and auditable.

7. **Shader platform targeting** → **Triple compilation:** AssetCompiler runs once for Vulkan (SPIR-V), once for DX11 (DXIL), and once for DX12 (DXIL). Outputs in `assets/windows-vulkan/`, `assets/windows-dx11/`, and `assets/windows-dx12/`. Runtime selects based on `GraphicsDevice.Platform`. Directory naming is future-proofed for D3D12, Metal, and WebGPU (`{os}-{graphicsapi}` pattern). DX12 is included from the start because it's becoming the Windows standard and is required for future ray tracing support.

8. **GraphicsCompositor inheritance** → **Mods inherit the game's compositor by default.** The compositor loads before the scene (we fixed this ordering specifically). Mod scene's camera is assigned to the game compositor's "Main" slot. A mod can opt into shipping its own compositor but must define compatible camera slots.

9. **Build performance** → **Inputs/Outputs on ModCompileAssets target** (Trap 6 fix). MSBuild skips asset compilation entirely for code-only changes after the first build. Three `<Exec>` invocations (Vulkan + DX11 + DX12) only run when assets actually change.

10. **Package path property + version binding** → **Explicit `GeneratePathProperty="true"`** on the CompilerApp package reference (Trap 4 fix). `$(ModulusEngineVersion)` is bound via `Version.props` imported in the SDK `.props` file before targets evaluate (Trap 4 follow-up). Without either, the target can't find the compiler.

11. **SDK/engine version coupling** → `Modulus.Mod.Sdk` is tightly coupled to the engine's AssetCompiler version via `$(ModulusEngineVersion)`. In practice, the SDK package version, engine runtime version, and AssetCompiler compatibility version are three distinct concepts. The SDK version increments with SDK changes; the engine version increments with engine changes; the AssetCompiler compatibility is a constraint. Minor engine patches (e.g., 1.0.1 → 1.0.2) that don't change the asset serialization format should NOT require an SDK update. The `Version.props` file inside the SDK should track the minimum compatible AssetCompiler version, not the exact engine version. ⚠️ **This needs further design work before Phase 2 is implemented.**

12. **GraphicsDevice.Platform availability** → Phase 3 reads `GraphicsDevice.Platform` at `ModContentManager.RegisterModContent()` time. In Stride's lifecycle, `GraphicsDevice` is created in `InitializeBeforeRun()` which runs before `LoadContent()`. `LoadModsEarly()` is called in `Initialize()`, which may execute before the device is fully ready. If there's any uncertainty, reading the platform from engine config (available early) or deferring provider selection until the first `ContentManager.Load<>()` call is a safer fallback.

13. **Verify `RootNamespace` vs `AssemblyName` as URL prefix** → **Confirmed: NEITHER is used for URL prefixing.** Asset URLs are derived from file paths relative to the `Assets/` source folder (`PackageLoadingAssetFile.AssetLocation` in `PackageLoadingAssetFile.cs:28`). The previous plan revision incorrectly stated `RootNamespace` determines the URL prefix — this has been corrected. Namespace safety is achieved via subdirectory naming (`Assets/{ModId}/Scene.sdscene` → URL `{ModId}/Scene`).

## Integration Notes (Cross-References to Engine Plan)

### `.modpkg` layout alignment with MODULUS-ENGINE-PLAN.md

The engine plan (Phase 6.1) defines a flat `assets/` directory for a single platform. This compilation plan introduces `assets/windows-vulkan/`, `assets/windows-dx11/`, and `assets/windows-dx12/` sub-directories with future-proofed naming (`{os}-{graphicsapi}`). The canonical layout in this plan supersedes the engine plan's Phase 6.1 layout. When implementing `ModContentManager`, use the canonical triple-platform layout. The `data/` directory from the engine plan (for mod configuration) is preserved here as an optional directory — it's not compiled by the AssetCompiler and is simply packaged alongside the compiled assets.

### Engine plan phase mapping

| Compilation Plan Phase | Engine Plan Phase(s) | Notes |
|---|---|---|
| Phase 1 (PackTool) | Phase 6 (Packaging) | Tool is also used by Phase 8 (Test Mods) |
| Phase 2 (Mod.Sdk) | Phase 6 (Packaging) | Extends packaging with MSBuild targets |
| Phase 3 (ModContentManager) | Phase 3.4 + Phase 4.7 | Additions to already-planned files |
| Phase 4 (Templates) | Phase 9.3 + Phase 7 | dotnet new + Game Studio integration |
| Phase 5 (Test harness) | Phase 8 (Test Mods) | End-to-end pipeline validation |

### Build-time vs. runtime validators

The compilation plan's `Modulus.Mod.PackTool` verifiers cover the **build path** (developer building a mod). The engine plan's `ModValidator.cs` covers the **install/runtime path** (loading a downloaded `.modpkg`). Both are needed. A user who installs a `.modpkg` from the internet skips the build process entirely, so runtime `ModValidator.cs` must still perform: GUID collision check (against `explicitOverrides`), Stride DLL contamination check, and circular dependency detection. The checks should overlap significantly between build-time and runtime validators.

### ProjectReference isolation approach (corrected after review)

The `EnforceModProjectReferenceIsolation` target (Trap 2 fix) uses `Private=false` on ProjectReferences. This is the approach verified by the existing test mods (e.g., `mod-assets/ModAssets.csproj`), which use plain `ProjectReference` with no explicit metadata and rely on the `ExcludeEngineAssembliesFromOutput` target (Trap 1) to prevent Stride DLLs from reaching the output directory.

The previous plan revision used `ReferenceOutputAssembly=false + OutputItemType=Analyzer`, but this was **rejected after review** — `OutputItemType=Analyzer` loads assemblies as Roslyn diagnostic analyzers, NOT as metadata references. Mod code cannot compile against game types via Analyzer references. The correct approach is: normal compilation reference (`ReferenceOutputAssembly=true`, the default) + `Private=false` (don't copy transitive deps) + Trap 1's `ReferenceCopyLocalPaths` filtering (final safety net).

**AssetCompiler limitation remains:** The AssetCompiler is a separate `dotnet` process invoked via `<Exec>`. It resolves game-specific asset types through Stride's normal package resolution, not through MSBuild ProjectReference metadata. For mods using only standard Stride types this doesn't matter. For mods with game-specific custom asset types, the mod `.csproj` must reference the game package so the AssetCompiler can discover the types during its own resolution.
