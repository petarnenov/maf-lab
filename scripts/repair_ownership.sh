#!/usr/bin/env bash
# make up: gives back to the user who runs make whatever earlier versions of the stack left owned by root in the
# checkout — the api ran as root and wrote git objects, refs, reflogs and merged files (DECISIONS.md §70).
# Only paths owned by uid 0, only to MAF_LAB_UID:MAF_LAB_GID, never through a symlink, and only when the host's own
# `find` reports one, so on Docker Desktop (where nothing on the host is root's) it never starts a container.
# Usage: scripts/repair_ownership.sh <dir>...   (bash 3.2 compatible)
set -uo pipefail
uid="${MAF_LAB_UID:-$(id -u)}"
gid="${MAF_LAB_GID:-$(id -g)}"
IMAGE="${REPAIR_IMAGE:-alpine:3.22}"

if [ "$uid" = "0" ]; then
  echo "✓ ownership: the stack runs as root here, nothing to repair"
  exit 0
fi

# Distinct existing directories, a directory inside another one dropped (the e2e clone lives under the checkout).
roots=()
for dir in "$@"; do
  [ -d "$dir" ] || continue
  dir="$(cd "$dir" && pwd -P)"
  covered=0
  for other in "$@"; do
    [ -d "$other" ] || continue
    other="$(cd "$other" && pwd -P)"
    if [ "$other" != "$dir" ] && [ "${dir#"$other"/}" != "$dir" ]; then covered=1; fi
  done
  dup=0
  for seen in ${roots[@]+"${roots[@]}"}; do [ "$seen" = "$dir" ] && dup=1; done
  [ "$covered" = 0 ] && [ "$dup" = 0 ] && roots+=("$dir")
done

list="$(mktemp)"
trap 'rm -f "$list"' EXIT
total=0
failed=0
for root in ${roots[@]+"${roots[@]}"}; do
  # A root-only directory hides what is inside it until it is repaired, so look again (at most three passes).
  for pass in 1 2 3; do
    # node_modules is never written by a container and is the bulk of the tree.
    find "$root" -xdev -name node_modules -prune -o -user 0 -print0 2>/dev/null >"$list"
    count=$(tr -cd '\0' <"$list" | wc -c | tr -d ' ')
    [ "$count" = 0 ] && break
    echo "▸ ownership: $count path(s) under $root are owned by root; giving them to $uid:$gid (pass $pass)"
    tr '\0' '\n' <"$list" | head -5 | sed 's/^/    /'
    [ "$count" -gt 5 ] && echo "    … and $((count - 5)) more"
    # The directory at its own path, so the listed paths mean the same inside; chown -h changes a link, not its target.
    if docker run --rm -i --user 0:0 --network none -v "$root:$root" "$IMAGE" xargs -0 chown -h "$uid:$gid" <"$list"; then
      total=$((total + count))
    else
      failed=1
      echo "⚠ ownership: could not repair $root; run: sudo find '$root' -xdev -user 0 -exec chown -h $uid:$gid {} +"
      break
    fi
  done
done

if [ "$failed" = 1 ]; then
  echo "⚠ ownership: repaired $total path(s), some could not be repaired (${SECONDS}s)"
elif [ "$total" = 0 ]; then
  echo "✓ ownership: nothing owned by root in ${roots[*]:-the checkout}"
else
  echo "✓ ownership: repaired $total root-owned path(s) to $uid:$gid (${SECONDS}s)"
fi
exit 0
