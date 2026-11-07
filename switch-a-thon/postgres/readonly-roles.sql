\getenv readonly_password RESOLVEIQ_READONLY_PASSWORD
\getenv monitor_password RESOLVEIQ_MONITOR_PASSWORD

SELECT format(
    'CREATE ROLE resolveiq_reader LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT',
    :'readonly_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'resolveiq_reader')
\gexec

SELECT format(
    'ALTER ROLE resolveiq_reader WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT',
    :'readonly_password')
\gexec

SELECT format(
    'CREATE ROLE resolveiq_monitor LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT',
    :'monitor_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'resolveiq_monitor')
\gexec

SELECT format(
    'ALTER ROLE resolveiq_monitor WITH LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT',
    :'monitor_password')
\gexec

GRANT CONNECT ON DATABASE resolveiq TO resolveiq_reader, resolveiq_monitor;
GRANT USAGE ON SCHEMA public TO resolveiq_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO resolveiq_reader;
ALTER DEFAULT PRIVILEGES FOR ROLE resolveiq IN SCHEMA public
    GRANT SELECT ON TABLES TO resolveiq_reader;
GRANT pg_monitor TO resolveiq_monitor;
