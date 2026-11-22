#!/usr/bin/env bash
# Prints the path of ILC, the NativeAOT compiler, downloading it first if needed.
# ILC ships as a NuGet package; restoring tools/ilc/ilc.csproj fetches it.
set -euo pipefail
cd "$(dirname "$0")/.."

case "$(uname -m)" in
    aarch64) rid=linux-arm64 ;;
    *)       rid=linux-x64 ;;
esac

find_ilc() {
    local pkgs
    pkgs=$(dotnet nuget locals global-packages -l | sed 's/^global-packages: //')
    ls -d "${pkgs}runtime.${rid}.microsoft.dotnet.ilcompiler"/*/tools/ilc 2>/dev/null | sort -V | tail -n 1
}

ilc=$(find_ilc || true)
if [ -z "$ilc" ]; then
    echo "Downloading the NativeAOT compiler (ILC), this happens once..." >&2
    dotnet restore tools/ilc/ilc.csproj -v quiet >&2
    ilc=$(find_ilc)
fi
echo "$ilc"
