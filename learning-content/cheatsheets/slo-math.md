# Availability & error budget quick reference

## Downtime allowed per SLO

| SLO | Per day | Per week | Per 30 days | Per quarter | Per year |
|---|---|---|---|---|---|
| 90%     | 2h 24m | 16h 48m | 3d | 9d | 36.5d |
| 95%     | 1h 12m | 8h 24m | 1d 12h | 4d 12h | 18.25d |
| 99%     | 14m 24s | 1h 40m 48s | 7h 18m | 21h 54m | 3d 15h 36m |
| 99.5%   | 7m 12s | 50m 24s | 3h 39m | 10h 57m | 1d 19h 48m |
| 99.9%   | 1m 26s | 10m 4s | 43m 49s | 2h 11m | 8h 45m 57s |
| 99.95%  | 43.2s | 5m 2s | 21m 54s | 1h 5m | 4h 22m 58s |
| 99.99%  | 8.6s | 1m 0.5s | 4m 22s | 13m 8s | 52m 35s |
| 99.999% | 0.86s | 6.05s | 26.3s | 1m 18s | 5m 15s |

## Composite availability

Serial dependencies multiply:
$$A_{total} = \prod_{i=1}^{n} A_i$$

Five services at 99.9% in series → $0.999^5 = 99.5\%$ (≈ 3.6 hours/month, not 43 minutes).

Redundant components in parallel:
$$A_{total} = 1 - \prod_{i=1}^{n}(1 - A_i)$$

Two independent 99% components in parallel → $1 - 0.01^2 = 99.99\%$.
**Caveat:** only if failures are truly independent. Shared control plane, shared config, shared
dependency, or correlated load kills the assumption — this is the answer interviewers want.

## Error budget

$$\text{Error budget} = 1 - \text{SLO}$$
$$\text{Burn rate} = \frac{\text{observed error rate}}{1 - \text{SLO}}$$

A burn rate of 1 consumes the budget exactly over the SLO window. A burn rate of 14.4 consumes
2% of a 28-day budget in 1 hour.

$$\text{Time to exhaustion} = \frac{\text{budget remaining}}{\text{burn rate}} \times \text{window}$$

## Multi-window multi-burn-rate alerts (28-day window)

| Severity | Burn rate | Long window | Short window | Budget consumed before firing |
|---|---|---|---|---|
| Page | 14.4 | 1h | 5m | 2% |
| Page | 6 | 6h | 30m | 5% |
| Ticket | 3 | 1d | 2h | 10% |
| Ticket | 1 | 3d | 6h | 10% |

Both windows must breach. The long window gives confidence, the short window gives fast reset.

## MTT* definitions

| Term | Meaning | Who moves it |
|---|---|---|
| MTBF | Mean time between failures | Engineering quality, change safety |
| MTTD | Mean time to detect | Monitoring & alerting — SRE's biggest lever |
| MTTA | Mean time to acknowledge | Paging config, on-call health |
| MTTR | Mean time to repair/resolve | Runbooks, rollback speed, automation |

$$\text{Availability} = \frac{MTBF}{MTBF + MTTR}$$

Halving MTTR improves availability as much as doubling MTBF — and is usually far cheaper.

## DORA metrics (elite performer benchmarks)

| Metric | Elite |
|---|---|
| Deployment frequency | On demand (multiple per day) |
| Lead time for changes | < 1 hour |
| Change failure rate | 0–15% |
| Time to restore service | < 1 hour |

## Cost of nines (rule of thumb)

Each additional nine roughly **10x** the engineering cost. Always ask: can the user perceive the
difference, and will they pay for it? A 99.99% SLO on a service whose clients retry for 30 seconds
is usually waste.
