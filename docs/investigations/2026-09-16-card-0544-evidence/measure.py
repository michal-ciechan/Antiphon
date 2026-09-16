"""Reconstruct CARD-0544 observations from stored task rows and existing TRX files.

This reads evidence only; it never builds, tests, mutates source or calls a service.
Run beside the committed task-rows.json on the original evidence host. Outputs
are regenerated beside this script. Raw task-<guid>.json API captures, when
present, take precedence over the normalized task-rows.json.
"""
from pathlib import Path
from datetime import datetime
import json, csv, re, hashlib, xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = Path('C:/Antiphon/evidence')
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def read(p):
    return json.loads(p.read_text(encoding='utf-8-sig'))
def dt(v):
    return datetime.fromisoformat(re.sub(r'\.(\d+)', lambda m: '.' + m[1][:6].ljust(6,'0'), v.replace('Z', '+00:00')))
def duration(v):
    h,m,s=v.split(':'); return int(h)*3600+int(m)*60+float(s)
def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()
def output(name, rows):
    (HERE/name).write_text(json.dumps(rows,indent=2)+'\n',encoding='utf-8')

tasks=[]; runs=[]; new_cases=[]; summaries=[]; inputs={}
specs={
 '360e223e': ('code-360e223e',56,55),
 '4d210064': ('review-4d210064',56,55),
 '0d48a707': ('code-0d48a707',56,61),
 '32c1f938': ('review-32c1f938',56,61),
 'b23741b2': ('code-b23741b2',62,67),
 '7ef54b1c': ('review-7ef54b1c',62,67),
 'ca4eef9c': ('code-ca4eef9c',68,74),
 '80155ada': ('review-80155ada',68,74),
 '151b4e13': ('code-151b4e13',75,79),
 '1d0539bc': ('review-1d0539bc',75,79),
 '89bfbbdf': ('review-89bfbbdf',56,55),
}
base_names={'unit','classes','reply','outbox','distillation','worktree','cardreview','r17'}
sources=[read(tp) for tp in sorted(HERE.glob('task-*.json')) if re.fullmatch(r'task-[0-9a-f-]{36}\.json',tp.name)]
if not sources:
    sources=[{'summary':t,'goal':t['goal'],'result':t['result']} for t in read(HERE/'task-rows.json')]
