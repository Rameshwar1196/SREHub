# Resources

Curated, not exhaustive. Depth beats breadth — finish a few of these rather than skimming all.

## Books (priority order for SRE interviews)

1. **Site Reliability Engineering** — Google. Free: <https://sre.google/books/>
2. **The Site Reliability Workbook** — Google. Free. The SLO chapters are the most interview-relevant.
3. **Implementing Service Level Objectives** — Alex Hidalgo.
4. **Systems Performance, 2nd ed.** — Brendan Gregg. The Linux performance bible.
5. **Designing Data-Intensive Applications** — Martin Kleppmann. Essential for design rounds.
6. **Database Internals** — Alex Petrov (if you go deep on storage).
7. **Kubernetes Up & Running** / **Kubernetes Patterns** — Burns et al. / Ibryam & Huß.
8. **Accelerate** — Forsgren, Humble, Kim. DORA metrics, the language of engineering leadership.
9. **The Phoenix Project** / **The Unicorn Project** — narrative, useful for behavioural framing.
10. **Release It!, 2nd ed.** — Michael Nygard. Stability patterns, the best failure-mode catalogue.
11. **Chaos Engineering** — Rosenthal & Jones.
12. **Seeking SRE** — Blank-Edelman (breadth of industry practice).

## Free documentation worth reading end to end

- Kubernetes docs — Concepts + Tasks: <https://kubernetes.io/docs/>
- Prometheus docs + PromQL: <https://prometheus.io/docs/>
- Azure Architecture Center — Cloud Design Patterns & Well-Architected Framework:
  <https://learn.microsoft.com/azure/architecture/>
- Azure DevOps YAML schema: <https://learn.microsoft.com/azure/devops/pipelines/yaml-schema/>
- Terraform + AzureRM provider docs: <https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs>
- OpenTelemetry: <https://opentelemetry.io/docs/>
- Brendan Gregg's site (USE method, flame graphs, Linux perf tools): <https://brendangregg.com/>

## Courses & structured learning

- Microsoft Learn — free paths for AZ-104, AZ-400, AZ-305: <https://learn.microsoft.com/training/>
- KodeKloud — CKA/CKS labs, very hands-on.
- killer.sh — CKA/CKS exam simulators (2 sessions included with the exam).
- Linux Foundation — LFS/CKA official courses.
- Coursera — Google Cloud SRE specialisation (concepts transfer to Azure).

## Practice platforms

| Site | Use |
|---|---|
| <https://sadservers.com> | Broken-Linux-server scenarios. Closest to a real SRE screen. |
| <https://killercoda.com> | Free browser K8s/Linux scenarios. |
| <https://overthewire.org/wargames/bandit/> | Shell fluency. |
| <https://exercism.org> | Python/Go/Bash practice with mentoring. |
| <https://leetcode.com> (easy/medium only) | Arrays, hashmaps, strings, intervals, graphs. |
| <https://www.pramp.com> / interviewing.io | Free peer mock interviews. |
| <https://github.com/donnemartin/system-design-primer> | System design refresher. |

## Blogs, newsletters, talks

- Google SRE blog, Cloudflare blog, Netflix Tech Blog, Slack Engineering, Dropbox Tech.
- <https://github.com/danluu/post-mortems> — a large collection of public postmortems. Read 10.
- SREcon talks on the USENIX YouTube channel.
- Increment magazine (archive) — reliability and on-call issues.
- Azure Updates + AKS release notes (stay current on what changed).

## Reference repos

- <https://github.com/dastergon/awesome-sre>
- <https://github.com/kubernetes/kubernetes> — read a controller to understand reconciliation.
- <https://github.com/kubernetes/community/tree/master/contributors/design-proposals> — design docs.
- <https://github.com/Azure/AKS> — AKS issues and roadmap; great source of real failure modes.
- <https://github.com/bregman-arie/devops-exercises> — broad Q&A bank.

## Certifications

| Cert | Provider | Value for SRE |
|---|---|---|
| CKA | CNCF | ⭐⭐⭐⭐⭐ practical, widely respected |
| AZ-400 DevOps Engineer Expert | Microsoft | ⭐⭐⭐⭐⭐ for Azure/ADO roles |
| CKS | CNCF | ⭐⭐⭐⭐ differentiator |
| AZ-104 Administrator | Microsoft | ⭐⭐⭐ foundation |
| AZ-305 Solutions Architect | Microsoft | ⭐⭐⭐ design credibility |
| HashiCorp Terraform Associate | HashiCorp | ⭐⭐⭐ quick win |

## Tools to have installed and be fluent with

```
kubectl, kubectx/kubens, k9s, stern, helm, kustomize, kubeconform
az cli, azure-devops extension, Azure Resource Graph
terraform, tflint, tfsec/checkov
docker/podman, trivy
prometheus, promtool, grafana, k6/hey
jq, yq, curl, dig, tcpdump, mtr, ss, strace
git, gh/az repos, pre-commit
python 3.12+, pytest, httpx; PowerShell 7
```
