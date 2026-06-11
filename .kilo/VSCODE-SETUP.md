# VSCode Setup Instructions (manual)

The repo's permission rules block automated creation of `.vscode/extensions.json`
and `.vscode/settings.json`. Apply these manually via the VSCode UI or
`Ctrl+Shift+P` → "Preferences: Open Settings (JSON)".

## 1. Install required extensions

The recommended extensions are listed in
`kilo:extensions` below for quick copy-paste.

| Extension | Publisher | Purpose |
|---|---|---|
| `ms-dotnettools.csdevkit` | Microsoft | C# dev kit (project system, debugging) |
| `ms-dotnettools.csharp` | Microsoft | C# language support (fallback if not using DevKit) |
| `ms-vscode.cpptools` | Microsoft | C/C++ intellisense (Stride native shader tools) |
| `ms-vscode.cmake-tools` | Microsoft | CMake language + project tools |
| `twxs.cmake` | twxs | CMake syntax highlighting |
| `kilocode.kilo-code` | Kilo | This agent |

Quick install (one command):
```powershell
code --install-extension ms-dotnettools.csdevkit ms-dotnettools.csharp ms-vscode.cpptools ms-vscode.cmake-tools twxs.cmake kilocode.kilo-code
```

## 2. Settings to apply

Open `Ctrl+Shift+P` → "Preferences: Open User Settings (JSON)" and merge in:

```jsonc
{
  "files.exclude": {
    "**/bin": true,
    "**/obj": true,
    "**/.vs": true,
    "**/node_modules": true
  },
  "search.exclude": {
    "**/bin": true,
    "**/obj": true,
    "**/.vs": true,
    "**/node_modules": true,
    "**/docs-site/docs-site-out/**": true,
    "**/docs-site/_site/**": true
  },
  "files.associations": {
    "*.shader": "hlsl",
    "*.sdsl": "hlsl"
  },
  "csharp.semanticHighlighting.enabled": true,
  "csharp.inlayHints.enableInlayHintsForParameters": true,
  "dotnet.defaultSolution": "build/Stride.sln",
  "[csharp]": {
    "editor.tabSize": 4
  }
}
```

## 3. First-time setup

1. Open the repo root folder in VSCode.
2. Accept the workspace trust prompt if shown.
3. When prompted to install the recommended extensions, click "Install All".
4. The C# Dev Kit will index `build/Stride.sln` (~120 projects — first load takes
   a few minutes; subsequent loads are fast).
5. `Ctrl+Shift+B` → choose "build-engine" task to build the solution.
6. `F5` → choose "Launch Game Studio (WPF)" to start the editor with the
   debugger attached.

## 4. The auto-created files

Two VSCode config files ARE committed (the only ones the permission rules allow):

- `.vscode/tasks.json` — build/test/clean tasks (Ctrl+Shift+P → "Tasks: Run Task")
- `.vscode/launch.json` — debug launch configs (F5)
