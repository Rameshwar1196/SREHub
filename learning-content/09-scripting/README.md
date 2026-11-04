# 09 — Scripting & Automation

SRE coding interviews are **not** LeetCode-hard. Expect: parse this log, call this API,
reconcile these two lists, write a retry wrapper, implement a rate limiter, build a small CLI.
Clean, tested, production-minded code beats clever code.

## What to practise

| Skill | Why it appears |
|---|---|
| File/log parsing & aggregation | Every incident involves logs |
| HTTP clients with retries/backoff/timeouts | Calling cloud APIs reliably |
| JSON/YAML manipulation | Manifests, API responses |
| Concurrency (threads/asyncio/goroutines) | Fan-out over many hosts/resources |
| Data structures: dict/set/heap/deque | Counting, dedup, top-N, sliding windows |
| CLI ergonomics: args, exit codes, stderr | Scripts that pipelines depend on |
| Idempotency & dry-run | Anything that mutates production |

## Python patterns

```python
# Retry with exponential backoff + full jitter — know this by heart.
import random, time, logging
from typing import Callable, TypeVar

T = TypeVar("T")

def retry(fn: Callable[[], T], attempts: int = 5, base: float = 0.5,
          cap: float = 30.0, retry_on: tuple[type[Exception], ...] = (Exception,)) -> T:
    for attempt in range(1, attempts + 1):
        try:
            return fn()
        except retry_on:
            if attempt == attempts:
                raise
            sleep = random.uniform(0, min(cap, base * 2 ** (attempt - 1)))
            logging.warning("attempt %d failed, retrying in %.2fs", attempt, sleep)
            time.sleep(sleep)
    raise AssertionError("unreachable")
```

```python
# Streaming log aggregation: p95 latency and error rate per endpoint, constant memory per key.
import re, sys
from collections import defaultdict
from statistics import quantiles

LINE = re.compile(r'(?P<ip>\S+) \S+ \S+ \[(?P<ts>[^\]]+)\] "(?P<m>\S+) (?P<path>\S+)[^"]*" '
                  r'(?P<status>\d{3}) \d+ .* (?P<ms>\d+)$')

lat: dict[str, list[int]] = defaultdict(list)
total: dict[str, int] = defaultdict(int)
errors: dict[str, int] = defaultdict(int)

for line in sys.stdin:
    m = LINE.match(line)
    if not m:
        continue
    path = m["path"].split("?")[0]
    total[path] += 1
    if m["status"].startswith("5"):
        errors[path] += 1
    lat[path].append(int(m["ms"]))

for path, count in sorted(total.items(), key=lambda kv: -kv[1])[:20]:
    samples = sorted(lat[path])
    p95 = samples[int(len(samples) * 0.95) - 1] if samples else 0
    print(f"{path:40s} n={count:<8d} err={errors[path] / count:.2%} p95={p95}ms")
```

```python
# Concurrent health check across many endpoints with a timeout budget.
import asyncio, httpx

async def check(client: httpx.AsyncClient, url: str) -> tuple[str, int | str]:
    try:
        r = await client.get(url, timeout=5.0)
        return url, r.status_code
    except Exception as exc:
        return url, repr(exc)

async def main(urls: list[str]) -> None:
    limits = httpx.Limits(max_connections=50)
    async with httpx.AsyncClient(limits=limits) as client:
        results = await asyncio.gather(*(check(client, u) for u in urls))
    for url, status in results:
        print(f"{status}\t{url}")
```

Also practise: paginating a REST API, `argparse` CLIs with `--dry-run`, reading K8s objects with
the `kubernetes` client, and writing `pytest` tests for the above.

## Bash patterns

```bash
#!/usr/bin/env bash
set -Eeuo pipefail            # fail fast; -E propagates traps
IFS=$'\n\t'
trap 'echo "error on line $LINENO" >&2' ERR

readonly LOG_DIR=${LOG_DIR:-/var/log}
usage() { echo "usage: $0 -e <env> [-n]" >&2; exit 64; }

dry_run=false
while getopts ":e:n" opt; do
  case "$opt" in
    e) env=$OPTARG ;;
    n) dry_run=true ;;
    *) usage ;;
  esac
done
[[ -n ${env:-} ]] || usage

run() { $dry_run && { echo "DRY: $*"; return 0; }; "$@"; }
run kubectl -n "$env" rollout restart deploy/web
```

Know: `xargs -P` for parallelism, `timeout`, `flock` for mutual exclusion,
`mktemp` + trap cleanup, `jq`/`yq`, process substitution `<(...)`.

## PowerShell patterns (Azure automation)

```powershell
#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-WithRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][scriptblock] $Script,
        [int] $Attempts = 5,
        [double] $BaseDelaySeconds = 1
    )
    for ($i = 1; $i -le $Attempts; $i++) {
        try { return & $Script }
        catch {
            if ($i -eq $Attempts) { throw }
            $delay = [Math]::Min(30, $BaseDelaySeconds * [Math]::Pow(2, $i - 1))
            $jitter = Get-Random -Minimum 0 -Maximum $delay
            Write-Warning "Attempt $i failed: $($_.Exception.Message). Retrying in $([Math]::Round($jitter,1))s."
            Start-Sleep -Seconds $jitter
        }
    }
}

function Get-UnhealthyAksNode {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $ResourceGroup,
          [Parameter(Mandatory)][string] $ClusterName)

    Invoke-WithRetry { az aks get-credentials -g $ResourceGroup -n $ClusterName --overwrite-existing | Out-Null }

    (kubectl get nodes -o json | ConvertFrom-Json).items |
        Where-Object {
            ($_.status.conditions | Where-Object type -eq 'Ready').status -ne 'True'
        } |
        Select-Object @{n='Node';e={$_.metadata.name}},
                      @{n='Reason';e={($_.status.conditions | Where-Object status -eq 'True').type -join ','}}
}
```

Know: `Get-AzResource`, `Search-AzGraph` (Resource Graph queries at scale), splatting,
`ShouldProcess` for `-WhatIf` support, pipeline-friendly functions, Pester tests.

```powershell
# Find all public IPs across every subscription, fast.
Search-AzGraph -Query "
  Resources
  | where type =~ 'microsoft.network/publicipaddresses'
  | project name, resourceGroup, subscriptionId, ip = properties.ipAddress
" -First 1000
```

## Go (nice to have)

Many SRE tools are Go. Being able to read Go (K8s controllers, exporters, operators) and write
a small HTTP service with graceful shutdown, contexts, and timeouts is a real differentiator.

## Practice problems (do these)

1. Parse an nginx access log → per-endpoint RPS, error rate, p50/p95/p99.
2. Given two lists (desired vs actual resources), output add/remove/update diff. Idempotent reconciler.
3. Implement a token-bucket rate limiter.
4. Write a CLI that drains a K8s node safely, respecting PDBs, with `--dry-run`.
5. Call a paginated REST API, handle 429 with `Retry-After`, and aggregate results.
6. Detect anomalies: given a time series, flag points > 3σ from a rolling mean.
7. Merge overlapping maintenance windows (interval merge — classic).
8. Given a list of services and dependencies, detect cycles (topological sort).
9. Tail a log file and alert when error rate over a 60s sliding window exceeds a threshold.
10. Write a script that reports which AKS pods have no resource limits, as CSV.

Solutions/skeletons live in [exercises/](exercises/).
