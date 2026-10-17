-- =============================================================================
-- CivicConnect PostgreSQL Role Provisioning & Grant Verification Script
-- Standards: NFR-10 (Least-Privilege Database Access, IT-SEC-02),
--            NFR-04 (Protection of Personal Information, IT-SEC-01)
-- =============================================================================

-- 1. Create distinct PostgreSQL roles if not already present
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'civic_migrate') THEN
        CREATE ROLE civic_migrate WITH LOGIN PASSWORD 'civic_migrate_secure_pass';
    END IF;

    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'civic_app') THEN
        CREATE ROLE civic_app WITH LOGIN PASSWORD 'civic_app_secure_dev_pass';
    END IF;

    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'civic_report') THEN
        CREATE ROLE civic_report WITH LOGIN PASSWORD 'civic_report_secure_pass';
    END IF;
END
$$;

-- 2. Database connection privileges
DO $$
BEGIN
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO civic_migrate, civic_app, civic_report', current_database());
END
$$;

-- 3. Schema USAGE and DDL permissions
GRANT USAGE, CREATE ON SCHEMA public TO civic_migrate;
GRANT USAGE ON SCHEMA public TO civic_app, civic_report;

-- Strictly revoke DDL from civic_app and civic_report
REVOKE CREATE ON SCHEMA public FROM civic_app;
REVOKE CREATE ON SCHEMA public FROM civic_report;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- 4. Pipeline permissions for civic_migrate
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO civic_migrate;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO civic_migrate;
GRANT ALL PRIVILEGES ON ALL ROUTINES IN SCHEMA public TO civic_migrate;

ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO civic_migrate;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO civic_migrate;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON ROUTINES TO civic_migrate;

-- 5. Sequences for civic_app
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO civic_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO civic_app;
ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO civic_app;

-- 6. Operational tables for civic_app (SELECT, INSERT, UPDATE; NO DELETE)
DO $$
DECLARE
    tbl text;
    operational_tables text[] := ARRAY[
        'app_user', 'role', 'user_role', 'service_team', 'team_role', 'team_member',
        'category', 'priority_target', 'location_kind', 'location', 'asset',
        'incident', 'ticket', 'submission_request', 'incident_subscription',
        'incident_status_transition', 'resolution_code', 'resolution', 'notification'
    ];
BEGIN
    FOREACH tbl IN ARRAY operational_tables
    LOOP
        IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = tbl) THEN
            EXECUTE format('GRANT SELECT, INSERT, UPDATE ON TABLE public.%I TO civic_app', tbl);
            EXECUTE format('REVOKE DELETE ON TABLE public.%I FROM civic_app', tbl);
        END IF;
    END LOOP;
END
$$;

-- 7. Append-only audit & history tables (SELECT, INSERT; NO UPDATE, NO DELETE)
DO $$
DECLARE
    tbl text;
    append_only_tables text[] := ARRAY[
        'audit_log', 'incident_status_history', 'incident_note'
    ];
BEGIN
    FOREACH tbl IN ARRAY append_only_tables
    LOOP
        IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = tbl) THEN
            EXECUTE format('GRANT SELECT, INSERT ON TABLE public.%I TO civic_app', tbl);
            EXECUTE format('REVOKE UPDATE, DELETE ON TABLE public.%I FROM civic_app', tbl);
        END IF;
    END LOOP;
END
$$;

-- 8. View and migration history permissions
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_views WHERE schemaname = 'public' AND viewname = 'staff_directory') THEN
        GRANT SELECT ON TABLE public.staff_directory TO civic_app, civic_report;
    END IF;

    IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = '__ef_migrations_history') THEN
        GRANT SELECT ON TABLE public.__ef_migrations_history TO civic_app;
    END IF;

    -- POPIA s19(1) (NFR-04): Revoke direct app_user access from civic_report
    IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = 'app_user') THEN
        REVOKE ALL ON TABLE public.app_user FROM civic_report;
    END IF;
END
$$;

-- 9. Default privileges for future objects
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO civic_app;
ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO civic_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO civic_report;
ALTER DEFAULT PRIVILEGES FOR ROLE civic_migrate IN SCHEMA public GRANT SELECT ON TABLES TO civic_report;
