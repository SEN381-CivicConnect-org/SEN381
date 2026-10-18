-- =============================================================================
-- CivicConnect PostgreSQL Role Initialization Script (NFR-10, NFR-04, IT-SEC-02)
-- Placed in /docker-entrypoint-initdb.d/ to run on initial database creation.
-- =============================================================================

-- 1. Enable citext extension in the current database
CREATE EXTENSION IF NOT EXISTS citext;

-- 2. Create dedicated PostgreSQL roles with separation of duties
DO $$
BEGIN
    -- Migration pipeline role (DDL / schema owner)
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'civic_migrate') THEN
        CREATE ROLE civic_migrate WITH LOGIN PASSWORD 'civic_migrate_secure_pass';
    END IF;

    -- Application runtime role (least-privilege DML: SELECT, INSERT, UPDATE)
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'civic_app') THEN
        CREATE ROLE civic_app WITH LOGIN PASSWORD 'civic_app_secure_dev_pass';
    END IF;

    -- Reporting read-only role (NFR-04, IT-SEC-01: views only, no direct app_user access)
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'civic_report') THEN
        CREATE ROLE civic_report WITH LOGIN PASSWORD 'civic_report_secure_pass';
    END IF;
END
$$;

-- 3. Database connection privileges
DO $$
BEGIN
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO civic_migrate, civic_app, civic_report', current_database());
END
$$;

-- 4. Schema USAGE and DDL permissions
GRANT USAGE, CREATE ON SCHEMA public TO civic_migrate;
GRANT USAGE ON SCHEMA public TO civic_app, civic_report;

-- Strictly deny DDL to runtime and reporting roles
REVOKE CREATE ON SCHEMA public FROM civic_app;
REVOKE CREATE ON SCHEMA public FROM civic_report;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- 5. Full privileges for civic_migrate across schema public
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO civic_migrate;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO civic_migrate;
GRANT ALL PRIVILEGES ON ALL ROUTINES IN SCHEMA public TO civic_migrate;

ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO civic_migrate;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO civic_migrate;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON ROUTINES TO civic_migrate;

-- 6. Sequence privileges for civic_app
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO civic_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO civic_app;
ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO civic_app;

-- 7. Operational table privileges for civic_app (SELECT, INSERT, UPDATE; NO DELETE)
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO civic_app;
ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO civic_app;

-- 8. Read-only view privileges for civic_report
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO civic_report;
ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public GRANT SELECT ON TABLES TO civic_report;
