import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';

export function ownedRoot(root) {
  if (!path.isAbsolute(root) || !/^c994-real-[A-Za-z0-9]+$/.test(path.basename(root)) ||
      fs.lstatSync(root).isSymbolicLink() || fs.realpathSync(root)!==root) throw Error('FixtureRootInvalid');
  const file=path.join(root,'ownership.json');
  if(fs.lstatSync(file).isSymbolicLink())throw Error('FixtureLedgerInvalid');
  const ledger=JSON.parse(fs.readFileSync(file));
  if(ledger.schema!==1||!/^c994rd[0-9a-f]{16}$/.test(ledger.prefix))throw Error('FixtureLedgerInvalid');
  return ledger;
}
export function ownedObject(ledger,fact) {
  const name=(fact.Name||'').replace(/^\//,'');
  if(!name.startsWith(ledger.prefix)||name.includes('antiphon-runner')||
      fact.Config?.Labels?.['io.antiphon.c994-real']!==ledger.prefix)throw Error('FixtureOwnershipMismatch');
}
export function ownedDaemon(name,hostname,inContainer) {
  if(!inContainer||name!==hostname)throw Error('SiblingDaemonRefused');
}
export async function boundedChild(file,args,options={},milliseconds=90000) {
  const child=cp.spawn(file,args,{...options,stdio:['ignore','pipe','pipe'],detached:true});
  let stdout='',stderr='';child.stdout.on('data',b=>stdout+=b);child.stderr.on('data',b=>stderr+=b);
  let timedOut=false;
  const timer=setTimeout(()=>{timedOut=true;try{process.kill(-child.pid,'SIGKILL');}catch(e){if(e.code!=='ESRCH')throw e;}},milliseconds);
  try {
    const code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('close',resolve);});
    // close follows process death and both redirected streams' drain.
    if(timedOut)throw Object.assign(Error('FixtureChildTimeout'),{pid:child.pid,stdout,stderr,reaped:true});
    return {code,stdout,stderr};
  } finally {clearTimeout(timer);}
}
