# 04 — Kubernetes

Target level: comfortable operating and debugging a production cluster; CKA-equivalent depth.

## 1. Architecture

**Control plane**
- `kube-apiserver` — the only component that talks to etcd; authn/authz/admission; REST API.
- `etcd` — consistent KV store (Raft). Quorum = `(n/2)+1`. Back it up. It is the cluster.
- `kube-scheduler` — filtering (predicates) then scoring (priorities) → binds Pod to Node.
- `kube-controller-manager` — reconciliation loops (deployment, replicaset, node, endpoint...).
- `cloud-controller-manager` — cloud LB, routes, node lifecycle.

**Node**
- `kubelet` — reconciles PodSpecs, runs probes, reports status.
- Container runtime via CRI — containerd / CRI-O.
- `kube-proxy` — Service VIP → endpoints (iptables/IPVS), or replaced by eBPF (Cilium).
- CNI plugin — pod networking.

**Declarative reconciliation** is the core idea: desired state in etcd, controllers drive actual → desired.

### What happens when you `kubectl apply` a Deployment?
1. kubectl → API server (authn → authz/RBAC → admission: mutating, then validating) → persisted to etcd.
2. Deployment controller sees new Deployment → creates ReplicaSet.
3. ReplicaSet controller → creates Pods (unscheduled).
4. Scheduler binds Pods to Nodes.
5. Kubelet on the node pulls image, creates sandbox + containers via CRI, CNI wires networking.
6. Probes pass → Pod Ready → EndpointSlice updated → traffic flows.

## 2. Workloads

- **Pod** — smallest unit, shares net namespace + volumes. Init containers, sidecars (`restartPolicy: Always` in initContainers, K8s 1.29+).
- **ReplicaSet / Deployment** — stateless, rolling updates, `maxSurge`/`maxUnavailable`, revision history, `kubectl rollout undo`.
- **StatefulSet** — stable identity + stable storage + ordered operations. Headless Service required.
- **DaemonSet** — one pod per node (agents, CNI, log shippers).
- **Job / CronJob** — batch; `backoffLimit`, `completions`, `parallelism`, `concurrencyPolicy`, `startingDeadlineSeconds`.

### Probes (a very common interview topic)
- `liveness` — restart the container if it fails. Keep it cheap and dependency-free.
- `readiness` — remove from Service endpoints. This is the one that protects users.
- `startup` — gives slow starters time; disables the other probes until it passes.
- Anti-pattern: liveness probe that checks a downstream DB → one DB blip restarts your whole fleet.

### Resources & QoS
- `requests` drive scheduling; `limits` drive enforcement.
- QoS: **Guaranteed** (requests==limits for all) > **Burstable** > **BestEffort** — eviction order is reverse.
- CPU limits cause **throttling** (`container_cpu_cfs_throttled_seconds_total`); many teams set CPU requests only.
- Memory limit exceeded → **OOMKill** (exit 137). No throttling for memory.
- `LimitRange` (defaults per namespace) and `ResourceQuota` (namespace totals).

### Scheduling controls
`nodeSelector`, `affinity/antiAffinity` (`requiredDuringScheduling...`, `preferred...`),
taints & tolerations, `topologySpreadConstraints`, `priorityClassName` + preemption,
Pod Disruption Budgets (`minAvailable`/`maxUnavailable`) to survive drains.

## 3. Networking

Rules: every Pod gets a routable IP; pods communicate without NAT; node↔pod works.

- **Service types**: ClusterIP, NodePort, LoadBalancer, ExternalName. Headless (`clusterIP: None`) → DNS returns pod IPs.
- **EndpointSlice** replaces Endpoints at scale.
- **kube-proxy** modes: iptables (O(n) rules), IPVS (hash table, better at scale), eBPF (no kube-proxy).
- **Ingress** — L7 HTTP routing (nginx, AGIC, Traefik). **Gateway API** is the successor — know it exists.
- **NetworkPolicy** — default allow-all until a policy selects the pod; then default deny for that direction.
  Requires a CNI that enforces it (Calico, Cilium, Azure NPM).
- **CoreDNS** — `svc.namespace.svc.cluster.local`; `ndots:5` causes extra lookups → use FQDN with trailing dot
  or tune `dnsConfig` for latency-sensitive apps. NodeLocal DNSCache for DNS load.
- **Service mesh** — mTLS, retries, traffic splitting, observability; cost = latency + complexity (Istio, Linkerd).

## 4. Storage

- `PersistentVolume` / `PersistentVolumeClaim` / `StorageClass` (dynamic provisioning).
- Access modes: RWO, ROX, RWX, RWOP. Azure Disk = RWO; Azure Files = RWX.
- `volumeBindingMode: WaitForFirstConsumer` — essential for zonal disks.
- Reclaim policy: Delete vs Retain. Use Retain for anything you care about.
- CSI drivers, volume snapshots, expansion (`allowVolumeExpansion`).
- `emptyDir`, `configMap`, `secret`, `projected`, `downwardAPI` volumes.

## 5. Config & security

- ConfigMap / Secret (base64, **not** encrypted by default → enable etcd encryption at rest, or use
  CSI Secrets Store + Azure Key Vault + workload identity).
