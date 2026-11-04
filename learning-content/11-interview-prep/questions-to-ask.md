# Questions to ask them

Pick 3–4 per round, matched to the interviewer. Good questions are also a signal — they show
seniority. Listen hard to the answers; you are evaluating them too.

## Reliability practice (asks: is this a real SRE org?)

- Do you have SLOs in production today? Who owns them — SRE or the product team?
- Is there an error-budget policy, and has it ever actually stopped a launch?
- What percentage of SRE time goes to project work vs operations?
- Do developers carry pagers for their own services?
- Can you describe your last significant incident and what changed as a result?
- Are postmortem action items tracked and completed, or do they decay in a backlog?

## On-call (asks: will this ruin my life?)

- How many people are in the rotation, and how long is a shift?
- Roughly how many pages does an on-call engineer get per shift? Overnight?
- Is on-call compensated? Is there time-off-in-lieu after a rough night?
- What's the escalation path at 3am if I'm stuck?
- Do runbooks exist for the top alerts, and are they current?

## The role & team

- What does success in this role look like at 3, 6 and 12 months?
- Is this role embedded with a product team, or a central platform/SRE team?
- What's the biggest reliability problem the team has right now?
- How is work prioritised between reliability, features, and cost?
- What's the team's level distribution, and who would I learn from?
- Why is this role open — growth or backfill?

## Engineering practice

- What does your deployment pipeline look like, and how long from merge to production?
- How often do you deploy, and what's your change failure rate?
- How do you roll back, and when was the last time you did?
- How much of the infrastructure is in code? Is there drift?
- What's the state of your observability stack? What's missing?
- How much legacy/toil is there that nobody wants to touch?

## Culture

- How does the team handle a mistake that causes an outage?
- How are technical decisions made and documented? Do you use ADRs/RFCs?
- What's the remote/on-site expectation, and how does that work for on-call?
- What's something the team is currently unhappy with?
- How do you measure the impact of SRE work?

## For the hiring manager

- What's your roadmap for the platform over the next year?
- What would make you say in 6 months that hiring me was a great decision?
- How do you protect the team's time for engineering work?
- What's the promotion path and criteria for this level?
- What keeps you up at night about this system?

## Red-flag answers to listen for

- "We don't really have SLOs, we just watch dashboards."
- "On-call is pretty quiet" — followed by no data.
- "Postmortems? We move fast, we don't dwell."
- "Everyone is on-call all the time."
- "We deploy on Fridays at 6pm, it's fine."
- Interviewer can't name a single thing the team wants to improve.
- Vague or evasive answers about why the role is open.
