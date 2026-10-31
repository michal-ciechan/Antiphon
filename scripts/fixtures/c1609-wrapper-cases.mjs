import fs from 'node:fs';
import cp from 'node:child_process';
import path from 'node:path';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
const repo=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const vectors=JSON.parse(fs.readFileSync(repo+'/scripts/fixtures/c1008-recycle-cases.json'));
const root=fs.mkdtempSync('/tmp/c1609-wrapper-');fs.chmodSync(root,0o700);
try {
 for(const mode of ['preview','apply','refused']) {
  const state={scenario:'c1008',sha:vectors.sourceSha,oldDeployed:false,tempContainer:false,clockMs:0,
   statuses:{server2:structuredClone(vectors.mainAccepting),'server2-temp':structuredClone(vectors.tempRetiredAbsent)},
   tasks:structuredClone(vectors.emptyTasks),failRetirementPreview:mode==='refused'};
  const file=root+'/'+mode+'.json',trace=root+'/'+mode+'.jsonl',token=root+'/operator-token';
  fs.writeFileSync(file,JSON.stringify(state));fs.writeFileSync(token,'C1609_WRAPPER_FAKE_AUTH',{mode:0o600});
  const args=['-NoProfile','-NonInteractive','-File',repo+'/scripts/deploy-server2.ps1','-Rolling','-Sha',state.sha,'-Phase','retire-temp'];
  if(mode==='preview')args.push('-DryRun');
  const env={...process.env,ANTIPHON_OPERATOR_TOKEN_FILE:token,ANTIPHON_TASK_TOKEN:'',ANTIPHON_API:'http://127.0.0.1:1',
   C727_TEST_HTTP_STUB:repo+'/scripts/fixtures/c727-fake-http.ps1',C727_TEST_VERIFY_STUB:repo+'/scripts/fixtures/c727-fake-verify.ps1',
   C727_TEST_STATE:file,C727_TEST_TRACE:trace,C727_TEST_WAIT_MS:'100',C727_TEST_POLL_MS:'5',C727_TEST_LAND_WAIT_MS:'100'};
  const r=cp.spawnSync('pwsh',args,{env,encoding:'utf8',timeout:30000});
  assert(!r.error);assert.equal(r.status,mode==='refused'?2:0,mode+': '+r.stdout+r.stderr);
  const rows=fs.readFileSync(trace,'utf8').trim().split('\n').map(JSON.parse);
  const calls=rows.filter(x=>x.kind==='case'&&x.name==='retire-temp-runner');
  assert.deepEqual(calls.map(x=>x.recycle.dryRun),mode==='apply'?[true,false]:[true],mode+' must reach full Preview before any Apply');
  if(mode==='apply')assert.equal(calls[0].recycle.operationId,calls[1].recycle.operationId);
  assert(!rows.some(x=>x.method==='POST'),'retirement does not clear or drain');
 }
 console.log('C1609_WRAPPER passed full-preview auto-apply refusal-stop');
} finally {fs.rmSync(root,{recursive:true,force:true});}
