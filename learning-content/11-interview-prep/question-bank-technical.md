# Technical question bank

Answer each one out loud, timed at 2–3 minutes. Mark ✅ only when you can answer without notes.

## SRE practice

- [ ] Define SLI, SLO, SLA, error budget. Give an example of each for an API.
- [ ] How do you choose SLIs for a service nobody has instrumented?
- [ ] What's the error-budget policy at your current job, and was it actually enforced?
- [ ] What is toil? Give a concrete example you eliminated and the hours saved.
- [ ] Walk me through your incident response process end to end.
- [ ] What makes a good postmortem? What makes a useless one?
- [ ] How do you decide whether an alert should page or file a ticket?
- [ ] Explain multi-window multi-burn-rate alerting and why it's better than a static threshold.
- [ ] What are the four golden signals? RED? USE?
- [ ] How do you make on-call sustainable?
- [ ] What's your approach to capacity planning?
- [ ] How do you run a chaos experiment safely in production?

## Linux

- [ ] 60-second triage on a slow box — which commands, in what order, and why?
- [ ] Load average 40 on 4 cores, CPU 20% idle. Explain.
- [ ] `df` full, `du` not. Why?
- [ ] Explain the OOM killer and `oom_score_adj`.
- [ ] Process stuck in `D` state — what does that mean and how do you investigate?
- [ ] What is TIME_WAIT and when does it become a problem?
- [ ] Hard link vs symlink. Inode exhaustion.
- [ ] How do containers isolate processes? Namespaces vs cgroups vs capabilities.
- [ ] How do you find which process is holding a port / a deleted file?
- [ ] Explain `strace` vs `perf` vs `eBPF` tooling and when to use each.

## Networking

- [ ] Full path of `https://example.com` from keypress to rendered page.
- [ ] TCP handshake, teardown, and where retransmits show up in metrics.
- [ ] 502 vs 503 vs 504 — how do you tell them apart in practice?
- [ ] How does DNS failover work and why is TTL your enemy?
- [ ] Explain SNAT port exhaustion and three fixes.
- [ ] L4 vs L7 load balancing — what do you lose and gain?
- [ ] How do you debug intermittent packet loss?
- [ ] TLS handshake steps; what breaks when a cert expires mid-chain?
- [ ] Subnet a /16 into /20s and tell me the usable hosts in a /26.
- [ ] What's an MTU black hole and how would you spot it?

## Kubernetes

- [ ] What happens from `kubectl apply` to a running pod?
- [ ] Pod stuck in `Pending` — full diagnostic tree.
- [ ] `CrashLoopBackOff` — how do you debug when logs are empty?
- [ ] Liveness vs readiness vs startup probes; give an outage caused by getting this wrong.
- [ ] QoS classes and eviction order.
- [ ] Why might a Service have no endpoints?
- [ ] Explain graceful shutdown and the endpoint-propagation race. How do you get zero-downtime deploys?
- [ ] Deployment vs StatefulSet vs DaemonSet — decision criteria.
- [ ] How does NetworkPolicy work, and what's the default before any policy exists?
- [ ] How does the Cluster Autoscaler decide to scale down, and what blocks it?
- [ ] CPU limits: should you set them? Argue both sides.
- [ ] How do you upgrade a production cluster with no downtime?
- [ ] What happens if etcd loses quorum?
- [ ] How do you handle secrets in Kubernetes properly?
- [ ] HPA vs VPA vs KEDA vs Cluster Autoscaler — how do they interact?

## Azure

- [ ] Managed identity vs service principal vs workload identity federation.
- [ ] Private Endpoint vs Service Endpoint; what's the #1 misconfiguration?
- [ ] Design a multi-region active/active app. Where does the data live?
- [ ] AKS pod can't reach private Azure SQL — debug it step by step.
- [ ] How do you enforce a security standard across 200 subscriptions?
- [ ] Front Door vs Traffic Manager vs Application Gateway vs Load Balancer.
- [ ] Explain Azure availability zones vs region pairs vs paired-region failover.
- [ ] Your Log Analytics cost tripled. Investigate and fix.
- [ ] How do you compute the composite SLA of a 5-service chain?
- [ ] How would you bootstrap a brand-new subscription to production-ready?

## CI/CD & Azure DevOps

- [ ] Design a pipeline for a microservice to AKS with dev/stage/prod. Where are the gates?
- [ ] How do you guarantee the artifact tested in stage is the one deployed to prod?
- [ ] How do you handle secrets in pipelines without storing them?
- [ ] What's your rollback strategy and what's your realistic MTTR?
- [ ] Explain the DORA metrics; how would you improve change failure rate?
- [ ] Self-hosted vs Microsoft-hosted agents.
- [ ] Your pipeline takes 45 minutes. Get it to 10.
- [ ] How do you enforce a mandatory security scan across every pipeline in the org?
- [ ] Trunk-based vs GitFlow — which and why, for an SRE-owned repo?

## IaC

- [ ] How do you structure Terraform state for many services and environments?
- [ ] Someone changed something in the portal. Detect and remediate.
- [ ] Refactor a module without destroying resources.
- [ ] Plan shows an unexpected database destroy. What now?
- [ ] Terraform vs Bicep for an Azure-only shop.
- [ ] Explain GitOps and what it gives you over push-based CD.

## Observability

- [ ] Histogram vs summary in Prometheus — and why it matters for aggregation.
- [ ] Write a PromQL query for p99 latency and for an error-budget burn rate.
- [ ] Why must you `rate()` before `sum()`?
- [ ] Prometheus is OOMing. Fix it.
- [ ] p99 spiked, p50 flat. Hypotheses?
- [ ] How do you correlate a log line to a trace to a deploy?
- [ ] Design observability for a greenfield service.
- [ ] How do you reduce logging cost without losing debuggability?

## Coding

- [ ] Parse a log file → per-endpoint error rate and p95.
- [ ] Implement retry with exponential backoff and jitter.
- [ ] Implement a token bucket rate limiter.
- [ ] Reconcile desired vs actual state, idempotently.
- [ ] Merge overlapping intervals.
- [ ] Paginate a REST API, handle 429 with `Retry-After`.
- [ ] Detect cycles in a service dependency graph.
