# 13 — Git

Version control depth expected of a senior SRE: recovering broken repositories, keeping history
reviewable, and wiring Git into pipelines and GitOps safely.

## Mental model

- Git stores **snapshots**, not diffs. Four object types, all content-addressed by SHA-1/SHA-256:
  - **blob** — file contents, **tree** — directory listing, **commit** — tree + parents + metadata,
    **tag** — annotated tag object.
- A **branch** is a mutable pointer to a commit. `HEAD` points at a branch (attached) or a commit (detached).
- The three areas: **working tree** → **index/staging** → **repository**. `git status` tells you which
  boundary a change has crossed.
- Nothing is lost until garbage collection: every ref update is recorded in the **reflog**
  (default 90 days for reachable, 30 for unreachable).
- `.git/` layout worth knowing: `HEAD`, `refs/heads`, `refs/remotes`, `objects/`, `packed-refs`, `hooks/`.

```bash
git cat-file -t <sha>          # object type
git cat-file -p HEAD           # commit contents: tree, parent, author
git rev-parse HEAD~2
git ls-tree -r HEAD --name-only
```

## Branching and release strategies

| Strategy | Shape | Use when |
|---|---|---|
| **Trunk-based** | Short-lived branches (<1 day) merged to `main`, release via feature flags | High deploy frequency, strong CI, the default for SRE-owned services |
| **GitHub flow** | `main` + feature branches + PR, deploy from `main` | Small teams, continuous delivery |
| **GitLab flow** | `main` + environment branches (`staging`, `production`) | Promotion gates between environments |
| **Git flow** | `develop`, `release/*`, `hotfix/*`, `main` | Versioned, shipped-to-customer products; too heavy for services |

- Protect `main`: required reviews, required status checks, linear history, no force push, CODEOWNERS.
- Tag releases with **annotated, signed** tags (`git tag -s v1.4.2 -m ...`); lightweight tags carry no metadata.
- Release branches exist to stabilise, not to develop. Cherry-pick fixes **from** `main`, never the reverse.

## Rebase vs merge

- **Merge** preserves true history and is safe on shared branches. `--no-ff` keeps the feature boundary visible.
- **Rebase** rewrites commits (new SHAs) to produce a linear history. Only rebase branches nobody else uses.
- The golden rule: **never rewrite published history** that others have based work on.
- `git pull --rebase` avoids noisy "Merge branch 'main' into main" commits; set it globally.
- **Squash merge** gives one commit per PR — clean `main`, but loses intermediate commits and makes
  `git bisect` coarser. Fine for most app repos, painful for large refactors.
- `git rerere` records conflict resolutions so repeated rebases of a long-lived branch stop hurting.

```bash
git config --global pull.rebase true
git config --global rerere.enabled true
git rebase -i origin/main          # reword, squash, fixup, drop, edit
git rebase --onto main release/1.4 feature/x
```

## Recovery and archaeology

This is what interviewers actually probe. Know the recovery path for each failure.

| Situation | Fix |
|---|---|
| Reset/rebase destroyed work | `git reflog` → `git reset --hard HEAD@{n}` or `git branch rescue <sha>` |
| Committed to the wrong branch | `git reset --soft HEAD~1`, switch branch, commit again |
| Need to undo a pushed commit | `git revert <sha>` — never `reset --hard` + force push on a shared branch |
| Force push clobbered a colleague | Their commits are in `git reflog` on their clone, or in the remote's reflog / `ORIG_HEAD` |
| Which commit broke it? | `git bisect start; git bisect bad; git bisect good <tag>; git bisect run ./test.sh` |
| Who changed this line and why? | `git log -L 10,20:path/file`, `git blame -w -C -C`, `git log -S'secretFn'` |
| Dangling objects after a bad gc | `git fsck --lost-found`, objects land in `.git/lost-found` |
| Detached HEAD with commits | `git switch -c rescue` before switching away |

```bash
git reflog --date=iso
git log --oneline --graph --decorate --all
git log --all --source -S'ConnectionString' -- '*.cs'   # pickaxe: when was a string added/removed
git diff main...feature          # changes on feature since it diverged (three dots)
git show <sha>:path/to/file      # file as of that commit
```

