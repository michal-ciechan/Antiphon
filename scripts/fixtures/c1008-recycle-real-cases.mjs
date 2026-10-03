import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import cp from 'node:child_process';
import crypto from 'node:crypto';
import http from 'node:http';
import util from 'node:util';

// Ordinary RD evidence only. This fixture never runs from a SourceLanding snapshot.
const exec = util.promisify(cp.execFile);
const docker = (...args) => cp.execFileSync('docker', args, {encoding:'utf8', timeout:90000}).trim();
if (!fs.existsSync('/.dockerenv') || docker('info','--format','{{.Name}}') !== os.hostname()) throw Error('SiblingDaemonRefused');
const root = fs.mkdtempSync('/tmp/c1008-real-');
console.log('C1008_REAL_ROOT=' + root);
const prefix = 'c1008rd' + crypto.randomBytes(8).toString('hex');
const label = 'io.antiphon.c1008-real';
const source = cp.execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim();
const base = 'c6d5d56b5b4c565d36157e21de9c85029cc45b53';
const scripts = {B:cp.execFileSync('git',['show',base+':scripts/c590-remote.sh'],{encoding:'utf8'}), C:fs.readFileSync('scripts/c590-remote.sh','utf8')};
const roles = ['work','runner-tmp','dind-data','runner-state'];
const cacheRoles = ['nuget-packages','nuget-scratch','npm-content'];
const mounts = ['/work','/tmp','/var/lib/docker','/state','/home/app/.nuget/packages','/var/cache/antiphon/nuget-scratch','/home/app/.npm/_cacache'];
const objects = {containers:new Set(), volumes:new Set(), networks:new Set(), images:new Set()};
const results = [], commands = [];
const guard = name => {
  if (!name.startsWith(prefix) || name.includes('antiphon-runner') || /[\s;/'"`$]/.test(name)) throw Error('ProductionNameRefused');
  return name;
};
for (const name of ['antiphon-runner','antiphon-runner-temp','antiphon-runner_work','antiphon-runner-temp_work','antiphon-runner-cache-npm-content']) {
  let refused=false; try {guard(name);} catch {refused=true;} if (!refused) throw Error('NameFirewallMissing');
}
const runDocker = (...args) => {commands.push(args); return docker(...args);};
const image = guard(prefix + ':fixture');
const hash = text => crypto.createHash('sha256').update(text).digest('hex');
const volumeFacts = name => JSON.parse(runDocker('volume','inspect',guard(name)))[0];
const allVolumes = () => runDocker('volume','ls','-q').split('\n').filter(Boolean);
const containers = project => runDocker('ps','-aq','--no-trunc','--filter','label=com.docker.compose.project='+guard(project)).split('\n').filter(Boolean);
let helper, active, failure, realDf;
const vectors = JSON.parse(fs.readFileSync('scripts/fixtures/c1008-recycle-cases.json','utf8'));
const endpoint = http.createServer((req,res) => {
  const u=new URL(req.url,'http://fixture.invalid'); res.setHeader('Content-Type','application/json');
  let body;
  if (u.pathname === '/api/agent-tasks') body={...vectors.emptyTasks,scope:{projectId:u.searchParams.get('projectId'),unscoped:'include'}};
  else if (u.pathname.startsWith('/api/session-runners/')) {
    const runner=u.pathname.split('/')[3]; body=structuredClone(active.statuses[runner]);
    if (runner === 'server2' && active.mainReplacement) {
      const ids=containers(active.main).filter(id=>JSON.parse(docker('inspect',id))[0].Config.Labels['com.docker.compose.service']==='session-runner');
      const live=ids.some(id=>JSON.parse(docker('inspect',id))[0].State.Running);
      if (!live) Object.assign(body,{runnerSessions:null,available:false,dispatchEligible:false});
      else if (ids[0]!==active.originalRunner) body.buildVersion=source;
    }
  } else {res.statusCode=404; body={};}
  res.end(JSON.stringify(body));
});
await new Promise(resolve=>endpoint.listen(0,'127.0.0.1',resolve));
const originUrl='http://127.0.0.1:'+endpoint.address().port;
const shellRoot = (...args) => runDocker('exec',helper,'nsenter','-t','1','-m','-r','-w','--',...args);

function createVolume(name, labels) {
  runDocker('volume','create','--label',label+'='+prefix,...labels.flatMap(x=>['--label',x]),guard(name)); objects.volumes.add(name);
}
function createContainer(f, service, running, names, project=f.main, overrideName='') {
  const args=['run','-d','--network','none','--name',guard(overrideName||f.prefix+'-'+service+'-'+crypto.randomBytes(3).toString('hex')),
    '--label',label+'='+prefix,'--label','com.docker.compose.project='+guard(project),'--label','com.docker.compose.service='+service];
  names.forEach((name,i)=>args.push('--mount','type=volume,source='+guard(name)+',target='+mounts[i]));
  const id=runDocker(...args,image,'sleep','infinity'); objects.containers.add(id);
  if (!running) runDocker('stop','--time','1','--',id);
  return id;
}
function fixture(mainReplacement=true) {
  const p=prefix+'c'+crypto.randomBytes(4).toString('hex');
  const f={prefix:p,main:p+'main',temp:p+'temp',root:path.join(root,p),mainReplacement,op:'c1008'+crypto.randomBytes(16).toString('hex')};
  f.caches=cacheRoles.map(r=>p+'cache-'+r);
  f.names=[...roles.map(r=>f.main+'_'+r),...roles.map(r=>f.temp+'_'+r),...f.caches,p+'main_work-extra',p+'schoolrevision-staging',p+'openclaw-state'];
  f.origin=p+'origin'; f.statuses={'server2':structuredClone(vectors[mainReplacement?'mainDrained':'mainAccepting']), 'server2-temp':structuredClone(vectors[mainReplacement?'tempAccepting':'tempRetiredAbsent'])};
  f.statuses.server2.buildVersion=mainReplacement?'old':source;
  fs.mkdirSync(f.root+'/server/cache',{recursive:true}); fs.mkdirSync(f.root+'/evidence',{recursive:true});
  fs.writeFileSync(f.root+'/temp.env','RUNNER_GROK_STORE_DIR=/fixture/grok\n');
  fs.writeFileSync(f.root+'/server/cache/seed-accepted','schema=2\nkind=cold\nsentinel='+p+'\n');
  for (const project of [f.main,f.temp]) {
    for (const role of roles) createVolume(project+'_'+role,['com.docker.compose.project='+project,'com.docker.compose.volume='+role]);
    const volumes=Object.fromEntries([...roles.map(r=>[r,{name:project+'_'+r}]),...cacheRoles.map((r,i)=>['runner-'+r,{name:f.caches[i],external:true}])]);
    const services={'session-runner':{image,command:['sleep','infinity'],labels:{[label]:prefix},volumes:[...roles,...cacheRoles.map(r=>'runner-'+r)].map((r,i)=>r+':'+mounts[i])},
      'state-init':{image,command:['true'],labels:{[label]:prefix},volumes:['work:/work','runner-state:/state']}};
    fs.writeFileSync(f.root+'/'+project+'.json',JSON.stringify({services,volumes}));
  }
  cacheRoles.forEach((r,i)=>createVolume(f.caches[i],['io.antiphon.owner=server2-runner','io.antiphon.cache-schema=1','io.antiphon.cache-role='+r]));
  f.names.slice(11).forEach(n=>createVolume(n,[])); createVolume(f.origin,[]);
  const args=f.names.flatMap((n,i)=>['--mount','type=volume,source='+guard(n)+',target=/v'+i]);
  runDocker('run','--rm','--network','none','--label',label+'='+prefix,...args,image,'bash','-c',f.names.map((n,i)=>`printf 'sentinel:${n}:old\\n' > /v${i}/sentinel; chown 1654:1654 /v${i}; chmod 0700 /v${i}`).join('; '));
  f.before=Object.fromEntries(f.names.map(n=>[n,volumeFacts(n)]));
  f.marker=fs.readFileSync(f.root+'/server/cache/seed-accepted','utf8');
  runDocker('compose','-p',f.main,'-f',f.root+'/'+f.main+'.json','up','-d');
  f.originalRunner=containers(f.main).find(id=>JSON.parse(docker('inspect',id))[0].Config.Labels['com.docker.compose.service']==='session-runner');
  f.broker=createContainer(f,'build-slots',true,[]);
  active=f; return f;
}
function payload(f,name,expected=true) {
  const args=['run','--rm','--network','none','--label',label+'='+prefix,'--user','1654:1654','--mount','type=volume,source='+guard(name)+',target=/data,readonly',image,'bash','-c',expected?'cat /data/sentinel':'test ! -e /data/sentinel'];
  const value=runDocker(...args); if (expected && value!==`sentinel:${name}:old`) throw Error('PayloadChanged:'+name);
}
function preservation(f) {
  const retained=[f.main+'_runner-state',...f.caches,...f.names.slice(11)];
  for (const n of retained) {if (JSON.stringify(volumeFacts(n))!==JSON.stringify(f.before[n])) throw Error('GenerationChanged:'+n); payload(f,n);}
  if (fs.readFileSync(f.root+'/server/cache/seed-accepted','utf8')!==f.marker) throw Error('MarkerChanged');
  if (!JSON.parse(runDocker('inspect',f.broker))[0].State.Running) throw Error('BrokerStopped');
}
async function host(f,name,version='C',options={}) {
  active=f;
  const hostCase=f.mainReplacement?'deploy-parent':'retire-temp-runner';
  const dest=f.root+'/evidence/'+hostCase; fs.mkdirSync(dest,{recursive:true});
  const transformed=scripts[version].replaceAll('antiphon-runner-temp',f.temp).replaceAll('antiphon-runner-cache-',f.prefix+'cache-').replaceAll('antiphon-runner',f.main);
  const compose = project => `command docker compose -p '${project}' -f '${f.root}/${project}.json' "$@"`;
  const injected=`
SERVER2_ROOT='${f.root}/server'; ROOT='${f.root}'; EVIDENCE_ROOT='${f.root}/evidence'; CASE_DIR="$EVIDENCE_ROOT/$CASE"
SERVER2_ENV='${f.root}/main.env'; SERVER2_TEMP_ENV='${f.root}/temp.env'; C849_READY="$SERVER2_ROOT/cache/seed-accepted"
C1008_CONTEXT=default; C1008_OPERATION=${f.op}; C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1; C1008_RESUME=${options.resume?1:0}
C590_TEMP_RETIRED_AT=2026-10-03T09:30:00Z; RUNNER_GIT_USER_NAME=Fixture; RUNNER_GIT_USER_EMAIL=fixture@example.invalid
mkdir -p "$CASE_DIR"
detect_lane() { LANE=host; }
ensure_dirs() { mkdir -p "$CASE_DIR"; }
ensure_checkout() { :; }; ensure_runner_boot_files() { :; }; retire_c590_leftovers() { :; }; broker_sha12() { echo aaaaaaaaaaaa; }
compose_host() { ${compose(f.main)}; }; compose_temp() { ${compose(f.temp)}; }
c849_lock() { :; }; c849_prepare() { :; }; c849_require_ready() { :; }
sudo() { [ "$1" = -n ] && shift; if [ "$1" = install ]; then mkdir -p "\${@: -1}"; elif [ "$1" = df ] && [ '${options.lowDisk?1:0}' = 1 ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\\nfixture 10000000 1 1000 99%% /fixture\\n'; else command docker exec '${helper}' nsenter -t 1 -m -r -w -- "$@"; fi; }
df() { if [ '${options.lowDisk?1:0}' = 1 ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\\nfixture 10000000 1 1000 99%% /fixture\\n'; else command docker exec '${helper}' nsenter -t 1 -m -r -w -- df "$@"; fi; }
docker() {
  for argument in "$@"; do if [[ "$argument" == *antiphon-runner* ]]; then return 97; fi; done
  if [ "$1:$2" = image:inspect ]; then printf '%s\\n' '${docker('image','inspect','--format','{{.Id}}',image)}'
  elif [ "$1" = ps ] && [ '${options.censusError?1:0}' = 1 ]; then return 77
  elif [ "$1:$2" = volume:rm ]; then
    n=0; [ ! -f '${f.root}/rm-count' ] || n="$(cat '${f.root}/rm-count')"
    if [ '${options.failAt||0}' -gt 0 ] && [ "$n" = '${(options.failAt||0)-1}' ]; then return 77; fi
    command docker "$@" || return $?
    printf '%s\\n' "$((n+1))" > '${f.root}/rm-count'
  elif [ "$1" = create ]; then
    shift
    if [ '${options.rootAudit?1:0}' = 1 ]; then arguments=(); for a in "$@"; do if [ "$a" = 1654:1654 ]; then a=0:0; fi; arguments+=("$a"); done; set -- "\${arguments[@]}"; fi
    command docker create --label '${label}=${prefix}' --network none --mount 'type=volume,source=${f.origin},target=/origin,readonly' "$@"
  else command docker "$@"; fi
}
build_server2_images() {
  compose_host up -d --no-build >> "$CASE_DIR/command.log" 2>&1 || write_result false FixtureComposeFailed 2
  if declare -F c1008_record_recreated >/dev/null; then
    c1008_record_recreated
    container="$(command docker ps -q --filter 'label=com.docker.compose.project=${f.main}' --filter label=com.docker.compose.service=session-runner)"
    c1008_verify_tmp "$container"
  fi
  write_result ${options.partialUp?'false FixtureVerificationInterrupted 2':"true '' 0"}
}
`;
  const script=f.root+'/'+name+'.sh'; fs.writeFileSync(script,transformed.replace("trap 'ec=$?;",injected+"\ntrap 'ec=$?;"));
  const env={...process.env,C590_CASE:hostCase,C590_SHA:source,C590_RUN:'c1008real',C590_REEXEC:'1',C604_SERVER_ORIGIN:originUrl};
  let outcome; try {outcome=await exec('bash',['-x',script],{env,timeout:90000,maxBuffer:1024*1024});outcome.code=0;} catch(e){outcome=e;}
  fs.writeFileSync(f.root+'/'+name+'.log',(outcome.stdout||'')+(outcome.stderr||''));
  const result=JSON.parse(fs.readFileSync(dest+'/c590-result.json','utf8'));
  const journalPath=f.root+'/server/recycle/'+f.op+'.json';
  const journal=fs.existsSync(journalPath)?JSON.parse(fs.readFileSync(journalPath)):null;
  return {name,version,exit:outcome.code,diagnosis:result.diagnosis,accepted:result.accepted,journal,output:outcome.stdout||'',f};
}
function record(result,check) {
  check(result); preservation(result.f);
  const fact={name:result.name,version:result.version,exit:result.exit,diagnosis:result.diagnosis,accepted:result.accepted,operation:result.f.op,journal:result.journal};
  results.push(fact); console.log('C1008_REAL '+JSON.stringify({...fact,journal:undefined}));
}
const refused = r => {if(r.exit!==2||r.accepted!==false)throw Error('ExpectedRefusal:'+r.name); for(const n of roles.slice(0,3).map(x=>(r.f.mainReplacement?r.f.main:r.f.temp)+'_'+x)) payload(r.f,n);};
const reclaimed = r => {if(r.exit!==0||!r.accepted)throw Error('ExpectedSuccess:'+r.name+':'+r.diagnosis);};
function assertRemovals(r,count) {if(Object.values(r.journal.volumes).filter(v=>v.outcome==='removed').length!==count)throw Error('RemovalCount:'+r.name);}
function gitGraph(f,variant) {
  const work=f.main+'_work';
  const args=['run','--rm','--network','none','--user','0:0','--label',label+'='+prefix,'--mount','type=volume,source='+work+',target=/work','--mount','type=volume,source='+f.origin+',target=/origin',image,'bash','-c'];
  runDocker(...args,'chown 1654:1654 /work /origin');
  const graph=`set -e; export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM=/dev/null; git init -q --bare /origin; git init -q -b master /work/repo; git -C /work/repo config user.name Fixture; git -C /work/repo config user.email fixture@example.invalid; git -C /work/repo remote add origin /origin; echo A > /work/repo/file; git -C /work/repo add file; git -C /work/repo commit -qm A; git -C /work/repo push -q origin master; `;
  let fault='';
  if (['linked','detached','branch','bare','tag'].includes(variant)) {
    fault='git -C /work/repo commit --allow-empty -qm B; ';
    if (variant==='branch') fault+='git -C /work/repo branch unpublished; git -C /work/repo checkout -q --detach HEAD~1; ';
    if (variant==='tag') fault+='git -C /work/repo tag unpublished; git -C /work/repo reset -q --hard HEAD~1; ';
    if (variant==='linked'||variant==='detached') fault+=`git -C /work/repo worktree add -q ${variant==='detached'?'--detach':'-b unpublished-linked'} '/work/linked space' HEAD; git -C /work/repo reset -q --hard HEAD~1; `;
    if (variant==='bare') fault+='git clone -q --mirror /work/repo /work/bare.git; git -C /work/bare.git remote set-url origin /origin; git -C /work/bare.git branch unpublished HEAD; git -C /work/bare.git update-ref refs/heads/master HEAD~1; git -C /work/repo reset -q --hard HEAD~1; ';
  } else if (variant==='dirty') fault='echo dirty >> /work/repo/file; ';
  else fault="git -C /work/repo worktree add -q --detach '/work/linked clean' HEAD; git clone -q /origin /work/standalone; git clone -q --mirror /origin /work/bare.git; ";
  args[args.indexOf('0:0')]='1654:1654'; runDocker(...args,graph+fault);
}
try {
  fs.writeFileSync(root+'/Dockerfile','FROM debian:trixie-slim\nRUN apt-get update && apt-get install -y --no-install-recommends bash git coreutils findutils util-linux ca-certificates && rm -r /var/lib/apt/lists/* && mkdir -p /tmp/antiphon-pty-hosts && chmod 1777 /tmp && printf image-asset > /tmp/antiphon-pty-hosts/fixture\n');
  objects.images.add(image); runDocker('build','-t',image,root);
  helper=runDocker('run','-d','--privileged','--pid','host','--network','none','--name',guard(prefix+'-root'),'--label',label+'='+prefix,image,'sleep','infinity'); objects.containers.add(helper);
  shellRoot('test','-d',root);
  realDf=shellRoot('df','-Pk',docker('info','--format','{{.DockerRootDir}}'));
  for (const version of ['B','C']) {
    const f=fixture(); const r=await host(f,version+'-default',version);
    record(r,r=>{reclaimed(r); if(version==='B'){roles.slice(0,3).forEach(x=>payload(f,f.main+'_'+x));}else{assertRemovals(r,3);roles.slice(0,3).forEach(x=>payload(f,f.main+'_'+x,false)); const runner=containers(f.main).find(id=>JSON.parse(docker('inspect',id))[0].Config.Labels['com.docker.compose.service']==='session-runner'); if(runDocker('exec','-u','1654:1654',runner,'cat','/tmp/antiphon-pty-hosts/fixture')!=='image-asset')throw Error('TmpCopyupMissing'); if(runDocker('exec',runner,'stat','-c','%a','/tmp')!=='1777')throw Error('TmpModeWrong');}});
  }
  for(const service of ['session-runner','state-init']) for(const version of ['B','C']) {
    const f=fixture(false); createContainer(f,service,service==='session-runner',[f.temp+'_work'],f.temp);
    record(await host(f,version+'-null-'+service,version),refused);
  }
  for(const version of ['B','C']) {
    const f=fixture(false); const r=await host(f,version+'-absent-null',version);
    record(r,version==='B'?refused:r=>{reclaimed(r);assertRemovals(r,4);if(roles.some(x=>allVolumes().includes(f.temp+'_'+x)))throw Error('TempVolumeRetained');});
  }
  {
    const f=fixture(false); for(const r of roles)runDocker('volume','rm','--',f.temp+'_'+r);
    record(await host(f,'C-four-alreadyAbsent'),r=>{reclaimed(r);if(Object.values(r.journal.volumes).filter(v=>v.outcome==='alreadyAbsent').length!==4||roles.some(x=>allVolumes().includes(f.temp+'_'+x)))throw Error('AlreadyAbsentWrong');});
  }
  for(const running of [true,false]) {
    const f=fixture();createContainer(f,'foreign',running,[f.main+'_work'],f.prefix+'foreign');
    record(await host(f,'C-foreign-'+(running?'running':'stopped')),refused);
  }
  {const f=fixture();record(await host(f,'C-prefix-neighbours'),reclaimed);}
  for(const variant of ['linked','detached','branch','bare','tag','dirty','uid0','published']) {
    const f=fixture();gitGraph(f,variant);
    record(await host(f,'C-git-'+variant,'C',{rootAudit:variant==='uid0'}),variant==='published'?reclaimed:r=>{refused(r);if(!['RecycleUnpublishedWork','RecycleWorktreeDirty','RecycleGitAuditUnknown'].includes(r.diagnosis))throw Error('GitVerdictWrong');});
  }
  for(const failAt of [2,3]) {
    const f=fixture();
    record(await host(f,'C-partial-'+(failAt-1),'C',{failAt}),r=>{if(r.exit!==2||!r.journal)throw Error('PartialMissing');assertRemovals(r,failAt-1);if(r.journal.outcome!=='partial')throw Error('PartialDishonest');});
    const before=Number(fs.readFileSync(f.root+'/rm-count','utf8'));
    record(await host(f,'C-resume-'+(failAt-1),'C',{resume:true}),r=>{reclaimed(r);assertRemovals(r,3);if(Number(fs.readFileSync(f.root+'/rm-count','utf8'))-before!==4-failAt)throw Error('ResumeRedelDeleted');});
  }
  {
    const f=fixture();await host(f,'prepare-intrusion','C',{failAt:2});createVolume(f.main+'_work',['com.docker.compose.project='+f.main,'com.docker.compose.volume=work']);
    record(await host(f,'C-recreated-volume-intrusion','C',{resume:true}),r=>{if(r.exit!==2||r.diagnosis!=='RecycleResumeMismatch'||Number(fs.readFileSync(f.root+'/rm-count'))!==1)throw Error('GenerationIntrusionAdmitted');});
  }
  for(const foreign of [false,true]) {
    const f=fixture();const partial=await host(f,'prepare-partial-up','C',{partialUp:true});if(partial.exit!==2||partial.journal.phase!=='recreating')throw Error('PartialUpNotRecorded');
    if(foreign) {
      const runner=containers(f.main).find(id=>JSON.parse(docker('inspect',id))[0].Config.Labels['com.docker.compose.service']==='session-runner');
      runDocker('stop','--time','1','--',runner);runDocker('rm','--',runner);createContainer(f,'session-runner',true,[...roles.map(r=>f.main+'_'+r),...f.caches]);
    }
    record(await host(f,foreign?'C-foreign-recreated-container':'C-recorded-partial-up','C',{resume:true}),r=>{if(foreign){if(r.exit!==2||r.diagnosis!=='RecycleResumeMismatch')throw Error('UnrecordedContainerAdmitted');}else reclaimed(r);if(Number(fs.readFileSync(f.root+'/rm-count'))!==3)throw Error('PartialUpRedelDeleted');});
  }
  {const f=fixture(false);if(containers(f.temp).length!==0)throw Error('WrapperCensusNotAbsent');fs.writeFileSync(f.root+'/wrapper-absence.json',JSON.stringify({containers:[],retiredAt:f.statuses['server2-temp'].retiredAt}));createContainer(f,'session-runner',true,[f.temp+'_work'],f.temp);record(await host(f,'C-wrapper-absent-host-present'),refused);}
  {const f=fixture(false);record(await host(f,'C-census-error-empty','C',{censusError:true}),refused);}
  for(const version of ['B','C']) {
    const f=fixture(false);f.statuses['server2-temp'].runnerSessions=0;
    record(await host(f,version+'-low-disk-retire',version,{lowDisk:true}),version==='B'?refused:r=>{reclaimed(r);assertRemovals(r,4);});
  }
  {const f=fixture();record(await host(f,'C-main-reclaim-low-budget','C',{lowDisk:true}),r=>{if(r.exit!==2||r.diagnosis!=='CacheDiskLow')throw Error('AllocationGateOrderWrong');assertRemovals(r,3);if(containers(f.main).some(id=>JSON.parse(docker('inspect',id))[0].Config.Labels['com.docker.compose.service']==='session-runner'))throw Error('AllocatedAfterLowBudget');});}
  if(results.length!==32||results.filter(r=>r.version==='B').length!==5||results.filter(r=>r.version==='C').length!==27||new Set(results.map(r=>r.name)).size!==32)throw Error('OutcomeCensusWrong');
} catch(e) {failure=e;console.error(e.stack);}
finally {
  // Discover Compose-created objects through the fixture's exact ownership label.
  for(const id of runDocker('ps','-aq','--no-trunc','--filter','label='+label+'='+prefix).split('\n').filter(Boolean)) {
    if(JSON.parse(docker('inspect',id))[0].Config.Labels[label]!==prefix)throw Error('CleanupOwnershipMismatch'); objects.containers.add(id);
  }
  for(const id of objects.containers) if(runDocker('ps','-aq','--no-trunc').split('\n').includes(id))runDocker('rm','-f','--',id);
  for(const n of objects.volumes)if(allVolumes().includes(n))runDocker('volume','rm','--',guard(n));
  for(const id of runDocker('network','ls','-q','--filter','label=com.docker.compose.project').split('\n').filter(Boolean)) {
    const facts=JSON.parse(docker('network','inspect',id))[0]; if(facts.Labels?.['com.docker.compose.project']?.startsWith(prefix)){objects.networks.add(id);runDocker('network','rm',id);}
  }
  for(const n of objects.images)if(cp.spawnSync('docker',['image','inspect',guard(n)],{encoding:'utf8'}).status===0)runDocker('image','rm',guard(n));
  const remaining=runDocker('ps','-aq','--no-trunc','--filter','label='+label+'='+prefix);
  if(remaining||[...objects.volumes].some(n=>allVolumes().includes(n))||[...objects.images].some(n=>cp.spawnSync('docker',['image','inspect',n],{encoding:'utf8'}).status===0))throw Error('FixtureResidue');
  await new Promise(resolve=>endpoint.close(resolve));
  fs.writeFileSync(root+'/evidence.json',JSON.stringify({source,base,scriptDigests:{base:hash(scripts.B),changed:hash(scripts.C)},docker:docker('version','--format','{{.Server.Version}}'),compose:docker('compose','version','--short'),prefix,results,failure:failure?.message||null,cleanup:'all recorded containers, volumes, networks and fixture image absent',objects:Object.fromEntries(Object.entries(objects).map(([k,v])=>[k,[...v]])),commands,shims:['lane/root relocation','private HTTP status/task endpoint','sudo through owned nested-root namespace helper','fixed helper-image pin and origin fixture mount','expensive build/provider startup boundary with real Compose up','controlled rm/ps faults','df capacity only for RD-12'],realDf},null,2));
  console.log('C1008_REAL cases='+results.length+' base='+results.filter(r=>r.version==='B').length+' changed='+results.filter(r=>r.version==='C').length+' failures='+(failure?1:0)+' cleanup=absent evidence='+root+'/evidence.json');
}
if(failure)process.exit(1);
