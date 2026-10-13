import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import cp from 'node:child_process';
const [repo,dll,mode,shape]=process.argv.slice(2);
const held=shape==='held';
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
const child=cp.spawn('pwsh',args,{detached:true,stdio:['ignore','pipe','pipe']});
let stdout='',stderr='',expired=false;
child.stdout.on('data',d=>stdout+=d); child.stderr.on('data',d=>stderr+=d);
function killDescendant(){if(fs.existsSync(pidfile)){try{process.kill(Number(fs.readFileSync(pidfile,'utf8')),'SIGKILL')}catch(e){if(e.code!=='ESRCH')throw e}}}
const bound=mode==='project'?4000:mode.includes('eval')?125000:mode==='script-git'?20000:35000;
const start=Date.now();
const timer=setTimeout(()=>{expired=true;killDescendant();try{process.kill(-child.pid,'SIGKILL')}catch(e){if(e.code!=='ESRCH')throw e}},bound);
const result=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',(code,signal)=>resolve({code,signal}))});
clearTimeout(timer);
let survivor=false;
if(held && mode!=='project' && fs.existsSync(pidfile)){
 const pid=Number(fs.readFileSync(pidfile,'utf8'));
 try {const stat=fs.readFileSync(`/proc/${pid}/stat`,'utf8');survivor=stat.slice(stat.lastIndexOf(')')+2).split(' ')[0]!=='Z';} catch(e){if(e.code!=='ENOENT')throw e}
}
killDescendant();try{process.kill(-child.pid,'SIGKILL')}catch(e){if(e.code!=='ESRCH')throw e}
console.log(JSON.stringify({mode,held,bound,elapsed:Date.now()-start,expired,survivor,...result,stdout,stderr}));
// A hard-bound termination or surviving descendant is an assertion failure, never a passing timeout.
process.exitCode=expired||survivor||result.code!==0?1:0;
fs.rmSync(fixture,{recursive:true});
