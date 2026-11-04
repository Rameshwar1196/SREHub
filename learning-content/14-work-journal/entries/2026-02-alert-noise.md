# Cut on-call alert noise by 70%

> Example entry — replace or delete once you have written your own.

- **Date:** 2026-01 — 2026-02
- **Role:** led
- **Theme:** Observability, toil reduction
- **Tech:** Prometheus, Alertmanager, Grafana, Azure Monitor, PagerDuty
- **Evidence:** `files/2026-02-alert-noise/`

## Situation

The on-call rotation was taking 41 pages per week across 9 services. Roughly 70% were auto-resolving
within five minutes or had no runbook. Two engineers had asked to leave the rotation, and the team was
routinely acknowledging pages without reading them.

## Task

I owned the alerting cleanup for the platform services and the definition of what we were allowed to
page on.

## Action

- Pulled 90 days of page history and classified every alert by *did a human take action*. 62% had no
  action in any instance.
- Rejected the obvious option of just raising thresholds — that hides regressions rather than fixing
  signal quality. Chose symptom-based SLO burn-rate alerts instead.
- Defined SLOs for the four user-facing journeys, then replaced 34 cause-based rules with multi-window
  multi-burn-rate alerts (fast 1h/5m burn → page, slow 6h/30m burn → ticket).
- Made a runbook link a required field; any rule without one was demoted to a ticket.
- Rolled out per-service over three weeks, keeping the old rules firing to a silent channel so we could
  compare and prove nothing was missed.

## Result

| Metric | Before | After |
|---|---|---|
| Pages per week | 41 | 12 |
| Pages with no human action | 62% | 9% |
| Alert rules | 58 | 24 |
| MTTA | 11 min | 4 min |
| Missed incidents during rollout | — | 0 |

Night pages dropped from 9 to 2 per week. One real incident surfaced *later* than before, because the
burn-rate window traded a few minutes of detection latency for far fewer false positives — an accepted
trade recorded in the SLO doc.

## What I'd do differently

I ran the comparison period for three weeks per service, which stretched the project to six weeks. Two
weeks would have given enough confidence. I also should have agreed the "no runbook, no page" rule with
the other team leads *before* demoting their alerts — that caused one avoidable escalation.

## Follow-up / still open

- Error-budget policy is written but not yet enforced in the release pipeline.
- Three legacy services still alert on CPU; they're scheduled for the next quarter.

## Interview questions

**Q: Tell me about a time you reduced toil for your team.**
Our on-call was taking 41 pages a week and two people had asked off the rotation. I owned the alerting
cleanup. I started from data rather than opinion: 90 days of page history classified by whether a human
actually did anything — 62% hadn't. Rather than raising thresholds, which just hides regressions, I
defined SLOs for the four user journeys and replaced 34 cause-based rules with multi-window burn-rate
alerts, and made a runbook link mandatory for anything allowed to page. I rolled it out per service
with the old rules still firing silently so I could prove nothing was being missed. Pages went from 41
to 12 a week, no-action pages from 62% to 9%, and MTTA from 11 to 4 minutes, with zero missed incidents
during the migration. The lesson was that I should have socialised the "no runbook, no page" rule with
the other leads before applying it — the technical part was easy, the agreement part wasn't.
