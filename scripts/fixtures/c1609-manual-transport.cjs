const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const root=process.env.C1609_MANUAL_FIXTURE;
if(!root||!path.isAbsolute(root)||fs.realpathSync(root)!==root||!/^c1008-host-[A-Za-z0-9]+$/.test(path.basename(root)))throw Error('owned manual fixture required');
const args=process.argv.slice(3);
const map=s=>s.replaceAll('/home/mc/antiphon-server2',root+'/server').replaceAll('/home/mc/antiphon-c590',root+'/control');
if(process.argv[2]==='scp'){
 const v=[];for(let i=0;i<args.length;i++){if(args[i]==='-o'){i++;continue;}if(args[i]==='-r')continue;v.push(args[i]);}
 const resolve=s=>map(s.replace(/^mc@server2:/,'')).replace(/^\/work\/test-evidence\/[^/]+/,root+'/evidence');
 let src=resolve(v[0]),dst=resolve(v[1]);
 // Local script uploads are read-only. Every destination and remote download
 // source must stay within this exact fixture root.
 let allowed=dst.startsWith(root+'/');
 const registry=root+'/wrapper-evidence-roots.json';
 if(!allowed&&fs.existsSync(registry)){
  const roots=JSON.parse(fs.readFileSync(registry));
  if(!Array.isArray(roots))throw Error('fixture evidence registry');
  allowed=roots.some(dir=>typeof dir==='string'&&/\/\.antiphon\/rolling-server2\/c727[0-9a-f]{12}$/.test(dir)&&
   fs.realpathSync(dir)===dir&&(dst===dir||dst.startsWith(dir+'/')));
 }
 if(!allowed)throw Error('fixture destination escaped');
 if(v[0].startsWith('mc@server2:')&&!src.startsWith(root+'/'))throw Error('fixture source escaped');
 if(fs.existsSync(dst)&&fs.statSync(dst).isDirectory())dst=path.join(dst,path.basename(src));
 fs.cpSync(src,dst,{recursive:true});process.exit(0);
}
if(process.argv[2]!=='ssh')throw Error('transport mode');
let cmd=args.includes('-s')?fs.readFileSync(0,'utf8'):args.at(-1);
cmd=cmd.replace(/printf '%s' '([A-Za-z0-9+/=]+)' \| base64 -d \| bash/g,(_,encoded)=>
 "printf '%s' '"+Buffer.from(map(Buffer.from(encoded,'base64').toString('utf8'))).toString('base64')+"' | base64 -d | bash");
cmd=map(cmd);
const m=cmd.match(/bash '([^']+\/c590-remote.sh)'/);
if(m){
 if(!m[1].startsWith(root+'/control/'))throw Error('unowned host script');
 let source=fs.readFileSync(m[1],'utf8');
 const injection=fs.readFileSync(root+'/manual-injection.sh','utf8');
 source=source.replaceAll("trap 'ec=$?;",injection+"\ntrap 'ec=$?;");
 const file=root+'/manual-host-'+process.pid+'.sh';fs.writeFileSync(file,source);
 cmd=cmd.replace(m[0],"bash '"+file+"'");
}
fs.appendFileSync(root+'/manual-ssh-trace',JSON.stringify({packet:args.includes('-s'),hostCase:!!m,caseName:cmd.match(/export C590_CASE='([a-z-]+)'/)?.[1]||null})+'\n');
fs.writeFileSync(root+'/manual-command-'+process.pid+'.sh',cmd,{mode:0o600});
if(cmd.includes("export C1609_EXCHANGE='1'")){
 const child=cp.spawn('bash',['-c',cmd],{env:process.env,stdio:['pipe','pipe','pipe']});
 const log=fs.createWriteStream(root+'/manual-command-'+process.pid+'.log',{mode:0o600});
 process.stdin.pipe(child.stdin);child.stdin.on('error',()=>{});
 child.stdout.on('data',chunk=>{log.write(chunk);process.stdout.write(chunk);});
 child.stderr.on('data',chunk=>{log.write(chunk);process.stderr.write(chunk);});
 child.once('error',()=>{log.end();process.exitCode=2;});
 child.once('close',code=>{process.stdin.unpipe(child.stdin);process.stdin.pause();log.end(()=>process.exit(code??2));});
}else{
 const r=cp.spawnSync('bash',['-c',cmd],{env:process.env,encoding:'utf8',timeout:45000,maxBuffer:4*1024*1024});
 fs.writeFileSync(root+'/manual-command-'+process.pid+'.log',(r.stdout||'')+(r.stderr||''),{mode:0o600});
 process.stdout.write(r.stdout||'');process.stderr.write(r.stderr||'');process.exit(r.status??2);
}
