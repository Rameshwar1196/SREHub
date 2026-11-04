# 05 — Azure for SRE

## 1. Foundations

- Hierarchy: **Management Group → Subscription → Resource Group → Resource**.
- **Azure Resource Manager (ARM)** is the control plane. Everything is a REST call to ARM.
- **Regions, Availability Zones, region pairs.** Zone-redundant vs zonal vs regional services.
- **Entra ID (formerly Azure AD)**: users, groups, service principals, **managed identities**
  (system-assigned vs user-assigned), app registrations, **workload identity federation** (no secrets).
- **RBAC**: role definition (actions/notActions/dataActions) + scope + principal = role assignment.
  Deny assignments come from Blueprints/Managed Apps. Least privilege; prefer built-in roles.
- **Azure Policy**: audit/deny/deployIfNotExists. Guardrails at scale (e.g. "deny public IPs on VMs",
  "require tags", "enforce TLS 1.2"). Initiatives group policies.
- **Landing zones (CAF/Enterprise Scale)** — know the concept: platform subs (identity, connectivity,
  management) + application landing zones.

## 2. Core services you must be able to discuss

| Domain | Services |
|---|---|
| Compute | VM / VMSS, AKS, App Service, Container Apps, Functions, Batch |
| Storage | Blob (hot/cool/cold/archive), Files, Disks (Premium SSD v2, Ultra), Data Lake |
| Data | Azure SQL, SQL MI, PostgreSQL Flexible Server, Cosmos DB, Redis Cache |
| Network | VNet, NSG, UDR, ASG, Load Balancer, App Gateway + WAF, Front Door, Traffic Manager, Private Link, Private DNS, NAT Gateway, Azure Firewall, VPN/ExpressRoute, Bastion |
| Messaging | Service Bus, Event Hubs, Event Grid, Storage Queues |
| Security | Key Vault, Managed HSM, Defender for Cloud, Sentinel |
| Observability | Azure Monitor, Log Analytics, App Insights, Managed Prometheus/Grafana, Alerts, Action Groups |
| Governance | Policy, Blueprints, Cost Management, Resource Graph, Locks, Tags |

## 3. Networking essentials

- VNet + subnets; NSG rules are stateful, evaluated by priority, applied at subnet and NIC.
- **UDR / route tables** override system routes — classic cause of "traffic disappears".
- **Service Endpoints** vs **Private Endpoints**: private endpoint gives a private IP in your VNet
  and requires a **Private DNS Zone** linked to the VNet — the #1 misconfiguration.
- **Peering** is non-transitive. Hub-spoke with Azure Firewall / NVA for transit.
- **SNAT exhaustion**: default outbound access is deprecated; use NAT Gateway (recommended) or
  explicit LB outbound rules with enough ports.
- Effective security rules / effective routes blades are your debugging tools.
- Network Watcher: connection troubleshoot, NSG flow logs, packet capture, connection monitor.

## 4. AKS (the piece most SRE roles care about)

