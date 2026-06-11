# Fork Management Guide — Modulus Engine

> How to keep your fork healthy, your branches clean, and your PRs reviewable.

---

## 1. Branch Strategy

```
main          ← Always buildable. Tagged releases only. Never commit directly.
  └─ dev      ← Active integration branch. All feature PRs target this.
       ├─ feature/hot-reload-v2       ← Single-purpose feature branches
       ├─ feature/modding-api-v1
       └─ fix/editor-crash-on-reload
```

| Branch | Purpose | Merge Into |
|--------|---------|------------|
| `main` | Stable snapshots. Mirrors every Modulus milestone release. | — |
| `dev` | Where features land for integration testing. | `main` (on release) |
| `feature/*` | Isolated work on a single feature or fix. | `dev` |

**Rules:**

- Never force-push `main` or `dev`.
- `feature/*` branches live until merged; delete them after PR acceptance.
- Rebase your feature branch on the latest `dev` before opening a PR.

---

## 2. Upstream Sync Cadence

Modulus Engine is a fork of [Stride](https://github.com/stride3d/stride). We sync with upstream **at every major Stride release** (e.g., 4.2 → 4.3) or when a critical upstream fix lands that affects our code paths.

| Trigger | Action |
|---------|--------|
| Stride major/minor release | Full merge into `dev`, test, then promote to `main` |
| Critical upstream bugfix | Cherry-pick the relevant commit(s) into `dev` |
| No upstream activity | No action — we stay on our last sync point |

Do **not** sync on every upstream commit. Frequent small syncs create unnecessary churn with no benefit.

---

## 3. Sync Commands

### Initial Setup (one-time)

```bash
# Add the Stride repo as an upstream remote
git remote add upstream https://github.com/stride3d/stride.git

# Verify
git remote -v
# origin    https://github.com/<you>/modulus-engine.git (fetch)
# origin    https://github.com/<you>/modulus-engine.git (push)
# upstream  https://github.com/stride3d/stride.git (fetch)
# upstream  https://github.com/stride3d/stride.git (push)
```

### Performing a Sync

```bash
# 1. Make sure your local dev is current
git checkout dev
git pull origin dev

# 2. Fetch upstream tags and branches
git fetch upstream --tags

# 3. Merge the upstream release tag into dev
#    Replace v4.2.0.1 with the actual Stride release tag
git merge v4.2.0.1 --no-ff -m "Sync with Stride v4.2.0.1"

# 4. Resolve conflicts (see below)
# 5. Test
dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false
dotnet test build/Stride.Tests.Simple.slnf

# 6. Push
git push origin dev
```

### Resolving Conflicts

Conflicts will concentrate in files Stride touches frequently (rendering, ECS core). Our modding code lives in isolated directories (see §4), so conflicts there should be rare.

```bash
# See what's conflicted
git status

# For each conflicted file:
#   - Open in your editor
#   - Keep Modulus changes (ours) unless the upstream change is structural
#   - Re-apply any Modulus-specific logic on top of the new upstream code

# After resolving all conflicts:
git add .
git commit -m "Resolve merge conflicts from Stride v4.2.0.1 sync"
```

**Pro tip:** Use `git diff --name-only --diff-filter=U` to list only unmerged files.

---

## 4. Change Isolation Rules

All Modulus-specific code must live in dedicated directories. **Never sprinkle modding logic into upstream Stride source files.**

```
stride/
├── sources/
│   ├── engine/
│   │   └── Stride.Engine/
│   │       └── Modding/          ← ALL new modding runtime code goes here
│   │           ├── ModLoader.cs
│   │           ├── HotReloadManager.cs
│   │           ├── ModManifest.cs
│   │           └── ...
│   └── editor/
│       └── Stride.Modding.Editor/ ← ALL editor-side modding UI goes here
│           ├── ModdingPanel.xaml
│           ├── ModdingPanel.xaml.cs
│           ├── ModBrowserViewModel.cs
│           └── ...
├── build/
└── ...
```

**Why this matters:**

- Upstream files get rewritten on sync. Any inline Modulus changes will create merge conflicts.
- Isolated directories let `git merge` handle upstream changes with zero conflicts in our code.
- It makes the scope of every Modulus PR immediately obvious.

### What if I need to hook into an upstream class?

Use event dispatch hooks (§5) rather than modifying the upstream class directly.

---

## 5. Minimizing Merge Conflicts

The #1 source of sync pain is **inline logic** — Modulus code added directly into upstream files. Avoid it.

### ❌ Bad: Inline Logic

```csharp
// In sources/engine/Stride.Engine/Entity.cs (UPSTREAM FILE — DO NOT EDIT)
public class Entity
{
    public void Update()
    {
        // ... upstream logic ...

        // Modulus: notify mods of entity update  ← CONFLICT MAGNET
        ModLoader.OnEntityUpdate(this);
    }
}
```

### ✅ Good: Event Dispatch Hook

```csharp
// In sources/engine/Stride.Engine/Modding/ModEngineHooks.cs (OUR FILE)
public static class ModEngineHooks
{
    public static void Initialize(Game game)
    {
        // Subscribe to existing Stride events or use a lightweight patcher
        game.GameSystems.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
                foreach (var system in e.NewItems)
                    ModLoader.OnSystemAdded(system);
        };
    }
}
```

**Principles:**

| Do | Don't |
|----|-------|
| Use Stride's existing events/callbacks | Add `if (ModulusEnabled)` branches in upstream code |
| Create new files in `Modding/` | Edit upstream `.cs` files |
| Use partial classes if Stride supports them | Fork core classes to add one method |
| Hook at initialization time | Patch methods at runtime unless absolutely necessary |

---

## 6. Build Commands

### Full Solution Build

```bash
dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false
```

> The `-p:StrideNativeWindowsArm64Enabled=false` flag skips ARM64 native builds, which saves significant time on x64 dev machines.

### Build Only Modulus Projects

```bash
dotnet build sources/engine/Stride.Engine/Stride.Engine.csproj
dotnet build sources/editor/Stride.Modding.Editor/Stride.Modding.Editor.csproj
```

### Clean Build

```bash
dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false --no-incremental
```

---

## 7. Test Commands

### Run All Tests

```bash
dotnet test build/Stride.Tests.Simple.slnf
```

### Run Only Modulus Tests

```bash
dotnet test build/Stride.Tests.Simple.slnf --filter "FullyQualifiedName~Modulus"
```

### Run Tests for a Specific Project

```bash
dotnet test sources/engine/Stride.Engine.Tests/Stride.Engine.Tests.csproj --filter "FullyQualifiedName~Modding"
```

Always run the full test suite before opening a PR.

---

## 8. Solution Filters for Fast Iteroration

The full `Stride.sln` contains **70+ projects**. Loading it in an IDE is slow. Use **solution filters** (`.slnf`) to load only what you need.

### Available Filters

| Filter | What It Loads | Use When |
|--------|---------------|----------|
| `build/Stride.Tests.Simple.slnf` | Core engine + test runner | Running tests |
| `build/Stride.sln` | Everything | Full build, CI |

### Create Your Own Filter

1. Open `Stride.sln` in Visual Studio.
2. Unload all projects except the ones you need.
3. Right-click the solution → **Save As Solution Filter**.
4. Save as `build/Stride.Modding.slnf` (or similar).

Example custom filter (for Modulus-only work):

```json
{
  "solution": {
    "path": "Stride.sln",
    "projects": [
      "sources\\engine\\Stride.Engine\\Stride.Engine.csproj",
      "sources\\editor\\Stride.Modding.Editor\\Stride.Modding.Editor.csproj",
      "sources\\engine\\Stride.Engine.Tests\\Stride.Engine.Tests.csproj"
    ]
  }
}
```

Save this as `build/Stride.Modding.slnf` and open it instead of the full `.sln` for 10× faster IDE loads.

---

## 9. PR Workflow

Each logical phase of work is a **separate PR into `dev`**. Do not bundle unrelated changes.

### Phase-Based PR Structure

```
Phase 1: Mod loader skeleton        → PR #1 into dev
Phase 2: Hot-reload core            → PR #2 into dev
Phase 3: Editor integration         → PR #3 into dev
Phase 4: Documentation & samples    → PR #4 into dev
```

### PR Checklist

Before opening a PR, ensure:

- [ ] Branch is rebased on latest `dev`
- [ ] All new code lives in `sources/engine/Stride.Engine/Modding/` or `sources/editor/Stride.Modding.Editor/`
- [ ] No upstream files were modified (unless explicitly discussed and approved)
- [ ] `dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false` succeeds
- [ ] `dotnet test build/Stride.Tests.Simple.slnf` passes
- [ ] PR description explains **what** and **why**, not just **how**
- [ ] Commit messages follow: `type(scope): description` (e.g., `feat(modding): add hot-reload file watcher`)

### PR Template

```markdown
## What

Brief description of what this PR does.

## Why

Why this change is needed. Link to issue or design doc if applicable.

## How

Key implementation decisions. Mention any trade-offs.

## Testing

How you verified this works. Include command output if relevant.

## Phase

This is Phase N of the Modulus Engine implementation plan.
```

### Merge Strategy

- **Squash merge** feature branches into `dev` (keeps `dev` history clean).
- **Merge commit** (no squash) when merging `dev` into `main` (preserves full history for releases).

---

## Quick Reference

```bash
# Sync with upstream
git fetch upstream --tags
git merge v4.2.0.1 --no-ff -m "Sync with Stride v4.2.0.1"

# Build
dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false

# Test
dotnet test build/Stride.Tests.Simple.slnf

# Create a feature branch
git checkout dev
git pull origin dev
git checkout -b feature/my-feature

# Open PR
git push origin feature/my-feature
# → Open PR on GitHub targeting `dev`
```

---

*Keep upstream files untouched. Keep modding code isolated. Keep PRs focused.*
