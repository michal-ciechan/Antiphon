import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';

// Actual public wrapper -> verifier -> bridge -> host coordinator -> real audit.
// Only transport, API observations and Docker resolve into this owned root.
const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const root=process.argv[2];
assert(path.isAbsolute(root)&&fs.realpathSync(root)===root&&/^c1008-host-[A-Za-z0-9]+$/.test(path.basename(root)));
const q=s=>"'"+String(s).replaceAll("'","'\\''")+"'";
const bin=root+'/wrapper-bin';fs.mkdirSync(bin);
fs.mkdirSync(root+'/control');
const token=root+'/wrapper-operator-token';fs.writeFileSync(token,'C1609_WRAPPER_OPERATOR_SENTINEL',{mode:0o600});
const source=fs.readFileSync(root+'/remote.sh','utf8');
const begin=source.indexOf("C1008_FIXTURE_ROOT='");
const end=source.indexOf("\ntrap 'ec=$?;",begin);
assert(begin>=0&&end>begin);
let injection=source.slice(begin,end).split('\n').filter(l=>
 !/^\s*C1008_OPERATION=|^\s*C1008_DRY_RUN=|^\s*C994_VERSION=|^\s*C994_PROJECT_ID=/.test(l)).join('\n');

// Tighten only the final observation age to five seconds in the disposable
// fixture copy. Production remains 900s. Six seconds of real elapsed time after
// the real audit makes this a bounded renewal witness without a 15-minute sleep.
const retirement=fs.readFileSync(repo+'/scripts/c1008-retirement.sh','utf8');
const age='[ "$((now-completed))" -le 900 ]';
assert.equal(retirement.split(age).length-1,1,'exact final publication-age boundary');
fs.writeFileSync(root+'/retirement-short-age.sh',retirement.replace(age,'[ "$((now-completed))" -le 5 ]'));
injection+=`\nC1609_RETIREMENT_FILE=${q(root+'/retirement-short-age.sh')}
eval "$(declare -f c1008_audit_readonly | sed '1s/c1008_audit_readonly/c1609_fixture_audit_readonly/')"
c1008_audit_readonly() {
  if [ "\${1:-}" = inventory ]; then
    lock=held
    if (exec 8>&-; flock -n "$SERVER2_ROOT/locks/rollout.lock" true); then lock=free; fi
    printf '%s %s %s\\n' "$CASE" "$BASHPID" "$lock" >> "$C1008_FIXTURE_ROOT/wrapper-inventory-locks"
  fi
  c1609_fixture_audit_readonly "$@" || return $?
  if [ "\${1:-}" != inventory ]; then sleep 6; fi
}
`;
fs.writeFileSync(root+'/manual-injection.sh',injection);
fs.writeFileSync(bin+'/sudo','#!/bin/bash\n[ "$1" != -n ] || shift\nif [ "$1" = install ]; then mkdir -p -- "${@: -1}"; else exec "$@"; fi\n',{mode:0o755});
fs.writeFileSync(bin+'/docker','#!/bin/bash\nexec bash '+q(repo+'/scripts/fixtures/c1008-fake-docker.sh')+' "$@"\n',{mode:0o755});
for(const mode of ['ssh','scp'])fs.writeFileSync(bin+'/'+mode,'#!/bin/bash\nexec node '+q(repo+'/scripts/fixtures/c1609-manual-transport.cjs')+' '+mode+' "$@"\n',{mode:0o755});

const gitExe=cp.execFileSync('which',['git'],{encoding:'utf8'}).trim();
const bare=root+'/wrapper-origin.git',target=root+'/volumes/antiphon-runner-temp_work/_data/repos/wrapper';
const publisher=root+'/wrapper-publisher',origin='https://fixture.invalid/card-1609/wrapper.git';
const gitEnv={...process.env,GIT_CONFIG_GLOBAL:'/dev/null',GIT_CONFIG_SYSTEM:'/dev/null',GIT_TERMINAL_PROMPT:'0',
 GIT_AUTHOR_NAME:'Fixture',GIT_AUTHOR_EMAIL:'fixture@example.invalid',GIT_COMMITTER_NAME:'Fixture',GIT_COMMITTER_EMAIL:'fixture@example.invalid'};
