# Working instructions for agent teams

Several teams (Team A, Team B, Team C, ...) work on this repo at the same time. Each team handles **one GitHub issue
at a time**. Your team name comes from the prompt ("You are Team B"); if it is missing, ask once. Below, `<t>` is the
lower-case team letter, e.g. `team:b`.

Read first (in this order, skip what you already know): this file, `CONTRIBUTING.md`, the relevant section of
`PRODUCT-DESCRIPTION.md`, `docs/adr/`. Do not re-explore the repo from scratch; use the map below.

## Repo map

.NET 10, `AvmBalanceIndexer.slnx`, central package versions (`Directory.Packages.props`), warnings are errors.

| Project | Contains |
|---|---|
| `src/...Domain` | Provider-neutral contracts: `Storage/` (IChainStorage, IRoundWriter, stores), `Tokens/` (`Amount` is BigInteger-backed), `Effects/`, `Chains/` |
| `src/...Indexing` | `Adapters/` (Algorand block fetch via gossip, decode, hash/genesis verification), `Normalization/` (block -> token effects), `Ingestion/` (RoundIndexer, BalanceTracker, OrderedPrefetch) |
| `src/...Storage.*` | Providers (PostgreSQL done, Cassandra = issue #14). Provider-specific SQL/CQL stays here |
| `src/...Hosting`, `...Api`, `...Worker` | DI wiring, HTTP API, background indexer |
| `tests/...TestSupport` | `StorageContractTests` (shared suite every provider derives from), fakes, `InMemoryChainStorage`, `PostgresFixture` |
| `tests/...Storage.ContractTests` | One thin class per provider deriving from `StorageContractTests` (`InMemory...`, `PostgreSql...`); a new provider adds its class here and a CI service |
| `tests/...Live.Tests` | Network tests: first 1000 rounds of TestNet+MainNet vs. ledger deltas |
| `deploy/`, `docker/` | Helm chart + per-chain values, Dockerfiles (CI lints/builds them) |

Rules that are easy to break (details in CONTRIBUTING.md):
- Token quantities are `Amount`, never `long`/`double`/`decimal`.
- No SQL/CQL concepts in Domain. A storage behaviour change needs a test in `StorageContractTests.cs` (covers all providers).
- Network access in tests only in `Live.Tests` (`AVM_SKIP_LIVE_TESTS=1` skips it).
- New architectural decision -> new numbered ADR in `docs/adr/`.
- Ledger facts and oracles (rewards pool, genesis/block hashing, `/v2/deltas/{round}` oracle) are in
  [docs/ledger-facts.md](docs/ledger-facts.md); read it before touching normalization, hashing or live tests.

## Commands

```sh
dotnet build AvmBalanceIndexer.slnx
dotnet test AvmBalanceIndexer.slnx   # fast loop with AVM_SKIP_LIVE_TESTS=1; starts its own PostgreSQL container
dotnet test tests/AvmBalanceIndexer.Indexing.Tests --filter "FullyQualifiedName~Name"   # single test while doing TDD
dotnet test tests/AvmBalanceIndexer.Live.Tests                    # needed when balance logic changes
```
Set the variable per shell: bash `export AVM_SKIP_LIVE_TESTS=1`, PowerShell `$env:AVM_SKIP_LIVE_TESTS=1`.
Docker must be running for PostgreSQL-backed tests unless `AVM_TEST_POSTGRES` is set. CI = build+unit+contract,
live-network, docker builds, helm lint; all must be green to merge.

## Team isolation

- Work in your **own clone** per team (e.g. `../avm-team-b`); never share a working directory. If you use
  `git worktree` instead, `main` is checked out elsewhere: use `git switch -c <branch> origin/main` and skip local-main steps.
- Labels: `team:a`, `team:b`, ... mark ownership. Create the label if missing:
  `gh label create team:b --color 5319e7 --force` (labels `team:a`..`team:d` already exist). Put your team label on every issue and PR you create or claim.
- Branch names: `feature/<issue>-<slug>` or `fix/<issue>-<slug>` (the issue number keeps branches unique across teams).
- **Never commit or push to `main` directly**; every change, including docs and instruction files, goes through an
  issue -> branch -> PR -> review -> squash-merge.
- **Every commit title and every PR title contains the issue id**: `feat(storage): add Cassandra writer (#14)`. The PR
  body has `Closes #<n>`. Create an issue first even for small chores. Only Dependabot PRs are exempt.
- Commit trailer and PR footer follow the attribution lines given by the harness.

## Team assignments

Labels are the source of truth: `gh issue list --label team:<t>`. Initial split (2026-10-11): Team A = indexing / chain
correctness (#20, #23, #16, #18, #15); Team B = storage / serving (#14, #19, #22, #17). Teams C+ take unlabelled issues.
Within your list the order is the one given above (it follows dependencies), not simply the lowest number. Coordinate via
PR comments when #18 (normalizer) needs storage contract members that #14/#22 change.

## The loop (repeat until told to stop)

0. **Sync**: `git fetch origin` (and `git checkout main && git pull --ff-only` in a normal clone). Do this at the start of every iteration
   and again before opening a PR, before every review round, and before merging.
1. **Open PRs of your team first**: `gh pr list --label team:<t> --state open`. For each: rebase on `origin/main`,
   check CI (`gh pr checks <n>`), fix failures, address review comments, finish the review/merge steps below. Only when
   none are left, continue.
2. **Pick an issue**: `gh issue list --label team:<t> --state open`. Take the first one that is not blocked (use the order in "Team assignments", else lowest number).
   If none is assigned to your team, look for an **unclaimed** open issue (no `team:*` label), preferring those on the
   critical path of `PRODUCT-DESCRIPTION.md`.
   **Claim it immediately** by adding your label, then re-read the issue labels; if another team's label is also there,
   the lower team letter keeps it; the other team **removes its own label** and picks another
   issue. Dependabot PRs without a team label can be taken the same way, only when no feature issue is available; they
   are exempt from the issue-id/TDD rules (a version bump needs only green CI; keep the Dependabot title). Never
   push to `dependabot/*` branches; comment `@dependabot rebase` instead.
3. **If no issue exists**: create one (see "Creating issues"), label it with your team, then take it.
4. **Implement with TDD**: write a failing test first (smallest scope that proves the behaviour), see it fail for the
   right reason, make it pass, refactor. Every behaviour change needs tests: unit, shared contract test for storage,
   live test when balance logic, hashing, adapters or normalization change, Helm lint for chart changes. No untested code paths in the PR.
5. **Before the PR**: `git fetch && git rebase origin/main`, full build + tests (`AVM_SKIP_LIVE_TESTS=1` unless balance
   logic, hashing, adapters or normalization changed; CI runs the live job regardless), update docs/ADR if a decision changed. Push (after any rebase: `git push --force-with-lease`), open the PR with `Closes #<n>`, the team label, and the
   repo PR template.
6. **Code review loop**: run the `code-review` skill on the PR (use `high` or higher). Fix every real finding (with a
   test where the finding is a bug), push, rebase if main moved, review again. Repeat until a round returns nothing to
   change. A finding you reject gets a short PR comment explaining why and counts as resolved. If round 4 still
   produces only speculative or style findings, stop and note it in the PR; real bugs are never skipped.
7. **Merge**: if main moved and conflicts with you, rebase and force-push (CI restarts); otherwise just wait for green CI
   (`gh pr checks <n> --watch`), then
   `gh pr merge <n> --squash --delete-branch`. Confirm the issue is closed (`gh issue view <n>`); if not, close it with
   a comment linking the PR.
8. **Retrospective**: note what cost time (CI failures, review findings, merge conflicts, tooling quirks, wrong
   assumptions). If the lesson will make future work faster or safer, add it to the right place: repo-wide lessons in the
   "Lessons learned" section below (tiny separate PR), machine/tool quirks in memory. One line each,
   no duplicates, delete entries that became wrong.
9. Go to step 0.

## Creating issues

Sources: `PRODUCT-DESCRIPTION.md`, the current code (`git log`, open issues, TODOs, unimplemented sections), gaps found
in review, and a short web search for current best practice (Algorand/Voi node and block APIs, ARC-20/ARC-200,
Npgsql/Cassandra driver practices, indexer performance, Kubernetes). First check `gh issue list --state all` for
duplicates. Use `.github/ISSUE_TEMPLATE/feature.md` (Goal, Scope, Out of scope, Acceptance criteria incl. the tests that
prove it), size it to one PR, and add `enhancement` plus your team label. Cite sources found online in the issue.

## Conflict avoidance

- Small, focused PRs; merge quickly. Rebase often, never merge main into a branch.
- Hot files that collide: `Directory.Packages.props`, `StorageContractTests.cs`, `HostingExtensions.cs`, `Program.cs`,
  `AvmBalanceIndexer.slnx`, `ci.yml`, and the end of the "Lessons learned" list below
  (add lessons in a tiny separate PR, not inside feature PRs). Keep edits there minimal and rebase right before pushing.
- After a rebase conflict, re-run tests: a clean textual merge can still break behaviour.
- `git push --force-with-lease` only on your own feature branch, never on main, never on another team's branch.
- Do not edit another team's open PR or branch; comment on it instead.

## Tooling pitfalls (Windows)

- Bash tool: keep each call under ~150 lines and avoid triple-quoted Python or quote-heavy strings; write long content
  with the Write tool. Use PowerShell for `gh`/`dotnet` when quoting gets awkward; multi-line bodies via
  `--body-file` from a scratchpad file instead of inline strings.
- Run builds/tests with `-v q` or filter output (`| Select-Object -Last 40`) to save context.

## Lessons learned

(Append one line per lesson: what happened -> what to do. Keep it short.)

- Algorand5 SDK block model drops apply data and ignores HTTP status; the repo fetches raw block bytes and reads apply
  data itself. Do not "simplify" this back to the SDK model.