**Design decisions**
- Network plugin: kubenet (legacy) vs **Azure CNI** vs **Azure CNI Overlay** (recommended: pod CIDR
  doesn't consume VNet IPs) vs Cilium dataplane.
- Private cluster (private API endpoint) vs authorized IP ranges.
- System node pool (CriticalAddonsOnly taint) + user node pools + spot pools for batch.
- Availability zones for node pools; zone-redundant control plane with the Uptime SLA.
- Upgrade channels, node image upgrades, maintenance windows, max surge.
- **Entra ID integration + Azure RBAC for Kubernetes authorization.**
- **Workload Identity** (federated) → pod gets an Entra token → access Key Vault / Storage / SQL without secrets.
- ACR attached to AKS via `AcrPull` role on the kubelet identity.
- Ingress: AGIC (App Gateway), managed NGINX (app routing add-on), or Front Door → internal LB.
- Add-ons: Azure Monitor managed Prometheus, Container Insights, Key Vault CSI driver, Defender, Policy.

**Common AKS incidents**
| Issue | Cause / fix |
|---|---|
| Pods `Pending`, autoscaler idle | Subnet IP exhaustion (Azure CNI), quota (vCPU) limits, max node count |
| `ImagePullBackOff` from ACR | Missing `AcrPull` on kubelet identity, private ACR without private endpoint/DNS |
| Nodes `NotReady` after upgrade | Node image incompatibility, PDB blocking drain, disk pressure |
| Outbound calls failing intermittently | SNAT port exhaustion → add NAT Gateway |
| Private endpoint DNS failures | Private DNS zone not linked to VNet, or CoreDNS not forwarding |
| API server throttled | Too many watch/list clients, hot controllers — check `apiserver_request_total` |
| PVC stuck `Pending` | Zonal disk + pod scheduled to another zone; use `WaitForFirstConsumer` |

```bash
az aks get-credentials -g rg -n aks --admin
az aks show -g rg -n aks -o table
az aks nodepool list -g rg --cluster-name aks -o table
az aks upgrade -g rg -n aks --kubernetes-version 1.30.4
az aks nodepool upgrade -g rg --cluster-name aks -n userpool --node-image-only
az aks command invoke -g rg -n aks -c "kubectl get pods -A"   # private cluster access
```

## 5. Reliability on Azure

- SLAs are per-service and **composite** — a chain of 99.9% services multiplies down.
- Availability Zones → zone-redundant deployments; region pairs → DR.
- Patterns: Retry with backoff, Circuit Breaker, Bulkhead, Queue-Based Load Levelling,
  Throttling, Health Endpoint Monitoring, Deployment Stamps, Geode.
  (Azure Architecture Center "Cloud Design Patterns" — worth skimming before interviews.)
- **Well-Architected Framework** pillars: Reliability, Security, Cost, Operational Excellence, Performance.
  Senior interviews often ask you to critique a design against these.
- Backup: Azure Backup, geo-redundant storage (GRS/RA-GRS), SQL PITR + LTR, AKS Backup.
- Resource locks (CanNotDelete / ReadOnly) on production resource groups.

## 6. Azure Monitor & KQL

Data flows: platform metrics, resource logs (diagnostic settings), activity log, App Insights
telemetry, Prometheus metrics → Log Analytics workspace / Azure Monitor Metrics.

```kusto
// Top failing requests in the last hour
requests
| where timestamp > ago(1h) and success == false
| summarize count() by name, resultCode
| order by count_ desc

// p50/p95/p99 latency per operation
requests
| where timestamp > ago(6h)
| summarize percentiles(duration, 50, 95, 99) by operation_Name
| order by percentile_duration_99 desc

// Availability SLI over 5-minute buckets
requests
| where timestamp > ago(28d)
| summarize good = countif(success == true), total = count() by bin(timestamp, 5m)
| extend sli = todouble(good) / total
| where sli < 0.999

// AKS container restarts
KubePodInventory
| where TimeGenerated > ago(1d)
| summarize Restarts = max(PodRestartCount) by Name, Namespace
| where Restarts > 0
| order by Restarts desc

// Container logs containing errors
ContainerLogV2
| where TimeGenerated > ago(30m)
| where LogMessage has_any ("error", "exception", "panic")
| project TimeGenerated, PodName, ContainerName, LogMessage
| take 200

// Node CPU pressure
Perf
| where ObjectName == "K8SNode" and CounterName == "cpuUsageNanoCores"
| summarize avg(CounterValue) by Computer, bin(TimeGenerated, 5m)
| render timechart

// Who deleted the resource?
AzureActivity
| where OperationNameValue endswith "DELETE" and ActivityStatusValue == "Success"
| project TimeGenerated, Caller, ResourceId, OperationNameValue

// NSG flow logs: denied traffic
AzureNetworkAnalytics_CL
| where FlowStatus_s == "D"
| summarize count() by SrcIP_s, DestIP_s, DestPort_d
```

KQL operators to know: `where summarize extend project join union let bin ago make-series
render top order parse mv-expand arg_max percentile countif materialize`.

## 7. Cost (FinOps) — senior interviews ask this

- Reserved Instances / Savings Plans / Spot; right-sizing; autoscale to zero off-hours.
- Storage lifecycle policies (hot→cool→archive).
- Log Analytics: **commitment tiers**, table-level retention, Basic Logs for verbose data,
  sampling in App Insights. Logging bills are a common runaway cost.
- Tagging strategy + Cost Management budgets + anomaly alerts + Azure Resource Graph for inventory.

## 8. Interview questions

1. Design a highly available web app in Azure across regions. What's the RTO/RPO and where's the data?
2. Explain managed identity vs service principal. When do you use each?
3. A pod in AKS can't reach a private Azure SQL. Debug it. (DNS → private endpoint → private DNS zone
   link → NSG → UDR → firewall → SQL firewall rules → auth.)
4. How do you secure secrets for workloads in AKS?
5. How would you roll out an AKS version upgrade in production with zero downtime?
6. Your Log Analytics bill tripled. What do you do?
7. Difference between Front Door, Traffic Manager, and Application Gateway — when do you use each?
8. How would you enforce "no public IPs" across 200 subscriptions? (Azure Policy at management group scope.)

## Certifications
- **AZ-104** (admin) → **AZ-400** (DevOps Engineer Expert) → **AZ-305** (Solutions Architect).
- For SRE roles, AZ-400 + CKA is the strongest pair.
