import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import cp from 'node:child_process';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {boundedChild, ownedDaemon} from './c994-real-custody.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const cli = '/usr/local/bin/docker';
const label = 'io.antiphon.c1609-real';
const q = value => "'" + String(value).replaceAll("'", "'\\''") + "'";
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const argv = process.argv.slice(2);
const rootArg = () => argv[argv.indexOf('--root') + 1];
let scope;
function call(file, args, allowFailure = false, timeout = 90000) {
  const started = performance.now();
  const r = cp.spawnSync(file, args, {encoding:'utf8', timeout, maxBuffer:8 * 1024 * 1024});
  if (scope) fs.appendFileSync(scope.root + '/commands.jsonl', JSON.stringify({
    file, args:args.map((a,i) => i && args[i-1] === '-c' ? {bytes:Buffer.byteLength(a),sha256:hash(a)} : a),
    exit:r.status, milliseconds:performance.now()-started, stdoutBytes:Buffer.byteLength(r.stdout || ''),
    stderrBytes:Buffer.byteLength(r.stderr || ''), phase:scope.phase || 'case'
  }) + '\n');
  if (!allowFailure && (r.error || r.status !== 0)) throw Error('FixtureCLI ' + file + ' exit=' + r.status + ' ' + (r.stderr || r.error?.message || ''));
  return {code:r.status, stdout:r.stdout || '', stderr:r.stderr || ''};
}
const d = (...args) => call(cli, args).stdout.trim();
const json = (...args) => JSON.parse(d(...args));
function save() { fs.writeFileSync(scope.root + '/ownership.json', JSON.stringify(scope), {mode:0o600}); }
function load(root) {
  assert.match(root || '', /^\/tmp\/c1609-real-[A-Za-z0-9]+$/);
  assert.equal(fs.realpathSync(root), root);
  const info = fs.lstatSync(root + '/ownership.json');
  assert(info.isFile() && !info.isSymbolicLink() && info.nlink === 1);
  const value = JSON.parse(fs.readFileSync(root + '/ownership.json'));
  assert.equal(value.schema, 1); assert.equal(value.root, root); assert.match(value.prefix, /^c1609rd[0-9a-f]{16}$/);
  assert.match(value.source, /^[0-9a-f]{40}$/);
  assert.equal(value.hostRoot, '/tmp/' + value.prefix + '-host');
  scope = value;
  ownedDaemon(d('info','--format','{{.Name}}'), os.hostname(), fs.existsSync('/.dockerenv'));
  assert.equal(d('info','--format','{{.ID}}'), value.daemon);
}
function known(name) {
  assert(name.startsWith(scope.prefix) && !name.includes('antiphon-runner') && /^[a-zA-Z0-9_.:-]+$/.test(name), 'ProductionNameRefused');
  return name;
}
function rootRun(text, allowFailure = false) {
  return call(cli, ['exec',scope.helper,'nsenter','-t','1','-m','-r','-w','--','bash','-ceu',text],allowFailure);
}
function rootRead(file) {
  assert(file.startsWith(scope.hostRoot + '/'));
  return rootRun('cat -- ' + q(file)).stdout;
}
function ownedVolume(name) {
  const v = json('volume','inspect', known(name))[0];
  assert.equal(v.Labels?.[label], scope.prefix);
  return v;
}
function createVolume(name, project, role) {
  known(name); scope.volumes.push(name); save();
  d('volume','create','--label',label+'='+scope.prefix,'--label','com.docker.compose.project='+project,
    '--label','com.docker.compose.volume='+role,name);
}
function removeContainer(id) {
  const facts = json('inspect', id)[0];
  assert.equal(facts.Config.Labels?.[label], scope.prefix);
  d('rm','-f','--',id);
}
function cleanupObjects(names, ids) {
  for (const id of ids) if (call(cli,['inspect',id],true).code === 0) removeContainer(id);
  const present = new Set(d('volume','ls','-q').split('\n'));
  for (const name of names) if (present.has(name)) { ownedVolume(name); d('volume','rm','--',name); }
  for (const name of names) assert(!new Set(d('volume','ls','-q').split('\n')).has(name), 'FixtureVolumeResidue');
}
async function prepare() {
  ownedDaemon(d('info','--format','{{.Name}}'), os.hostname(), fs.existsSync('/.dockerenv'));
  const root = fs.mkdtempSync('/tmp/c1609-real-');
  fs.chmodSync(root,0o700);
  const prefix = 'c1609rd' + crypto.randomBytes(8).toString('hex');
  scope = {schema:1,root,prefix,source:call('git',['rev-parse','HEAD']).stdout.trim(),
    daemon:d('info','--format','{{.ID}}'),hostRoot:'/tmp/'+prefix+'-host',image:prefix+':audit',volumes:[],phase:'prepare'};
  save(); console.log('C1609_REAL_ROOT=' + root);
  fs.writeFileSync(root+'/Dockerfile',
    'FROM debian:trixie-slim\nLABEL '+label+'='+prefix+'\nRUN apt-get update && apt-get install -y --no-install-recommends bash git coreutils findutils util-linux ca-certificates jq && rm -r /var/lib/apt/lists/* && printf image-copy-sentinel > /tmp/c1609-image-asset\n');
  call(cli,['build','-t',scope.image,root],false,120000);
  scope.digest=json('image','inspect',scope.image)[0].Id;
  scope.helper=d('run','-d','--privileged','--pid','host','--network','none','--name',prefix+'-metadata',
    '--label',label+'='+prefix,scope.image,'sleep','infinity');
  save();
  rootRun('umask 077; mkdir ' + q(scope.hostRoot) + '; chmod 0700 ' + q(scope.hostRoot));
  fs.mkdirSync(root+'/bin'); fs.mkdirSync(root+'/inspects');
  fs.writeFileSync(root+'/docker-calls.jsonl','');
  fs.writeFileSync(root+'/bin/docker','#!/bin/sh\nexec /usr/local/bin/node '+q(fileURLToPath(import.meta.url))+' --docker-shim --root '+q(root)+' -- "$@"\n',{mode:0o755});
  const origin=root+'/origin.git', seed=root+'/seed';
  call('git',['init','-q','--bare',origin]); call('git',['-C',origin,'symbolic-ref','HEAD','refs/heads/main']);
  call('git',['init','-q','-b','main',seed]);
  call('git',['-C',seed,'config','user.name','Fixture']); call('git',['-C',seed,'config','user.email','fixture@example.invalid']);
  fs.writeFileSync(seed+'/file','published A\n');
  call('git',['-C',seed,'add','file']); call('git',['-C',seed,'-c','commit.gpgsign=false','commit','-qm','A']);
  call('git',['-C',seed,'push','-q',origin,'HEAD:refs/heads/main']);
  scope.a=call('git',['-C',seed,'rev-parse','HEAD']).stdout.trim();
  call('git',['-C',seed,'remote','add','origin','https://fixture.invalid/card-1609/native.git']);
  const publisher=root+'/publisher';
  call('git',['clone','-q',origin,publisher]);
  fs.writeFileSync(publisher+'/file','published B\n');
  call('git',['-C',publisher,'-c','user.name=Fixture','-c','user.email=fixture@example.invalid','-c','commit.gpgsign=false','commit','-qam','B']);
  call('git',['-C',publisher,'push','-q','origin','HEAD:refs/heads/main']);
  scope.b=call('git',['-C',publisher,'rev-parse','HEAD']).stdout.trim();
  call('git',['-C',publisher,'merge-base','--is-ancestor',scope.a,scope.b]);
  assert.notEqual(call('git',['-C',seed,'cat-file','-e',scope.b+'^{commit}'],true).code,0);
  save();
}
function fixture(id) {
  const stem=scope.prefix+'-'+id.toLowerCase()+'-'+crypto.randomBytes(4).toString('hex');
  const f={stem,project:stem+'temp',main:stem+'main',op:'c1008'+crypto.randomBytes(16).toString('hex'),host:scope.hostRoot+'/'+stem,ids:[]};
  f.roles=['runner-state','dind-data','runner-tmp','work'];
  f.targets=f.roles.map(r=>f.project+'_'+r);
  f.kept=['work','runner-tmp','dind-data','runner-state'].map(r=>f.main+'_'+r)
    .concat(['nuget-packages','nuget-scratch','npm-content'].map(r=>stem+'cache-'+r),[stem+'sentinel']);
  f.names=f.targets.concat(f.kept);
  for(let i=0;i<f.names.length;i++)createVolume(f.names[i],i<4?f.project:f.main,i<4?f.roles[i]:'preserved');
  d('run','--rm','--network','none','--label',label+'='+scope.prefix,
    '--mount','type=bind,source='+scope.root+'/seed,target=/seed,readonly',
    ...f.names.flatMap((n,i)=>['--mount','type=volume,source='+n+',target=/v'+i+',volume-nocopy']),
    scope.image,'bash','-ceu',
    f.names.map((n,i)=>'chmod 0777 /v'+i+'; chown 1654:1654 /v'+i+(i===2?'':'; printf '+q(n)+' > /v'+i+'/sentinel')).join('; ') +
    '; mkdir -p /v3/repos; cp -a /seed /v3/repos/outer; chown -R 1654:1654 /v3/repos');
  rootRun('umask 077; mkdir -p '+q(f.host+'/server/retirement')+' '+q(f.host+'/evidence')+'; chmod 0700 '+q(f.host+'/server'));
  f.before=Object.fromEntries(f.kept.map(n=>[n,ownedVolume(n)]));
  return f;
}
const originURL='https://fixture.invalid/card-1609/native.git';
function proof(f) {
  const dir=scope.root+'/'+f.stem+'-proof'; fs.mkdirSync(dir);
  const bin=dir+'/bin';fs.mkdirSync(bin);
  fs.writeFileSync(bin+'/git','#!/bin/bash\nargs=(); for a in "$@"; do if [ "$a" = '+q(originURL)+' ]; then args+=('+q(scope.root+'/origin.git')+'); else args+=("$a"); fi; done\nexec /usr/local/bin/git "$'+'{args[@]}"\n',{mode:0o755});
  f.proof=dir+'/proof';fs.mkdirSync(f.proof);
  const psi=call('pwsh',['-NoProfile','-NonInteractive','-Command',
    '$ErrorActionPreference="Stop"; $env:PATH='+q(bin+':'+process.env.PATH)+'; . '+q(repo+'/scripts/c1008-host-proof.ps1')+
    '; Write-C1008HostOriginProof -OriginUrl '+q(originURL)+' -RepoRelative repos/outer -OperationId '+q(f.op)+
    ' -ProofDirectory '+q(f.proof)+' -CreatedUnix '+Math.floor(Date.now()/1000)+' -LogPath '+q(dir+'/proof.log')]);
  assert.equal(psi.code,0);
  f.started=Math.floor(Date.now()/1000)-5;
}
function shell(f, body, suffix='run') {
  fs.writeFileSync(scope.root+'/current-operation.json',JSON.stringify({op:f.op,receipt:f.host+'/server/retirement/'+f.op+'/receipt.json'}));
  let src=fs.readFileSync(repo+'/scripts/c590-remote.sh','utf8');
  const marker="trap 'ec=$?; if [ \"$WROTE\" != 1 ]";
  assert.equal(src.split(marker).length,2,'unique real entrypoint injection');
  const lines=[
    'LANE=host','HOST_PROJECT='+q(f.main),'TEMP_PROJECT='+q(f.project),'C1008_PROJECT='+q(f.project),
    'SERVER2_ROOT='+q(f.host+'/server'),'CASE_DIR='+q(f.host+'/evidence'),
    'C1008_OPERATION='+q(f.op),'C1008_RETIRED_AT=2026-10-03T09:30:00Z',
    'C1008_TARGETS=('+f.targets.map(q).join(' ')+')',
    'C1008_RECORD='+q(JSON.stringify({image:scope.digest,project:f.project,volumes:{}})),
    'C1609_CONTENT_AUDIT_FILE='+q(repo+'/scripts/c1008-content-audit.sh'),
    'C1609_VOLUME_METADATA_FILE='+q(repo+'/scripts/c1008-volume-metadata.sh'),
    'source '+q(repo+'/scripts/c1008-retirement.sh'),
    'sudo() { [ "$1" = -n ] && shift; if [ "$1" = install ]; then for last; do :; done; mkdir -p -- "$last"; chmod 0700 "$last"; else "$@"; fi; }',
    'write_result() { WROTE=1; printf "NATIVE_RESULT %s %s %s\\n" "$1" "$2" "$3"; exit "$3"; }',
    'c1008_status_proof() { C1008_CENSUS="$(c1008_container_census)" || c1008_refuse RecycleVolumeCensusUnknown; }',
    'export PATH='+q(scope.root+'/bin')+':"$PATH"',
    'export C1609_CUT_PID=$$',
    f.proof?'C1008_HOST_PROOF='+q(f.proof)+'; C1008_OPERATION_STARTED='+f.started:'',
    body,'exit 0'
  ].join('\n');
  src=src.replace(marker,lines+'\n'+marker);
  const filename=scope.root+'/'+f.stem+'-'+suffix+'.sh';
  fs.writeFileSync(filename,'export C590_CASE=retire-temp-runner C590_SHA='+q(scope.source)+' C590_RUN=native C590_REEXEC=1\n'+src,{mode:0o600});
  return boundedChild(cli,['exec',scope.helper,'nsenter','-t','1','-m','-r','-w','--','bash',filename],{},180000);
}
function receipt(f) {return JSON.parse(rootRead(f.host+'/server/retirement/'+f.op+'/receipt.json'));}
const auditReady=[
  'c1008_lock',
  'c1609_state_credential_scan || c1008_refuse RecycleGitAuditUnknown',
  'c1609_load_volume_metadata; c1609_role_metadata || c1008_refuse RecycleGitAuditUnknown',
  'c1008_audit_readonly > "$CASE_DIR/preview-audit.out" || { cat "$CASE_DIR/preview-audit.out"; exit 2; }',
  'c1609_write_ready_receipt || exit 2'
].join('\n');
async function ready(f) {const r=await shell(f,auditReady,'preview');assert.equal(r.code,0,'real preview: '+r.stdout+r.stderr);}
function preserve(f) {
  for (const name of f.kept) assert.deepEqual(ownedVolume(name),f.before[name],'preserved incarnation '+name);
  const out=d('run','--rm','--network','none','--user','1654:1654','--label',label+'='+scope.prefix,
    ...f.kept.flatMap((n,i)=>['--mount','type=volume,source='+n+',target=/v'+i+',readonly,volume-nocopy']),
    scope.image,'bash','-ceu',f.kept.map((n,i)=>'cat /v'+i+'/sentinel; printf "\\n"').join('; '));
  assert.deepEqual(out.split('\n'),f.kept,'main/all three caches/sentinel bytes');
}
async function n1(f) {
  proof(f);
  const probe=[
    'set -euo pipefail',
    'printf "N1_KERNEL_PROBE\\n"',
    '[ "$(id -u):$(id -g)" = 1654:1654 ] || { echo N1_UID; exit 70; }',
    'for n in /sys/class/net/*; do [ "$'+'{n##*/}" = lo ] || { echo N1_OFFLINE; exit 70; }; done',
    'grep -Eq " / [^ ]* ro[, ]" /proc/mounts || { echo N1_ROOTFS; exit 70; }',
    'for p in /work /tmp; do if touch "$p/n1-write" 2>/audit-scratch/write-error; then echo N1_WRITABLE; exit 70; fi; grep -q "Read-only file system" /audit-scratch/write-error; done',
    '[ ! -S /var/run/docker.sock ]',
    '! env | cut -d= -f1 | grep -Ei "(TOKEN|PASSWORD|SECRET|API_KEY|SSH_AUTH)"',
    'for n in $(seq 1 100); do [ ! -e /audit-scratch/n1-release ] || break; sleep 0.02; done',
    '[ -e /audit-scratch/n1-release ]'
  ].join('\n');
  const original='orig_program="$(declare -f c1008_git_program)"; eval "$(printf "%s" "$orig_program" | sed "1s/c1008_git_program/native_original/")"\nc1008_git_program() { printf "%s\\n" '+q(probe)+'; native_original; }\n';
  const r=await shell(f,original+'c1008_audit_readonly','kernel');
  assert.equal(r.code,0,'N1 kernel namespace and real host proof: '+r.stdout+r.stderr);
  assert(r.stdout.includes('repositories=1'),'real scanner reached the real published repository');
  const facts=fs.readdirSync(scope.root+'/inspects').map(n=>JSON.parse(fs.readFileSync(scope.root+'/inspects/'+n)))
    .filter(c=>c.Config.Labels['io.antiphon.preview-audit']?.startsWith(f.op+':'));
  assert(facts.length>0,'captured actual owned helper inspect');
  const fact=facts[0];
  assert.equal(fact.HostConfig.NetworkMode,'none');assert.equal(fact.HostConfig.ReadonlyRootfs,true);
  for(const target of ['/work','/tmp']){
    const m=fact.Mounts.find(m=>m.Destination===target);assert(m && !m.RW,'kernel read-only '+target);
    const declaration=fact.HostConfig.Mounts.find(m=>m.Target===target);assert.equal(declaration.VolumeOptions.NoCopy,true);
  }
  assert.equal(d('run','--rm','--network','none','--label',label+'='+scope.prefix,'--mount','type=volume,source='+f.targets[2]+',target=/check,readonly,volume-nocopy',scope.image,'find','/check','-mindepth','1'),'', 'initial empty tmp stays empty');
  // Real absolute linked pointer. No helper-side path rewriting is used here.
  d('run','--rm','--network','none','--user','1654:1654','--label',label+'='+scope.prefix,
    '--mount','type=volume,source='+f.targets[3]+',target=/work,volume-nocopy',
    '--mount','type=volume,source='+f.targets[2]+',target=/tmp,volume-nocopy',scope.image,
    'git','-C','/work/repos/outer','worktree','add','--detach','/tmp/linked','HEAD');
  const linked=await shell(f,'c1008_audit_readonly','linked');
  assert.equal(linked.code,0,'native /tmp linked worktree: '+linked.stdout+linked.stderr);
  assert(linked.stdout.includes('/tmp/linked'),'linked checkout actually visited');
  const foreign=d('run','-d','--network','none','--label',label+'='+scope.prefix,'--name',f.stem+'-foreign-helper',scope.image,'sleep','infinity');f.ids.push(foreign);
  const timed=await shell(f,'c1008_git_program() { printf "exec sleep 30\\n"; }\nC1008_AUDIT_TIMEOUT_SECONDS=1\nif c1008_audit_readonly; then exit 71; fi','cancel');
  assert.equal(timed.code,0,'owned helper timeout refuses and joins: '+timed.stdout+timed.stderr);
  assert.equal(json('inspect',foreign)[0].State.Running,true,'foreign helper not killed');
  assert.equal(d('ps','-aq','--filter','label=io.antiphon.preview-audit','--filter','name=c1008-preview-'+f.op),'','owned helper absent after timeout');
  preserve(f);
}
async function n2(f) {
  proof(f);await ready(f);
  const old=receipt(f);
  ownedVolume(f.targets[0]);d('volume','rm','--',f.targets[0]);createVolume(f.targets[0],f.project,f.roles[0]);
  const replaced=await shell(f,'c1008_lock; c1609_require_ready_preview','replacement');
  assert.notEqual(replaced.code,0,'N2 same name is not the same incarnation');
  assert.deepEqual(receipt(f),old,'replacement cannot consume receipt');ownedVolume(f.targets[0]);
  // A fresh operation supplies independent named and bind-reference controls.
  const g=fixture('N2refs');proof(g);await ready(g);
  try {
    for(const type of ['volume','bind']){
      const source=type==='volume'?g.targets[3]:ownedVolume(g.targets[3]).Mountpoint;
      const id=d('run','-d','--network','none','--label',label+'='+scope.prefix,'--mount','type='+type+',source='+source+',target=/foreign,readonly'+(type==='volume'?',volume-nocopy':''),scope.image,'sleep','infinity');
      g.ids.push(id);
      const blocked=await shell(g,'c1008_lock; c1008_status_proof; c1008_references "$C1008_CENSUS" "[]"','ref-'+type);
      assert.notEqual(blocked.code,0,'N2 real '+type+' reference blocks');assert(blocked.stdout.includes('RecycleVolumeInUse'));
      removeContainer(id);
    }
    const clean=await shell(g,'c1008_lock; c1008_status_proof; c1008_references "$C1008_CENSUS" "[]"; c1609_require_ready_preview','no-reference');
    assert.equal(clean.code,0,'N2 unreferenced control: '+clean.stdout+clean.stderr);
    // Real kernel flock, with a real Docker create after the supported lock.
    const held=g.host+'/held',release=g.host+'/release',entered=g.host+'/entered';
    const holder=shell(g,'c1008_rollout_lock; touch '+q(held)+'; for n in $(seq 1 1000); do [ ! -e '+q(release)+' ] || exit 0; sleep 0.02; done; exit 72','holder');
    await barrier(()=>rootRun('test -f '+q(held),true).code===0,holder);
    const created=g.stem+'-writer-created'; scope.volumes.push(created);g.names.push(created);save();
    const writer=shell(g,'c1008_rollout_lock; docker volume create --label '+q(label+'='+scope.prefix)+' '+q(created)+'; touch '+q(entered),'writer');
    try {
      // A nonblocking independent flock probe proves the lock is held, without a timing guess.
      assert.notEqual(rootRun('flock -n '+q(g.host+'/server/locks/rollout.lock')+' true',true).code,0);
      assert.notEqual(rootRun('test -e '+q(entered),true).code,0);
    } finally {rootRun('touch '+q(release));await holder;const w=await writer;assert.equal(w.code,0,'supported writer resumes');}
    preserve(g);
  } finally {cleanupObjects(g.names,g.ids);}
  preserve(f);
}
async function barrier(predicate, running) {
  let settled=false;running.finally(()=>{settled=true;});
  const start=performance.now();
  while(!settled && performance.now()-start<10000){try{if(predicate())return;}catch{}await new Promise(r=>setTimeout(r,20));}
  assert.fail('owned barrier not reached');
}
const sourceText=fs.readFileSync(repo+'/scripts/c590-remote.sh','utf8');
const effectStart='    for name in "$'+'{C1008_TARGETS[@]}"; do\n        c1008_status_proof; c1008_references "$C1008_CENSUS" \'[]\'';
const effectEnd='    after="$(c1008_disk)" || { after=null; code=2; }';
function effectBody() {
  const start=sourceText.indexOf(effectStart),end=sourceText.indexOf(effectEnd,start);
  assert(start>=0 && end>start && sourceText.indexOf(effectStart,start+1)<0,'exact real removal-loop extraction');
  return sourceText.slice(start,end);
}
const legacySeed=[
  'mkdir -p "$SERVER2_ROOT/recycle"',
  'C1008_JOURNAL="$SERVER2_ROOT/recycle/$C1008_OPERATION.json"',
  'volumes="{}"; for name in "$'+'{C1008_TARGETS[@]}"; do facts="$(c1008_volume "$name")"; volumes="$(printf "%s" "$volumes" | jq -c --arg name "$name" --argjson facts "$facts" \'.[$name]={original:$facts,outcome:"pending"}\')"; done',
  'C1008_RECORD="$(jq -cn --arg project "$C1008_PROJECT" --argjson volumes "$volumes" \'{schema:1,project:$project,phase:"removing",volumes:$volumes}\')"',
  'C1008_ACTIVE=1; C1609_APPLY_ACTIVE=1; c1008_save'
].join('\n');
async function n3(f) {
  proof(f);await ready(f);
  const applied=await shell(f,'c1008_lock; c1609_require_ready_preview || exit 2\n'+legacySeed+'\n'+effectBody(),'apply');
  assert.equal(applied.code,0,'N3 exact real delete loop: '+applied.stdout+applied.stderr);
  assert.equal(receipt(f).state,'Completed');for(const n of f.targets)assert.equal(call(cli,['volume','inspect',n],true).code,1);
  preserve(f);
  for(const cut of ['after-rm','replacement','no-intent']){
    const g=fixture('N3'+cut.replaceAll('-',''));proof(g);await ready(g);
    try {
      const fault={operation:g.op,name:g.targets[0],cut:cut==='no-intent'?'none':'kill-after-rm'};
      fs.writeFileSync(scope.root+'/fault.json',JSON.stringify(fault));
      const body='c1008_lock; c1609_require_ready_preview || exit 2\n'+legacySeed+'\n'+
        (cut==='no-intent'?'command '+q(cli)+' volume rm -- '+q(g.targets[0])+'; exit 0':effectBody());
      const first=await shell(g,body,'crash');
      fs.writeFileSync(scope.root+'/fault.json','{}');
      if(cut!=='no-intent')assert.notEqual(first.code,0,'N3 crash occurred after real rm');
      if(cut==='replacement')createVolume(g.targets[0],g.project,g.roles[0]);
      const resumeBody='c1008_lock; c1008_audit_readonly > "$CASE_DIR/preview-audit.out" || exit 2; c1609_load_volume_metadata; c1609_role_metadata || exit 2; c1609_state_credential_scan || exit 2; c1609_prepare_resume || exit 2; C1609_RESUME_VALIDATED=1\n'+
        'C1008_JOURNAL="$SERVER2_ROOT/recycle/$C1008_OPERATION.json"; C1008_RECORD="$(cat "$C1008_JOURNAL")"; C1008_ACTIVE=1; C1609_APPLY_ACTIVE=1\n'+effectBody();
      const resumed=await shell(g,resumeBody,'resume');
      if(cut==='after-rm'){
        assert.equal(resumed.code,0,'N3 resumes actual durable intent: '+resumed.stdout+resumed.stderr);
        const r=receipt(g);assert.equal(r.state,'Completed');assert.equal(r.effects.find(e=>e.name===g.targets[0]).result,'removed-observed');
      } else {
        assert.notEqual(resumed.code,0,'N3 replacement/unattributed absence refuse');
        for(const n of g.targets.slice(1))ownedVolume(n);
        if(cut==='replacement')ownedVolume(g.targets[0]);
      }
      preserve(g);
    } finally {cleanupObjects(g.names,g.ids);}
  }
  const calls=fs.readFileSync(scope.root+'/docker-calls.jsonl','utf8').trim().split('\n').filter(Boolean).map(l=>JSON.parse(l));
  const removes=calls.filter(c=>c.args[0]==='volume'&&c.args[1]==='rm');
  assert(removes.length>=4);assert(removes.every(c=>c.args.length===4&&c.args[2]==='--'),'N3 only exact non-force deletion');
  assert(removes.every(c=>c.intentBeforeRm),'N3 every real delete has a durable intent');
}
async function shim() {
  load(rootArg());
  const args=argv.slice(argv.indexOf('--')+1),original=[...args];
  if(args[0]==='ps')args.push('--filter','label='+label+'='+scope.prefix);
  if(args[0]==='run')args.splice(1,0,'--label',label+'='+scope.prefix);
  let intentBeforeRm=null;
  if(args[0]==='volume'&&args[1]==='rm'){
    assert.equal(args[2],'--');for(const name of args.slice(3))ownedVolume(name);
    const factFile=scope.root+'/fault.json';
    const fault=fs.existsSync(factFile)?JSON.parse(fs.readFileSync(factFile)):{};
    const opFile=scope.root+'/current-operation.json';
    const operation=JSON.parse(fs.readFileSync(opFile));
    const record=JSON.parse(fs.readFileSync(operation.receipt));
    intentBeforeRm=record.intents?.some(i=>i.name===args[3]&&i.intent==='remove'&&i.result==='pending')===true;
    fs.appendFileSync(scope.root+'/docker-calls.jsonl',JSON.stringify({args:original,intentBeforeRm})+'\n');
    const r=call(cli,args,true);
    process.stdout.write(r.stdout);process.stderr.write(r.stderr);
    if(r.code===0&&fault.cut==='kill-after-rm'&&fault.operation===operation.op&&fault.name===args[3])process.kill(Number(process.env.C1609_CUT_PID),'SIGKILL');
    process.exit(r.code ?? 2);
  }
  const child=cp.spawn(cli,args,{stdio:['inherit','inherit','inherit']});
  let captured=false;
  const cidAt=args.indexOf('--cidfile');
  const timer=setInterval(()=>{
    if(captured||cidAt<0)return;
    const file=args[cidAt+1];
    try {
      const id=fs.readFileSync(file,'utf8').trim();if(!/^[0-9a-f]{64}$/.test(id))return;
      const c=json('inspect',id)[0];assert.equal(c.Config.Labels[label],scope.prefix);
      fs.writeFileSync(scope.root+'/inspects/'+id+'.json',JSON.stringify(c));captured=true;
      call(cli,['exec','--user','1654:1654',id,'touch','/audit-scratch/n1-release'],true);
    } catch {}
  },20);
  const code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});
  clearInterval(timer);process.exit(code ?? 2);
}
if(argv[0]==='--docker-shim')await shim();
else if(argv[0]==='--prepare')await prepare();
else {
  load(rootArg());
  if(argv[0]==='--cleanup'){
    scope.phase='teardown';
    const ids=d('ps','-aq','--no-trunc','--filter','label='+label+'='+scope.prefix).split('\n').filter(Boolean);
    cleanupObjects([...new Set(scope.volumes)],ids);
    const image=json('image','inspect',scope.image)[0];assert.equal(image.Id,scope.digest);assert.equal(image.Config.Labels[label],scope.prefix);
    d('image','rm',scope.image);
    assert.equal(d('ps','-aq','--filter','label='+label+'='+scope.prefix),'');
    assert.equal(d('volume','ls','-q','--filter','label='+label+'='+scope.prefix),'');
    scope.cleaned=true;save();console.log('C1609_NATIVE session cleanup=absent root='+scope.root);
  } else {
    assert.equal(argv[0],'--case');const id=argv[1];assert(['N1','N2','N3'].includes(id));
    scope.imageSource=scope.imageSource || scope.source;
    scope.source=call('git',['rev-parse','HEAD']).stdout.trim();scope.phase='case';save();
    const f=fixture(id);let failure;
    // Update the shim's operation binding for every exact real source invocation.
    try {await ({N1:n1,N2:n2,N3:n3}[id])(f);}
    catch(e){failure=e;console.error(e.stack);}
    finally {cleanupObjects(f.names,f.ids);}
    fs.writeFileSync(scope.root+'/'+id+'-'+scope.source.slice(0,12)+'-'+f.stem+'.json',JSON.stringify({source:scope.source,imageSource:scope.imageSource,id,failure:failure?.message||null,phase:'case',cleanup:'absent',shims:['private project names','fixture-scoped complete Docker census','deterministic live status; Docker references real','owned root metadata namespace']},null,2));
    if(failure)process.exitCode=1;else console.log('C1609_NATIVE '+id+' passed cleanup=absent evidence='+scope.root+'/'+id+'-'+scope.source.slice(0,12)+'-'+f.stem+'.json');
  }
}
