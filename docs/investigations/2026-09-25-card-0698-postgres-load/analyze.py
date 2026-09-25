"""Summarize finite activity samples; never mistake sampled calls/ages for pg_stat_statements."""
import collections
import datetime as dt
import hashlib
import json
import re
from pathlib import Path
import statistics

ROOT = Path(__file__).parent
def read(name): return json.loads((ROOT / name).read_text(encoding="utf-8"))
def stamp(value):
    if not value: return None
    value = re.sub(r'\.(\d+)(?=[+-])', lambda m: '.'+m[1].ljust(6,'0'), value)
    return dt.datetime.fromisoformat(value)
def qid(query): return hashlib.sha256(query.encode()).hexdigest()[:12]
def ranges(values): return {"min": min(values), "max": max(values), "mean": statistics.mean(values)}

samples, before, after, docker = [read(x) for x in
    ("activity.json", "before.json", "after.json", "docker-stats.json")]
duration = (stamp(after['at'])-stamp(before['at'])).total_seconds()
sample_window = (stamp(samples[-1]['at'])-stamp(samples[0]['at'])).total_seconds() + 2
queries = {}
connections, actives, idle_tx = [], [], []
groups = collections.defaultdict(list)
backend_types = collections.Counter()
for sample in samples:
    rows = sample['activity']
    clients = [r for r in rows if r['backend_type']=='client backend']
    connections.append(len(clients))
    actives.append(sum(r['state']=='active' for r in clients))
    idle_tx.append(sum((r['state'] or '').startswith('idle in transaction') for r in clients))
    counts = collections.Counter((r['datname'],r['application_name'],r['state']) for r in clients)
    for key in counts: groups[key].append(counts[key])
    for r in rows:
        backend_types[r['backend_type']] += 1
        if not r['query'] or r['datname'] != 'antiphon': continue
        key = qid(r['query'])
        q = queries.setdefault(key, {'query':r['query'], 'active_samples':0,
            'worker_active_samples':0, 'observations':0, 'executions':{}, 'pids':set(),
            'waits':collections.Counter()})
        if r['backend_type']=='parallel worker':
            q['worker_active_samples'] += r['state']=='active'
            continue
        if r['backend_type']!='client backend': continue
        q['observations'] += 1
        q['pids'].add(r['pid'])
        execution = (r['pid'], r['query_start'])
        e = q['executions'].setdefault(execution, {'completed_ms':None, 'max_active_age_ms':0})
        if r['state']=='active':
            q['active_samples'] += 1
            q['waits'][str((r['wait_event_type'],r['wait_event']))] += 1
            age = (stamp(sample['at']) - stamp(r['query_start'])).total_seconds()*1000
            e['max_active_age_ms'] = max(e['max_active_age_ms'], age)
        elif r['state']=='idle' and r['state_change']:
            e['completed_ms'] = (stamp(r['state_change'])-stamp(r['query_start'])).total_seconds()*1000

out=[]
for key,q in queries.items():
    completed = [e['completed_ms'] for e in q['executions'].values() if e['completed_ms'] is not None]
    active_ages = [e['max_active_age_ms'] for e in q['executions'].values() if e['max_active_age_ms']>0]
    in_window = sum(stamp(start) >= stamp(before['at']) for pid,start in q['executions'])
    out.append({'qid':key,'query':q['query'],'distinct_observed_calls':len(q['executions']),
        'observed_calls_per_min':len(q['executions'])*60/sample_window,
        'calls_started_in_stats_window':in_window,
        'preexisting_observed_calls':len(q['executions'])-in_window,
        'stats_window_observed_calls_per_min':in_window*60/duration,
        'active_samples':q['active_samples'],'worker_active_samples':q['worker_active_samples'],
        'estimated_client_active_seconds':q['active_samples']*sample_window/len(samples),
        'estimated_worker_active_seconds':q['worker_active_samples']*sample_window/len(samples),
        'observed_connection_pids':len(q['pids']), 'completed_observations':len(completed),
        'completed_mean_ms':statistics.mean(completed) if completed else None,
        'completed_total_ms':sum(completed),
        'active_age_max_ms':max(active_ages) if active_ages else None,
        'waits':dict(q['waits'])})
out.sort(key=lambda x:x['active_samples']+x['worker_active_samples'],reverse=True)
tables=[]
for t in after['tables']:
    old=next(x for x in before['tables'] if x['relid']==t['relid'])
    fields=['seq_scan','seq_tup_read','idx_scan','idx_tup_fetch','n_tup_ins','n_tup_upd','n_tup_del',
            'autovacuum_count','autoanalyze_count']
    tables.append({'table':t['relname'], **{f: (t[f] or 0)-(old[f] or 0) for f in fields}})
tables.sort(key=lambda x:x['seq_tup_read']+x['idx_tup_fetch'], reverse=True)
db_old=next(x for x in before['databases'] if x['datname']=='antiphon')
db_new=next(x for x in after['databases'] if x['datname']=='antiphon')
fields=['xact_commit','xact_rollback','blks_read','blks_hit','tup_returned','tup_fetched',
        'tup_inserted','tup_updated','tup_deleted','temp_files','temp_bytes','deadlocks','sessions']
summary={'before':before['at'],'after':after['at'],'stats_seconds':duration,
 'sample_seconds':sample_window,'samples':len(samples),'connections':ranges(connections),
 'active_client_connections':ranges(actives),'idle_in_transaction':ranges(idle_tx),
 'connection_groups':[{'key':k,'observed_counts':ranges(v),'samples_present':len(v)} for k,v in groups.items()],
 'backend_sample_counts':dict(backend_types),
 'docker_cpu_percent':ranges([float(x['CPUPerc'].rstrip('%')) for x in docker if 'CPUPerc' in x]),
 'db_deltas':{f:db_new[f]-db_old[f] for f in fields},'table_deltas':tables,'queries':out}
(ROOT/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in summary.items() if k not in ['queries','table_deltas']},indent=2))
print('TOP ACTIVE QUERY PREFIXES')
for q in out[:16]:
    print(json.dumps({k:v for k,v in q.items() if k not in ['query','waits']}), q['query'][:160].replace('\n',' '))
print('TOP TABLE DELTAS',json.dumps(tables[:12],indent=2))
