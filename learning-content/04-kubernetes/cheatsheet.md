# Kubernetes cheatsheet

```bash
alias k=kubectl
export do="--dry-run=client -o yaml"
export now="--force --grace-period=0"
source <(kubectl completion bash)     # or zsh
```

## Context / namespace
```bash
kubectl config get-contexts
kubectl config use-context <ctx>
kubectl config set-context --current --namespace=<ns>
kubectl api-resources | grep -i ingress
kubectl explain deploy.spec.template.spec.containers --recursive | less
```

## Fast object creation
```bash
k run nginx --image=nginx $do > pod.yaml
k create deploy web --image=nginx --replicas=3 $do
k expose deploy web --port=80 --target-port=8080 --type=ClusterIP $do
k create cm app-config --from-literal=LOG_LEVEL=debug $do
k create secret generic app-secret --from-literal=KEY=value $do
k create job hello --image=busybox -- /bin/sh -c 'echo hi'
k create cronjob nightly --image=busybox --schedule='0 2 * * *' -- /bin/sh -c 'echo hi'
k create sa deploy-bot
k create role dev --verb=get,list,watch --resource=pods
k create rolebinding dev-rb --role=dev --serviceaccount=default:deploy-bot
```

## Inspect
```bash
k get po -A -o wide
k get po -l app=web --show-labels
k get po --field-selector status.phase!=Running -A
k get po -o custom-columns='NAME:.metadata.name,NODE:.spec.nodeName,CPU:.spec.containers[*].resources.requests.cpu'
k get po -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.status.podIP}{"\n"}{end}'
k get deploy web -o yaml | yq '.spec.template.spec.containers[0].image'
k describe node <node> | sed -n '/Allocated resources/,/Events/p'
k get events -A --sort-by=.lastTimestamp | tail -40
```

## Logs & exec
```bash
k logs -f deploy/web --all-containers --max-log-requests=10
k logs <pod> -c <container> --previous --since=15m --timestamps
k logs -l app=web --tail=100 --prefix
k exec -it <pod> -- sh
k debug -it <pod> --image=nicolaka/netshoot --target=<container>
k cp <pod>:/var/log/app.log ./app.log
k port-forward svc/web 8080:80
```

## Rollouts
```bash
k set image deploy/web web=myimg:v2 --record
k rollout status deploy/web -w --timeout=300s
k rollout history deploy/web
k rollout undo deploy/web --to-revision=3
k rollout restart deploy/web
k scale deploy/web --replicas=5
k autoscale deploy/web --min=3 --max=20 --cpu-percent=70
```

## Node ops
```bash
k cordon <node>
k drain <node> --ignore-daemonsets --delete-emptydir-data --grace-period=60
k uncordon <node>
k taint node <node> key=value:NoSchedule
k label node <node> workload=batch
```

## RBAC / auth
```bash
k auth can-i create deploy -n prod
k auth can-i '*' '*' --as system:serviceaccount:prod:app-sa
k auth whoami
```

## Troubleshooting one-liners
```bash
# Pods not Running
k get po -A --field-selector=status.phase!=Running

# Restart counts, highest first
k get po -A --sort-by=.status.containerStatuses[0].restartCount | tail -20

# Pods with no resource limits
k get po -A -o json | jq -r '.items[] | select(.spec.containers[].resources.limits == null) | "\(.metadata.namespace)/\(.metadata.name)"'

# Services with no endpoints
k get endpointslices -A -o json | jq -r '.items[] | select(.endpoints==null) | .metadata.name'

# Top consumers
k top po -A --sort-by=cpu | head
k top no

# Node capacity vs requests
k describe no | grep -A6 'Allocated resources'

# Stuck terminating namespace
k get ns <ns> -o json | jq '.spec.finalizers=[]' | k replace --raw /api/v1/namespaces/<ns>/finalize -f -
```

## Resource shortnames
`po pods` · `deploy deployments` · `rs replicasets` · `sts statefulsets` · `ds daemonsets`
`svc services` · `ing ingresses` · `cm configmaps` · `ns namespaces` · `no nodes`
`pv/pvc` · `sa serviceaccounts` · `hpa` · `pdb` · `netpol` · `sc storageclasses` · `crd` · `ep/endpointslices`

## Exit codes
`0` ok · `1` app error · `125` runtime error · `126` not executable · `127` not found
`128+n` signal n → `137` SIGKILL/OOM · `139` SIGSEGV · `143` SIGTERM
