#!/usr/bin/env bash
# Build self-contained single-file binaries of the offline signer for all
# supported platforms. Output goes to ./artifacts/signer/<rid>/ — one binary per
# RID, ready to be copied into the plugin repo under plugins/xrpl/bin/.
#
# Usage:
#   ./build-signer-binaries.sh             # all 5 platforms
#   ./build-signer-binaries.sh win-x64     # one platform
#
# Requires .NET 10 SDK on PATH.

set -euo pipefail

PROJECT="src/StaticBit.Xrpl.Mcp.Signer/StaticBit.Xrpl.Mcp.Signer.csproj"
OUT_BASE="artifacts/signer"

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
    bin_name="StaticBit.Xrpl.Mcp.Signer.exe"
  else
    bin_name="StaticBit.Xrpl.Mcp.Signer"
    chmod +x "$out_dir/$bin_name" 2>/dev/null || true
  fi

  size=$(du -h "$out_dir/$bin_name" 2>/dev/null | cut -f1 || echo "?")
  echo "   ✓ $bin_name ($size)"
done

echo
echo "Done. Artifacts under $OUT_BASE/."
