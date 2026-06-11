---
description: Build the full Modulus/Stride engine
---
Build the full engine solution.

Run from `D:\Modulus-Game-Engine`:

```bash
dotnet build build/Stride.sln -p:StrideNativeWindowsArm64Enabled=false
```

If the build fails, report the errors with `file:line` references. Do not attempt to fix source code — return the failure to the user with a clear summary.
