#!/usr/bin/env bash
# File-backed Docker boundary. Every unknown operation fails; never contacts a daemon.
set -euo pipefail
node - "$@" <<'NODE'
const fs = require('node:fs'), path = require('node:path'), cp = require('node:child_process');
const root = process.env.C1008_FIXTURE_ROOT;
if (!root || !/^c1008-host-/.test(path.basename(root))) throw Error('fixture root invalid');
const file = path.join(root, 'docker.json'), trace = path.join(root, 'docker-trace.jsonl');
const args = process.argv.slice(2), state = JSON.parse(fs.readFileSync(file));
fs.appendFileSync(trace, JSON.stringify(args)+'\n');
const save = () => fs.writeFileSync(file, JSON.stringify(state));
const out = x => process.stdout.write(typeof x === 'string' ? x : JSON.stringify(x));
const fail = () => process.exit(2);
const name = args.at(-1), fault = state.fault || '';
if (fault === 'ps-error' && args[0] === 'ps') fail();
if (fault === 'inspect-error' && args[0] === 'inspect') fail();
if (fault === 'volume-ls-error' && args[0] === 'volume' && args[1] === 'ls') fail();
if (args[0] === 'info') { out(root+'\n'); }
else if(args[0]==='image'&&args[1]==='inspect')out('sha256:'+'a'.repeat(64)+'\n');
else if (args[0] === 'ps') {
 let items = state.containers;
 const filter = args[args.indexOf('--filter')+1];
 if(args.includes('--filter')) {
  if(filter.startsWith('label=')) {const [key,value]=filter.slice(6).split('=');items=items.filter(c=>c.Config.Labels[key]===value);}
  else if(filter.startsWith('volume=')) items=items.filter(c=>c.Mounts.some(m=>m.Name===filter.slice(7)));
  else fail();
 }
 if(!args.some(a=>a.includes('a') && a.startsWith('-'))) items=items.filter(c=>c.State.Running);
 out(items.map(c=>c.Id).join('\n')+(items.length?'\n':''));
} else if(args[0]==='inspect') {
 const c=state.containers.find(c=>c.Id===name); if(!c)process.exit(1);
 if(fault==='inspect-empty'){out('');process.exit(0);}
 out([c]);
} else if(args[0]==='stop') {
 if(fault==='stop-failed')fail(); const c=state.containers.find(c=>c.Id===name);if(!c)fail();
 if(fault!=='stop-stays-running')c.State={Running:false,Status:'exited'};save();out(name+'\n');
} else if(args[0]==='rm') {
 if(args.some(a=>a==='-f'||a==='--force'||a==='-v'))fail();
 const c=state.containers.find(c=>c.Id===name);if(!c||c.State.Running||fault==='helper-rm-failed'&&c.Config.Labels['io.antiphon.audit'])fail();
 if(fault==='owned-rm-failed'&&c.Config.Labels['com.docker.compose.service']==='session-runner')fail();
 state.containers=state.containers.filter(c=>c.Id!==name);save();out(name+'\n');
} else if(args[0]==='volume'&&args[1]==='ls')out(Object.keys(state.volumes).join('\n')+'\n');
else if(args[0]==='volume'&&args[1]==='inspect') {
 if(fault==='generation-after-stop'&&!state.generationChanged&&name==='antiphon-runner_work'&&
    !state.containers.some(c=>['session-runner','state-init'].includes(c.Config.Labels['com.docker.compose.service']))) {
   state.volumes[name].CreatedAt='2026-10-03T10:00:00Z';state.generationChanged=true;save();
 }
 if(fault==='volume-inspect-error')fail(); const v=state.volumes[name];if(!v){process.stderr.write('No such volume\n');process.exit(1);}
 out([v]);
} else if(args[0]==='volume'&&args[1]==='rm') {
 if(args.length!==4||args[2]!=='--')fail();
 if(state.containers.some(c=>c.Mounts.some(m=>m.Name===name))){process.stderr.write('volume is in use\n');process.exit(1);}
 if(fault==='rm-first-failed'&&state.removed.length===0||fault==='rm-second-failed'&&state.removed.length===1||fault==='rm-third-failed'&&state.removed.length===2)fail();
 if(!state.volumes[name])fail();delete state.volumes[name];state.removed.push(name);save();out(name+'\n');
} else if(args[0]==='compose') {
 const project=args[args.indexOf('-p')+1];
 if(args.includes('config'))out(state.models[project]);
 else if(args.includes('down')) {
  if(!args.includes('-v'))fail();
  state.containers=state.containers.filter(c=>c.Config.Labels['com.docker.compose.project']!==project);
  for(const v of Object.values(state.models[project].volumes))if(!v.external) {
   if(state.containers.some(c=>c.Mounts.some(m=>m.Name===v.name)))fail();
   if(state.volumes[v.name]&&fault!=='down-retains-volume'){delete state.volumes[v.name];state.removed.push(v.name);}
  }save();
 } else fail();
} else if(args[0]==='create') {
 if(!args.includes('--user')||args[args.indexOf('--user')+1]!=='1654:1654'||!args.includes('--entrypoint'))fail();
 const mount=args[args.indexOf('--mount')+1];
 if(!mount.endsWith(',readonly')||!mount.includes('target=/work'))fail();
 const vol=mount.match(/source=([^,]+)/)[1];if(!state.volumes[vol])fail();
 const id='6'.repeat(64);state.auditProgram=args[args.indexOf('-c')+1];state.containers.push({Id:id,Image:'sha256:'+'a'.repeat(64),State:{Running:false,Status:'created'},Config:{Labels:{'io.antiphon.audit':'true'}},Mounts:[{Type:'volume',Name:vol,Source:state.volumes[vol].Mountpoint,Destination:'/work'}]});save();out(id+'\n');
} else if(args[0]==='start') {
 const id=args.at(-1),c=state.containers.find(c=>c.Id===id);if(!c)fail();
 // Remap the mount path, preserving unrelated scratch filenames such as /worktrees.
 const program=state.auditProgram.replace(/\/work(?=[\/\s"')]|$)/g,path.join(root,'work'));
 const run=cp.spawnSync('bash',['-c',program],{env:{...process.env},encoding:'utf8',timeout:15000});out(run.stdout||'');c.State={Running:false,Status:'exited'};save();process.exit(run.status??2);
} else if(args[0]==='cp') {
 // The pinned audit helper receives only the test-materialized production program.
 const source=args[1];fs.copyFileSync(source,path.join(root,'audit.sh'));
} else fail();
NODE
