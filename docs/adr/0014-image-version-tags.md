# ADR 0014: Image tags are `<version>-abi-<commit date>-<short commit>`

- Status: accepted
- Date: 2026-10-13
- Issue: #81

## Context

ADR 0013 deployed testnet with `main-<sha7>` and production with the release tag. Operators want a tag that tells at a glance which product line, which
version and which build an image is, without opening Git.

## Decision

- Tag = `<VERSION>-abi-<UTC date of the commit>-<7 character commit id>`, for example `1.0-abi-2026-10-13-abcdef1` (`abi` = avm balance indexer).
  `VERSION` is a file in the repository root with `major.minor`, raised by hand when a release changes the API or the stored data.
- The date is the commit's, not the build's, so rebuilding a commit reproduces the same tag and the tag stays immutable.
- Both environments deploy this tag. Production additionally pushes the release tag `vX.Y.Z` and `latest`; the release tag must match `VERSION` (`v1.0.N` for `1.0`), so one build cannot carry two different version numbers.
- The string is stored as the image label `org.opencontainers.image.version`.
- A rollback names the same tag; its commit (last segment) selects the chart and values that belong to it.

- An image whose tag already exists in the registry is not rebuilt: the release tag and `latest` are put on the existing image (`imagetools create`), so what ran on testnet is what reaches production.

## Consequences

- After `VERSION` is raised, release tags of the old line (`v1.0.N`) can still be deployed by image tag with the `ref` input, not by re-running the tag.

- Image tags from before this ADR (`main-<sha7>`) stay valid for rollbacks given with an explicit `ref`.
- Raising the major or minor version needs a commit that changes `VERSION` and a matching release tag.
