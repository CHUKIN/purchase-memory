#!/usr/bin/env bash
set -euo pipefail

dotnet_root="${DOTNET_ROOT:-$HOME/.dotnet}"
dotnet_executable="$dotnet_root/dotnet"

if [[ ! -x "$dotnet_executable" ]]; then
  echo "Required .NET SDK was not found at $dotnet_executable." >&2
  echo "Install the SDK version from global.json or set DOTNET_ROOT." >&2
  exit 1
fi

exec "$dotnet_executable" "$@"
