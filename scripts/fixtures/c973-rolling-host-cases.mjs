import fs from 'node:fs'; import os from 'node:os'; import path from 'node:path'; import cp from 'node:child_process'; import crypto from 'node:crypto';
// Run only through build-slot.ps1, inside the review container's own nested daemon.
if (!fs.existsSync('/.dockerenv') || cp.execFileSync('docker',['info','--format','{{.Name}}'],{encoding:'utf8'}).trim() !== os.hostname()) throw Error('C957_REAL requires the local nested daemon');
const root=fs.mkdtempSync('/tmp/c957-real-'); console.log('C957_REAL_ROOT='+root);
const prefix='c957'+crypto.randomBytes(6).toString('hex'), containers=[],volumes=[],results=[];
const docker=(...args)=>cp.execFileSync('docker',args,{encoding:'utf8'}).trim();
const base=cp.execFileSync('git',['show','afb8ccfb128c53300fb960cf0bbb4433184cb748:scripts/c590-remote.sh'],{encoding:'utf8'}),branch=fs.readFileSync('scripts/c590-remote.sh','utf8');
const sha='7'.repeat(40);
fs.writeFileSync(root+'/source.json',JSON.stringify({base:'afb8ccfb128c53300fb960cf0bbb4433184cb748',branch:cp.execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),baseScriptSha256:crypto.createHash('sha256').update(base).digest('hex'),branchScriptSha256:crypto.createHash('sha256').update(branch).digest('hex'),shims:['host lane and root paths','cache lock/evidence mkdir on private roots','sudo via root container','compose main census via real labels','deploy checkout/boot boundaries']},null,2));
try {
 docker('pull','busybox:1.37');
 const image=docker('image','inspect','-f','{{.Id}}','busybox:1.37'),dockerroot=docker('info','-f','{{.DockerRootDir}}');
 const helper=docker('run','-d','--name',prefix+'-root','--network','none','--user','0:0','--mount',`type=bind,source=${dockerroot},target=${dockerroot}`,'--mount',`type=bind,source=${root},target=${root}`,'debian:trixie-slim','sleep','infinity'); containers.push(helper);
 for(const role of ['nuget-packages','nuget-scratch','npm-content']) {
  const name=prefix+'-'+role; docker('volume','create','--label','io.antiphon.owner=server2-runner','--label','io.antiphon.cache-schema=1','--label','io.antiphon.cache-role='+role,name); volumes.push(name);
  docker('run','--rm','--network','none','--user','0:0','--mount',`type=volume,source=${name},target=/cache`,'busybox:1.37','sh','-c','chown 1654:1654 /cache; chmod 0700 /cache');
 }
 const main=docker('run','-d','--label','com.docker.compose.project='+prefix+'main','--label','com.docker.compose.service=session-runner','busybox:1.37','sleep','infinity');containers.push(main);
 const www=root+'/www';fs.mkdirSync(www+'/api/session-runners/server2-temp',{recursive:true});fs.writeFileSync(www+'/api/session-runners/server2-temp/status',JSON.stringify({sessions:0,runnerSessions:0,queuedTasks:0,draining:true,acceptingNewWork:false,redirectTo:'server2',retireWhenIdle:false,dispatchEligible:true}));
 const endpoint=docker('run','-d','--publish','127.0.0.1::8080','--mount',`type=bind,source=${www},target=/www,readonly`,'busybox:1.37','httpd','-f','-p','8080','-h','/www'); containers.push(endpoint);
 const port=docker('port',endpoint,'8080/tcp').split(':').at(-1);
 function run(label,src,hostCase,extra='',expected=null) {
  const caseRoot=root+'/'+label,server=caseRoot+'/server2',ready=server+'/cache/seed-accepted';fs.mkdirSync(server+'/cache',{recursive:true});
  fs.writeFileSync(ready,`schema=2\nkind=cold\ncold=true\nsource-sha=${sha}\nimage=${image}\npackages=${volumes[0]}\nscratch=${volumes[1]}\nnpm=${volumes[2]}\n`);
  const injection=`
EVIDENCE_ROOT='${caseRoot}/evidence'; CASE_DIR="$EVIDENCE_ROOT/$CASE"; SERVER2_ROOT='${server}'; C849_READY='${ready}'
HOST_PROJECT=${prefix}main; TEMP_PROJECT=${prefix}temp
C849_PACKAGES=${volumes[0]}; C849_SCRATCH=${volumes[1]}; C849_NPM=${volumes[2]}
detect_lane() { LANE=host; }
ensure_dirs() { mkdir -p "$CASE_DIR"; }
c849_evidence_dir() { mkdir -p "$CASE_DIR"; }
c849_lock() { :; }
compose_host() { command docker ps -q --filter label=com.docker.compose.project=$HOST_PROJECT; }
sudo() { [ "$1" = -n ] && shift; command docker exec ${helper} "$@"; }
${extra}
`;
  const script=caseRoot+'/remote.sh';fs.writeFileSync(script,src.replace("trap 'ec=$?;",injection+"\ntrap 'ec=$?;"));
  const proc=cp.spawnSync('bash',[script],{env:{...process.env,C590_CASE:hostCase,C590_SHA:sha,C590_RUN:'c957real',C590_REEXEC:'1',C604_SERVER_ORIGIN:'http://127.0.0.1:'+port},encoding:'utf8',timeout:60000});
  fs.writeFileSync(caseRoot+'/run.log',(proc.stdout||'')+(proc.stderr||''));
  const receipt=JSON.parse(fs.readFileSync(caseRoot+'/evidence/'+hostCase+'/c590-result.json','utf8'));
  const result={name:label,exit:proc.status,diagnosis:receipt.diagnosis,accepted:receipt.accepted};results.push(result);console.log('C957_REAL '+JSON.stringify(result));
  if(expected && (proc.status!==expected[0]||receipt.diagnosis!==expected[1]))throw Error('Unexpected host result '+JSON.stringify(result));
 }
 const temp=docker('run','-d','--label','com.docker.compose.project='+prefix+'temp','--label','com.docker.compose.service=session-runner','busybox:1.37','sleep','infinity');containers.push(temp);
 run('base-cold-existing-temp',base,'runner-cache-seed','',[0,'']);
 run('branch-cold-existing-temp',branch,'runner-cache-seed','',[2,'CacheTempContainerExists']);
 const boot='ensure_checkout() { :; }; ensure_runner_boot_files() { :; }; build_server2_images() { write_result false ExpensiveBuildReached 2; }; SERVER2_ENV=/dev/null';
 run('base-deploy-existing-temp',base,'deploy-temp-runner',boot);
 run('branch-deploy-existing-temp',branch,'deploy-temp-runner',boot,[2,'CacheTempContainerExists']);
 docker('rm','-f',temp);containers.splice(containers.indexOf(temp),1);
 run('branch-cold-absent-temp',branch,'runner-cache-seed','',[0,'']);
 docker('rm','-f',main);containers.splice(containers.indexOf(main),1);
 for(const [label,src,expected] of [['base-missing-image',base,[2,'UnhandledExit 2']],['branch-missing-image',branch,[2,'CacheHelperImageMissing']]])
  run(label,src,'runner-cache-seed',`sed -i 's/^image=.*/image=sha256:${'0'.repeat(64)}/' "$C849_READY"`,expected);
 const mount=docker('volume','inspect','-f','{{.Mountpoint}}',volumes[0]),link=root+'/link';docker('exec',helper,'ln','-s',mount,link);
 for(const reader of ['observe','prune'])for(const fault of ['symlink','sudo-test-refused','symlink-sudo-test-refused']) {
  const tree=fault.startsWith('symlink')?link:mount;
  const action=reader==='observe'?'c849_observe_volume "$C849_PACKAGES" nuget-packages 999999':`c849_prune_validate_tree "${tree}" "${tree}"`;
  const extra=`docker() { if [ "$1" = volume ] && [ "\${4:-}" = '{{.Mountpoint}}' ] && [[ ${fault} == symlink* ]]; then printf '%s\\n' '${link}'; else command docker "$@"; fi; }
if [[ ${fault} == *sudo-test-refused ]]; then sudo() { [ "$1" = -n ] && shift; if [ "$1" = test ] && [ "$2" = -L ]; then return 77; fi; command docker exec ${helper} "$@"; }; fi
c849_preview() { ${action}; write_result true '' 0; }`;
  run('base-'+reader+'-'+fault,base,'runner-cache-prune-preview',extra,(fault==='symlink'||(reader==='prune'&&fault.startsWith('symlink')))?[2,'CacheTargetInvalid']:[0,'']);
  run('branch-'+reader+'-'+fault,branch,'runner-cache-prune-preview',extra,[2,'CacheTargetInvalid']);
 }
 fs.writeFileSync(root+'/results.json',JSON.stringify(results,null,2));console.log(`C957_REAL cases=${results.length} failures=0 evidence=${root}`);
} finally {
 for(const c of containers)cp.spawnSync('docker',['rm','-f',c]);
 for(const v of volumes)cp.spawnSync('docker',['volume','rm',v]);
 fs.writeFileSync(root+'/results.json',JSON.stringify(results,null,2));
}
