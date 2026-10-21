#!/usr/bin/env bash
set -euo pipefail
mode="${1:-aot}"
case "$mode" in
  aot) profile="NativeAot" ;;
  singlefile) profile="SingleFile" ;;
  *) printf 'Usage: bash build.sh [aot|singlefile]\n' >&2; exit 2 ;;
esac
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
command -v dotnet >/dev/null || { printf '.NET 8 SDK is required.\n' >&2; exit 1; }
dotnet publish "$script_dir/Loggepector.csproj" -c Release -r linux-x64 --self-contained true \
  -p:PublishProfile="$profile" -o "$script_dir/artifacts/linux-x64/$mode"
printf 'Output: %s/artifacts/linux-x64/%s\n' "$script_dir" "$mode"
