import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';

// Public Close, real remote shell/locks, owned file-backed Docker transport.
// Only the SSH destination/root and Docker boundary are substituted.
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const root = process.argv[2];
assert(path.isAbsolute(root) && fs.realpathSync(root) === root);
assert(/^c1008-host-[A-Za-z0-9]+$/.test(path.basename(root)));
const op = 'c100800000000000000000000000000000001';
const dir = root + '/server/retirement/' + op;
const file = dir + '/receipt.json';
const lifecycle = dir + '/lifecycle.json';
const closure = dir + '/closure.json';
const pending = dir + '/close-pending.json';
const state = root + '/docker.json';
const q = s => "'" + String(s).replaceAll("'", "'\\''") + "'";
const hash = s => crypto.createHash('sha256').update(s).digest('hex');
const sort = x => Array.isArray(x) ? x.map(sort) : x && typeof x === 'object'
  ? Object.fromEntries(Object.keys(x).sort().map(k => [k, sort(x[k])])) : x;
const canonical = x => JSON.stringify(sort(x)) + '\n';
const write = (p, x) => fs.writeFileSync(p, canonical(x), {mode:0o600});
// Health telemetry changes independently of mounts/identity. It must not
// manufacture a reference-census drift on an otherwise safe observation.
const healthFile=root+'/health-probe.json';
write(healthFile,{targets:[],fingerprints:{}});
const healthScript='set -euo pipefail\nsource '+q(repo+'/scripts/c1008-retirement.sh')+'\n'+
  'C1008_OPERATION='+q(op)+'; C1008_PROJECT=antiphon-runner-temp\n'+
  'docker() { if [ "$1" = ps ]; then printf "%064d\\n" 1; else jq -cn --arg id "$(printf "%064d" 1)" --arg time "$(date +%s%N)" \'[{Id:$id,Config:{Labels:{}},State:{Running:true,Status:"running",Health:{Log:[$time]}},Mounts:[]}]\'; fi; }\n'+
  'a="$(c1609_close_census '+q(healthFile)+')"; b="$(c1609_close_census '+q(healthFile)+')"; [ "$a" = "$b" ]\n';
const health=cp.spawnSync('bash',['-c',healthScript],{encoding:'utf8',timeout:10000});
assert.equal(health.status,0,'health telemetry must not change the safety census: '+health.stderr);
if(process.argv.includes('--health-probe')){console.log('C1609_CLOSE health census passed');process.exit(0);}
for(const [from,to] of [[state,root+'/close-original-docker.json'],[file,root+'/close-original-receipt.json'],[lifecycle,root+'/close-original-lifecycle.json']]){
  if(fs.existsSync(to))fs.copyFileSync(to,from);else if(fs.existsSync(from))fs.copyFileSync(from,to);
}
const originalState = JSON.parse(fs.readFileSync(state));
const original = JSON.parse(fs.readFileSync(file));
const targets = original.targets;
assert.equal(targets.length, 4);
const record = {...original, state:'PartialRefused', consumed:true, owner:null,
  intents:[{name:targets[0],intent:'remove',result:'pending'}],effects:[]};
