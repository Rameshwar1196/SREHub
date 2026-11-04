# 01 — SRE Fundamentals

> This is the module interviewers use to separate "DevOps engineer" from "SRE".
> Know these definitions precisely and have real examples for each.

## 1. What SRE actually is

SRE is the practice of applying software engineering to operations problems, with the
goal of creating **scalable and reliable** systems. Core tenets:

- Reliability is the most important feature of any system.
- 100% is the wrong target — cost grows exponentially, users can't perceive the difference.
- Operations work (toil) is capped so engineers have time for engineering.
- Blameless culture: systems fail, not people.

**SRE vs DevOps vs Platform Engineering**

| | DevOps | SRE | Platform Eng |
|---|---|---|---|
| Nature | Culture/philosophy | Prescriptive implementation of DevOps | Product discipline |
| Output | Faster delivery | Measured reliability | Internal developer platform |
| Key artifact | CI/CD pipeline | SLO + error budget | Golden paths, self-service |

Good one-liner: *"class SRE implements DevOps."*

## 2. SLI / SLO / SLA / Error budget

- **SLI** — Service Level *Indicator*. A quantitative measure of service behaviour.
  `good events / valid events`. Example: `proportion of HTTP requests with status < 500 and latency < 300ms`.
- **SLO** — Service Level *Objective*. Target for an SLI over a window.
  Example: `99.9% of requests succeed within 300ms over a rolling 28 days`.
- **SLA** — Service Level *Agreement*. Contractual, with financial penalties. Always looser than the SLO.
- **Error budget** — `1 - SLO`. For 99.9% over 28 days ≈ **40m 19s** of allowed unavailability.

### Availability cheat table (per period of allowed downtime)

| SLO | Per day | Per week | Per 30 days | Per year |
|-----|---------|----------|-------------|----------|
| 99%     | 14m 24s | 1h 40m | 7h 18m | 3d 15h |
| 99.9%   | 1m 26s  | 10m 4s | 43m 49s | 8h 45m |
| 99.95%  | 43s     | 5m 2s  | 21m 54s | 4h 22m |
| 99.99%  | 8.6s    | 1m 0s  | 4m 22s  | 52m 35s |
| 99.999% | 0.86s   | 6s     | 26s     | 5m 15s |

### Error budget policy (what interviewers want to hear)

> When the error budget is exhausted: feature releases freeze, and all engineering effort
> goes to reliability work until the budget recovers. When the budget is healthy and
> untouched, we are being *too* conservative — ship faster / reduce cost.

### Burn rate alerting (multi-window, multi-burn-rate)

Burn rate = how fast you consume error budget relative to uniform consumption.

| Severity | Burn rate | Long window | Short window | Budget consumed |
|---|---|---|---|---|
| Page  | 14.4x | 1h  | 5m  | 2% in 1h |
| Page  | 6x    | 6h  | 30m | 5% in 6h |
| Ticket| 3x    | 1d  | 2h  | 10% in 1d |
| Ticket| 1x    | 3d  | 6h  | 10% in 3d |

Alert fires only when **both** windows breach — kills flapping and stale alerts.

### The Four Golden Signals
1. **Latency** — distinguish latency of successful vs failed requests.
2. **Traffic** — demand on the system (RPS, tx/sec, concurrent sessions).
3. **Errors** — explicit (500), implicit (200 with wrong body), policy (latency > SLO).
4. **Saturation** — how "full" the service is; the constrained resource. Usually the leading indicator.

Related: **RED** (Rate, Errors, Duration — services) and **USE** (Utilization, Saturation, Errors — resources).

## 3. Toil

Toil is work that is **manual, repetitive, automatable, tactical, devoid of enduring value,
and scales linearly with service growth**.

- Target: **< 50%** of an SRE's time on toil.
- Measure it (ticket counts, time tracking), then attack the top contributors.
- Not toil: design work, root-cause analysis that produces fixes, writing automation.

## 4. Incident management

### Roles (ICS model)
- **Incident Commander (IC)** — owns the incident, delegates, does not debug.
- **Operations/Ops Lead** — the only one making changes to the system.
- **Communications Lead** — status page, stakeholders, customers.
- **Planning/Scribe** — timeline, handoffs, follow-ups.

### Lifecycle
`Detect → Triage → Declare & assign roles → Mitigate → Resolve → Postmortem → Follow-up`

**Mitigate before you root-cause.** Rollback, failover, feature-flag off, scale up, drain a region.

### Severity matrix (example)

