# 0019 Relays serve the blocks near the chain tip

Status: accepted (issue #141)

## Context
The fetcher used only archival gossip nodes (DNS SRV, four per public network). When the indexer has caught up it asks
for one new block every ~3 seconds, and every request still went to those four nodes, the scarcest and most rate-limited
part of the fleet. Relays (7 on TestNet) answer `/v1/{genesisId}/block/{round}` for recent blocks too, but they prune
old ones and answer 404 for them.

## Decision
- `IGossipHostProvider.GetRelayHostsAsync` returns the relays (SRV purpose `Relay`); discovery is cached and fails
  independently of the archival list. A failed lookup means "no relays", never a failed fetch.
- `IBlockFetcher.FetchBlockAsync(round, latestRound, ct)` tells the fetcher where the tip is. A round within
  `Algorand:RecentRoundsWindow` (default 100, 0 = off) of it is fetched from archival nodes and relays together, ordered by
  the same latency-weighted selection; older rounds, and calls without a tip, use archival nodes only.
- `RoundIndexer` passes the tip (the algod latest round) only for unbounded passes that start within
  `Indexer:FollowingThresholdRounds` of it. A bounded run's target may be far behind the real tip, and a long backfill pass
  outlives its target, where a relay would only answer 404.
- Operator-configured `Algorand:GossipHosts` are used as given; relay discovery is skipped for them.
- Relays are optional capacity and never decide the health of a fetch. Within one fetch a relay is asked at most once;
  a relay that lacks the block, fails or rate limits is left out for the rest of that fetch without using up an attempt
  (after two failing relays all of them are), is not counted towards the throttle timeout and does not count towards the
  "block not out yet" decision, which only archival answers make. Failures cool the relay down and a miss lowers its
  latency score like any host's.

## Consequences
Tip following is spread over about 11 nodes instead of 4. Relays keep a bounded history, so the window must stay well below
it.
