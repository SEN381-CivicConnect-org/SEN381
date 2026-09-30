#!/usr/bin/env bash
# Applies every pending EF Core migration to CONNECTION_STRING's database.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

dotnet tool restore
dotnet ef database update \
  --project src/CivicConnect.Data "$@"
