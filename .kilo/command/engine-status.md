---
description: Check engine status via the HTTP API
---
Verify a running engine instance by hitting `/api/v1/status`.

```bash
curl http://localhost:9876/api/v1/status
```

If the connection is refused, the engine is not running — suggest `/launch-editor`.
