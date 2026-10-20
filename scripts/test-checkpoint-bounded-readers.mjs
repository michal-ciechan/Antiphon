import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import cp from 'node:child_process';
import { randomBytes } from 'node:crypto';
const [repo,dll,mode,shape,delayArgument]=process.argv.slice(2);
const preReadDelayMs=delayArgument===undefined?0:Number(delayArgument);
if(!Number.isInteger(preReadDelayMs)||preReadDelayMs<0||preReadDelayMs>10000||(delayArgument!==undefined&&mode!=='project'))
 throw new Error('pre-read delay requires project mode and an integer from 0 to 10000 ms');
const held=shape==='held';
const nonce=randomBytes(16).toString('hex');
const fixture=fs.mkdtempSync(path.join(os.tmpdir(),'antiphon-c1199-reader-'));
fs.mkdirSync(path.join(fixture,'bin'));
const pidfile=path.join(fixture,'descendant.pid');
const fifo=path.join(fixture,'gate');
if(held) cp.execFileSync('mkfifo',[fifo]);
if(mode==='project') {
 if(held) cp.execFileSync('mkfifo',[path.join(fixture,'fixture.csproj')]);
 else fs.writeFileSync(path.join(fixture,'fixture.csproj'),'<Project><ItemGroup><ProjectReference Include="child.csproj" /></ItemGroup></Project>');
} else {
 fs.writeFileSync(path.join(fixture,'fixture.csproj'),'<Project />');
 const payload=mode.includes('eval')?'{"Items":{"ProjectReference":[]}}':'ordinary-control';
 // The direct child exits; its descendant blocks in a FIFO open while retaining both pipes.
 const stub='#!/bin/sh\n'+(held?`/bin/cat '${fifo}' &\nprintf '%s' "$!" > '${pidfile}'\n`:'')+`printf '%s\\n' '${payload}'\nexit 0\n`;
 fs.writeFileSync(path.join(fixture,'bin',mode.includes('eval')?'dotnet':'git'),stub,{mode:0o700});
}
const args=['-NoProfile','-File',path.join(repo,'scripts/test-checkpoint-bounded-reader.ps1'),'-Repository',repo,'-ToolDll',dll,'-Mode',mode,'-Fixture',fixture];
if(held) args.push('-Held');
if(delayArgument!==undefined) args.push('-PreReadDelayMs',String(preReadDelayMs));
if(mode==='project') args.push('-TimingNonce',nonce);
const child=cp.spawn('pwsh',args,{detached:true,stdio:['ignore','pipe','pipe']});
let stdout='',stderr='',expired=false;
child.stdout.on('data',d=>stdout+=d); child.stderr.on('data',d=>stderr+=d);
function killDescendant(){if(fs.existsSync(pidfile)){try{process.kill(Number(fs.readFileSync(pidfile,'utf8')),'SIGKILL')}catch(e){if(e.code!=='ESRCH')throw e}}}
const bound=mode==='project'?4000:mode.includes('eval')?125000:mode==='script-git'?20000:35000;
// Project acceptance times only the real read; this separate ceiling rescues a stuck child.
const watchdogMs=mode==='project'?30000:bound;
const start=Date.now();
const timer=setTimeout(()=>{expired=true;killDescendant();try{process.kill(-child.pid,'SIGKILL')}catch(e){if(e.code!=='ESRCH')throw e}},watchdogMs);
const result=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',(code,signal)=>resolve({code,signal}))});
clearTimeout(timer);
let survivor=false;
if(held && mode!=='project' && fs.existsSync(pidfile)){
 const pid=Number(fs.readFileSync(pidfile,'utf8'));
 try {const stat=fs.readFileSync(`/proc/${pid}/stat`,'utf8');survivor=stat.slice(stat.lastIndexOf(')')+2).split(' ')[0]!=='Z';} catch(e){if(e.code!=='ENOENT')throw e}
}
killDescendant();try{process.kill(-child.pid,'SIGKILL')}catch(e){if(e.code!=='ESRCH')throw e}
let readElapsedMs=null,delayElapsedMs=null,timingValid=false,timingError=null;
if(mode==='project') {
 const records=stderr.split(/\r?\n/).filter(line=>line.startsWith('C1381_READ'));
 if(records.length!==1) timingError='expected exactly one project timing record';
 else {
  const match=/^C1381_READ ([0-9a-f]{32}) (project) (ordinary|held) (0|[1-9][0-9]*) (0|[1-9][0-9]*)$/.exec(records[0]);
  if(!match||match[1]!==nonce||match[2]!==mode||match[3]!==shape) timingError='invalid project timing binding';
  else {
   const read=Number(match[4]),delay=Number(match[5]);
   if(!Number.isSafeInteger(read)||!Number.isSafeInteger(delay)) timingError='invalid project timing duration';
   else {readElapsedMs=read;delayElapsedMs=delay;timingValid=true;}
  }
 }
}
console.log(JSON.stringify({mode,held,bound,watchdogMs,elapsed:Date.now()-start,nonce,preReadDelayMs,
 readElapsedMs,delayElapsedMs,timingValid,timingError,expired,survivor,...result,stdout,stderr}));
// Neither a valid timing receipt nor an expected refusal can excuse watchdog rescue or a survivor.
const projectTimingFailed=mode==='project'&&(!timingValid||readElapsedMs>=bound||delayElapsedMs<preReadDelayMs);
process.exitCode=expired||survivor||result.code!==0||projectTimingFailed?1:0;
fs.rmSync(fixture,{recursive:true});
