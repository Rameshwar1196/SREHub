# Postmortem: <short incident title>

| | |
|---|---|
| **Incident ID** | INC-YYYY-NNN |
| **Date** | YYYY-MM-DD |
| **Severity** | Sev1 / Sev2 / Sev3 |
| **Duration** | HH:MM (detection → mitigation) |
| **Author(s)** | |
| **Incident Commander** | |
| **Status** | Draft / In review / Complete |

> This document is **blameless**. We assume everyone acted reasonably with the information
> available at the time. We are fixing systems, not people.

## Summary

Two or three sentences a director could read: what broke, who was affected, for how long, and
what fixed it.

## Impact

- **Users affected:** (count / %, which segments, which regions)
- **Functionality affected:**
- **Duration of user impact:**
- **SLO / error budget consumed:** (e.g. 62% of the monthly budget for the checkout SLO)
- **Business impact:** (revenue, SLA credits, support tickets, reputational)

## Detection

- **How we found out:** (alert name / customer report / engineer noticed)
- **Time to detect (MTTD):**
- **Did the alert work as intended?** If a customer told us first, that's an action item.

## Timeline (all times UTC)

| Time | Event |
|---|---|
| 00:00 | Change X deployed to prod |
| 00:14 | Error rate begins climbing |
| 00:21 | Alert `HighErrorBudgetBurn` fires, on-call paged |
| 00:26 | Incident declared Sev2, IC assigned |
| 00:38 | Hypothesis A ruled out |
| 00:47 | Rollback initiated |
| 00:55 | Error rate returns to baseline |
| 01:10 | Incident resolved |

## Root cause(s)

Describe the causal chain, not a single "the engineer made a typo". Use 5 Whys or a causal graph.
Include the **contributing factors**: missing test, insufficient monitoring, unclear runbook,
time pressure, ambiguous ownership.

## Resolution / mitigation

What actually stopped the bleeding, and why that was chosen over alternatives.

## What went well

-

## What went badly

-

## Where we got lucky

- (e.g. "it happened during business hours", "a senior engineer happened to be online")

## Lessons learned

-

## Action items

| # | Action | Type | Owner | Priority | Ticket | Due |
|---|---|---|---|---|---|---|
| 1 | | Prevent | | P1 | | |
| 2 | | Detect | | P1 | | |
| 3 | | Mitigate | | P2 | | |
| 4 | | Process | | P2 | | |

Types: **Prevent** (stop it happening), **Detect** (find it faster), **Mitigate** (reduce impact),
**Process** (how we work).

Every action item must have an owner, a ticket, and a due date. Untracked action items mean this
postmortem was theatre.

## Supporting data

Dashboards, queries, log excerpts, graphs, relevant PRs and config diffs.