function git(args,options={}){return cp.execFileSync(gitExe,args,{env:gitEnv,encoding:'utf8',timeout:10000,...options}).trim();}
git(['init','-q','--bare',bare]);git(['--git-dir',bare,'symbolic-ref','HEAD','refs/heads/main']);
git(['init','-q','-b','main',target]);fs.writeFileSync(target+'/published','A\n');
git(['-C',target,'add','published']);git(['-C',target,'-c','commit.gpgsign=false','commit','-qm','A']);
git(['-C',target,'remote','add','origin',origin]);git(['-C',target,'push','-q',bare,'HEAD:refs/heads/main']);
git(['clone','-q','-b','main',bare,publisher]);fs.writeFileSync(publisher+'/published','B\n');
git(['-C',publisher,'add','published']);git(['-C',publisher,'-c','commit.gpgsign=false','commit','-qm','B']);
git(['-C',publisher,'push','-q','origin','main']);
const a=git(['-C',target,'rev-parse','HEAD']),b=git(['-C',publisher,'rev-parse','HEAD']);
git(['-C',publisher,'merge-base','--is-ancestor',a,b]);
assert.notEqual(cp.spawnSync(gitExe,['-C',target,'cat-file','-e',b+'^{commit}'],{env:gitEnv}).status,0,'B absent in audited target');
fs.writeFileSync(root+'/server/retirement-origins.json',JSON.stringify({schema:1,projects:{'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1':{origins:[origin]}}},null,0)+'\n');
// The registry is canonical, owner-protected host configuration, not an origin
// supplied by the target or a fabricated publication verdict.
fs.writeFileSync(root+'/server/retirement-origins.json',cp.execFileSync('jq',['-cS','.'],{input:fs.readFileSync(root+'/server/retirement-origins.json'),encoding:'utf8'}),{mode:0o600});
fs.writeFileSync(bin+'/git','#!/bin/bash\nargs=(-c protocol.file.allow=always)\nfor arg in "$@"; do\n if [ "$arg" = '+q(origin)+' ]; then args+=('+q(bare)+'); else args+=("$arg"); fi\ndone\nprintf "%s\\n" "$*" >> '+q(root+'/wrapper-host-git-trace')+'\nexec '+q(gitExe)+' "${args[@]}"\n',{mode:0o755});
fs.writeFileSync(root+'/helper-auth-128','1\n');

const adapter=root+'/wrapper-verifier.ps1';
fs.writeFileSync(adapter,`
param([string]$Case,[string]$Manifest)
$ErrorActionPreference='Stop'
if($Case -in @('retire-temp-runner','retire-temp-containers')) {
  $r=Get-Content -Raw -LiteralPath $Manifest|ConvertFrom-Json
  $path=[IO.Path]::GetFullPath([string]$r.evidenceRoot)
  if($path -cnotmatch '/[.]antiphon/rolling-server2/c727[0-9a-f]{12}$'){throw 'unowned wrapper evidence'}
  $registry=Join-Path $env:C1609_MANUAL_FIXTURE 'wrapper-evidence-roots.json'
  $roots=@();if(Test-Path -LiteralPath $registry){$roots=@(Get-Content -Raw -LiteralPath $registry|ConvertFrom-Json)}
  if($roots -cnotcontains $path){$roots+=@($path)}
  [IO.File]::WriteAllText($registry,(ConvertTo-Json -InputObject @($roots) -Compress))
  @{kind='real-case';name=$Case;recycle=$r.recycle}|ConvertTo-Json -Compress -Depth 20|Add-Content -LiteralPath $env:C727_TEST_TRACE
  & pwsh -NoProfile -NonInteractive -File '${repo}/scripts/verify-docker-stack.ps1' -Case $Case -Manifest $Manifest
  exit $LASTEXITCODE
}
if($Case -eq 'retirement-proof-read') {
  $r=Get-Content -Raw -LiteralPath $Manifest|ConvertFrom-Json
  . '${repo}/scripts/retire-runner-volumes.ps1'
  . '${repo}/scripts/c1609-read-packet.ps1'
  Invoke-Server2Bash -Script (New-C1609RetirementReadPacket -Operation $r.operationId)
  exit 0
}
& '${repo}/scripts/fixtures/c727-fake-verify.ps1' -Case $Case -Manifest $Manifest
exit $LASTEXITCODE
`);
const vectors=JSON.parse(fs.readFileSync(repo+'/scripts/fixtures/c1008-recycle-cases.json'));
fs.writeFileSync(root+'/wrapper-state.json',JSON.stringify({scenario:'c1008',sha:'a'.repeat(40),oldDeployed:false,tempContainer:false,clockMs:0,
 statuses:JSON.parse(fs.readFileSync(root+'/statuses.json')),tasks:vectors.emptyTasks}));
const env={...process.env,PATH:bin+':'+process.env.PATH,C1609_MANUAL_FIXTURE:root,C1008_FIXTURE_ROOT:root,
 ANTIPHON_OPERATOR_TOKEN_FILE:token,ANTIPHON_TASK_TOKEN:'',ANTIPHON_API:'http://127.0.0.1:1',
 C727_TEST_HTTP_STUB:repo+'/scripts/fixtures/c727-fake-http.ps1',C727_TEST_VERIFY_STUB:adapter,
 C727_TEST_STATE:root+'/wrapper-state.json',C727_TEST_TRACE:root+'/wrapper-trace.jsonl',C727_TEST_WAIT_MS:'100',C727_TEST_POLL_MS:'5',C727_TEST_LAND_WAIT_MS:'100'};
