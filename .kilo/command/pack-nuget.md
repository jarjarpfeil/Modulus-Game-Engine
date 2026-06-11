---
description: Pack the Modulus.Engine NuGet package to bin/packages/
---
Build the engine and pack the NuGet package.

```bash
dotnet pack sources/engine/Stride.Engine/Stride.Engine.csproj \
  -p:StrideNativeWindowsArm64Enabled=false
```

Output: `bin/packages/Modulus.Engine.{version}.nupkg`. Confirm the file exists and report its path.
