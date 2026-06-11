---
description: Run the simple Stride test suite
---
Run the fast test suite (no Graphics/Physics/Audio).

```bash
dotnet test build/Stride.Tests.Simple.slnf -p:StrideNativeWindowsArm64Enabled=false
```

If you only want modding tests:

```bash
dotnet test build/Stride.Tests.Simple.slnf -p:StrideNativeWindowsArm64Enabled=false \
  --filter "FullyQualifiedName~Modding"
```

Report pass/fail counts and surface the first failure with file:line.
