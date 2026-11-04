# Git cheatsheet

```bash
git config --global pull.rebase true
git config --global fetch.prune true
git config --global rerere.enabled true
git config --global diff.algorithm histogram
git config --global init.defaultBranch main
git config --global core.hooksPath .githooks
```

## Inspect
```bash
git status -sb
git log --oneline --graph --decorate --all -20
git log --since='2 days ago' --author=rames --stat
git log --follow -p -- path/to/file          # across renames
git log -L 40,60:src/app.ts                  # history of a line range
git log -S'ConnectionString' --all           # pickaxe: added/removed a string
git log -G'regex' --all                      # pickaxe: diff matches a regex
git show <sha> --stat
git show <sha>:path/to/file
git blame -w -C -C -L 20,40 path/to/file     # ignore whitespace, follow moves
git shortlog -sne                            # commits per author
```

## Diff
```bash
git diff                       # working tree vs index
git diff --staged              # index vs HEAD
git diff HEAD                  # working tree vs HEAD
git diff main...feature        # feature changes since divergence
git diff --name-only main..HEAD
git diff --word-diff
git range-diff main..v1 main..v2   # compare two versions of a branch
```

## Branch / switch
```bash
git switch -c feature/x
git switch -                   # previous branch
git switch --detach <sha>
git branch -vv                 # upstream tracking + ahead/behind
git branch --merged main       # safe to delete
git branch -d old   /  -D old
git push origin --delete old
git fetch --prune
```

## Stage and commit
```bash
git add -p                     # interactively split a messy tree
git add -N newfile && git add -p
git commit -m "..." -m "body"
git commit --amend --no-edit   # only before pushing
git commit --fixup=<sha>
git rebase -i --autosquash origin/main
git commit -S -m "signed"
```

## Undo
```bash
git restore path               # discard working-tree change
git restore --staged path      # unstage
git reset --soft HEAD~1        # keep changes staged
git reset --mixed HEAD~1       # keep changes unstaged (default)
git reset --hard HEAD~1        # discard (reflog still has it)
git revert <sha>               # safe undo on shared branches
git revert -m 1 <merge-sha>    # revert a merge, keep parent 1
git clean -nd  /  -fd          # dry run, then delete untracked
```

## Rescue
```bash
git reflog --date=iso
git reset --hard HEAD@{3}
git branch rescue <sha>
git fsck --lost-found
git cat-file -p <sha>
cat .git/ORIG_HEAD
```

## Rebase / merge / conflicts
```bash
git rebase origin/main
git rebase --onto main release/1.4 feature/x
git rebase --continue / --skip / --abort
git merge --no-ff feature/x
git merge --abort
git mergetool
git checkout --ours path  /  --theirs path
git cherry-pick <sha> / -x <sha>       # -x records the source sha
git cherry-pick --continue / --abort
```

## Stash / worktree
```bash
git stash push -m "wip" -u             # -u includes untracked
git stash list / show -p stash@{1}
git stash pop / apply / drop
git worktree add ../hotfix release/1.4 # second checkout, same repo
git worktree list / remove ../hotfix
```

## Bisect
```bash
git bisect start
git bisect bad                 # current is broken
git bisect good v1.3.0
git bisect run ./repro.sh      # exit 0 = good, non-zero = bad, 125 = skip
git bisect reset
```

## Remotes / tags
```bash
git remote -v
git remote set-url origin <url>
git push -u origin HEAD
git push --force-with-lease    # never bare --force
git push --follow-tags
git tag -s v1.4.2 -m "release 1.4.2"
git tag -l 'v1.4.*' --sort=-v:refname
git describe --tags --always --dirty
git ls-remote --heads origin
```

## Big / CI clones
```bash
git clone --depth=1 --branch main --single-branch <url>
git clone --filter=blob:none --sparse <url>
git sparse-checkout set services/payments libs/common
git clone --mirror <url>       # backup mirror
git remote update --prune      # refresh the mirror
git gc --aggressive --prune=now
git count-objects -vH
```

## History rewrite (coordinate first)
```bash
pip install git-filter-repo
git filter-repo --invert-paths --path secrets/prod.env
git filter-repo --path services/payments --path-rename services/payments:
git push --force-with-lease --all && git push --force-with-lease --tags
```

## Submodules / LFS
```bash
git submodule add <url> libs/shared
git clone --recurse-submodules <url>
git submodule update --init --recursive --remote
git lfs install && git lfs track '*.pdf'
git lfs ls-files
```

## Useful one-liners
```bash
git for-each-ref --sort=-committerdate refs/heads --format='%(refname:short) %(committerdate:relative)'
git log --pretty='%h %an %ad %s' --date=short --no-merges
git diff --cached --name-only | xargs -r grep -nE 'password|secret|BEGIN .*PRIVATE KEY'
git rev-list --count main                      # commit count
git rev-list --left-right --count main...HEAD  # behind/ahead
git archive --format=zip -o src.zip HEAD
```
