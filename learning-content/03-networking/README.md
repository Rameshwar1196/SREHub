# 03 — Networking

## Model

OSI layers 1–7 vs TCP/IP. In interviews you mostly live at L3/L4/L7.
Know exactly what a load balancer does differently at L4 vs L7.

## TCP

- 3-way handshake: `SYN → SYN/ACK → ACK`. 4-way close: `FIN/ACK ×2` + `TIME_WAIT` (2×MSL).
- Backlogs: SYN queue (`tcp_max_syn_backlog`) and accept queue (`somaxconn` + app `listen()` backlog).
  Overflow → `ListenOverflows`/`ListenDrops` in `nstat`.
- Congestion control: slow start, congestion avoidance, fast retransmit/recovery. BBR vs CUBIC.
- Flow control = receive window. Congestion control = network. Different things.
- Nagle + delayed ACK interaction → 40ms stalls. `TCP_NODELAY`.
- Keepalives vs application-level heartbeats.
- MTU / MSS, Path MTU Discovery black holes (ICMP blocked) → hangs on large payloads only.

**TCP vs UDP**: ordering, reliability, congestion control, head-of-line blocking. QUIC/HTTP3 moves
reliability to userspace over UDP to remove HOL blocking.

## DNS

- Resolution: stub resolver → recursive resolver → root → TLD → authoritative.
- Records: `A`, `AAAA`, `CNAME`, `ALIAS/ANAME`, `MX`, `TXT`, `SRV`, `NS`, `SOA`, `CAA`, `PTR`.
- CNAME cannot coexist with other records at the same name, and cannot be at the zone apex.
- TTL controls failover speed — lower TTL *before* a planned migration.
- Negative caching is governed by the SOA minimum.
- Split-horizon / private DNS zones (Azure Private DNS).
- Common outage cause: DNS TTL too long, or resolver caching a stale record after failover.

```bash
dig +trace example.com
dig @8.8.8.8 example.com A +short
dig example.com SOA
nslookup -type=txt example.com
```

## HTTP & TLS

- HTTP/1.1 keep-alive & pipelining → HTTP/2 multiplexing (+ HOL at TCP) → HTTP/3 over QUIC.
- Status codes: know 301 vs 302 vs 307/308, 401 vs 403, 502 vs 503 vs 504.
  - **502** upstream returned garbage/closed. **503** no healthy upstream / overloaded. **504** upstream timed out.
- Idempotent methods and safe retries.
- Caching: `Cache-Control`, `ETag`, `Vary`, CDN cache keys, stale-while-revalidate.
- TLS 1.2 vs 1.3 handshake (1-RTT, 0-RTT), SNI, ALPN, mTLS, certificate chain & OCSP stapling.
- Cert expiry is a classic outage — monitor expiry as an SLI, alert at 30/14/7 days.

```bash
openssl s_client -connect host:443 -servername host < /dev/null | openssl x509 -noout -dates -subject -issuer
curl -vk https://host
```

## Load balancing

| Type | Layer | Sees | Use |
|---|---|---|---|
| Azure Load Balancer | L4 | TCP/UDP | Regional, high throughput |
| Application Gateway | L7 | HTTP | WAF, path routing, TLS termination |
| Azure Front Door | L7 global | HTTP | Global anycast, CDN, WAF |
| Traffic Manager | DNS | — | DNS-based global routing |
| Nginx/Envoy/HAProxy | L4/L7 | Both | Self-managed, service mesh data plane |

Algorithms: round robin, least connections, least request + **power of two choices**,
consistent hashing (ring hash / maglev) for cache affinity.

Health checks: active vs passive (outlier detection). Shallow (`/healthz`) vs deep (`/readyz`
checks dependencies). Deep checks can cascade failures — be careful.

## Subnetting (be fast at this)

```
/24 = 256 addrs (254 usable, Azure reserves 5)
/25 = 128   /26 = 64   /27 = 32   /28 = 16   /29 = 8   /30 = 4
10.0.0.0/16 split into /20s => 10.0.0.0, 10.0.16.0, 10.0.32.0 ...
```
RFC1918: `10/8`, `172.16/12`, `192.168/16`. CGNAT: `100.64/10`.
Avoid overlapping CIDRs — it blocks future peering. Plan AKS pod CIDRs carefully.

## NAT, proxies, and source IP

- SNAT port exhaustion is a top cloud outage cause (Azure Load Balancer outbound rules, NAT Gateway).
- Preserve client IP: `X-Forwarded-For`, PROXY protocol, or `externalTrafficPolicy: Local` in K8s.

## Troubleshooting flow: "Site is slow"

1. Is it DNS? `dig`, compare resolvers.
2. Is it the network path? `mtr` for loss/latency per hop.
3. Is it TLS? `curl -w` timing breakdown — `time_appconnect` gap.
4. Is it the LB or the backend? Compare LB-side vs app-side latency metrics.
5. Is it saturation? Check connection counts, queue depth, thread pools.
6. Is it one AZ/region/pod? Slice the metric by dimension.

## Interview questions

1. Type `https://example.com` and press Enter — describe everything, end to end.
2. What causes a 504 and how do you distinguish app slowness from LB misconfiguration?
3. Explain SNAT port exhaustion and three ways to fix it.
4. How would you do a zero-downtime DNS migration?
5. Why might a service be reachable from one subnet and not another? (NSG/UDR/route table/peering/NAT/firewall.)
6. Explain how a service mesh changes the network path and what it costs you.
