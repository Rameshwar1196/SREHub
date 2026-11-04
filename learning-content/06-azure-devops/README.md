# 06 — Azure DevOps

Five services: **Boards, Repos, Pipelines, Test Plans, Artifacts**. SRE interviews focus on
Pipelines, Repos (branching), Artifacts (supply chain), and governance.

## 1. Pipelines concepts

```
Pipeline → Stages → Jobs → Steps (tasks / scripts)
```
- **Stage** — deployment boundary; can have approvals via Environments.
- **Job** — unit of scheduling on an agent. `job` (regular), `deployment` (with strategies), `server`
  (agentless: manual validation, delay, invoke REST API).
- **Agents**: Microsoft-hosted (clean VM each run, 10GB, time-limited) vs **self-hosted** /
  **scale set agents** (needed for private network access, custom tooling, long jobs).
- **Service connections** — auth to Azure/ACR/K8s. Prefer **workload identity federation** over
  secret-based SPs (no secret rotation).
- **Variable groups** (linked to Key Vault), secret variables, **runtime parameters** (typed, appear at queue time).
- **Environments** — deployment targets with approvals, checks (branch control, business hours,
  Azure Monitor alert check, REST invoke), and deployment history.
- **Templates** — `extends` templates for governance (you cannot bypass them), `includes` for reuse.
- **Triggers**: CI (`trigger`), PR (`pr:` — for GitHub; for Azure Repos use branch policies),
  `schedules`, `resources.pipelines` (pipeline completion), `resources.repositories`, `resources.containers`.
- **Matrix** and `strategy: parallel`, `dependsOn`, `condition`, `continueOnError`, `timeoutInMinutes`.
- **Output variables** between jobs/stages: `isOutput=true` + `dependencies.<job>.outputs['step.var']`.
- **Approvals & checks** are evaluated at the *stage* level before a deployment job runs.

### Deployment strategies (`deployment` job)
- `runOnce` — simple.
- `rolling` — batches (`maxParallel`), VM resources.
- `canary` — `preDeploy → deploy (increments) → routeTraffic → postRouteTraffic → on: success|failure`.
- Kubernetes canary via `KubernetesManifest@1` with `strategy: canary` + `trafficSplitMethod: smi`.

## 2. Pipeline YAML you should be able to write

See [pipelines/](pipelines/):
- [build-test-scan.yml](pipelines/build-test-scan.yml) — build, test, coverage, SCA, container scan, push to ACR.
- [deploy-aks.yml](pipelines/deploy-aks.yml) — multi-stage deploy to AKS with approvals and canary.
- [terraform.yml](pipelines/terraform.yml) — plan/apply with remote state and manual approval.
- [templates/](pipelines/templates/) — reusable job templates.

## 3. Repos & branching

- Branch policies: min reviewers, linked work items, comment resolution, build validation,
  status checks, merge strategy (squash / rebase / merge), **required reviewers by path**.
- Strategies: **trunk-based** (short-lived branches, feature flags — best for SRE/DORA) vs GitFlow
  (release branches, heavier) vs GitHub Flow.
- Protect `main`; no direct pushes; signed commits where required.
- `CODEOWNERS` equivalent = path-based required reviewers in branch policies.
- Commit message convention — see the org standard (`ISSUE-NUMBER ISSUE-NAME. Short title`).

## 4. Artifacts & supply chain

- Feeds (NuGet, npm, Maven, Python, Universal), upstream sources with caching, retention policies.
- View promotion: `@local → @prerelease → @release`.
- Immutable, versioned artifacts. **Build once, promote the same artifact through environments.**
- SBOM generation, dependency scanning, container image signing, provenance.
- Advanced Security for Azure DevOps: secret scanning, code scanning (CodeQL), dependency scanning.

## 5. Governance & reliability of the pipeline itself

- Pipeline as code in the repo, reviewed like app code.
- Required `extends` templates at org level to enforce scanning/approvals.
- Least-privilege service connections scoped to a single resource group, per environment.
- Separate subscriptions per environment; production service connection restricted to protected branches.
- Agent pool isolation for production.
- Secrets never in YAML. Key Vault-linked variable groups; mark secrets; avoid `echo` of secrets.
- Retry/idempotency: deployments must be safe to re-run.
- Pipeline metrics → **DORA**: deployment frequency, lead time for changes, change failure rate, MTTR.

## 6. Useful CLI

```bash
az extension add --name azure-devops
az devops configure --defaults organization=https://dev.azure.com/ORG project=PROJ

az pipelines list -o table
az pipelines run --name my-pipeline --branch main --parameters env=prod
az pipelines runs list --status inProgress -o table
az pipelines runs show --id 1234
az pipelines variable-group list -o table

az repos pr create --source-branch feature/x --target-branch main --title "..." --work-items 12345
az repos pr list --status active -o table
az repos policy list --branch main -o table

az boards work-item show --id 12345
az artifacts universal download --organization ... --feed myfeed --name pkg --version 1.0.0 --path .
```

## 7. Common pipeline failures and fixes

| Symptom | Cause |
|---|---|
| `##[error]TF400813` / auth failures | Expired service connection secret → move to workload identity federation |
| Agent can't reach private resources | Microsoft-hosted agent outside VNet → use self-hosted/scale set agents or private endpoints + allowlisted IP ranges |
| Flaky tests fail the pipeline randomly | Quarantine + `--blame-hang`, fix or isolate; never blanket `continueOnError` |
| Slow builds | No caching (`Cache@2`), no shallow fetch (`fetchDepth: 1`), monolithic jobs; parallelise |
| "Pipeline stuck queued" | Parallel job limit exhausted, agent pool offline, demands not satisfied |
| Secret leaked in logs | Secrets are masked only if marked secret; avoid transforming them (base64 breaks masking) |
| Deploy succeeded but app broken | No smoke test/post-deploy gate; add health check + automated rollback |

## 8. Interview questions

1. Design a CI/CD pipeline for a microservice deploying to AKS across dev/stage/prod. Where are the gates?
2. How do you prevent a bad deploy from reaching production? (Build validation, unit/integration tests,
   artifact promotion, staged rollout with canary, automated SLO-based gates, auto-rollback.)
3. How do you handle secrets in pipelines?
4. How do you roll back? What's your target MTTR and what makes rollback fast?
5. Explain the DORA metrics and how you'd improve change failure rate.
6. Self-hosted vs Microsoft-hosted agents — trade-offs.
7. How would you enforce that every pipeline in the org runs a security scan? (`extends` template + policy.)
8. Your deploy pipeline takes 45 minutes. How do you cut it to 10?
