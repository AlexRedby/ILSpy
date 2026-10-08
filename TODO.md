# TODO

## Publish the compact array producer to Game

- [ ] Regenerate raw source from the pinned 1.19.2 build
  `f7b5c81eb3c4d502` using the verified compact-array producer and existing
  tokenized settings. Keep the current raw source until the replay is verified.
- [ ] Reapply authored and rendered maps in a staging directory; verify binding
  coverage, exact identities, replay idempotence, and the two large payloads.
  Do not migrate versions, rebind selectors, or edit Game C# by hand.
- [ ] Activate the verified source and producer profile together, update local
  artifact paths, and commit the tools/Game changes independently. The active
  local producer path currently points at this mutable Debug build directory;
  freeze the accepted producer in a dedicated artifact directory before the
  next deobfuscation loop. Do not publish a PR.
