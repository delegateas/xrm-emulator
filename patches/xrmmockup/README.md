# XrmMockup patches

Local changes carried on top of the pinned `external/XrmMockup` submodule.

XrmMockup is upstream (`delegateas/XrmMockup`) and changes there wait on the owner's review, but the
emulator depends on a handful of behaviour fixes to run real plugins. Keeping them as working-tree
edits does not survive `git submodule update`, and an unpatched XrmMockup does not fail loudly — it
answers differently. So they live here as patch files, re-applied by
[`scripts/xrmmockup-patches`](../../scripts/xrmmockup-patches).

```bash
scripts/xrmmockup-patches apply     # re-apply everything (idempotent)
scripts/xrmmockup-patches status    # what is applied, missing or conflicting
scripts/xrmmockup-patches check     # read-only; non-zero exit if anything is missing
scripts/xrmmockup-patches save 005-my-fix src/XrmMockup365/Foo.cs   # capture a new change
```

## Making a change to XrmMockup

1. Edit the file under `external/XrmMockup/` as normal.
2. `scripts/xrmmockup-patches save NNN-short-name <paths…>` — the delta is taken against the
   submodule's HEAD *plus the patches already saved*, so a file touched by an earlier patch yields
   only your new hunks.
3. Add a header to the patch file: what it fixes, what depends on it, and whether it is
   upstream-worthy. Headers survive a re-`save` of the same patch.

Patch order matters — the numeric prefix is the sequence, and more than one patch may touch the
same file.

## When the submodule is bumped

`BASE` records the commit the patches were authored against. If the submodule moves, `apply` and
`status` warn, and any patch that no longer applies has to be rebased onto the new upstream code.
Update `BASE` once they all apply again.

Each patch is written to be offerable upstream as-is. When one lands in `delegateas/XrmMockup`,
delete the file here after the submodule is bumped past it.