## Keeping history reviewable

- Conventional commit subjects and a body that explains **what and why**, not how.
- One logical change per commit; `git add -p` to split a messy working tree.
- `git commit --fixup=<sha>` + `git rebase -i --autosquash` to fold review fixes into the right commit.
- `.gitattributes` for line endings (`* text=auto`), binary markers, and `merge=ours` on generated files.
- Large binaries → **Git LFS** or an artifact feed. A 2 GB repo with 500 MB of PDFs makes every clone slow.
- Ignore generated output; never ignore lock files (`.terraform.lock.hcl`, `package-lock.json`).

## Submodules, subtrees, monorepos

- **Submodule** — a pinned pointer to another repo. Exact, but every clone needs
  `--recurse-submodules` and contributors forget to update the pointer.
- **Subtree** — vendored copy inside your tree. No extra clone step, messier history.
- **Monorepo** — one repo, path-filtered pipelines. Needs sparse-checkout and partial clone to stay fast:

```bash
git clone --filter=blob:none --sparse <url>
git sparse-checkout set services/payments libs/common
git clone --depth=1 --branch main <url>        # CI shallow clone
```

## Git in CI/CD and GitOps

- CI should use a **shallow, single-branch** clone and a scoped token, not a full clone with a PAT that
  can push to `main`.
- Build provenance: stamp the image/artifact with the commit SHA; never with just a branch name.
- GitOps (Argo CD / Flux): the cluster pulls desired state from Git. Git becomes the audit log and the
  rollback mechanism — `git revert` *is* the rollback. No cluster credentials in CI.
- Mirror/backup: `git clone --mirror` + scheduled `git remote update` into a second remote. A repo with
  one remote and no mirror is a single point of failure.
- Merge queues serialise "green on main" checks and prevent semantic conflicts between concurrently
  merged PRs.

## Security

- **Signed commits/tags** (GPG, SSH, or Sigstore `gitsign`) + branch protection requiring signatures.
- Secret scanning in pre-commit **and** server side — client-side hooks are advisory and easy to skip.
- A pushed secret is **compromised**: rotate first, scrub second. Scrubbing with
  `git filter-repo` (preferred over `filter-branch`) rewrites every SHA, so coordinate the force push,
  re-clone everywhere, and purge forks/caches.
- `core.fsmonitor`, hooks, and `.git/config` are executable attack surface — never run
  `git` commands against an untrusted repo with hooks enabled.

```bash
git filter-repo --invert-paths --path secrets/prod.env
git commit -S -m "..."            # sign
git log --show-signature -3
```

## Interview questions with model answers

**Q: Rebase or merge — what do you use and why?**
Rebase locally to keep my branch current and linear while it's still private; merge (or squash merge)
into `main` through a PR. The rule is never to rewrite anything already pushed to a shared branch,
because everyone else's clone then diverges and the next pull creates duplicated commits. For
long-lived branches I enable `rerere` so repeated conflict resolutions are replayed automatically.

**Q: Someone force-pushed over main and deleted two days of commits. Walk me through recovery.**
First stop further pushes — lock the branch. The commits still exist: find the previous tip in the
remote's reflog if the server exposes it, otherwise from any clone or CI agent that fetched before the
push (`git reflog`, `ORIG_HEAD`, or `git fsck --lost-found`). Create a rescue branch at that SHA, verify
with `git log --oneline` that the expected commits are back, then fast-forward `main` to it via a normal
push. Afterwards: enable branch protection with force-push denied, and add a mirror backup. The real
fix is that `main` was pushable at all.

**Q: A bug appeared somewhere in the last 200 commits. How do you find it?**
`git bisect` with an automated test: `git bisect start`, mark the current tip bad and a known-good tag
good, then `git bisect run ./repro.sh`. That's ~8 steps instead of 200. If the repo uses squash merges
the granularity is per-PR, which usually still narrows it enough. If the test is flaky, bisect on a
deterministic reproducer first — bisect amplifies flakiness.

