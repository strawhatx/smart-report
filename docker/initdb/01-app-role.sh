#!/bin/sh
# Creates the non-owner application role.
#
# Everything the web app does runs as this role, which is deliberately NOT the owner and NOT a
# superuser: both bypass row-level security (A6, D4). Table-level grants are issued by the EF
# migration that creates each table, so a new table is unreachable until its grants and policies
# are written — which is the behaviour we want (D1).
#
# Runs once, on first creation of the data volume.
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<SQL
  CREATE ROLE "$APP_DB_USER" LOGIN PASSWORD '$APP_DB_PASSWORD' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;

  GRANT CONNECT ON DATABASE "$POSTGRES_DB" TO "$APP_DB_USER";
  GRANT USAGE ON SCHEMA public TO "$APP_DB_USER";

  -- No blanket table grants: the app role starts with access to nothing.
  REVOKE ALL ON ALL TABLES IN SCHEMA public FROM "$APP_DB_USER";
SQL

echo "init: created non-owner application role '$APP_DB_USER' (NOBYPASSRLS)"
