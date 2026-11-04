# 11 — Interview Preparation

## 1. Your positioning (5+ years, Azure/K8s/ADO)

Write a 90-second pitch and rehearse it until it's boring to you:

> "I'm an SRE with 5+ years running production Azure workloads — AKS, Azure DevOps pipelines, and
> Terraform-managed infrastructure. Most recently I [scope: number of clusters/services/teams].
> The work I'm proudest of is [outcome with a number — reduced MTTR by X, cut toil by Y hours/week,
> took availability from A to B]. I'm looking for [what you want] because [genuine reason]."

Lead with **outcomes and numbers**, not tool lists. Quantify everything you can:
- Availability before/after, MTTD/MTTR reduction, incident volume reduction.
- Deploy frequency, lead time, change failure rate.
- Toil hours eliminated, cost saved, alert volume cut.
- Scale: nodes, pods, RPS, data volume, team size, number of services.

## 2. The question bank

- [Technical questions by topic](question-bank-technical.md)
- [Behavioural questions + STAR stories](star-stories-template.md)
- [Questions you should ask them](questions-to-ask.md)

## 3. Behavioural — the stories you must have ready

Prepare **8–10 STAR stories**. Most behavioural questions are reskins of these:

| # | Story | Signals it demonstrates |
|---|---|---|
| 1 | Worst production incident you led | Calm under pressure, IC skills, mitigation instinct |
| 2 | An outage you caused | Ownership, blamelessness applied to yourself |
| 3 | Large toil-elimination / automation project | Engineering mindset, measurable impact |
| 4 | Time you pushed back on a launch / said no | Judgement, data-driven influence |
| 5 | Disagreement with a senior engineer or manager | Conflict handling, ego management |
| 6 | Something you improved without being asked | Initiative, ownership |
| 7 | Mentoring / levelling up a teammate | Force multiplication (critical at senior level) |
| 8 | A project that failed or was cancelled | Honesty, learning |
| 9 | Driving a cross-team change (e.g. SLO adoption) | Influence without authority |
| 10 | Tough on-call period and how you fixed the root cause | Sustainability, systems thinking |

**STAR discipline**: Situation (20s) → Task (10s) → Action (60s, "I" not "we") → Result (20s, with a
number) → Reflection (10s, what you'd do differently). Total ~2 minutes.

## 4. Interview-day tactics

- **Think out loud.** Silence is scored as "didn't know".
- **Clarify before answering.** "When you say the service is down — is it all users or a subset?"
- **State assumptions explicitly** and check them.
- **For troubleshooting**: narrate the *method*, not just the command. "I'd check X because it would
  distinguish between hypothesis A and B."
- **When stuck**: say what you do know, state what you'd look up, and propose how you'd find out.
  "I haven't used that specific feature; here's how I'd approach it and where I'd verify."
- **Never bluff.** Senior interviewers probe. "I don't know" plus a good approach scores higher than
  a confident wrong answer.
- **Mitigate before root-cause** — say it in every troubleshooting scenario.
- **Bring the SRE lens** to every answer: how would you measure it, alert on it, roll it back?

## 5. Common red flags to avoid

- Saying "we" for everything — interviewers can't tell what *you* did.
- Blaming teammates, previous employers, or "bad management".
- Listing tools without outcomes.
- No numbers in any story.
- Jumping to `kubectl delete pod` as a first response to any incident.
- Designing for 100% availability without discussing cost.
- Not asking any questions at the end.

## 6. Two-week final sprint

| Days | Focus |
|---|---|
| 1–2 | Re-read module 01; write your SLO/error-budget story; polish the 90-second pitch |
| 3–4 | Kubernetes: do 20 `kubectl` troubleshooting drills; re-read the symptom→cause table |
| 5–6 | Linux + networking drills (sadservers, tcpdump, dig, curl timing) |
| 7–8 | Two full system design writeups using the template |
| 9–10 | Two scripting exercises under a 30-minute timer, then review |
| 11–12 | Azure/AKS + Azure DevOps: talk through a full CI/CD + incident scenario aloud |
| 13 | Mock interview with a peer (or record yourself); refine STAR stories |
| 14 | Light review, logistics, sleep |

## 7. Offer stage

- Know your number before the call. Research bands (levels.fyi, Glassdoor, local market).
- Ask about: on-call rotation size and compensation, page volume, error-budget policy adherence,
  ratio of project work to toil, team size and level distribution, promotion criteria.
- Get the full comp picture: base, bonus target, equity (grant, vesting, refresh), on-call pay.
- Never accept on the call. "Thanks — can I have until [date] to review?"
