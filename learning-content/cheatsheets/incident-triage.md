# Incident triage one-pager

Print this. Use it when a page fires — and narrate it in troubleshooting interviews.

## 0. First 60 seconds

1. **Acknowledge** the page. Stop the clock on MTTA.
2. **Confirm real impact.** Is it users, or is it the monitoring? Check from outside the system.
3. **Declare** if impact is real. Assign **IC** (not yourself if you're debugging).
4. **Open a channel** — one place for the incident. Post a status line every 15–30 min even if nothing changed.

## 1. Establish scope

- What is broken, for whom, since when?
- Which SLO is burning, and how fast?
- All regions / one region / one AZ / one tenant / one version?
- Is it total or partial (error rate %, latency percentile)?

## 2. What changed?

> ~70% of incidents are caused by a change. Always check this before deep debugging.

- Deployments (app, infra, config) in the last 24h.
- Feature flags toggled.
- Certificate / credential / token expiry.
- DNS or traffic-routing changes.
- Traffic pattern change (spike, new client, retry storm, bot).
- Dependency or cloud-provider status page.
- Scheduled jobs, data migrations, cron.

## 3. Localise (binary search the request path)

```
client → DNS → CDN/WAF → LB/Ingress → service → cache → DB/queue → downstream
```
Slice metrics by: region, AZ, node, pod, version, endpoint, tenant, client version.
Find the narrowest dimension where it's broken and the widest where it's fine.

## 4. Mitigate — before root cause

| Lever | When |
|---|---|
| **Rollback** the last deploy | Anything correlated with a release |
| **Toggle a feature flag off** | New feature implicated |
| **Failover** region/AZ/replica | Localised infra failure |
| **Scale out** | Saturation |
| **Shed load / rate limit** | Overload, retry storm |
| **Drain a bad node/pod** | Single-instance corruption |
| **Restart** (last resort, and capture state first) | Leak, wedged process |
| **Revert config** | Config-driven behaviour change |

Take a heap dump / capture / log snapshot **before** restarting, or you lose the evidence.

## 5. Verify

- Error rate and latency back to baseline on the **user-facing** metric, not just internals.
- Backlogs and queues drained.
- No secondary damage (duplicate writes, poisoned cache, stuck jobs).

## 6. Close out

- Declare resolved; update status page; thank people.
- Schedule the postmortem within 48h while memory is fresh.
- Capture the timeline **now** from the chat log.

---

## Command starter pack

```bash
# Kubernetes
kubectl get po -A --field-selector=status.phase!=Running
kubectl get events -A --sort-by=.lastTimestamp | tail -40
kubectl rollout history deploy/<name>
kubectl rollout undo deploy/<name>
kubectl top po -A --sort-by=cpu | head

# Is it DNS / network / TLS?
dig +short <host>; mtr -rwz <host>
curl -sv -o /dev/null -w 'dns=%{time_namelookup} conn=%{time_connect} tls=%{time_appconnect} ttfb=%{time_starttransfer} total=%{time_total}\n' https://<host>
openssl s_client -connect <host>:443 -servername <host> </dev/null 2>/dev/null | openssl x509 -noout -dates

# Linux box
uptime; dmesg -T | tail -40; vmstat 1 5; iostat -xz 1 3; df -h; df -i; free -m; ss -s

# Azure
az aks show -g <rg> -n <aks> --query provisioningState
az monitor activity-log list --offset 6h --query "[?contains(operationName.value,'write') || contains(operationName.value,'delete')].[eventTimestamp,caller,operationName.value,resourceId]" -o table
```

## Comms template

```
[SEV2] Checkout API elevated errors — INVESTIGATING
Impact: ~8% of checkout requests failing since 14:12 UTC. EU region only.
Current status: rolling back release 2024.10.3. ETA 10 minutes.
IC: <name>   Comms: <name>
Next update: 14:45 UTC
```

## Phrases that score well in interviews

- "First I'd confirm the impact is real and scope it, before touching anything."
- "I'd mitigate first and root-cause afterwards."
- "The first thing I'd check is what changed in the last 24 hours."
- "I'd capture diagnostics before restarting so we don't lose the evidence."
- "I'd assign an IC and keep myself free to debug — or vice versa."
- "Afterwards I'd run a blameless postmortem and track the action items."
