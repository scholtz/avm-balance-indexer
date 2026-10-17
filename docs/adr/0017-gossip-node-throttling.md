# ADR 0017: Adaptive per-node throttling of archival gossip requests

- Status: accepted
- Date: 2026-10-16
- Issue: #120

## Context

A backfill downloads millions of blocks from a handful of public archival gossip nodes. `Indexer:FetchConcurrency` defaults to 128 and the fetcher
prefers the hosts that answer fastest (issue #59, ADR 0011 on batched round commits), so the favourite of four nodes received most of ~128 simultaneous requests. The nodes answer `429 Too Many
Requests` (some with `Retry-After`). The fetcher treated that as an ordinary failure: it used up one of the `MaxAttempts`, set a fixed 30 s cooldown and logged a
warning with a stack trace per request, and when several nodes throttled together the fetch failed and the worker eventually stopped.

## Decision

- Each node has a **gate** (`HostGate`) that limits the requests in flight. The limit adapts like TCP congestion control: it **halves** when the node rate-limits and
  **grows by about one request per limit's worth of successes** up to `Algorand:MaxConcurrentRequestsPerHost` (32, so that 128 fetches over four nodes still fit).
  The node tells us what it tolerates; no figure has to be guessed per node or per network.
- A `429`, or a `503` with `Retry-After`, **pauses** the node: for its `Retry-After` (seconds or HTTP date), and when a node keeps rate-limiting for at least as long as
  1 s, 2 s, 4 s and so on, at most `Algorand:MaxThrottleDelay` (60 s). A plain `503` stays an ordinary error with the existing cooldown.
- A response belongs to the **episode** that is already running when its request was admitted before the pause ended: it does not halve the limit or lengthen the back-off
  again, so a burst of 128 simultaneous 429s costs the node one step. Only a request admitted after the pause counts as new evidence.
- A rate-limited answer **does not use up an attempt**: the request goes to the next node that is neither paused nor full (load spills over instead of queueing behind
  the favourite), or waits for the earliest pause to end. The time a fetch spends on rate-limited requests and waiting is bounded by `Algorand:ThrottleTimeout` (5 min); then it
  fails like any fetch failure, which the worker retries with its back-off and, after `MaxConsecutiveFailures`, escalates to a stop.
- The log has no warning per request and no line per pause: each pause is a debug line, and `Algorand:ThrottleLogInterval` (1 min) turns the pauses of all nodes into one information summary (the first pause is reported at once; held-back pauses are flushed when the next fetch completes or a fetch fails with a throttle timeout). Issue #122.
- The gate remembers the level at which a node last rate-limited and grows eight times slower between three quarters and 1.25 times that level (quickly again below and above), so the limit probes the node's tolerance instead of saw-toothing into the next 429.

## Consequences

- Throughput follows what the nodes allow, without operator tuning; operators with their own nodes can raise `MaxConcurrentRequestsPerHost` to the node's capacity.
- A node that never recovers keeps a limit of 1 and long pauses; the other nodes carry the load. If all do, fetches wait and finally fail after `ThrottleTimeout`.
- When more fetches are in flight than the nodes' limits allow, the surplus polls every 10 ms for a free slot. That costs a lock-protected check per waiter and wakeup,
  a few thousand per second at most; a queue with release signals would remove it but is not worth its complexity at this scale.
- The state lives in memory per worker process and starts at the maximum again after a restart.
