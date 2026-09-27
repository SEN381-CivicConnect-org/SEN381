#!/usr/bin/env bash
# Reverses every applied EF Core migration, leaving an empty database.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

dotnet tool restore
dotnet ef database update 0 \
  --project src/CivicConnect.Data "$@"
