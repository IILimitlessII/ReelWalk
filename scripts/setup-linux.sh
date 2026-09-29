#!/usr/bin/env bash
# Install .NET 10 SDK for ReelWalk development on Linux and build Debug.
# Usage: ./scripts/setup-linux.sh

set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    echo "Installing .NET 10 SDK to $DOTNET_ROOT ..."
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel 10.0
    export DOTNET_ROOT
    export PATH="$DOTNET_ROOT:$PATH"
    if ! grep -q 'DOTNET_ROOT' "$HOME/.bashrc" 2>/dev/null; then
        {
            echo ''
            echo '# .NET SDK'
            echo "export DOTNET_ROOT=\"\$HOME/.dotnet\""
            echo 'export PATH="$HOME/.dotnet:$PATH"'
        } >> "$HOME/.bashrc"
    fi
fi

export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

echo "dotnet version: $(dotnet --version)"

if ! dpkg -l libvlc5 >/dev/null 2>&1; then
    echo "LibVLC not found. Install with: sudo apt install libvlc5 vlc-plugin-base"
fi

cd "$ROOT"
dotnet restore ReelWalk.slnx
dotnet build src/ReelWalk/ReelWalk.csproj -c Debug

echo
echo "Done. In Cursor/VS Code:"
echo "  1. Install recommended extensions when prompted (C# / Avalonia)."
echo "  2. Open Run and Debug, choose 'ReelWalk (Linux)', press Play (F5)."
echo "  3. First run creates ReelWalk.toml next to the Debug binary; add folder paths and F5 again."
