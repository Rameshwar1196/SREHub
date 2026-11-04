# 08 — Observability

**Monitoring** answers known questions ("is CPU high?"). **Observability** lets you ask new
questions about the system without shipping new code. Three pillars + profiling.

## 1. Metrics — Prometheus

### Model
- Time series = metric name + label set. Types: **Counter**, **Gauge**, **Histogram**, **Summary**.
- Pull-based scraping; service discovery (K8s, Azure); `Pushgateway` only for batch jobs.
- **Cardinality is the killer.** Never put user IDs, request IDs, or full URLs in labels.

### PromQL you must know
```promql
# Request rate per second (5m window)
sum(rate(http_requests_total[5m])) by (service)

# Error ratio (an availability SLI)
sum(rate(http_requests_total{status=~"5.."}[5m]))
  / sum(rate(http_requests_total[5m]))

# p99 latency from a histogram
histogram_quantile(0.99,
  sum(rate(http_request_duration_seconds_bucket[5m])) by (le, service))

# Latency SLI: fraction of requests under 300ms
sum(rate(http_request_duration_seconds_bucket{le="0.3"}[5m]))
  / sum(rate(http_request_duration_seconds_count[5m]))

# Error-budget burn rate for a 99.9% SLO
(1 - (sum(rate(http_requests_total{status!~"5.."}[1h]))
    / sum(rate(http_requests_total[1h])))) / (1 - 0.999)

# Multi-window multi-burn-rate page condition
(burn_1h > 14.4 and burn_5m > 14.4) or (burn_6h > 6 and burn_30m > 6)

# Saturation
100 * (1 - avg by(instance)(rate(node_cpu_seconds_total{mode="idle"}[5m])))
node_filesystem_avail_bytes / node_filesystem_size_bytes < 0.10

# Predict disk full within 4 hours
predict_linear(node_filesystem_avail_bytes{mountpoint="/"}[6h], 4*3600) < 0

# Kubernetes
sum(rate(container_cpu_cfs_throttled_periods_total[5m])) by (pod)
  / sum(rate(container_cpu_cfs_periods_total[5m])) by (pod)
increase(kube_pod_container_status_restarts_total[1h]) > 3
kube_deployment_status_replicas_available / kube_deployment_spec_replicas < 1

# Aggregation gotcha: always rate() BEFORE sum(), never sum() before rate()
```

Operators: `rate irate increase sum avg max min count by without offset
@ topk bottomk histogram_quantile absent absent_over_time changes
predict_linear delta deriv clamp label_replace group_left`.

`rate` vs `irate`: `rate` = average over window (use for alerting/graphing);
`irate` = last two samples (use for volatile, high-resolution graphs only).

Recording rules precompute expensive queries. Alerting rules have `for:` duration to debounce.

### Stack
`exporters (node, kube-state-metrics, cAdvisor, blackbox) → Prometheus → Alertmanager → Grafana`
Long-term storage: Thanos / Mimir / Cortex / Azure Monitor managed Prometheus.
Alertmanager: grouping, inhibition, silences, routing trees, receivers (PagerDuty, Teams, email).

## 2. Logs

- **Structured logging (JSON)** with consistent fields: `timestamp, level, service, env, trace_id,
  span_id, user_id(hashed), message`.
- Correlate logs and traces via `trace_id`. This is the single highest-value logging practice.
- Log levels used properly: `ERROR` = needs human attention. If everything is ERROR, nothing is.
- Sampling for high-volume debug logs; never sample errors.
- Retention tiering: hot searchable (14–30d) → archive (1y+). Logging cost is a real SRE problem.
- Stacks: Loki (label-based, cheap), Elastic/OpenSearch, Azure Log Analytics, Splunk.

```logql
{app="web"} |= "error" | json | status >= 500
sum(rate({app="web"} | json | status >= 500 [5m])) by (pod)
{app="web"} | json | line_format "{{.trace_id}} {{.message}}"
```

## 3. Traces

- **OpenTelemetry** is the standard: API + SDK + Collector. Vendor-neutral; instrument once.
- Trace → spans → parent/child; context propagation via W3C `traceparent` header.
- Head-based vs tail-based sampling (tail lets you keep all error/slow traces).
- Use traces to answer "where did the 2 seconds go" across services.
- Backends: Tempo, Jaeger, Azure Application Insights.

## 4. Dashboards that are actually useful

- One **service overview** per service: the four golden signals + SLO burn + deploy annotations.
- USE dashboard for infrastructure, RED dashboard for services.
- Put the SLO and the error budget remaining at the top. Everything else is secondary.
- Annotate deployments on every graph — most incidents correlate with a deploy.
- Avoid "wall of 60 graphs" — nobody reads them during an incident.

## 5. Alerting philosophy

- **Alert on symptoms, page on user impact.** Cause-based alerts become tickets.
- Every page must be: urgent, actionable, and require human judgement.
- Include in every alert: what broke, user impact, dashboard link, runbook link, recent changes.
- Review page volume monthly. Delete or downgrade anything that never resulted in action.
- Have a tested **paging escalation** path and a deadman's switch (alert if monitoring stops reporting).

```yaml
groups:
  - name: slo
    rules:
      - record: job:sli_error_ratio:rate5m
        expr: |
          sum(rate(http_requests_total{status=~"5.."}[5m])) by (job)
            / sum(rate(http_requests_total[5m])) by (job)

      - alert: ErrorBudgetBurnFast
        expr: |
          (job:sli_error_ratio:rate1h > 14.4 * 0.001)
            and (job:sli_error_ratio:rate5m > 14.4 * 0.001)
        for: 2m
        labels: { severity: page }
        annotations:
          summary: "{{ $labels.job }} burning error budget 14.4x"
          description: "2% of the 28-day budget will be gone within an hour."
          runbook_url: "https://runbooks/…/error-budget-burn"
          dashboard: "https://grafana/…/service-overview?var-job={{ $labels.job }}"
```

## 6. Interview questions

1. Design observability for a new microservice from scratch.
2. Difference between a histogram and a summary in Prometheus, and why it matters for aggregation.
   (Summaries compute quantiles client-side and **cannot be aggregated** across instances; histograms can.)
3. Your Prometheus is OOMing. What do you do? (Cardinality audit via `topk(10, count by(__name__)({__name__=~".+"}))`,
   drop labels in relabel configs, reduce retention, shard/federate, move to Mimir/Thanos.)
4. How do you alert on something that happens rarely but matters a lot?
5. How do you measure the SLI of an async pipeline? (Freshness/latency of processing, queue lag, correctness.)
6. p99 latency jumped but p50 is flat. What does that suggest? (Tail effects: GC pauses, cold caches,
   a single bad shard/node, retries, lock contention, noisy neighbour.)
7. How would you correlate a spike in errors to a specific deployment?
