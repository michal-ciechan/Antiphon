import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';
import crypto from 'node:crypto';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';

// The public command, real bridge, host gate and durable writer execute unchanged.
// SSH/SCP, API status and Docker are mapped only into this owned fixture root.
const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const root=process.argv[2];
assert(path.isAbsolute(root)&&fs.realpathSync(root)===root&&/^c1008-host-[A-Za-z0-9]+$/.test(path.basename(root)));
const op='c100800000000000000000000000000000001';
const receipt=root+'/server/retirement/'+op+'/receipt.json';
const initial=JSON.parse(fs.readFileSync(receipt));
const bin=root+'/manual-bin';fs.mkdirSync(bin);
const control=root+'/control';fs.mkdirSync(control);
const token=root+'/manual-operator-token';fs.writeFileSync(token,'C1609_MANUAL_OPERATOR_SENTINEL',{mode:0o600});
const q=s=>"'"+String(s).replaceAll("'","'\\''")+"'";
const hash=s=>crypto.createHash('sha256').update(s).digest('hex');
const trace=[];
const templateSource=fs.readFileSync(root+'/remote.sh','utf8');
const begin=templateSource.indexOf("C1008_FIXTURE_ROOT='");
const end=templateSource.indexOf("\ntrap 'ec=$?;",begin);
assert(begin>=0&&end>begin,'fixture setup supplied actual host injection');
let injection=templateSource.slice(begin,end)
  .split('\n').filter(l=>!/^\s*C1008_OPERATION=|^\s*C1008_DRY_RUN=/.test(l)).join('\n');
fs.writeFileSync(root+'/manual-injection.sh',injection);
fs.writeFileSync(bin+'/sudo','#!/bin/bash\n[ "$1" != -n ] || shift\nif [ "$1" = install ]; then mkdir -p -- "${@: -1}"; else exec "$@"; fi\n',{mode:0o755});
fs.writeFileSync(bin+'/docker','#!/bin/bash\nexec bash '+q(repo+'/scripts/fixtures/c1008-fake-docker.sh')+' "$@"\n',{mode:0o755});
fs.writeFileSync(bin+'/ssh',`#!/usr/local/bin/node
const fs=require('node:fs'),cp=require('node:child_process');
const root=process.env.C1609_MANUAL_FIXTURE;
const args=process.argv.slice(2);let cmd=args.at(-1);
if(args.includes('-s'))cmd=fs.readFileSync(0,'utf8');
const map=s=>s.replaceAll('/home/mc/antiphon-server2',root+'/server').replaceAll('/home/mc/antiphon-c590',root+'/control');
cmd=cmd.replace(/printf '%s' '([A-Za-z0-9+/=]+)' \| base64 -d \| bash/g,(_,encoded)=>
 "printf '%s' '"+Buffer.from(map(Buffer.from(encoded,'base64').toString('utf8'))).toString('base64')+"' | base64 -d | bash");
cmd=map(cmd);
const m=cmd.match(/bash '([^']+\/c590-remote.sh)'/);
if(m){
  let source=fs.readFileSync(m[1],'utf8');
  const injection=fs.readFileSync(root+'/manual-injection.sh','utf8');
  source=source.replace("trap 'ec=$?;",injection+"\\ntrap 'ec=$?;");
  const file=root+'/manual-host-'+process.pid+'.sh';fs.writeFileSync(file,source);
  cmd=cmd.replace(m[0],"bash '"+file+"'");
}
fs.appendFileSync(root+'/manual-ssh-trace',JSON.stringify({packet:args.includes('-s'),hostCase:!!m})+'\\n');
const r=cp.spawnSync('bash',['-c',cmd],{env:process.env,encoding:'utf8',timeout:45000,maxBuffer:4*1024*1024});
process.stdout.write(r.stdout||'');process.stderr.write(r.stderr||'');process.exit(r.status??2);
`,{mode:0o755});
fs.writeFileSync(bin+'/scp',`#!/usr/local/bin/node
const fs=require('node:fs'),path=require('node:path');const root=process.env.C1609_MANUAL_FIXTURE;
let a=process.argv.slice(2),v=[];for(let i=0;i<a.length;i++){if(a[i]==='-o'){i++;continue;}if(a[i]==='-r')continue;v.push(a[i]);}
const map=s=>s.replace(/^mc@server2:/,'').replace('/home/mc/antiphon-server2',root+'/server').replace('/home/mc/antiphon-c590',root+'/control').replace(/^\/work\/test-evidence\/[^/]+/,root+'/evidence');
let src=map(v[0]),dst=map(v[1]);if(fs.existsSync(dst)&&fs.statSync(dst).isDirectory())dst=path.join(dst,path.basename(src));
fs.cpSync(src,dst,{recursive:true});
`,{mode:0o755});
// The real manual entry reads exactly one status endpoint. The host independently
// verifies all counters, retirement, routing, task/land and reference observations.
const launcher=root+'/manual-launch.ps1';
fs.writeFileSync(launcher,`
param([string]$Request)
$ErrorActionPreference='Stop'
function Invoke-RestMethod { param($Uri,$Headers,$TimeoutSec,$Method)
  if($Uri -notmatch '/api/session-runners/server2-temp/status$'){throw 'unexpected API path'}
  return ([IO.File]::ReadAllText($env:C1609_MANUAL_FIXTURE+'/statuses.json')|ConvertFrom-Json).'server2-temp'
}
$a=[IO.File]::ReadAllText($Request)|ConvertFrom-Json -AsHashtable
& '${repo.replaceAll("'","''")}/scripts/retire-runner-volumes.ps1' @a
exit $LASTEXITCODE
`);
const env={...process.env,PATH:bin+':'+process.env.PATH,C1609_MANUAL_FIXTURE:root,
 C1008_FIXTURE_ROOT:root,ANTIPHON_OPERATOR_TOKEN_FILE:token,ANTIPHON_TASK_TOKEN:''};
