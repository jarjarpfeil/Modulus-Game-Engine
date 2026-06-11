Running natively on Windows via git-bash (MSYS2/MINGW64). Home: /c/Users/jarja. Project at D:\modulus-game-engine (Stride engine fork, accessible as /d/modulus-game-engine/ in git-bash). Default CWD is C:\Users\jarja — always specify workdir=/d/modulus-game-engine for project commands.
§
Modulus Engine: Fork of stride3d/stride. Modding-focused game engine with ALC isolation, cross-game mods, hot-swappable assemblies. 10-phase plan at C:\Users\jarja\.local\share\opencode\plans\MODULUS-ENGINE-PLAN.md. Phase 0 = fork setup (build, test, Game Studio). Phase 1 = HTTP API on port 9876. Phases 3-9 = modding foundation through docs.
§
User (jarjar) is building Modulus Engine. .NET 10, Windows, AMD RX 6950 XT, 4K @ 150% DPI. Prefers working code over verbose explanations. Don't ask user to test changes manually — launch editor/API and verify yourself. Read log files directly from disk. Commit incrementally.
§
Plan decisions: Fork full Stride repo (~120+ projects). Keep Game Studio (WPF). Physics/Audio/Rendering replaceable via services (deferred). Modulus.Modding.Api = stable ABI. Mods = managed C# only, no native DLLs. Load order = topological sort + cycle detection. No sandboxing v1 (NeoForge model). Stride source mods go in sources/engine/Stride.Engine/Modding/. Event hooks (1-2 lines) in Stride files, logic in Modulus assemblies.
§
Stride build gotchas (2026-06-04): Must pass -p:StrideNativeWindowsArm64Enabled=false to all dotnet build/test commands — ARM64 native linking fails without MSVC ARM64 toolset installed. Build commands: dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false, dotnet test build/Stride.Tests.Simple.slnf -p:StrideNativeWindowsArm64Enabled=false --no-build. Full solution: 0 errors, 1086 warnings. Tests: 1635 passed, 7 skipped, 0 failed. BACKERS.md is required by GameStudio About page — don't delete it.
§
Stride API gotchas: IServiceRegistry is in Stride.Core (not Stride.Games). GraphicsDevice.Platform is STATIC (not instance). GraphicsDevice.RendererName for GPU name. SceneInstance inherits EntityManager which implements IReadOnlySet<Entity> — use scene.Add()/Remove()/Count/Select(), NOT scene.Entities. EntityManager.Add() is internal (accessible within same assembly). GlobalLogger.GlobalMessageLogged is Action<ILogMessage> (one arg, not two). ILogMessage has .Type (LogMessageType), .Module (string?), .Text (string). GameSystemBase in Stride.Games, Game in Stride.Engine.
§
MCP server configured in ~/.hermes/config.yaml as 'modulus' — stdio transport via dotnet run. Tools prefixed mcp_modulus_*. Only works when Game Studio is running (HTTP API on port 9876). Tools: get_engine_status, get_entities, get_entity, create_entity, delete_entity, get_scenes, get_editor_status, get_logs, get_console.
§
Phase 3+4 complete: Modding foundation (12 files + 1 in Core.Serialization) + Mod lifecycle (7 new files). 59 modding tests pass. Game.ModHost auto-init in Game.Initialize(). GUID-aware pipeline via asset-guids.json. OrphanComponentHandler for save compatibility. Key gotchas: TypeDescriptorFactory in Stride.Core.Reflection assembly (use reflection from Engine), DataSerializerFactory.ClearAssemblySerializers must clear ALL aliases, MicroThreadState has no Waiting, ModShaderDeclaration not ModShaderEntry, DataContractAttribute.Alias not .Name, ALC WeakReference tests unreliable in xUnit (100-cycle memory test is authoritative).
§
Phase 6 (Mod Packaging & Distribution) complete: ModLoadOrderResolver.cs (topological sort + DFS cycle detection + loadOrder tie-breaking), ModPackageManager.cs (.modpkg ZIP packaging/install/uninstall/listing/validation with metadata tracking for clean uninstall). ModHost.LoadAllMods() now uses ModLoadOrderResolver for dependency-ordered loading. 148 modding tests pass total.
§
Phase 7 (Editor Integration) complete. Phase 8 (Test Mods) complete. 180 total modding tests pass. Comprehensive audit-and-fix session (2026-06-05): fixed 45 issues across 19 files — 6 critical bugs (handler chain, QueryString crash, double-registration, EventBus modId, EnableStatePersistence, dead Draw queue), 4 security vulns (path traversal, native DLL validation, backup overwrite, rate limiting), 6 API gaps (IModContext.ModId, GUID injection, OrphanComponent wiring, EffectSystem integration), 5 reliability fixes (MicroThread detection, cycle detection null-parent, frame budget, EnableMod dedup), 8 design flaws (shared assembly logic, temp dir, discovery cache, serialization guard, content dir caching, backup on uninstall), 3 missing features (ModCompatibility check on load, ModEventBusProxy, ModDiscovery cache). Updated 3 versioning tests to match new behavior. Build: 0 errors, 180/180 tests pass. Key new gotchas: DefaultEntityComponentProcessorAttribute uses TypeName (not ProcessorType), ContentManager in Stride.Core.Serialization.Contents, EffectSystem in Stride.Rendering (not Effects), ModLoadContext must return null for shared assemblies (not the assembly itself), ModEventBusProxy auto-tags subscriptions with modId.
§
SpaceEscape mod decomposition (2026-06-06): Successfully decomposed SpaceEscape sample into 6 portable mods (character, background, ui, rendering, assets, chaos). All mods compile. Key gotchas: (1) write_file with /d/ paths doubles in MSYS - use D:\ absolute paths, (2) EntityTransformExtensions.AddChild() needs explicit class call when Entity type conflicts, (3) EntityProcessor<T> has no Entity property - use ComponentDatas.First().Key.Entity, (4) Central Package Management requires Directory.Packages.props entry + PackageReference without Version, (5) IModEventBus doesn't expose SubscriptionCount - cast to ModEventBus, (6) delegate_task may 401 on custom providers - fall back to direct implementation. Extended HTTP API with /mod/discover, /mod/load, /mod/unload, /mod/reload, /mod/event-bus/status, /mod/test/run endpoints. All 180 existing modding tests pass. Full solution builds clean.
§
SpaceEscape mod decomposition: 6 mods created and loading successfully. Key gotchas found:
1. Game() constructor (line 246 of Game.cs) already creates and registers ModHost — don't pre-register in host app
2. Use Game.Started event to load mods after initialization
3. EntityTransformExtensions.AddChild/RemoveChild are extension methods, not instance methods on Entity
4. EntityProcessor<T> has no Entity property — use ComponentDatas[kvp].Key.Entity to get the entity
5. Stride.UI types require PackageReference to Stride.UI (added to tests/Directory.Packages.props)
6. IContentManager is in Stride.Core.Serialization.Contents namespace
7. PlayingAnimation is in Stride.Animations namespace
8. GraphicsDevice is in Stride.Graphics namespace
9. ssdeps files get locked during build — delete them before rebuilding
10. freetype native DLL must be in runtimes/win-x64/native/ for font system
§
SpaceEscape mod decomposition session 2026-06-06 (continued): Fixed ModReassemblyHost crash - Game() constructor already creates ModHost, don't create second one. Use Game.GameStarted event pattern. freetype native DLL must be in runtimes/win-x64/native/. ssdeps files lock during build - delete before rebuild. Updated stride-engine-development skill with new pitfalls (90-93) and reference file spaceescape-decomposition-2026-06-06.md.
§
SpaceEscape mod decomposition - CRITICAL ALC fix (2026-06-06):
Problem: Mod components couldn't be cast to EntityComponent because mod assemblies loaded their own copy of Stride.Engine.dll in their ALC, creating separate type identity.
Solution: Two-part fix:
1. Add RemoveStrideDlls post-build target to all mod .csproj files that deletes Stride.*.dll from mod output after build
2. This ensures mod components inherit from the engine's EntityComponent (default ALC), not a mod-local copy
Also needed: copy freetype.dll native dependency to host output for font system
Result: All 6 mods load, entities with mod components can be created, game runs successfully
§
SpaceEscape mod decomposition session 2 (2026-06-06):
- Fixed ALC type identity by adding RemoveStrideDlls post-build target + PrivateAssets="all" on mod project references
- SceneInstance inherits EntityManager (IReadOnlySet<Entity>) — use scene.FirstOrDefault() not scene.Entities
- Must create scene BEFORE LoadAllMods() so processors can register against EntityManager
- Loose .sd* files can't be loaded by ObjectDatabase without indexing — need compiled asset.db
- Game() constructor creates ModHost — use Game.Services.GetService<ModHost>() not new ModHost()
- Updated stride-engine-development skill with 3 new pitfalls (#19-21) and rewrote reference file
§
SpaceEscape mod decomposition final state (2026-06-06):
- 6 mods load successfully in ModReassemblyHost
- Game window renders with 9 colored cube entities
- ALC type identity fix: RemoveStrideDlls post-build target + PrivateAssets="all"
- GameStarted event timing: use SyncScript for scene creation, not GameStarted
- Material/Mesh creation APIs documented in stride-engine-development skill
- All 180 modding tests pass
- Key files: .hermes/plans/2026-06-06-spaceescape-mod-decomposition.md (plan), stride-engine-development skill (pitfalls 17-23)
§
Game.Initialize() mod fix (2026-06-06): Modified Game.cs Initialize() to check for existing ModHost before creating one. The Game() constructor already creates and registers ModHost (line 246-247). Initialize() was creating a second one, causing "Service is already registered" crash. Fix: ModHost = Services.GetService<ModHost>(); if (ModHost == null) { ModHost = new ModHost(Services); ModHost.RegisterService(); }  GameProperty.ModHost is set from the service. This fix was necessary for ANY host app that creates a Game instance — without it, Stride.GameStudio.exe also crashes.
§
Mod auto-loading (2026-06-07): Game.Initialize() now adds ModAutoLoadSystem — a one-shot GameSystemBase that calls ModHost.LoadAllMods() on first Update(). Zero game code needed. Mods in mods/ directory auto-load. File: Stride.Engine/Modding/ModAutoLoadSystem.cs. Also: ModContentManager.InjectGuidMappingsIntoRuntime now actually injects entries into ContentIndexMap using indexer (indexMap[url] = (ObjectId)guid). Previously it was just logging. This enables ContentManager.Load<Scene>(url) to resolve mod assets.
§
Modulus.Engine NuGet package rename (2026-06-07): Added <PackageId>Modulus.Engine</PackageId> to Stride.Engine.csproj. PackageId only (not AssemblyName) — InternalsVisibleTo("Stride.Engine") in other Stride assemblies would break with AssemblyName rename. NuGet package is Modulus.Engine.nupkg but DLL stays Stride.Engine.dll. TestGame references "Modulus.Engine" PackageReference. nuget.config in TestGame points to D:\Modulus-Game-Engine\bin\packages as local source. All 192 modding tests pass.
§
SpaceEscape mods wired into TestGame (2026-06-07): 6 mods load in D:\TestGame\MyGame\Bin\Windows\Debug\mods\. Dependency graph: assets(1)→rendering(5)→character(10)→background(20)→ui(30), chaos(999). mod.json files updated with scenes[] and dependencies. All mod.json files need manual copy from source to test output (build doesn't copy them). TestGame uses Modulus.Engine NuGet package from local source (nuget.config points to D:\Modulus-Game-Engine\bin\packages). MD5 verification confirms correct DLL resolution.
§
Phase 9 (Documentation & Distribution) complete: DocFX setup (docfx.json, index.md, toc.yml), 8 articles (~170KB total: writing-a-mod, mod-api-reference, cross-game-mods, architecture, mcp-setup, mcp-tool-reference, stride-modifications, fork-management), 3 CI/CD workflows (ci.yml, release.yml, docs.yml), 2 dotnet new templates (modulus-game, modulus-mod), ModulusEngine.Templates.csproj. Stride.Modding.Editor added to Stride.sln. 192 total modding tests pass. Deferred items: Standard Library mod, Translation Mods, Mod Configuration System.