write(file, record);
const bytes = fs.readFileSync(file);
const phaseBytes = fs.existsSync(lifecycle) ? fs.readFileSync(lifecycle) : null;
const absent = structuredClone(originalState);
for (const name of targets) delete absent.volumes[name];
const bin = root + '/close-bin';fs.mkdirSync(bin,{recursive:true});
const reason = root + '/close-reason.txt';fs.writeFileSync(reason, 'observed absent after recovery\nquoted "reason" \\ path');
const token = root + '/operator-token';fs.writeFileSync(token, 'C1609_CLOSE_OPERATOR_SENTINEL', {mode:0o600});
const sibling=op.slice(0,-1)+'2';
const siblingDir=root+'/server/retirement/'+sibling;
const siblingBytes=fs.readFileSync(siblingDir+'/receipt.json');
const trace = [];
process.once('exit',code=>{
  if(process.env.ANTIPHON_TEST_PHASE_ROOT){
    fs.mkdirSync(process.env.ANTIPHON_TEST_PHASE_ROOT,{recursive:true});
    if(code!==0){const saved=path.join(process.env.ANTIPHON_TEST_PHASE_ROOT,'close-failure-'+process.pid,path.basename(root));fs.mkdirSync(path.dirname(saved),{recursive:true});fs.cpSync(root,saved,{recursive:true});console.error('C1609_CLOSE_DIAGNOSTIC='+saved);}
    fs.writeFileSync(path.join(process.env.ANTIPHON_TEST_PHASE_ROOT,'close-calls-'+process.pid+'.json'),JSON.stringify({trace,
      cli:fs.existsSync(root+'/close-cli.jsonl')?fs.readFileSync(root+'/close-cli.jsonl','utf8').trim().split('\n').filter(Boolean).map(JSON.parse):[]}));
  }
});
fs.writeFileSync(bin+'/docker', `#!/usr/local/bin/node
const fs=require('node:fs'),cp=require('node:child_process');
const args=process.argv.slice(2),started=performance.now();
let r;
if(process.env.C1609_CLOSE_FIXTURE_FAULT==='volume-inspect-error' && args[0]==='volume' && args[1]==='inspect')r={status:2,stderr:'daemon unavailable'};
else r=cp.spawnSync('bash',[process.env.C1609_CLOSE_DOCKER,...args],{encoding:'utf8',timeout:10000});
fs.appendFileSync(process.env.C1609_CLOSE_FIXTURE+'/close-cli.jsonl',JSON.stringify({verb:args.slice(0,2),exit:r.status,milliseconds:performance.now()-started})+String.fromCharCode(10));
process.stdout.write(r.stdout||'');process.stderr.write(r.stderr||'');process.exit(r.status??2);
`,{mode:0o755});
fs.writeFileSync(bin+'/sudo','#!/bin/bash\n[ "$1" != -n ] || shift\nif [ "$1" = install ]; then mkdir -p -- "${@: -1}"; chmod 0700 "${@: -1}"; else exec "$@"; fi\n',{mode:0o755});
fs.writeFileSync(bin + '/ssh', `#!/usr/local/bin/node
import fs from 'node:fs';
import cp from 'node:child_process';
const root=process.env.C1609_CLOSE_FIXTURE;
let script=fs.readFileSync(0,'utf8').replaceAll('/home/mc/antiphon-server2',root+'/server');
const fault=process.env.C1609_CLOSE_FIXTURE_FAULT||'';
const prefix='set -euo pipefail\\n'+
  'sync() { if [ '+JSON.stringify(fault)+' = closure-sync ] && [ -f '+JSON.stringify(root+'/server/retirement/'+process.env.C1609_CLOSE_OP+'/closure.json')+' ]; then return 1; fi; command sync "$@"; }\\nexport -f sync\\n';
fs.writeFileSync(root+'/close-request.sh',prefix+script,{mode:0o600});
const r=cp.spawnSync('bash',[root+'/close-request.sh'],{encoding:'utf8',timeout:20000,maxBuffer:2*1024*1024});
process.stdout.write(r.stdout||'');process.stderr.write(r.stderr||'');process.exit(r.status??2);
`, {mode:0o755});
let vectors = 0;
function close(fault = '', digest = hash(bytes), authority = true, request={}) {
  const env = {...process.env, PATH:bin+':'+process.env.PATH,
    C1609_CLOSE_FIXTURE:root, C1609_CLOSE_FIXTURE_FAULT:fault, C1609_CLOSE_OP:request.op||op,
    C1609_CLOSE_DOCKER:repo+'/scripts/fixtures/c1008-fake-docker.sh', C1008_FIXTURE_ROOT:root,
    ANTIPHON_OPERATOR_TOKEN_FILE:authority?token:root+'/missing-token', ANTIPHON_TASK_TOKEN:''};
  delete env.C1609_RETIREMENT_ROOT;delete env.C1609_VOLUME_CENSUS_FILE;
  const started=performance.now();
  const args=['-NoProfile','-NonInteractive','-File',repo+'/scripts/retire-runner-volumes.ps1',
    '-Close','-OperationId',request.op||op,'-ExpectedJournalDigest',digest,'-ReasonFile',reason];
  if(request.authorityFile)args.push('-AuthorityFile',request.authorityFile);
  if(request.defer)return cp.spawn('pwsh',args,{env,stdio:['ignore','pipe','pipe']});
  const r=cp.spawnSync('pwsh',args,{env,encoding:'utf8',timeout:30000,maxBuffer:2*1024*1024});
  trace.push({fault,exit:r.status,milliseconds:performance.now()-started});
  assert(!r.error, 'Close driver/fixture failure: '+r.error);
  assert(!(r.stdout+r.stderr).includes('C1609_CLOSE_OPERATOR_SENTINEL'),'operator credential never transported/logged');
  return {code:r.status,text:r.stdout+r.stderr};
}
function reset() {
  fs.writeFileSync(file,bytes);write(state, absent);
  if(phaseBytes)fs.writeFileSync(lifecycle,phaseBytes);
}
function refused(label, mutate=()=>{}, fault='', digest=hash(bytes), authority=true) {
  reset();mutate();
  const before=fs.readFileSync(file);
  const r=close(fault,digest,authority);
  assert.notEqual(r.code,0,'Close must refuse '+label+'; '+r.text);
  assert(!fs.existsSync(closure),'refusal cannot publish Closed: '+label);
  assert.deepEqual(fs.readFileSync(file),before,'Close cannot rewrite history: '+label);
  vectors++;
}
reset();
// At the predecessor this reaches its actual public command and closes despite
// ps failing: it never asks for the required complete container census.
refused('incomplete container census',()=>{const s=structuredClone(absent);s.fault='ps-error';write(state,s);});
refused('volume list failure',()=>{const s=structuredClone(absent);s.fault='volume-ls-error';write(state,s);});
refused('unknown inspect is not absence',()=>{},'volume-inspect-error');
refused('container inspect failure',()=>{const s=structuredClone(absent);s.fault='inspect-error';write(state,s);});
refused('wrong digest',()=>{},'', '0'.repeat(64));
refused('missing operator authority',()=>{},'', hash(bytes),false);
assert(phaseBytes,'real Preview must publish joined-child lifecycle facts');
const phase=JSON.parse(phaseBytes);
for(const kind of ['incomplete','unjoined','active','missing','linked']) {
  refused('child ownership '+kind,()=>{
    const p=structuredClone(phase);
    if(kind==='incomplete')p.complete=false;
    if(kind==='unjoined')p.children.push({name:'c1008-preview-'+op+'-'+'f'.repeat(16),label:op+':'+'f'.repeat(16),joined:false});
    if(kind==='active'){
      p.owner={pid:process.pid,startTicks:fs.readFileSync('/proc/self/stat','utf8').split(') ')[1].split(' ')[19]};
      p.active=true;
    }
    if(kind==='missing')fs.unlinkSync(lifecycle);
    else if(kind==='linked'){fs.renameSync(lifecycle,lifecycle+'.owned');fs.symlinkSync(lifecycle+'.owned',lifecycle);}
    else write(lifecycle,p);
  });
  if(kind==='linked'){fs.unlinkSync(lifecycle);fs.renameSync(lifecycle+'.owned',lifecycle);}
}
for(const kind of ['present','replaced','helper','bind','named','malformed']) {
  refused(kind,()=>{
    const s=structuredClone(absent);
    if(kind==='present'||kind==='replaced'){
      s.volumes[targets[0]]=structuredClone(originalState.volumes[targets[0]]);
      if(kind==='replaced')s.volumes[targets[0]].CreatedAt='2099-01-01T00:00:00Z';
    } else {
      const c={Id:'f'.repeat(64),Name:'foreign',Image:'sha256:'+'a'.repeat(64),
        State:{Running:true,Status:'running'},Config:{Labels:{}},Mounts:[],HostConfig:{Tmpfs:{},Mounts:[]}};
      if(kind==='helper')c.Config.Labels['io.antiphon.preview-audit']=op+':'+'f'.repeat(16);
      else if(kind==='named')c.Mounts.push({Type:'volume',Name:targets[3],Source:original.fingerprints[targets[3]].Mountpoint,Destination:'/foreign',RW:false});
      else if(kind==='malformed')c.Mounts.push({Type:'volume'});
      else c.Mounts.push({Type:'bind',Source:original.fingerprints[targets[3]].Mountpoint,Destination:'/foreign',RW:false});
      s.containers.push(c);
    }
    write(state,s);
  });
}
for(const key of ['daemon','targets','owner']) {
  reset();const bad=structuredClone(record);
  if(key==='daemon')bad.daemon='foreign-daemon';
  if(key==='targets')bad.targets[0]='antiphon-runner-cache-npm-content';
  if(key==='owner')bad.owner='unknown-child';
  const b=Buffer.from(canonical(bad));
  refused('receipt '+key,()=>fs.writeFileSync(file,b),'',hash(b));
}
// The same production read-only host guard must retain failed/older blockers.
const remote=fs.readFileSync(repo+'/scripts/c590-remote.sh','utf8');
function extract(name){const a=remote.indexOf(name+'() {'),b=remote.indexOf('\n}\n',a)+3;assert(a>=0&&b>a);return remote.slice(a,b);}
function guard() {
  const script='set -euo pipefail\nSERVER2_ROOT='+q(root+'/server')+'; C1008_FIXTURE_ROOT='+q(root)+'; export C1008_FIXTURE_ROOT\n'+
    'source '+q(repo+'/scripts/c1008-retirement.sh')+'\n'+extract('c1008_volume')+'\n'+extract('c1609_temp_retirement_clearable')+'\n'+
    'c1609_load_retirement() { :; }\nc1609_temp_retirement_clearable\n';
  const r=cp.spawnSync('bash',['-c',script],{env:{...process.env,PATH:bin+':'+process.env.PATH,C1609_CLOSE_FIXTURE:root,
    C1609_CLOSE_DOCKER:repo+'/scripts/fixtures/c1008-fake-docker.sh'},encoding:'utf8',timeout:20000});
  assert(!r.error);return r.status;
}
reset();
const hugeReason=fs.readFileSync(reason);fs.writeFileSync(reason,'x'.repeat(4097));
const tooLong=close();assert.notEqual(tooLong.code,0);assert(!fs.existsSync(closure));fs.writeFileSync(reason,hugeReason);
reset();
const failed=close('closure-sync');
assert.notEqual(failed.code,0,'failed closure persistence refuses');
assert(fs.existsSync(pending),'failed persistence leaves a deployment blocker');
assert.deepEqual(fs.readFileSync(file),bytes);
assert.notEqual(guard(),0,'an unconfirmed closure remains a deployment blocker');
// Real FD-8 barrier: no Docker read is allowed while a supported writer owns it.
const held=root+'/close-lock-held',release=root+'/close-lock-release';
const holder=cp.spawn('bash',['-c','exec 8>'+q(root+'/server/locks/rollout.lock')+'; flock 8; touch '+q(held)+'; while [ ! -e '+q(release)+' ]; do sleep 0.02; done'],{stdio:'ignore'});
const holderDone=new Promise(resolve=>holder.once('close',resolve));
let recovery;
try {
  for(let n=0;n<100&&!fs.existsSync(held);n++)await new Promise(r=>setTimeout(r,20));
  assert(fs.existsSync(held),'real Close lock holder started');
  const before=fs.statSync(root+'/docker-trace.jsonl').size;
  recovery=close('',hash(bytes),true,{defer:true});
  let text='';recovery.stdout.on('data',b=>text+=b);recovery.stderr.on('data',b=>text+=b);
  const done=new Promise(resolve=>recovery.once('close',resolve));
  await new Promise(r=>setTimeout(r,250));
  assert.equal(recovery.exitCode,null,'Close waits behind FD 8');
  assert.equal(fs.statSync(root+'/docker-trace.jsonl').size,before,'no Docker read before the lock');
  assert(fs.existsSync(pending),'no premature closure activation');
  fs.writeFileSync(release,'release');
  const code=await done;trace.push({fault:'lock-barrier-recovery',exit:code});assert.equal(code,0,'Close resumes after the actual lock: '+text);
} finally {
  fs.writeFileSync(release,'release');
  await holderDone;
  if(recovery&&recovery.exitCode===null){recovery.kill('SIGKILL');await new Promise(r=>recovery.once('close',r));}
}
const closedBytes=fs.readFileSync(closure);
const closed=JSON.parse(closedBytes);
assert.equal(closed.state,'Closed');assert.equal(closed.priorDigest,hash(bytes));
assert.equal(closed.operationId,op);assert.equal(closed.daemon,original.daemon);
assert.equal(closed.reconciliations.find(x=>x.name===targets[0]).result,'removed-observed');
assert(closed.reconciliations.filter(x=>x.name!==targets[0]).every(x=>x.result==='unattributed-absence'));
assert.equal(closed.reason,fs.readFileSync(reason,'utf8').trim());
assert(!fs.existsSync(pending));
assert.deepEqual(fs.readFileSync(siblingDir+'/receipt.json'),siblingBytes,'closing one operation never rewrites another');
assert.deepEqual(fs.readFileSync(file),bytes,'closure preserves exact original journal');
const again=close();assert.equal(again.code,0,again.text);
assert.deepEqual(fs.readFileSync(closure),closedBytes,'idempotent closure is append-only');
assert.equal(guard(),0,'Closed plus inert sibling and current absence admits');
const old=JSON.parse(siblingBytes);old.state='PartialRefused';old.consumed=true;old.owner=null;
old.intents=[{name:old.targets[0],intent:'remove',result:'pending'}];old.effects=[];
write(siblingDir+'/receipt.json',old);
const oldBytes=fs.readFileSync(siblingDir+'/receipt.json');
assert.notEqual(guard(),0,'a later Closed operation cannot erase an older unresolved intent');
const authority=root+'/close-authority.json';
write(authority,{schema:1,actor:'commissioned-delegate',authorityRef:'fixture-task-J8',operationId:sibling});
const delegated=close('',hash(oldBytes),true,{op:sibling,authorityFile:authority});
assert.equal(delegated.code,0,'commissioned delegate uses the same actual Close gate: '+delegated.text);
assert.deepEqual(fs.readFileSync(siblingDir+'/receipt.json'),oldBytes);
assert.equal(JSON.parse(fs.readFileSync(siblingDir+'/closure.json')).authority,'fixture-task-J8');
assert.equal(guard(),0,'only resolving the older operation clears its own blocker');
const calls=fs.readFileSync(root+'/docker-trace.jsonl','utf8').trim().split('\n').map(JSON.parse);
assert(!calls.some(a=>['rm','stop','kill'].includes(a[0])||a[0]==='volume'&&['rm','create','prune'].includes(a[1])), 'Close has zero target effects');
const evidence={schema:1,operation:op,vectors,trace,receiptDigest:hash(bytes),closureDigest:hash(closedBytes)};
if(process.env.ANTIPHON_TEST_PHASE_ROOT){fs.mkdirSync(process.env.ANTIPHON_TEST_PHASE_ROOT,{recursive:true});fs.writeFileSync(path.join(process.env.ANTIPHON_TEST_PHASE_ROOT,'close-'+crypto.randomUUID()+'.json'),JSON.stringify(evidence));}
console.log('C1609_CLOSE passed refusals='+vectors+' history=unchanged effects=0');
