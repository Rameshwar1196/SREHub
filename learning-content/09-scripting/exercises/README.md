# Scripting exercises

Each file is a skeleton with a docstring describing the task and a `pytest` test stub.
Implement them under time pressure (20–30 minutes each) to simulate the interview.

Suggested order:

1. `log_stats.py` — parse access log, report per-endpoint RPS / error rate / percentiles.
2. `reconcile.py` — diff desired vs actual state, emit idempotent actions.
3. `rate_limiter.py` — token bucket, thread-safe.
4. `api_client.py` — paginated REST client with backoff and 429 `Retry-After` handling.
5. `merge_windows.py` — merge overlapping maintenance windows.
6. `sliding_window_alert.py` — alert when the error rate over a 60s sliding window exceeds a threshold.
7. `dep_graph.py` — topological sort + cycle detection over a service dependency map.

## Working rules

- Write the test first for at least two of them.
- Handle: empty input, malformed lines, huge files (stream, don't `read()`).
- Always add `--dry-run` to anything that mutates state.
- Exit non-zero on failure; log to stderr; keep stdout machine-parseable.

## Quick start

```bash
python -m venv .venv
.venv\Scripts\Activate.ps1      # PowerShell
pip install pytest httpx
pytest -q
```
