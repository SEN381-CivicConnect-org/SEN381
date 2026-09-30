# SEN381

## Database

```sh
docker compose up -d db
cp .env.example .env   # fill CONNECTION_STRING to match docker-compose.yml's db service
./scripts/migrate-up.sh
```

`./scripts/migrate-down.sh` reverses every migration back to an empty database. Both scripts read `CONNECTION_STRING` from the environment (see `.env.example`); CI sets it directly rather than using `.env`.

Schema conventions and the tooling decisions behind this are recorded in `docs/decisions/`.