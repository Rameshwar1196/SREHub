# 8-Week Study Plan

Designed for someone with 5+ years of experience: this is **refresh and sharpen**, not learn from
scratch. Roughly 10–12 hours per week. Adjust the order to match the job descriptions you're targeting.

**Weekly rhythm**
- Mon–Thu: 1–1.5h focused study on the week's module.
- Fri: 1h hands-on lab.
- Sat: 2h — one system design writeup **or** two coding exercises, timed.
- Sun: 30 min — review the question bank, say answers out loud, update STAR stories.

---

## Week 1 — SRE fundamentals + your narrative
- Read [01-sre-fundamentals](../01-sre-fundamentals/README.md) fully. Memorise the availability table.
- Compute the error budget for your current service.
- Write your 90-second pitch and fill in the numbers cheat sheet in the STAR template.
- Draft STAR stories 1, 2 and 3.
- **Checkpoint:** you can explain SLI/SLO/error budget/burn rate without notes.

## Week 2 — Linux + networking
- Read modules [02](../02-linux/README.md) and [03](../03-networking/README.md).
- Do 5 sadservers.com scenarios, timed.
- Practise: `tcpdump` capture, `dig +trace`, `curl -w` timing breakdown, `ss` interpretation.
- **Checkpoint:** 60-second triage from memory; explain load vs CPU; explain 502/503/504.

## Week 3 — Kubernetes core
- Read [04-kubernetes](../04-kubernetes/README.md) and the [cheatsheet](../04-kubernetes/cheatsheet.md).
- Lab 1: local cluster, break and fix six ways.
- Write the reference manifests from memory, then diff against the repo copy.
- **Checkpoint:** answer the full `Pending` / `CrashLoopBackOff` diagnostic trees cold.

## Week 4 — Kubernetes operations + Azure/AKS
- Scaling, upgrades, PDBs, autoscalers, graceful shutdown.
- Read [05-azure](../05-azure/README.md); focus on AKS, networking, identity.
- Lab 3: Terraform-provisioned AKS with workload identity.
- **Checkpoint:** debug "pod can't reach private SQL" end to end, out loud.

## Week 5 — CI/CD, Azure DevOps, IaC
- Read [06-azure-devops](../06-azure-devops/README.md) and [07-iac](../07-iac/README.md).
- Lab 4: full multi-stage pipeline with canary and rollback.
- Write one pipeline YAML from scratch without copying.
- **Checkpoint:** design a pipeline on a whiteboard and name every gate and why it exists.

## Week 6 — Observability
- Read [08-observability](../08-observability/README.md).
- Lab 2: SLO dashboards + burn-rate alerts in Prometheus/Grafana.
- Write 10 PromQL queries from memory; 5 KQL queries from memory.
- **Checkpoint:** explain histogram vs summary and write a burn-rate query unaided.

## Week 7 — System design + coding
- Read [10-system-design](../10-system-design/README.md).
- Three full design writeups using the template (pick from the prompt list).
- Four coding exercises from [09-scripting](../09-scripting/README.md), 30 minutes each.
- **Checkpoint:** you naturally reach the reliability checklist in the last 10 minutes of a design.

## Week 8 — Interview simulation
- Finish all 10 STAR stories.
- Two mock interviews (peer, or record yourself and watch it back — uncomfortable but effective).
- Full pass over [question-bank-technical.md](../11-interview-prep/question-bank-technical.md); mark ✅.
- Lab 6: run an incident simulation and write the postmortem.
- Prepare [questions-to-ask.md](../11-interview-prep/questions-to-ask.md) per company.
- **Checkpoint:** every ❌ in the question bank has a plan.

---

## If you only have 2 weeks

Do: module 01 (all), the Kubernetes symptom→cause table, the Linux 60-second triage,
two system design writeups, and all 10 STAR stories. Skip the labs except Lab 1 and Lab 6.

## Certification track (parallel, optional)

| Cert | Effort | Value for SRE |
|---|---|---|
| CKA | 4–6 weeks | High — practical, respected |
| AZ-400 | 4 weeks | High for Azure/ADO roles |
| AZ-104 | 3 weeks | Medium — good foundation |
| Terraform Associate | 1–2 weeks | Medium — quick win |
| CKS | 4 weeks | Differentiator |
