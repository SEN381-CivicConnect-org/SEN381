--
-- PostgreSQL database cluster dump
--

\restrict zVHELTwJmsIzeKqngwFrLT2qpenFvDIdVN5RfKDcfqz7deAgraBGBkaEJ2NwJyF

SET default_transaction_read_only = off;

SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;

--
-- Roles
--

CREATE ROLE civicconnect;
ALTER ROLE civicconnect WITH SUPERUSER INHERIT CREATEROLE CREATEDB LOGIN REPLICATION BYPASSRLS PASSWORD 'SCRAM-SHA-256$4096:ZjdWa0PXPgEo3AVdtTgEnA==$BMrZe5UPaxYM2/7VrPBakHoOxW8OB88YB8ef+ykp6Ao=:ksL81ICVsHlNRhyEGVA2UQc+2RVHFg3z+oTiL6uKjyo=';

--
-- User Configurations
--






\unrestrict zVHELTwJmsIzeKqngwFrLT2qpenFvDIdVN5RfKDcfqz7deAgraBGBkaEJ2NwJyF

--
-- PostgreSQL database cluster dump complete
--

