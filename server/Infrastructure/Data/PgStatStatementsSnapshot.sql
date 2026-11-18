-- CARD-1987. One counter snapshot for scripts/pg-qps.ps1 and DbRateSampler.
-- Column order is the contract: VersionNum, Version, Dealloc, StatsReset,
-- QueryId, Calls, TotalExecMs, Rows. Text is the section after the split.
SELECT current_setting('server_version_num') AS "VersionNum",
       current_setting('server_version') AS "Version",
       i.dealloc::text AS "Dealloc",
       COALESCE(i.stats_reset::text, '') AS "StatsReset",
       s.queryid::text AS "QueryId",
       COALESCE(s.calls, 0)::text AS "Calls",
       COALESCE(s.total_exec_time, 0)::text AS "TotalExecMs",
       COALESCE(s.rows, 0)::text AS "Rows"
FROM pg_stat_statements_info AS i
LEFT JOIN pg_stat_statements(false) AS s
  ON s.dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
 AND s.toplevel
-- @@split@@
SELECT s.queryid::text AS "QueryId",
       encode(convert_to(s.query, 'UTF8'), 'base64') AS "QueryB64"
FROM pg_stat_statements(true) AS s
WHERE s.dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
  AND s.toplevel
  AND s.queryid IN (/*IDS*/)
