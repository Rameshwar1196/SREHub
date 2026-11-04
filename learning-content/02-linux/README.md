# 02 — Linux

The most common practical screen for an SRE. You must be able to debug a box you've never seen
in under 10 minutes and explain *why* each command helps.

## Topics to know cold

- Boot: firmware → bootloader (GRUB) → kernel → initramfs → `systemd` → targets.
- Processes: fork/exec, PID/PPID, states (`R D S Z T`), signals, zombies vs orphans, nice/ionice.
- Memory: virtual vs resident, page cache, swap, OOM killer, `/proc/meminfo`, cgroup limits.
- Filesystems: inodes, hard vs soft links, mount options, `ext4`/`xfs`, LVM, disk full vs inode full.
- Permissions: rwx, setuid/setgid/sticky, ACLs, capabilities, umask.
- Networking: interfaces, routing table, conntrack, iptables/nftables, sockets.
- systemd: units, journald, timers, `systemctl`/`journalctl`.
- Namespaces + cgroups (the basis of containers): pid, net, mnt, uts, ipc, user, cgroup.
- Performance: load average vs CPU utilisation, run queue, context switches, steal time, I/O wait.

## The 60-second triage (Netflix method)

```bash
uptime                 # load averages: 1/5/15 min trend
dmesg -T | tail -40    # OOM kills, disk errors, network resets
vmstat 1 5             # r (runqueue), b (blocked), si/so (swap), us/sy/id/wa/st
mpstat -P ALL 1 5      # per-CPU balance; one hot core = single-threaded bottleneck
pidstat 1 5            # per-process CPU over time
iostat -xz 1 5         # await, %util, r/s w/s  -> disk saturation
free -m                # available vs free; buff/cache
sar -n DEV 1 5         # network throughput per interface
sar -n TCP,ETCP 1 5    # retransmits, active/passive opens
top / htop             # final confirmation
```

## Troubleshooting playbooks

### High CPU
```bash
top -o %CPU
pidstat -t 1                       # per-thread
perf top -p <pid>                  # hot symbols
cat /proc/<pid>/status | grep -i thread
py-spy top --pid <pid>             # python
jstack <pid>                       # java
```
Ask: user vs system time? `sy` high → syscalls/kernel. `st` high → noisy neighbour/hypervisor.
`wa` high → it's disk, not CPU.

### High memory / OOM
```bash
free -m; cat /proc/meminfo
ps aux --sort=-%mem | head
dmesg -T | grep -i -E 'oom|killed process'
smem -rs uss                       # true per-process usage
cat /sys/fs/cgroup/memory.max      # container limit (cgroup v2)
cat /sys/fs/cgroup/memory.current
```
Common trap: `free` looks low but `available` is fine — page cache is reclaimable.

### Disk full
```bash
df -h                              # space
df -i                              # inodes  <-- often the real cause
du -xh --max-depth=1 / | sort -rh | head
lsof +L1                           # deleted-but-open files holding space
```
Fix for deleted-but-open: restart the holding process, or `truncate -s0 /proc/<pid>/fd/<n>`.

### Slow disk
```bash
iostat -xz 1                       # %util ~100 and high await = saturated
iotop -oPa
biolatency / biosnoop              # bcc tools
```

### Network
```bash
ss -s                              # socket summary
ss -tanp state time-wait | wc -l   # TIME_WAIT pressure
ss -lntp                           # what's listening
ip a; ip r; ip neigh
ping / mtr -rwz <host>             # loss + per-hop latency
dig +trace example.com
curl -v -o /dev/null -w '%{time_namelookup} %{time_connect} %{time_appconnect} %{time_starttransfer} %{time_total}\n' https://host
tcpdump -ni eth0 'port 443 and host 10.0.0.5' -c 100 -w /tmp/cap.pcap
conntrack -S                       # conntrack table drops
nstat -az | grep -i -E 'retrans|drop|listen'
```

### "Process is stuck"
```bash
cat /proc/<pid>/stack
strace -f -p <pid> -T -tt          # syscall latency
ltrace -p <pid>
cat /proc/<pid>/wchan              # what kernel function it sleeps in
```
State `D` (uninterruptible sleep) → almost always storage or NFS.

### Service won't start
```bash
systemctl status <svc> -l
journalctl -u <svc> -n 200 --no-pager
journalctl -u <svc> --since "10 min ago" -p err
systemd-analyze blame
```

## Text processing you will be asked to write live

```bash
# Top 10 IPs in an access log
awk '{print $1}' access.log | sort | uniq -c | sort -rn | head -10

# 5xx rate per minute
awk '$9 ~ /^5/ {print substr($4,2,17)}' access.log | uniq -c

# p95 latency from field 11
awk '{print $11}' access.log | sort -n | awk '{a[NR]=$1} END{print a[int(NR*0.95)]}'

# Requests per endpoint, excluding health checks
grep -v '/healthz' access.log | awk '{print $7}' | sort | uniq -c | sort -rn | head

# Follow logs and count errors live
tail -F app.log | grep --line-buffered ERROR | awk '{c++; print c, $0}'
```

## Hardening basics
- SSH: key-only, no root login, `AllowUsers`, fail2ban, non-default port (marginal).
- `ufw`/`firewalld` default deny inbound.
- Unattended security updates, minimal packages.
- Auditd, SELinux/AppArmor enforcing.
- No secrets in env files on disk with world-read permissions.

## Interview questions

1. Load average is 40 on a 4-core box but CPU is 20% idle. Explain.
   → Load counts `R` **and** `D` state. High load with idle CPU = processes blocked on I/O (or NFS).
2. `df` says disk is full, `du` says it's not. Why?
   → Deleted files still held open by a process (`lsof +L1`), or a filesystem mounted over a populated directory.
3. What happens when you run `ls`? Walk through shell → fork/exec → syscalls → output.
4. Difference between a hard link and a symlink? Can you hard-link a directory or across filesystems? (No to both.)
5. How does the OOM killer choose a victim? (`oom_score` from memory footprint, adjusted by `oom_score_adj`.)
6. Explain TIME_WAIT. Why does it exist and when is it a problem?
7. How do containers isolate processes? (namespaces = visibility, cgroups = resource limits, capabilities/seccomp = privileges.)
8. Server is unreachable but pings fine. Where do you look?
   → Listening socket? firewall/NSG? conntrack full? app accept queue full (`ss -lnt` Recv-Q)? TLS failure? DNS resolving to wrong IP?

## Practice
- <https://linuxjourney.com>
- <https://overthewire.org/wargames/bandit/> — shell fluency
- `sadservers.com` — realistic broken-server scenarios, closest thing to an SRE screen
- Brendan Gregg's *Systems Performance* (2nd ed.) + his USE method page
