---
description: Delete locked .ssdeps files (Windows file lock workaround)
---
Stride build sometimes leaves `*.ssdeps` files locked, causing subsequent builds to fail with file-in-use errors. This cleans them up.

From `D:\Modulus-Game-Engine`:

```bash
find . -name "*.ssdeps" -delete
```

Then re-run `/build-engine`.