for d in sources:
    s=d['summary']; short=s['id'][:8]
    task={k:s.get(k) for k in ['id','title','role','status','agentKind','modelLevel','dispatchedAt','completedAt','costUsd','tokensIn','tokensOut','cacheReadTokens','cacheCreationTokens','costPricingVersion','agentSessionId','worktreePath','cardId']}
    task.update(goal=d['goal'],result=d['result'])
    task['wallMinutes']=(dt(s['completedAt'])-dt(s['dispatchedAt'])).total_seconds()/60 if s.get('completedAt') and s.get('dispatchedAt') else None
    tasks.append(task)
    if short not in specs: continue
    rootname,lo,hi=specs[short]; root=ROOT/rootname
    vr=root/'v-r-results.json'; wanted=set()
    if vr.exists():
        inputs[str(vr)]=digest(vr)
        for row in read(vr):
            rid=row.get('id','')
            if rid.startswith('V-') and lo<=int(rid[2:])<=hi:
                wanted.update(row.get('methods',[]))
    current=[]
    for trx in sorted(root.glob('*/*.trx')):
        inputs[str(trx)]=digest(trx)
        doc=ET.parse(trx); time=doc.find('t:Times',NS).attrib
        name=trx.parent.name
        meta={}; mp=None
        for mn in ['metadata.json','command.json']:
            if (trx.parent/mn).exists():
                mp=trx.parent/mn; meta=read(mp); inputs[str(mp)]=digest(mp); break
        counts=doc.find('t:ResultSummary/t:Counters',NS).attrib
        run={'task':short,'name':name,'trx':str(trx),'trxSha256':digest(trx),'metadata':str(mp) if mp else None,
             'sha':meta.get('sha'),'filter':meta.get('filter'),
             'trxStarted':time['start'],'trxFinished':time['finish'],
             'trxSeconds':(dt(time['finish'])-dt(time['start'])).total_seconds(),
             'commandSeconds':None,'total':int(counts['total']),'passed':int(counts['passed']),'failed':int(counts['failed']),
             'group': 'baseline' if name.startswith('base') else 'first-matrix' if name in base_names else 'repeat-or-targeted'}
        ended=meta.get('ended') or meta.get('finished')
        if meta.get('started') and ended:
            run['commandSeconds']=(dt(ended)-dt(meta['started'])).total_seconds()
        methods={}
        for definition in doc.findall('t:TestDefinitions/t:UnitTest',NS):
            m=definition.find('t:TestMethod',NS)
            methods[definition.attrib['id']]=(m.attrib['className'],m.attrib['name'])
        for res in doc.findall('t:Results/t:UnitTestResult',NS):
            cls,meth=methods.get(res.attrib['testId'],('',''))
            row={'task':short,'run':name,'class':cls,'method':meth,'name':res.attrib['testName'],
                 'outcome':res.attrib['outcome'],'seconds':duration(res.attrib.get('duration','00:00:00')),
                 'start':res.attrib.get('startTime'),'finish':res.attrib.get('endTime'),'trx':str(trx)}
            if cls.rsplit('.',1)[-1]+'.'+meth in wanted and run['group']!='baseline':
                current.append(row)
        runs.append(run)
    # Latest execution per expanded test identity. This is body time, not scoped invocation wall.
    latest={}
    for row in sorted(current,key=lambda r:r['finish'] or ''):
        latest[(row['class'],row['method'],row['name'])]=row
    new_cases.extend(latest.values())
    tr=[r for r in runs if r['task']==short]
    def mins(group): return sum(r['trxSeconds'] for r in tr if r['group']==group)/60
    def union_seconds(rows):
        intervals=sorted((dt(r['trxStarted']),dt(r['trxFinished'])) for r in rows)
        end=None; total=0
        for a,b in intervals:
            if end is None or a>=end: total+=(b-a).total_seconds()
            elif b>end: total+=(b-end).total_seconds()
            end=max(end,b) if end else b
        return total
    builds=[]
    for f in root.glob('*build*.log'):
        inputs[str(f)]=digest(f)
        for h,m,sec in re.findall(r'Time Elapsed (\d+):(\d+):([\d.]+)', f.read_text(encoding='utf-8-sig',errors='replace')):
            builds.append({'path':str(f),'seconds':int(h)*3600+int(m)*60+float(sec)})
    matrix_wall=union_seconds([r for r in tr if r['group']=='first-matrix'])/60
    summ={'task':short,'evidenceRoot':str(root),'role':s['role'],'status':s['status'],'wallMinutes':task['wallMinutes'],'costUsd':s['costUsd'],
          'firstMatrixTrxMinutes':mins('first-matrix'),'baselineTrxMinutes':mins('baseline'),
          'repeatOrTargetedTrxMinutes':mins('repeat-or-targeted'),
          'allTrxUnionMinutes':union_seconds(tr)/60,
          'firstMatrixUnionMinutes':matrix_wall,
          'matrixSharePercent':100*matrix_wall/task['wallMinutes'] if task['wallMinutes'] else None,
          'buildLogMinutes':sum(x['seconds'] for x in builds)/60 if builds else None,'buildLogs':builds,
          'firstMatrixCommandOverheadMinutes':sum((r['commandSeconds']-r['trxSeconds'])/60 for r in tr if r['group']=='first-matrix' and r['commandSeconds']),
          'unitTrxMinutes':sum(r['trxSeconds'] for r in tr if r['name']=='unit')/60,
          'outboxDistillationTrxMinutes':sum(r['trxSeconds'] for r in tr if r['name'] in {'outbox','distillation'})/60,
          'outboxDistillationUnionMinutes':union_seconds([r for r in tr if r['name'] in {'outbox','distillation'}])/60,
          'incrementalVRange':f'V-{lo}..V-{hi}' if lo<=hi else None,
          'incrementalExpanded':len(latest),'incrementalMethodCount':len(wanted),
          'incrementalBodyMinutes':sum(r['seconds'] for r in latest.values())/60,
          'incrementalBodyPassed':sum(r['outcome']=='Passed' for r in latest.values())}
    summaries.append(summ)
tasks.sort(key=lambda t:t['dispatchedAt'] or '')
summaries.sort(key=lambda r: next((t['dispatchedAt'] or '' for t in tasks if t['id'].startswith(r['task'])),''))
output('task-rows.json',tasks); output('runs-measured.json',runs); output('incremental-cases.json',new_cases); output('summary-measured.json',summaries); output('input-hashes.json',inputs)
for s in summaries:
    print(s['task'],s['role'],s['status'],*(f'{s[k]:.2f}' if s[k] is not None else '?' for k in ['wallMinutes','costUsd','firstMatrixTrxMinutes','baselineTrxMinutes','repeatOrTargetedTrxMinutes','outboxDistillationTrxMinutes','incrementalBodyMinutes']), 'new',s['incrementalExpanded'])
