"""Read-only CARD-0698 evidence collector. No credentials or application row content.

Uses the documented dev container's local Unix socket. All SQL is SELECT against
catalog/statistics views; no extension/config changes, resets, EXPLAIN or writes.
Run: python <this-file> [seconds=180]
"""
import concurrent.futures
import datetime
import json
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).parent

def command(args):
    return subprocess.run(args, capture_output=True, text=True, encoding="utf-8",
                          timeout=25, check=True).stdout.strip()

def sql(query):
    return json.loads(command(["docker", "exec", "antiphon-postgres", "psql", "-X",
                               "-U", "antiphon", "-d", "antiphon", "-A", "-t", "-c", query]))

def save(name, value):
    (ROOT / name).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

ACTIVITY = """SELECT json_build_object('at',clock_timestamp(),'activity',
 (SELECT json_agg(x) FROM (SELECT pid, leader_pid, datname, application_name,
 client_addr, backend_type, backend_start, state, wait_event_type, wait_event,
 xact_start, query_start, state_change, query
 FROM pg_stat_activity WHERE pid <> pg_backend_pid()) x))"""

SNAPSHOT = """SELECT json_build_object('at',clock_timestamp(),
 'settings',(SELECT json_object_agg(name,setting) FROM pg_settings WHERE name IN
 ('max_connections','shared_preload_libraries','track_activities','track_counts',
 'track_activity_query_size','track_io_timing','autovacuum','autovacuum_vacuum_scale_factor',
 'autovacuum_vacuum_threshold','shared_buffers','max_parallel_workers_per_gather')),
 'extensions',(SELECT json_agg(extname) FROM pg_extension),
 'databases',(SELECT json_agg(x) FROM pg_stat_database x),
 'bgwriter',(SELECT row_to_json(x) FROM pg_stat_bgwriter x),
 'tables',(SELECT json_agg(x) FROM (SELECT s.*, pg_total_relation_size(s.relid) AS total_bytes,
 pg_relation_size(s.relid) AS heap_bytes FROM pg_stat_user_tables s WHERE schemaname='public') x),
 'indexes',(SELECT json_agg(x) FROM (SELECT s.*, pg_relation_size(s.indexrelid) AS bytes,
 pg_get_indexdef(s.indexrelid) AS definition FROM pg_stat_user_indexes s WHERE schemaname='public') x),
 'table_io',(SELECT json_agg(x) FROM pg_statio_user_tables x WHERE schemaname='public'),
 'schemas',(SELECT json_agg(x) FROM (SELECT n.nspname, count(c.oid) AS tables,
 sum(pg_total_relation_size(c.oid)) AS bytes FROM pg_namespace n
 JOIN pg_class c ON c.relnamespace=n.oid WHERE c.relkind='r'
 GROUP BY n.nspname ORDER BY n.nspname) x),
 'vacuum',(SELECT json_agg(x) FROM pg_stat_progress_vacuum x))"""

def stats_loop(duration):
    rows = []
    start = time.monotonic()
    while time.monotonic() - start < duration:
        at = datetime.datetime.now(datetime.timezone.utc).isoformat()
        try:
            value = json.loads(command(["docker", "stats", "antiphon-postgres", "--no-stream",
                                        "--format", "{{json .}}"])); rows.append({"at": at, **value})
        except Exception as exc:
            rows.append({"at": at, "error": type(exc).__name__})
        time.sleep(min(10, max(0, duration - (time.monotonic()-start))))
    return rows

if __name__ == "__main__":
    duration = int(sys.argv[1]) if len(sys.argv) > 1 else 180
    save("before.json", sql(SNAPSHOT))
    start = time.monotonic()
    rows = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
        stats = pool.submit(stats_loop, duration)
        for tick in range((duration + 1) // 2):
            time.sleep(max(0, start + tick * 2 - time.monotonic()))
            sample = sql(ACTIVITY)
            sample["sample"] = tick
            rows.append(sample)
            if tick % 15 == 0:
                print(json.dumps({"sample":tick, "at":sample["at"]}), flush=True)
        save("activity.json", rows)
        save("docker-stats.json", stats.result())
    save("after.json", sql(SNAPSHOT))
    print(json.dumps({"complete":True, "samples":len(rows),
                      "elapsed_seconds":time.monotonic()-start}), flush=True)
