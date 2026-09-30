#!/usr/bin/env sh
# Publishes standalone ReelWalk binaries into ./build, then removes bin/ and obj/.
# Usage:
#   ./scripts/build.sh           both win-x64 and linux-x64
#   ./scripts/build.sh --windows only Windows
#   ./scripts/build.sh --linux   only Linux
#   ./scripts/build.sh -w | -l   short forms
#   ./scripts/build.sh --help
#
# Note: Windows cannot keep both "ReelWalk" and "ReelWalk.exe" in the same folder,
# so each target goes under its own subdirectory.

set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
PROJECT="$ROOT/src/ReelWalk/ReelWalk.csproj"
OUT="$ROOT/build"
DO_WIN=0
DO_LINUX=0

PUBLISH_FLAGS="-c Release --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false"

usage() {
    cat <<'EOF'
Usage: ./scripts/build.sh [options]

  (default)     Build Windows and Linux standalone binaries
  -w, --windows Build only win-x64
  -l, --linux   Build only linux-x64
  -h, --help    Show this help

Outputs:
  build/win-x64/ReelWalk.exe
  build/linux-x64/ReelWalk

Project bin/ and obj/ folders are removed afterward.
EOF
}

for arg in "$@"; do
    case "$arg" in
        -w|--windows) DO_WIN=1 ;;
        -l|--linux) DO_LINUX=1 ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $arg" >&2
            usage >&2
            exit 1
            ;;
    esac
done

if [ "$DO_WIN" -eq 0 ] && [ "$DO_LINUX" -eq 0 ]; then
    DO_WIN=1
    DO_LINUX=1
fi

if [ ! -f "$PROJECT" ]; then
    echo "Project not found: $PROJECT" >&2
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK not found on PATH" >&2
    exit 1
fi

mkdir -p "$OUT"

publish_one() {
    rid=$1
    name=$2
    dest_dir="$OUT/$rid"
    echo "Publishing $rid..."
    # shellcheck disable=SC2086
    dotnet publish "$PROJECT" -r "$rid" $PUBLISH_FLAGS
    src="$ROOT/src/ReelWalk/bin/Release/net10.0/$rid/publish/$name"
    if [ ! -f "$src" ]; then
        echo "Publish output missing: $src" >&2
        exit 1
    fi
    mkdir -p "$dest_dir"
    # Remove any same-folder name clash leftovers from older builds.
    rm -f "$OUT/$name" "$dest_dir/$name"
    cp -f "$src" "$dest_dir/$name"
    if [ "$rid" = "linux-x64" ]; then
        chmod +x "$dest_dir/$name" 2>/dev/null || true
    fi
    echo "  -> $dest_dir/$name"
}

if [ "$DO_WIN" -eq 1 ]; then
    publish_one win-x64 ReelWalk.exe
fi

if [ "$DO_LINUX" -eq 1 ]; then
    publish_one linux-x64 ReelWalk
fi

echo "Removing bin and obj..."
rm -rf "$ROOT/src/ReelWalk/bin" "$ROOT/src/ReelWalk/obj"

echo "Done. Binaries are in $OUT"
if [ "$DO_WIN" -eq 1 ]; then
    ls -la "$OUT/win-x64"
fi
if [ "$DO_LINUX" -eq 1 ]; then
    ls -la "$OUT/linux-x64"
fi