**Q: How do you handle a secret that was committed and pushed?**
Treat it as compromised and rotate it immediately — that's the only step that actually restores
security. Then scrub: `git filter-repo --invert-paths --path <file>`, force-push all refs, have everyone
re-clone, delete forks and ask the host to purge cached views, and invalidate any build caches that
contain the old objects. Finally, add server-side secret scanning and a pre-commit hook so it can't
recur, and move the value into Key Vault referenced by the pipeline.

**Q: What's the difference between `git reset --soft`, `--mixed` and `--hard`?**
All three move the branch pointer. `--soft` leaves the index and working tree untouched, so the changes
are staged and ready to recommit. `--mixed` (the default) resets the index but keeps the working tree,
so changes are unstaged. `--hard` resets both and discards uncommitted work — the only one that loses
data, and even then committed work is recoverable via the reflog.

**Q: `git revert` vs `git reset` on a shared branch?**
`revert` creates a new commit that undoes a change, so history is append-only and everyone's clone stays
consistent — that's what you use on `main`. `reset` rewrites the branch pointer and requires a force
push, which breaks every other clone. The only time I reset a shared branch is during an incident where
the content itself is dangerous, with the team notified and the branch locked first.

**Q: What does `git pull` actually do, and why do people get surprised by it?**
It's `git fetch` followed by `git merge` (or `rebase` with `pull.rebase`). The surprise is the implicit
merge commit when the local and remote branches have both moved, which litters history with "Merge
branch 'main' of ..." commits. I set `pull.rebase=true` and `fetch.prune=true` globally, and in scripts
I use explicit `fetch` + `merge --ff-only` so the failure is loud rather than silent.

**Q: How do you keep a 10 GB monorepo usable in CI?**
Partial and shallow clone (`--filter=blob:none --depth=1 --branch <ref>`), sparse-checkout limited to
the paths the job needs, and path-filtered pipeline triggers so unrelated changes don't build
everything. Keep binaries in LFS or an artifact feed rather than in tree, run `git maintenance`/`gc` on
the server, and cache the `.git` directory between runs where the agent allows it.

**Q: What are the risks of client-side hooks for policy enforcement?**
They live in `.git/hooks`, aren't versioned, aren't distributed by clone, and anyone can bypass them
with `--no-verify`. They're a fast feedback mechanism, not a control. Real enforcement has to be
server-side: protected branches, required status checks, required reviews, required signatures, and
push rules. I use `core.hooksPath` plus a tool like pre-commit so hooks are at least versioned and
installable, but I never rely on them as the only gate.

**Q: How do you structure branching for a service that deploys 20 times a day?**
Trunk-based. Branches live hours, not days; everything merges to `main` behind feature flags; `main` is
always releasable and every merge produces a deployable artifact tagged with the SHA. Release branches
only appear if we need to support an old version for a customer. Long-lived branches are the thing that
makes merges painful, so the strategy is built around not having them.

**Q: Explain `git cherry-pick` and when it's the wrong tool.**
It replays a commit's diff onto another branch as a new commit. It's right for pulling a hotfix from
`main` into a release branch. It's wrong as a routine integration method: you get duplicated logical
changes with different SHAs, which confuses `git log`, makes later merges conflict, and hides that the
branches have diverged. If I'm cherry-picking a lot, the branching model is wrong.

## Hands-on drills

1. Break a repo deliberately (`reset --hard`, bad rebase, force push) and recover each one from reflog.
2. Write a `repro.sh` and drive `git bisect run` end to end against a seeded bug.
3. Take a 40-commit messy branch and turn it into 4 reviewable commits with interactive rebase.
4. Commit a fake secret, then remove it with `git filter-repo` and verify with `git log -S`.
5. Configure a repo with signed commits + protected branch and confirm an unsigned push is rejected.

## Reading

- *Pro Git* (Scott Chacon, free online) — chapters 7 and 10 are the ones that matter.
- `git help revisions`, `git help everyday` — short and consistently underread.
- Git internals: "Git from the bottom up".
- `git-filter-repo` documentation, for the history-rewrite path.
