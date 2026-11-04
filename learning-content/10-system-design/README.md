# 10 — System Design for SRE

An SRE design interview is a normal system design interview **plus** an obsession with failure modes,
operability, and measurable reliability. Differentiate yourself in the last 15 minutes by talking
about SLOs, blast radius, and what you'd do at 3am.

## Framework (45–60 min)

1. **Clarify (5 min)** — functional requirements, users, scale, read/write ratio, consistency needs,
   latency budget, regions, compliance. Ask for the **reliability target** explicitly.
2. **Estimate (5 min)** — QPS, storage/day, bandwidth, working set. Round aggressively.
3. **High-level design (10 min)** — boxes and arrows. Client → edge → API → services → data → async.
4. **Deep dive (15 min)** — the interviewer picks; be ready on data model, partitioning, caching, queues.
5. **Reliability pass (10 min)** — ← *this is where SREs win*. See checklist below.
6. **Operability pass (5 min)** — deploy, rollback, observe, on-call, cost.

## Back-of-envelope numbers

```
1 day ≈ 86,400 s (~100k)             1 million req/day ≈ 12 req/s
Memory read 100 ns        SSD read 100 µs        Disk seek 10 ms
Same-DC round trip 0.5 ms  Cross-region RTT 50–150 ms
1 Gbps ≈ 125 MB/s
A modern core handles ~1–10k simple req/s; a DB does ~5–20k simple queries/s per node
Rule of thumb: peak = 2–5x average
```

## Reliability checklist (use it out loud)

- **SLIs/SLOs** — what do we measure, where, and what's the target? What's the error budget?
- **Failure modes** — for each component: what if it's slow, down, or returns wrong data?
- **Single points of failure** — DB primary, single region, shared cache, config service, DNS.
- **Blast radius** — cells/shards/stamps, per-tenant isolation, bulkheads.
- **Dependency management** — timeouts (always), retries with backoff+jitter+budget, circuit breakers,
  fallbacks, graceful degradation. Rank dependencies as critical vs. optional.
- **Overload** — load shedding, admission control, priority queues, rate limiting per tenant, backpressure.
- **Statefulness** — replication, quorum, failover (automatic? split brain?), backup + tested restore, RPO/RTO.
- **Data correctness** — idempotency keys, exactly-once illusions (at-least-once + dedupe), reconciliation jobs.
- **Caching** — invalidation strategy, stampede protection (single-flight, jittered TTL), cold-start behaviour.
- **Multi-region** — active/active vs active/passive, data replication lag, failover runbook, split-brain.
- **Change safety** — canary, feature flags, schema migrations (expand/contract), auto rollback.
- **Capacity** — headroom, autoscaling limits, what happens at 10x traffic.
- **Security** — authn/authz, secrets, network segmentation, encryption in transit/at rest, audit.

## Design patterns worth naming

| Pattern | Problem it solves |
|---|---|
| Deployment stamps / cells | Blast radius, tenant isolation, scale unit |
| Queue-based load levelling | Spiky traffic vs fixed downstream capacity |
| CQRS / read replicas | Read scaling, isolating heavy queries |
| Outbox + CDC | Reliable eventing without dual-write inconsistency |
| Saga | Distributed transactions without 2PC |
| Circuit breaker + bulkhead | Prevent cascading failure |
| Sidecar / service mesh | Cross-cutting concerns without app changes |
| Leader election | Singleton work in a replicated system |
| Consistent hashing | Cache/shard rebalancing with minimal movement |
| Expand/contract migration | Zero-downtime schema change |

## CAP / PACELC in practice

CAP: during a **partition**, choose availability or consistency. PACELC adds: *else*, choose
latency or consistency. Real systems are tunable — quorum reads/writes, read-your-writes,
bounded staleness (Cosmos DB gives you five explicit levels — a great concrete example).

## Practice prompts (work through one per week, write the answer down)

1. Design a globally available URL shortener with a 99.99% availability SLO.
2. Design a metrics/monitoring pipeline ingesting 5M samples/sec.
3. Design a multi-tenant CI/CD system. How do you isolate noisy tenants?
4. Design a rate limiter usable across 500 API pods.
5. Design a distributed job scheduler with at-least-once execution and no thundering herd.
6. Design the deployment system for 2,000 microservices. How do you stop a bad rollout globally?
7. Design log ingestion + search for 10 TB/day at a budget.
8. Design a payment webhook receiver: exactly-once semantics, retries, idempotency, poison messages.
9. Design DR for a stateful service with RPO 5 minutes and RTO 30 minutes.
10. Design a feature-flag service that must never take down its callers (what's its failure mode?).

Write each answer using [design-template.md](design-template.md).

## Troubleshooting interview ("the site is down")

Structure beats brilliance:

1. **Establish impact** — what's broken, for whom, since when, how bad (SLO burn)?
2. **Check recent changes** — deploys, config, feature flags, infra, certs, credentials, DNS, traffic.
3. **Localise** — which layer? Slice metrics by region/AZ/version/tenant/endpoint. Binary-search the path.
4. **Form hypotheses, test cheaply** — say your reasoning aloud, state what evidence would disprove it.
5. **Mitigate first** — rollback, failover, shed load, scale out, disable the feature.
6. **Then root-cause**, then postmortem, then systemic fix.

Explicitly say: *"I'd mitigate before root-causing"* — interviewers listen for it.