| Sev | Definition | Response |
|---|---|---|
| Sev1 | Full outage / data loss / security breach | Page immediately, IC, exec comms, status page |
| Sev2 | Major degradation, SLO burning fast | Page, IC assigned |
| Sev3 | Minor degradation, workaround exists | Ticket, business hours |
| Sev4 | Cosmetic / internal only | Backlog |

### Postmortems
- **Blameless** — focus on systemic contributing factors, not individuals.
- Must contain: summary, impact (users + $ + SLO burn), detection, timeline, root cause(s),
  what went well, what went badly, where we got lucky, action items with owners and due dates.
- Action items must be **tracked in the backlog** with a priority, or the postmortem is theatre.
- Techniques: 5 Whys, Causal Chain, "how did we detect" and "how could we have detected sooner".

**MTTD / MTTA / MTTR / MTBF** — know the difference and which ones SRE can actually move (MTTD and MTTR).

## 5. On-call

- Sustainable: max ~2 incidents per shift, 25% of time on-call, compensated.
- Every page must be **actionable, urgent, and novel**. Otherwise delete it or turn it into a ticket.
- Runbooks linked directly from the alert.
- Handoff notes at shift change.
- Track **page load** as a health metric of the service.

## 6. Release engineering & reliability patterns

- **Progressive delivery**: canary → percentage rollout → full. Automated rollback on SLO breach.
- **Blue/Green** vs **Rolling** vs **Canary** vs **Shadow/Dark launch** — know trade-offs.
- **Feature flags** decouple deploy from release.
- **Graceful degradation**, **load shedding**, **circuit breakers**, **bulkheads**, **backpressure**.
- **Retries**: exponential backoff + **jitter** + retry budget. Naive retries cause metastable failures.
- **Idempotency** for safe retries.
- **Cascading failure** and **thundering herd** — causes and mitigations.

## 7. Capacity & change management

- Organic vs inorganic growth; forecast from traffic trends plus known launches.
- Headroom target (e.g., N+2 region redundancy, 40% CPU steady state).
- ~70% of outages are caused by **change**. Hence: gradual rollouts, fast detection, fast rollback.

## 8. Chaos / resilience engineering

Hypothesis → blast radius control → run experiment in prod (or prod-like) → learn → automate.
Examples: instance kill, AZ isolation, latency injection, dependency failure, disk fill.
Tools: Chaos Mesh, LitmusChaos, Azure Chaos Studio, Gremlin.

## 9. Disaster recovery

- **RPO** — max acceptable data loss. **RTO** — max acceptable downtime.
- Strategies by cost: Backup & restore → Pilot light → Warm standby → Active/Active.
- A backup you have not restored is not a backup. Test restores on a schedule.

---

## Interview questions with model answers

**Q: How would you define an SLO for a new service?**
Start with user journeys (critical paths), not infrastructure. Pick 2–4 SLIs per journey
(availability, latency, freshness, correctness). Measure current performance for 4 weeks to
get a baseline, set the SLO slightly below achievable current performance, agree with product
on the error budget policy, then iterate quarterly. Measure as close to the user as possible
(load balancer or client-side, not the app's own logs).

**Q: Your error budget is exhausted in week 1 of the month. What do you do?**
Freeze risky changes per the error-budget policy (reliability fixes and security patches still
ship). Identify the top budget consumers from the SLO breakdown. Run/refresh postmortems.
Reprioritise the sprint to reliability work. Re-evaluate whether the SLO is realistic — if the
budget burns every month, either the SLO is wrong or there's systemic debt; escalate with data.

**Q: How do you reduce alert fatigue?**
Alert on symptoms (SLO burn) not causes. Delete alerts that never led to action — audit page
history quarterly. Use multi-window multi-burn-rate alerts. Route non-urgent signals to tickets
and dashboards. Require every page to have a runbook. Track pages-per-shift as an SLO for the
on-call experience.

**Q: Difference between reliability and availability?**
Availability is the fraction of time the service is usable. Reliability is broader: it also
includes correctness, durability, latency, and freshness. A service can be 100% "up" and totally
unreliable if it returns wrong or stale data.

**Q: Walk me through your worst incident.**
Use STAR. Emphasise: how it was detected, how you mitigated *before* root-causing, how you
communicated, and — most importantly — the systemic fixes and whether the class of failure
recurred. See [11-interview-prep/star-stories-template.md](../11-interview-prep/star-stories-template.md).

## Reading
- *Site Reliability Engineering* (Google, free online) — ch. 1–6, 11–15.
- *The SRE Workbook* — SLO chapters are the most interview-relevant.
- *Implementing Service Level Objectives* — Alex Hidalgo.
- *Seeking SRE* — breadth of industry practice.
