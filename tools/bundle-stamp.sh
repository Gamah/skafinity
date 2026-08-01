#!/usr/bin/env sh
# Records WHICH SOURCES the committed web/_framework was built from, so CI can catch the one
# mistake the Pages deploy cannot survive: an engine change committed without re-staging the
# bundle. The site would keep serving the old engine while master claimed the new one, and
# nothing would 404 or fail — it is silent, which is why it needs a machine to notice.
#
# The stamp is `<kind> <hash>`: a sha256 over the content AND path of every source the wasm
# build compiles. `make stage` writes it, `make check-bundle` verifies it, and both use this
# one script so the two can never disagree about what is hashed.
#
# `kind` is aot or dev, because `make dev` stages an interpreted runtime that is functionally
# identical but slow in a browser. Fresh but interpreted is still not shippable, so the check
# demands aot.
#
# What this proves: the bundle was staged from these sources. What it does NOT prove: that the
# bundle's bytes came from that build (the stamp is written beside it, not derived from it). It
# is a guard against forgetting, not against tampering.
set -eu

cd "$(dirname "$0")/.."
STAMP=web/.bundle-stamp

# Sorted under LC_ALL=C so the digest is stable across machines and locales. sha256sum prints
# "<hash>  <path>", so a rename changes the digest even when no content does.
compute() {
	{
		find sbox-library/Skafinity/Code/Engine -type f -name '*.cs'
		echo wasm/Exports.cs
		echo wasm/Skafinity.Wasm.csproj
		echo wasm/runtimeconfig.template.json
	} | LC_ALL=C sort | xargs sha256sum | sha256sum | cut -d' ' -f1
}

case "${1:-}" in
write)
	kind="${2:-aot}"
	printf '%s %s\n' "$kind" "$(compute)" > "$STAMP"
	echo "stamped web/_framework ($kind, $(cut -d' ' -f2 "$STAMP" | cut -c1-12))"
	;;
check)
	if [ ! -f "$STAMP" ]; then
		echo "web/.bundle-stamp is missing — cannot tell whether web/_framework matches the engine." >&2
		echo "Run 'make' (a full AOT publish) and commit the re-staged bundle." >&2
		exit 1
	fi
	kind=$(cut -d' ' -f1 "$STAMP")
	have=$(cut -d' ' -f2 "$STAMP")
	want=$(compute)
	if [ "$have" != "$want" ]; then
		echo "web/_framework is STALE: the engine sources have changed since it was staged." >&2
		echo "  stamped: $have" >&2
		echo "  sources: $want" >&2
		echo "The deployed site would serve the OLD engine. Run 'make' and commit web/_framework." >&2
		exit 1
	fi
	if [ "$kind" != "aot" ]; then
		echo "web/_framework was staged by 'make dev' ($kind) — an interpreted runtime." >&2
		echo "It is fresh but slow in a browser. Run 'make' for a full AOT publish before committing." >&2
		exit 1
	fi
	echo "web/_framework is current ($kind, ${want}) "
	;;
*)
	echo "usage: $0 write [aot|dev] | check" >&2
	exit 2
	;;
esac
