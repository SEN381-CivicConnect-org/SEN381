# 0002: PostgreSQL as the database engine

**Status:** Accepted
**Date:** 2026-09-27

Closes FEC-01 (Database Persistence and Data Design), deferred in the PED to Milestone 2.

## Decision

CivicConnect uses PostgreSQL as its database engine, one database per organisation deployment.

## Why

- The Milestone 2 ERD (`../../M2/ERD@2x.png`) is drawn against PostgreSQL types directly: `citext`, `timestamptz`, `jsonb`, and Postgres-native enum-like columns (`incident_status`, `audit_action`). Building the schema now settles FEC-01 in favour of a relational database.
- Status transitions, priority computation, and idempotent intake all depend on row-level constraints and foreign keys enforced by the database, which a relational engine gives for free and a document store does not.
- `citext` gives case-insensitive uniqueness on `app_user.email` without extra application code.

## Consequences

- FEC-01 is closed. The NoSQL option is no longer under consideration for this project.
- Every migration written from here on targets PostgreSQL syntax (see [`0003-schema-conventions`](0003-schema-conventions.md)), not a database-agnostic subset.