const calls=[];
function run(extra=[]){
 const started=performance.now();
 // CARD-1609 measured complete calls: auto Preview/Apply 111.733s, DryRun 35.806s.
 // Rounded 2x single-sample collection limits; population p95 remains unknown.
 const timeout=extra.length?90000:240000;
 const r=cp.spawnSync('pwsh',['-NoProfile','-NonInteractive','-File',repo+'/scripts/deploy-server2.ps1','-Rolling','-Sha','a'.repeat(40),'-Phase','retire-temp',...extra],
 {env,encoding:'utf8',timeout,maxBuffer:4*1024*1024});
 calls.push({extra,exit:r.status,milliseconds:performance.now()-started});
 assert(!r.error,'bounded wrapper child: '+r.error);
 assert(!(r.stdout+r.stderr).includes('C1609_WRAPPER_OPERATOR_SENTINEL'),'operator credential never exported');
 return {code:r.status,text:r.stdout+r.stderr};
}
process.once('exit',code=>{
 if(process.env.ANTIPHON_TEST_PHASE_ROOT){const dir=process.env.ANTIPHON_TEST_PHASE_ROOT;fs.mkdirSync(dir,{recursive:true});
 fs.writeFileSync(dir+'/wrapper-calls-'+process.pid+'.json',JSON.stringify(calls));
 if(code!==0){const saved=dir+'/wrapper-failure-'+process.pid+'/'+path.basename(root);fs.mkdirSync(path.dirname(saved),{recursive:true});fs.cpSync(root,saved,{recursive:true});console.error('C1609_WRAPPER_DIAGNOSTIC='+saved);}}
});
const operations=()=>fs.readdirSync(root+'/server/retirement').filter(x=>/^c1008[0-9a-f]{32}$/.test(x));
const prior=new Set(operations());
let r=run(['-DryRun']);
assert.equal(r.code,0,'real v2 wrapper must renew publication after a long content scan: '+r.text);
const created=operations().filter(x=>!prior.has(x));assert.equal(created.length,1);
const previewFile=root+'/server/retirement/'+created[0]+'/receipt.json';
const preview=fs.readFileSync(previewFile);assert.equal(JSON.parse(preview).state,'Ready');assert.equal(JSON.parse(preview).mode,'scripted');
assert.equal(JSON.parse(fs.readFileSync(root+'/docker.json')).removed.length,0,'DryRun has zero effects');
const transport=fs.readFileSync(root+'/manual-ssh-trace','utf8').trim().split('\n').map(JSON.parse);
assert.equal(transport.filter(x=>x.caseName==='retire-temp-runner').length,1,'one locked coordinator owns inventory and Preview');
assert.equal(transport.filter(x=>x.caseName==='retire-temp-inventory').length,0,'no separate unlocked inventory invocation');
assert(fs.readFileSync(root+'/wrapper-inventory-locks','utf8').trim().split('\n').every(x=>x.endsWith(' held')),'every inventory retains FD8');
const hostCalls=fs.readFileSync(root+'/wrapper-host-git-trace','utf8');
assert(hostCalls.split('\n').filter(x=>x.includes(' ls-remote ')).length>=4,'initial proof and fresh renewed graph both use the real origin');
assert(!fs.existsSync(root+'/helper-git-trace'),'helper stays offline');
assert.notEqual(cp.spawnSync(gitExe,['-C',target,'cat-file','-e',b+'^{commit}'],{env:gitEnv}).status,0,'proof does not populate target');

// Losing origin publication after Preview cannot be excused by its earlier
// Ready receipt, a signature, a cached graph or the wrapper's transport success.
git(['-C',publisher,'checkout','-q','--orphan','unrelated']);git(['-C',publisher,'-c','commit.gpgsign=false','commit','-qm','unrelated']);
// Transfer the new graph before pointing the bare advertisement at it.
git(['--git-dir',bare,'fetch','-q',publisher,'refs/heads/unrelated']);
git(['--git-dir',bare,'update-ref','refs/heads/main',git(['-C',publisher,'rev-parse','HEAD'])]);
r=run(['-RetirementProof',created[0]]);assert.notEqual(r.code,0,'withdrawn origin coverage refuses Apply');
assert.equal(JSON.parse(fs.readFileSync(root+'/docker.json')).removed.length,0);
assert.deepEqual(fs.readFileSync(previewFile),preview,'refusal cannot consume Ready');
git(['--git-dir',bare,'update-ref','refs/heads/main',b]);
r=run();assert.equal(r.code,0,'real wrapper auto Preview/Apply uses renewed origin coverage: '+r.text);
const state=JSON.parse(fs.readFileSync(root+'/docker.json'));
assert.deepEqual(state.removed,['antiphon-runner-temp_runner-state','antiphon-runner-temp_dind-data','antiphon-runner-temp_runner-tmp','antiphon-runner-temp_work']);
for(const name of ['antiphon-runner_work','antiphon-runner_runner-state','antiphon-runner-cache-npm-content','antiphon-runner-cache-nuget-packages','antiphon-runner-cache-nuget-scratch'])assert(state.volumes[name],name+' survives');
console.log('C1609_WRAPPER passed');
