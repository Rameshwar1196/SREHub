# 07 — Infrastructure as Code & Configuration Management

## Principles

- **Declarative over imperative.** Describe desired state, let the tool converge.
- **Idempotent.** Running twice changes nothing the second time.
- **Immutable infrastructure.** Replace, don't patch. No snowflake servers, no manual SSH fixes.
- **Everything in version control**, reviewed, tested, and deployed by pipeline — no local `apply`.
- **Drift detection.** Scheduled `plan` that alerts when reality diverges from code.

## Terraform

### Core concepts
- Providers, resources, data sources, variables, locals, outputs, modules.
- **State** — the mapping between config and real resources. Remote backend (Azure Storage) with
  **state locking** (blob lease). Never commit state; it contains secrets.
- `plan` → `apply` → `destroy`. Always apply a **saved plan** in CI.
- **Workspaces** vs directory-per-environment. Prefer directories + separate state files for prod isolation.
- `depends_on`, `count` vs `for_each` (use `for_each` — stable addressing), `lifecycle`
  (`prevent_destroy`, `create_before_destroy`, `ignore_changes`), `moved` blocks for refactors.
- Module design: small, composable, versioned, with sane defaults and no hardcoded environment values.
- Testing: `terraform validate`, `tflint`, `tfsec`/`checkov`, `terraform test`, Terratest.

### Commands
```bash
terraform init -upgrade -backend-config=backend.hcl
terraform fmt -recursive
terraform validate
terraform plan -out=tfplan -var-file=prod.tfvars
terraform show -json tfplan | jq '.resource_changes[] | select(.change.actions[0]=="delete")'
terraform apply tfplan
terraform state list
terraform state show azurerm_kubernetes_cluster.this
terraform state mv 'module.old.azurerm_storage_account.a' 'module.new.azurerm_storage_account.a'
terraform import azurerm_resource_group.rg /subscriptions/<sub>/resourceGroups/<rg>
terraform force-unlock <lock-id>       # only after confirming no apply is running
terraform taint / -replace=<addr>
```

### Interview-relevant traps
- Two pipelines applying the same state concurrently → state lock contention. Serialise via
  pipeline concurrency settings.
- `terraform destroy` in prod — protect with `prevent_destroy`, resource locks, and plan review
  that greps for deletes.
- Secrets in state → restrict access to the state container, enable encryption, use Key Vault refs.
- Provider version drift → pin `required_providers` and commit `.terraform.lock.hcl`.
- Huge blast radius from one state file → split state by lifecycle (network / platform / app).

### Minimal AKS example
```hcl
terraform {
  required_version = "~> 1.9"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
  }
  backend "azurerm" {}
}

provider "azurerm" {
  features {}
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${var.env}-aks"
  location = var.location
  tags     = var.tags
}

resource "azurerm_kubernetes_cluster" "this" {
  name                = "aks-${var.env}"
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  dns_prefix          = "aks-${var.env}"
  kubernetes_version  = var.kubernetes_version
  sku_tier            = "Standard"              # uptime SLA
  oidc_issuer_enabled = true
  workload_identity_enabled = true
  local_account_disabled    = true

  default_node_pool {
    name                 = "system"
    vm_size              = "Standard_D4ds_v5"
    zones                = ["1", "2", "3"]
    auto_scaling_enabled = true
    min_count            = 3
    max_count            = 6
    only_critical_addons_enabled = true
    upgrade_settings { max_surge = "33%" }
  }

  identity { type = "SystemAssigned" }

  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
    network_policy      = "cilium"
    outbound_type       = "userAssignedNATGateway"
  }

  azure_active_directory_role_based_access_control {
    azure_rbac_enabled = true
    admin_group_object_ids = var.admin_group_ids
  }

  lifecycle {
    ignore_changes = [default_node_pool[0].node_count]
  }
}
```

## Bicep / ARM

- Bicep is a DSL that transpiles to ARM JSON. No state file — ARM is the source of truth.
- Modules, `param`/`var`/`output`, decorators (`@allowed`, `@secure`, `@minLength`).
- Deployment scopes: resourceGroup, subscription, managementGroup, tenant.
- Deployment modes: **Incremental** (default) vs **Complete** (deletes resources not in the template — dangerous).
- `what-if` is the Bicep equivalent of `plan`.
- Deployment Stacks — lifecycle management + deny settings.

```bash
az bicep build --file main.bicep
az deployment group what-if -g rg -f main.bicep -p @prod.params.json
az deployment group create -g rg -f main.bicep -p @prod.params.json --name deploy-$(date +%s)
az deployment sub create -l westeurope -f subscription.bicep
```

**Terraform vs Bicep** — Terraform: multi-cloud, explicit state, richer ecosystem, better drift handling.
Bicep: no state to manage, day-0 Azure API coverage, native to Azure. Many Azure shops use Bicep for
platform and Terraform for everything else; pick one per layer and be consistent.

## Ansible (config management)

- Agentless, SSH/WinRM. Inventory → playbooks → roles → tasks → modules.
- Idempotent modules; avoid `shell`/`command` unless you add `creates`/`changed_when`.
- `--check` (dry run) and `--diff`.
- Ansible Vault for secrets; better: pull from Key Vault at runtime.
- Use for: OS baseline, golden image build (with Packer), one-off fleet operations.

```bash
ansible-inventory --graph
ansible all -m ping
ansible-playbook site.yml --check --diff --limit web
ansible-playbook site.yml -e "version=1.2.3" --tags deploy
```

## Packer + golden images

Build immutable VM images → Azure Compute Gallery → VMSS references image version.
Rolling upgrade of the scale set replaces instances. Patch = new image, not `apt upgrade` in place.

## GitOps (for Kubernetes)

- **Argo CD** / **Flux**: cluster pulls desired state from Git; continuous reconciliation and drift correction.
- App-of-apps pattern; Kustomize overlays or Helm values per environment.
- Benefits interviewers want: auditable change history, trivial rollback (`git revert`),
  no cluster credentials in CI, drift detection.
- Secrets: Sealed Secrets, SOPS, External Secrets Operator + Key Vault.

## Interview questions

1. How do you manage Terraform state for 50 microservices across 4 environments?
2. Someone changed a resource in the portal. How do you detect and fix it?
3. How do you refactor a module without destroying resources? (`moved` blocks / `state mv`, verify with plan.)
4. How do you safely handle a plan that shows an unexpected `destroy` of a database?
5. Helm vs Kustomize vs raw manifests — when do you use each?
6. Explain how you'd bootstrap a brand-new subscription from zero to running workloads.
