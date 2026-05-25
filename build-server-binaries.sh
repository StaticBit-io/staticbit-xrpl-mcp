#!/usr/bin/env bash
# Build self-contained single-file binaries of the XRPL MCP server for all
# supported platforms. The same binary serves either transport ("--transport
# stdio" for the local plugin, "--transport http" for the cloud deployment).
#
# Output goes to ./artifacts/server/<rid>/, one binary per RID, ready to be
# copied into the plugin repo under plugins/xrpl-local/bin/.
#
# Usage:
#   ./build-server-binaries.sh             # all 5 platforms
#   ./build-server-binaries.sh win-x64     # one platform
#
# Requires .NET 10 SDK on PATH.

set -euo pipefail

PROJECT="src/StaticBit.Xrpl.Mcp.Server/StaticBit.Xrpl.Mcp.Server.csproj"
OUT_BASE="artifacts/server"

RIDS=(
  "win-x64"
  "linux-x64"
  "linux-arm64"
  "osx-x64"
  "osx-arm64"
)

if [ $# -gt 0 ]; then
  RIDS=("$@")
fi

mkdir -p "$OUT_BASE"

for rid in "${RIDS[@]}"; do
  out_dir="$OUT_BASE/$rid"
  echo "── Publishing $rid → $out_dir"
  rm -rf "$out_dir"

  dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    --output "$out_dir" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=embedded \
    --nologo \
    --verbosity quiet

  if [[ "$rid" == win-* ]]; then
    bin_name="StaticBit.Xrpl.Mcp.Server.exe"
  else
    bin_name="StaticBit.Xrpl.Mcp.Server"
    chmod +x "$out_dir/$bin_name" 2>/dev/null || true
  fi

  size=$(du -h "$out_dir/$bin_name" 2>/dev/null | cut -f1 || echo "?")
  echo "   ✓ $bin_name ($size)"
done

echo
echo "Done. Artifacts under $OUT_BASE/."
