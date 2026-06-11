---
description: Launch the Stride Game Studio editor (WPF)
---
Launch the WPF Game Studio editor in the background.

```bash
dotnet run --project sources/editor/Stride.GameStudio/Stride.GameStudio.csproj \
  -p:StrideNativeWindowsArm64Enabled=false
```

This is a GUI app — it will not return until closed. Run in a background terminal.

Once running, the engine's HTTP API is available at `http://localhost:9876`. Verify with:
```bash
curl http://localhost:9876/api/v1/status
```