delete env.C1609_RETIREMENT_ROOT;delete env.C1609_VOLUME_CENSUS_FILE;
const source=fs.readFileSync(repo+'/scripts/retire-runner-volumes.ps1','utf8');
const supportsManual=/\[switch\]\$Manual\b/.test(source);
function run(extra,authority=true){
 const a={Preview:true,OperationId:op};
 // Baseline reaches its existing explicit public Preview refusal. It must not
 // earn red credit from unknown parameters introduced by the repair.
 if(supportsManual)Object.assign(a,{Manual:true,ProjectId:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1',
   RetiredRunnerId:'server2-temp',VolumeName:initial.targets,EvidenceDirectory:root+'/manual-evidence'},extra);
 const file=root+'/manual-request.json';fs.writeFileSync(file,JSON.stringify(a));
 const started=performance.now();
 const r=cp.spawnSync('pwsh',['-NoProfile','-NonInteractive','-File',launcher,'-Request',file],
  {env:{...env,ANTIPHON_OPERATOR_TOKEN_FILE:authority?token:root+'/missing-token'},encoding:'utf8',timeout:60000,maxBuffer:4*1024*1024});
 trace.push({request:Object.keys(extra),exit:r.status,milliseconds:performance.now()-started});
 assert(!r.error,'public manual driver failed: '+r.error);
 assert(!(r.stdout+r.stderr).includes('C1609_MANUAL_OPERATOR_SENTINEL'),'credential must never be exported');
 return {code:r.status,text:r.stdout+r.stderr};
}
process.once('exit',code=>{
 if(process.env.ANTIPHON_TEST_PHASE_ROOT){const dir=process.env.ANTIPHON_TEST_PHASE_ROOT;fs.mkdirSync(dir,{recursive:true});
 fs.writeFileSync(dir+'/manual-calls-'+process.pid+'.json',JSON.stringify(trace));
 if(code!==0){const saved=dir+'/manual-failure-'+process.pid+'/'+path.basename(root);fs.mkdirSync(path.dirname(saved),{recursive:true});fs.cpSync(root,saved,{recursive:true});console.error('C1609_MANUAL_DIAGNOSTIC='+saved);}}
});
let r=run({Template:true});
assert.equal(r.code,0,'public manual template must observe exact targets: '+r.text);
assert.match(r.text,/C1609_MANUAL_TEMPLATE/);
const templates=fs.readdirSync(root+'/manual-evidence',{recursive:true}).filter(p=>p.endsWith('attestation-template.json'));
assert.equal(templates.length,1);
const unsigned=JSON.parse(fs.readFileSync(root+'/manual-evidence/'+templates[0]));
assert.equal(unsigned.actor,'');assert.equal(unsigned.previewDigest,null);
const signed=root+'/manual-attestation.json';fs.writeFileSync(signed,JSON.stringify(unsigned));
const prior=fs.readFileSync(receipt);
r=run({AttestationFile:signed});assert.notEqual(r.code,0,'template is never silently signed');
assert.deepEqual(fs.readFileSync(receipt),prior);
Object.assign(unsigned,{actor:'operator',authorityRef:'operator-token',reason:'explicit fixture manual operation',utc:new Date().toISOString().replace(/\.\d{3}Z$/,'Z')});
fs.writeFileSync(signed,JSON.stringify(unsigned));
r=run({AttestationFile:signed},false);assert.notEqual(r.code,0,'task credential alone cannot authorize manual');
assert.deepEqual(fs.readFileSync(receipt),prior);
r=run({Preview:false,DryRun:true,AttestationFile:signed});assert.equal(r.code,0,'authenticated public DryRun reaches full Preview: '+r.text);
assert.equal(JSON.parse(fs.readFileSync(receipt)).mode,'manual');
r=run({AttestationFile:signed});assert.equal(r.code,0,'authenticated public Preview: '+r.text);
const ready=fs.readFileSync(receipt);assert.equal(JSON.parse(ready).mode,'manual');
assert.equal(JSON.parse(ready).consumed,false);
const apply={...unsigned,phase:'Apply',previewDigest:hash(ready)};
fs.writeFileSync(signed,JSON.stringify({...apply,previewDigest:'0'.repeat(64)}));
r=run({Preview:false,Apply:true,ExpectedProofDigest:hash(ready),AttestationFile:signed});
assert.notEqual(r.code,0,'wrong attestation cannot consume Preview');assert.deepEqual(fs.readFileSync(receipt),ready);
fs.writeFileSync(signed,JSON.stringify(apply));
r=run({Preview:false,Apply:true,ExpectedProofDigest:hash(ready),AttestationFile:signed});
assert.equal(r.code,0,'public manual Apply consumes the actual protected Preview: '+r.text);
const final=JSON.parse(fs.readFileSync(receipt));assert.equal(final.state,'Completed');
assert.equal(final.applyAttestation.reason,apply.reason);
r=run({Preview:false,Apply:true,Resume:true,ExpectedProofDigest:hash(fs.readFileSync(receipt)),AttestationFile:signed});
assert.equal(r.code,0,'public manual Resume reobserves the same durable intents: '+r.text);
const state=JSON.parse(fs.readFileSync(root+'/docker.json'));assert.equal(state.removed.length,4);
for(const name of initial.targets)assert(!state.volumes[name]);
for(const name of ['antiphon-runner_work','antiphon-runner_runner-state','antiphon-runner-cache-npm-content','antiphon-runner-cache-nuget-packages','antiphon-runner-cache-nuget-scratch'])assert(state.volumes[name],name+' survives');
console.log('C1609_MANUAL passed');
