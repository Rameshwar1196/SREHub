# 12 — Hands-on Labs

Reading does not survive an interview. Build these. Each lab ends with a **talking point** you can
use as an interview answer.

Keep costs low: use free tiers, `kind`/`minikube` locally, and **always destroy** Azure resources
when you're done (`terraform destroy` / `az group delete`).

---

## Lab 1 — Local Kubernetes playground
**Goal:** `kubectl` fluency and failure simulation.

```bash
kind create cluster --name lab --config kind-config.yaml
```
Tasks:
- Deploy a 3-replica app with the manifests from [../04-kubernetes/manifests/reference.yaml](../04-kubernetes/manifests/reference.yaml).
- Break it deliberately, one at a time, and diagnose each without looking at your notes:
  wrong image tag, missing ConfigMap key, memory limit 10Mi, liveness probe on a dead path,
  Service selector typo, NetworkPolicy that blocks DNS.
- Do a rolling update with `maxUnavailable: 0` while running `hey`/`k6` — prove zero dropped requests.
- Now remove the `preStop` hook and prove you *can* drop requests. Explain why.

**Talking point:** the endpoint-propagation race and how you achieve true zero-downtime deploys.

---

## Lab 2 — SLO + error budget with Prometheus and Grafana
**Goal:** turn theory into queries.

- Deploy kube-prometheus-stack via Helm.
- Instrument (or deploy) an app exposing `http_requests_total` and a latency histogram.
- Write recording rules for an availability SLI and a latency SLI.
- Build a Grafana dashboard: golden signals + error budget remaining + burn rate.
- Implement multi-window multi-burn-rate alerts in Alertmanager.
- Inject failures (`kubectl scale`, a fault-injection sidecar) and watch the budget burn.

**Talking point:** "here's the exact PromQL I use for burn-rate alerting, and why two windows."

---

## Lab 3 — Terraform-provisioned AKS
**Goal:** production-shaped infrastructure as code.

- Remote state in Azure Storage with locking.
- Modules: network (hub/spoke), AKS (zonal, autoscaling, workload identity, CNI overlay), ACR, Key Vault, Log Analytics.
- Workload Identity: a pod reads a secret from Key Vault with **no** secret in the cluster.
- NAT Gateway for deterministic egress.
- Then: change something in the portal, run `plan`, and show the drift.

**Talking point:** state design, blast radius, and how you detect drift.

---

## Lab 4 — Full Azure DevOps CI/CD
**Goal:** the pipeline story end to end.

- Build → test → coverage gate → Trivy scan → push to ACR by digest.
- Multi-stage deploy: dev (auto) → stage (smoke test) → prod (approval + canary + SLO gate).
- Environments with approvals and branch-control checks.
- A reusable `extends` template that every pipeline must use.
- Break it: make the smoke test fail and show the automatic rollback.

Starting point: [../06-azure-devops/pipelines/](../06-azure-devops/pipelines/).

**Talking point:** build-once-promote-everywhere and automated rollback on SLO breach.

---

## Lab 5 — GitOps with Argo CD
**Goal:** pull-based delivery.

- Install Argo CD, app-of-apps, Kustomize overlays for dev/stage/prod.
- Manually `kubectl edit` a Deployment and watch Argo revert it.
- Do a rollback by `git revert` and time it.

**Talking point:** why GitOps reduces MTTR and removes cluster credentials from CI.

---

## Lab 6 — Incident simulation + postmortem
**Goal:** the skill interviews actually test.

- Have a friend (or a script) inject a fault: CPU limit reduced, DNS broken, dependency latency,
  a certificate expired, disk filled.
- Run it as a real incident: declare, assign IC, mitigate, resolve — with a timer.
- Write a full blameless postmortem using [postmortem-template.md](postmortem-template.md).

**Talking point:** you have a written postmortem you can walk an interviewer through.

---

## Lab 7 — Chaos engineering
**Goal:** prove resilience claims.

- Install Chaos Mesh. Run: pod kill, network latency injection, DNS chaos, node drain.
- Define a hypothesis and a steady-state metric *before* each experiment.
- Find one thing that breaks that you expected to survive. Fix it.

**Talking point:** a specific hidden failure mode you found and eliminated.

---

## Lab 8 — Observability with OpenTelemetry
**Goal:** correlate the three pillars.

- Two services calling each other; instrument both with OTel SDK → Collector → Tempo/Jaeger + Loki + Prometheus.
- Propagate `trace_id` into structured logs.
- Inject 500ms of artificial latency in the downstream service; find it from a trace in under a minute.

**Talking point:** "here's how I cut a latency investigation from hours to minutes."

---

## Lab 9 — Load and capacity test
**Goal:** know your limits before production does.

- Use `k6` to ramp traffic until the SLO breaks. Record the knee point.
- Identify the saturating resource (CPU? connections? DB? thread pool?).
- Add an HPA; re-run; find the new limit and the scale-up lag.
- Implement load shedding and re-run — show graceful degradation instead of collapse.

**Talking point:** a real capacity number and the bottleneck you found.

---

## Lab 10 — Automation portfolio
**Goal:** something to show.

Build a small CLI (Python or PowerShell) that does something genuinely useful, e.g.:
- Audits an AKS cluster for pods without resource limits, PDBs, or probes → CSV report.
- Finds unattached disks / orphaned public IPs across subscriptions via Resource Graph.
- Computes SLO compliance from Log Analytics and posts it to Teams weekly.

Include tests, `--dry-run`, a README, and a pipeline that lints and tests it.

**Talking point:** a public repo link on your CV.
