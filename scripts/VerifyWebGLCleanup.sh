#!/bin/sh
# Run on Linux/WSL. No network access; only fresh, named /tmp fixtures are touched.
set -eu
cleanup_script=$(cd -- "$(dirname -- "$0")" && pwd)/CleanWebGL.sh
fixtures=$(mktemp -d /tmp/aitools-delete-safety.XXXXXXXXXX)
checks=0
check() {
    "$@" || { echo "FAILED: $*" >&2; exit 1; }
    checks=$((checks + 1))
}
reject() {
    if "$@" > "$fixtures/rejected.log" 2>&1; then
        echo 'FAILED: unsafe cleanup was accepted' >&2; exit 1
    fi
    checks=$((checks + 1))
}
make_site() {
    mkdir -p "$1/www/aitools/Build" "$1/www/aitools/TemplateData" \
        "$1/www/aitools/beta/Build" "$1/www/aitools/beta/TemplateData" "$1/www/aitoolsbeta/Build"
    touch "$1/www/aitools/Build/old" "$1/www/aitools/TemplateData/old" \
        "$1/www/aitools/beta/Build/old" "$1/www/aitools/beta/TemplateData/old" \
        "$1/www/aitools/index.html" "$1/www/aitoolsbeta/Build/keep"
}

make_site "$fixtures/release"
make_site "$fixtures/beta with spaces"
make_site "$fixtures/working-directory"
cd "$fixtures/working-directory"
env HOME="$fixtures/release" sh "$cleanup_script" aitools RELEASE
check test ! -e "$fixtures/release/www/aitools/Build"
check test ! -e "$fixtures/release/www/aitools/TemplateData"
check test -e "$fixtures/release/www/aitools/beta/Build/old"
check test -e "$fixtures/release/www/aitools/index.html"
env HOME="$fixtures/beta with spaces" sh "$cleanup_script" aitools BETA
check test ! -e "$fixtures/beta with spaces/www/aitools/beta/Build"
check test ! -e "$fixtures/beta with spaces/www/aitools/beta/TemplateData"
check test -e "$fixtures/beta with spaces/www/aitools/Build/old"
check test -e "$fixtures/beta with spaces/www/aitoolsbeta/Build/keep"
check test -e "$fixtures/working-directory/www/aitools/Build/old"

for bad in '' ' ' ../aitools /aitools '*' 'a/b' 'aitools;false' 'aitools"'; do
    reject env HOME="$fixtures/release" sh "$cleanup_script" "$bad" RELEASE
done
for bad in '' ' ' / // relative "$fixtures/release/.."; do
    reject env HOME="$bad" sh "$cleanup_script" aitools RELEASE
done
reject env HOME="$fixtures/release" sh "$cleanup_script" aitools unknown

mkdir -p "$fixtures/outside" "$fixtures/linked-home" "$fixtures/nested/www/aitools/Build"
touch "$fixtures/outside/keep" "$fixtures/nested/www/aitools/Build/keep"
ln -s "$fixtures/outside" "$fixtures/linked-home/www"
ln -s "$fixtures/outside" "$fixtures/nested/www/aitools/Build/link"
ln -s "$fixtures/release" "$fixtures/home-link"
reject env HOME="$fixtures/linked-home" sh "$cleanup_script" aitools RELEASE
reject env HOME="$fixtures/nested" sh "$cleanup_script" aitools RELEASE
reject env HOME="$fixtures/home-link" sh "$cleanup_script" aitools RELEASE
check test -e "$fixtures/outside/keep"
check test -e "$fixtures/nested/www/aitools/Build/keep"

echo "PASS: $checks remote cleanup checks. Fixtures retained at $fixtures"