- RBAC: Role/ClusterRole + RoleBinding/ClusterRoleBinding, ServiceAccounts. Least privilege; avoid `cluster-admin`.
- Pod Security Admission: `privileged` / `baseline` / `restricted` per namespace.
- `securityContext`: `runAsNonRoot`, `readOnlyRootFilesystem`, `allowPrivilegeEscalation: false`, drop ALL capabilities, seccomp `RuntimeDefault`.
- Admission control policy: OPA Gatekeeper or Kyverno (enforce labels, registries, resource limits).
- Image supply chain: private registry, digest pinning, signing (cosign), scanning (Trivy/Defender).

## 6. Scaling

- **HPA** — on CPU/memory or custom/external metrics (KEDA for queue-length, event-driven, scale-to-zero).
- **VPA** — right-sizes requests; don't run VPA and HPA on the same metric.
- **Cluster Autoscaler** — adds nodes when pods are `Pending` due to insufficient resources.
- **Karpenter / AKS Node Autoprovisioning** — just-in-time right-sized nodes.
- Scale-down blockers: pods without controllers, local storage, restrictive PDBs, `safe-to-evict: false`.

## 7. Troubleshooting playbook

```bash
kubectl get pods -A -o wide --sort-by=.status.startTime
kubectl describe pod <pod>              # Events section first, always
kubectl logs <pod> -c <container> --previous --timestamps
kubectl get events -A --sort-by=.lastTimestamp | tail -50
kubectl top pod / kubectl top node
kubectl get endpointslices -l kubernetes.io/service-name=<svc>
kubectl exec -it <pod> -- sh
kubectl debug -it <pod> --image=nicolaka/netshoot --target=<container>
kubectl debug node/<node> -it --image=ubuntu
kubectl auth can-i create pods --as system:serviceaccount:ns:sa
kubectl get --raw /metrics | head
```

### Symptom → cause table

| Symptom | Likely causes |
|---|---|
| `Pending` | Insufficient resources, no matching node selector/affinity, unbound PVC, taints, quota exceeded |
| `ImagePullBackOff` | Wrong tag, private registry without imagePullSecret, ACR RBAC missing, rate limit |
| `CrashLoopBackOff` | App exits on startup, bad config/secret, failing dependency, OOMKill, liveness too aggressive |
| `OOMKilled` (137) | Memory limit too low, leak, JVM heap not aware of cgroup limit |
| `Error 143` | SIGTERM — normal during rollout/eviction |
| `CreateContainerConfigError` | Missing ConfigMap/Secret key |
| `Init:Error` | Init container failing — check its logs |
| `Terminating` forever | Finalizers, volume detach stuck, node NotReady |
| Service has no endpoints | Selector doesn't match pod labels, or readiness probe failing |
| Intermittent 5xx during deploy | No `preStop` sleep + endpoint propagation race; add `terminationGracePeriodSeconds` + preStop |
| Node `NotReady` | kubelet down, disk/memory/PID pressure, network plugin failure |
| DNS slow/intermittent | CoreDNS under-scaled, ndots, conntrack races, missing NodeLocal DNSCache |

### Graceful shutdown sequence (know this)
Pod delete → (a) removed from EndpointSlices **and** (b) `preStop` hook then SIGTERM, **in parallel**.
Because they race, add a `preStop: sleep 5–15s` so in-flight traffic drains before the app stops accepting.
After `terminationGracePeriodSeconds`, SIGKILL.

## 8. Cluster operations

- Upgrades: control plane first, then node pools; surge nodes; cordon/drain respecting PDBs.
- etcd backup/restore (`etcdctl snapshot save/restore`) — managed for you in AKS, but know it.
- Certificate rotation, API deprecations (`kubectl-convert`, `pluto`, deprecation metrics).
- Multi-tenancy: namespaces + quotas + network policies + RBAC; or separate clusters.
- Cost: right-sizing, spot node pools, bin packing, idle node reclaim, Kubecost/OpenCost.
- GitOps: Argo CD / Flux — desired state in Git, drift detection, easy rollback.

## 9. Manifests worth memorising

See [manifests/](manifests/) for a production-quality Deployment, Service, HPA, PDB,
NetworkPolicy, and a debug pod.

## 10. Interview questions

1. Pod is `Pending`. Walk through your diagnosis. (Events → scheduler reason → resources/taints/PVC/quota.)
2. Difference between liveness and readiness, and a concrete example where mixing them causes an outage.
3. How does a request reach a pod from outside the cluster? (DNS → LB → node → iptables/IPVS DNAT → pod; or Ingress controller pod → service → pod.)
4. How do you do a zero-downtime rolling update? (Readiness probes, maxUnavailable=0, preStop drain, PDB, surge capacity, graceful shutdown in app.)
5. StatefulSet vs Deployment — when do you actually need a StatefulSet?
6. Explain QoS classes and eviction order.
7. How do you give a pod access to Azure Key Vault without secrets in the cluster? (Workload Identity → federated credential → AAD token → Key Vault; or CSI Secrets Store driver.)
8. Cluster is at capacity and Cluster Autoscaler isn't adding nodes. Why? (Max node count, quota, no instance capacity in zone, pods that can't fit any node size, pending due to PVC zone affinity.)
9. How would you debug 0.5% of requests failing after a deploy?
10. What breaks if etcd loses quorum?

## Certification
- **CKA** — most valuable for SRE. 2 hours, hands-on, `kubectl` speed matters.
- **CKS** — security, good differentiator.
- Practice: killer.sh (included with exam), kodekloud, `kubectl` aliases + `--dry-run=client -o yaml`.

```bash
alias k=kubectl
export do="--dry-run=client -o yaml"
k create deploy web --image=nginx $do > web.yaml
```
