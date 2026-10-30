#!/usr/bin/env bash
# File-backed Docker boundary. Every unknown operation fails; never contacts a daemon.
set -euo pipefail
node - "$@" <<'NODE'
const fs = require('node:fs'), path = require('node:path'), cp = require('node:child_process'), crypto = require('node:crypto');
const root = process.env.C1008_FIXTURE_ROOT;
if (!root || !/^c1008-host-/.test(path.basename(root))) throw Error('fixture root invalid');
const file = path.join(root, 'docker.json'), trace = path.join(root, 'docker-trace.jsonl');
const args = process.argv.slice(2), state = JSON.parse(fs.readFileSync(file));
fs.appendFileSync(trace, JSON.stringify(args)+'\n');
const save = () => fs.writeFileSync(file, JSON.stringify(state));
const out = x => process.stdout.write(typeof x === 'string' ? x : JSON.stringify(x));
const fail = () => process.exit(2);
// The preview child execs bare chroot. Resolve it once and keep that directory
// on PATH. A missing binary is a harness failure, not an unclassified audit.
let resolvedChrootDir = '';
function chrootDirectory() {
  if (resolvedChrootDir) return resolvedChrootDir;
  const looked = [];
  const seen = new Set();
  for (const dir of [...(process.env.PATH || '').split(':'), '/usr/sbin', '/sbin', '/usr/bin', '/bin']) {
    if (!dir || seen.has(dir)) continue;
    seen.add(dir);
    looked.push(dir);
    try {
      fs.accessSync(path.join(dir, 'chroot'), fs.constants.X_OK);
      resolvedChrootDir = dir;
      return dir;
    } catch {}
  }
  const msg = 'preview helper: chroot not found; looked in ' + looked.join(':') + '\n';
  try { fs.appendFileSync(path.join(root, 'preview-helper-stderr'), msg); } catch {}
  process.stderr.write(msg);
  process.exit(127);
}
function pathWithChroot(base) {
  const dir = chrootDirectory();
  const parts = (base || '/usr/bin:/bin').split(':').filter(Boolean);
  if (!parts.includes(dir)) parts.unshift(dir);
  return parts.join(':');
}
// Optional host-proof bind and inventory mode. With neither present, mounts and
// forwarded env stay the production set (timeout, optional scratch quota, runner-tmp).
const classifyAudit = (mounts, envs) => {
  const proofEnvs = envs.filter(e => /^(C1008_HOST_PROOF|C1008_OPERATION|C1008_OPERATION_STARTED)=/.test(e));
  const inventoryEnv = envs.filter(e => e.startsWith('C1008_AUDIT_MODE='));
  let auditMounts = mounts;
  let proofSource = '';
  if (inventoryEnv.length || proofEnvs.length) {
    if (inventoryEnv.length && proofEnvs.length) return null;
    if (inventoryEnv.length) {
      if (inventoryEnv.length !== 1 || inventoryEnv[0] !== 'C1008_AUDIT_MODE=inventory') return null;
      if (mounts.some(m => m.includes('c1008-host-proof'))) return null;
    }
    if (proofEnvs.length) {
      if (proofEnvs.length !== 3) return null;
      const keys = new Set(proofEnvs.map(e => e.slice(0, e.indexOf('='))));
      if (keys.size !== 3) return null;
      if (!proofEnvs.includes('C1008_HOST_PROOF=/c1008-host-proof')) return null;
      if (!proofEnvs.some(e => /^C1008_OPERATION=c1008[0-9a-f]{32}$/.test(e))) return null;
      if (!proofEnvs.some(e => /^C1008_OPERATION_STARTED=[0-9]+$/.test(e))) return null;
      const last = mounts[mounts.length - 1] || '';
      const matched = /^type=bind,source=(\/[A-Za-z0-9._/-]+),target=\/c1008-host-proof,readonly$/.exec(last);
      if (!matched || matched[1].includes('..') || matched[1].includes('//')) return null;
      let st;
      try { st = fs.lstatSync(matched[1]); } catch (err) { return null; }
      if (!st.isDirectory() || st.isSymbolicLink()) return null;
      proofSource = matched[1];
      auditMounts = mounts.slice(0, -1);
    }
  }
  const extra = envs.filter(e => e !== 'C1008_TMP_MOUNT=/runner-tmp' && !proofEnvs.includes(e) && !inventoryEnv.includes(e));
  return {mounts: auditMounts, extra, proofSource};
};
// CARD-1105. Run the pty-host probe against one fixture shape. tmpAssets===false
// stays a probe exit 1 and is not an absent directory.
const ptyProbe = (shape, script) => {
  const shapes = ['absent','empty','unreadable','other-uid','ok','file','symlink','dangling'];
  if (!shapes.includes(shape)) return 2;
  const dest = fs.mkdtempSync('/tmp/c1008-pty-');
  let status = 1;
  try {
    fs.chmodSync(dest, 0o755);
    const target = path.join(dest, 'antiphon-pty-hosts');
    if (shape === 'empty' || shape === 'unreadable' || shape === 'other-uid' || shape === 'ok') fs.mkdirSync(target);
    if (shape === 'unreadable') fs.chmodSync(target, 0o000);
    if (shape === 'other-uid') fs.writeFileSync(path.join(target, 'host.log'), 'other\n', { mode: 0o044 });
    if (shape === 'ok') fs.writeFileSync(path.join(target, 'host.log'), 'ok\n', { mode: 0o644 });
    if (shape === 'file') fs.writeFileSync(target, 'file\n', { mode: 0o755 });
    if (shape === 'symlink') {
      const real = path.join(dest, 'real');
      fs.mkdirSync(real);
      fs.writeFileSync(path.join(real, 'host.log'), 'ok\n', { mode: 0o644 });
      fs.symlinkSync(real, target);
    }
    if (shape === 'dangling') fs.symlinkSync(path.join(dest, 'missing'), target);
    const run = cp.spawnSync('bash', ['-c', script.split('/tmp/antiphon-pty-hosts').join(target)], { encoding: 'utf8', timeout: 15000 });
    status = run.status === null || run.status === undefined ? 2 : run.status;
  } finally {
    try { fs.chmodSync(path.join(dest, 'antiphon-pty-hosts'), 0o755); } catch (e) { /* absent or dangling */ }
    fs.rmSync(dest, { recursive: true, force: true });
  }
  return status;
};
const findContainer = (token) => state.containers.find(c => c.Id === token || c.Name === token);
// A name the preview did not choose. Inspect and run both observe it, and neither removes it.
const plantForeignPreview = (containerName) => {
  if (!containerName || !containerName.startsWith('c1008-preview-')) return null;
  let existing = findContainer(containerName);
  if (existing) return existing;
  existing = {Id: '9'.repeat(64), Name: containerName, State: {Running: false, Status: 'exited'},
    Config: {Labels: {'io.antiphon.foreign': '1'}}, Mounts: []};
  state.containers.push(existing);
  save();
  return existing;
};
// Bound retire-temp preview. The same program as apply, with both volumes
// read-only in a private mount namespace. Anything that could write a volume
// fails closed. --network none is apply's opposite: ls-remote and fetch then
// fail the way a network namespace with no DNS fails. No --network matches
// apply's docker create. A normal run records no container (--rm) and writes a
// cidfile id that was never created, so release's inspect misses. A hang marker
// keeps one running until SIGKILL.
const previewRun = () => {
  const reason = (why) => {
    fs.appendFileSync(path.join(root, 'preview-helper-stderr'), why + '\n');
    process.stderr.write(why + '\n');
    process.exit(2);
  };
  if (fs.existsSync(path.join(root, 'preview-name-taken'))) {
    const namedAt = args.indexOf('--name');
    const takenName = namedAt >= 0 ? args[namedAt + 1] : '';
    const foreign = plantForeignPreview(takenName);
    process.stderr.write('Conflict. The container name is already in use by container "' + (foreign ? foreign.Id : '') + '"\n');
    process.exit(125);
  }
  if (args.includes('--privileged') || args.includes('--cap-add') || args.includes('-v') || args.includes('--volume')) reason('preview flag refused');
  if (!args.includes('--rm')) reason('preview requires --rm');
  const network = args.indexOf('--network');
  if (network >= 0 && args[network + 1] !== 'none') reason('preview network does not match apply');
  const offline = network >= 0 && args[network + 1] === 'none';
  const dropped = args.indexOf('--cap-drop');
  if (dropped < 0 || args[dropped + 1] !== 'ALL') reason('preview requires --cap-drop ALL');
  if (args.indexOf('--user') < 0 || args[args.indexOf('--user') + 1] !== '1654:1654') reason('preview requires --user 1654:1654');
  if (args.indexOf('--entrypoint') < 0 || args[args.indexOf('--entrypoint') + 1] !== '/bin/bash') reason('preview requires --entrypoint /bin/bash');
  if (!args.includes('--init')) reason('preview requires --init');
  const security = args.indexOf('--security-opt');
  if (security < 0 || args[security + 1] !== 'no-new-privileges') reason('preview requires no-new-privileges');
  const named = args.indexOf('--name');
  const previewName = named >= 0 ? (args[named + 1] || '') : '';
  const previewNameOk = /^c1008-preview-(c1008[0-9a-f]{32})-([0-9a-f]{16})$/.exec(previewName);
  if (!previewNameOk) reason('preview name refused');
  const labelAt = args.indexOf('--label');
  const labelValue = labelAt >= 0 ? (args[labelAt + 1] || '') : '';
  if (labelValue !== 'io.antiphon.preview-audit=' + previewNameOk[1] + ':' + previewNameOk[2]) reason('preview label refused');
  const cidAt = args.indexOf('--cidfile');
  const cidPath = cidAt >= 0 ? args[cidAt + 1] : '';
  if (!cidPath) reason('preview requires --cidfile');
  const programAt = args.indexOf('-c');
  if (programAt < 1 || !/^sha256:[0-9a-f]{64}$/.test(args[programAt - 1] || '')) reason('preview image must be the pinned id');
  const mounts = args.flatMap((a, i) => a === '--mount' ? [args[i + 1]] : []);
  const envs = args.flatMap((a, i) => a === '--env' ? [args[i + 1]] : []);
  const mount = mounts[0] || '';
  if (!mount.endsWith(',readonly') || !mount.includes('target=/work')) reason('preview work mount refused');
  const vol = (mount.match(/source=([^,]+)/) || [])[1];
  if (!vol || !state.volumes[vol]) reason('preview work volume missing');
  if (mounts[1] !== 'type=tmpfs,destination=/tmp,tmpfs-mode=1777,tmpfs-size=2147483648') reason('preview tmpfs refused');
  const classified = classifyAudit(mounts, envs);
  if (!classified) reason('preview mount set refused');
  const auditMounts = classified.mounts;
  const proofSource = classified.proofSource;
  const tmpVol = vol.replace(/_work$/, '_runner-tmp');
  const mountEnv = envs.filter(e => e === 'C1008_TMP_MOUNT=/runner-tmp');
  const extra = classified.extra;
  const forwarded = extra.every(e => /^C1008_AUDIT_TIMEOUT_SECONDS=[1-9][0-9]{0,6}$/.test(e) || /^C1008_SCRATCH_QUOTA_BYTES=[0-9]+$/.test(e))
    && new Set(extra.map(e => e.split('=', 1)[0])).size === extra.length;
  if (!auditMounts.filter(m => m.startsWith('type=volume,')).every(m => m.endsWith(',readonly'))) reason('preview volume mount is writable');
  let tmpMount = '';
  if (auditMounts.length === 3) {
    if (auditMounts[2] !== 'type=volume,source=' + tmpVol + ',target=/runner-tmp,readonly' || !state.volumes[tmpVol] || mountEnv.length !== 1 || !forwarded) reason('preview runner-tmp mount refused');
    tmpMount = state.volumes[tmpVol].Mountpoint;
  } else if (auditMounts.length !== 2 || mountEnv.length || !forwarded || state.volumes[tmpVol]) reason('preview mount set refused');
  const program = args[programAt + 1];
  if (typeof program !== 'string' || program.length === 0) reason('preview program missing');
  if (fs.existsSync(path.join(root, 'preview-hang'))) {
    const id = crypto.randomBytes(32).toString('hex');
    if (cidPath) fs.writeFileSync(cidPath, id + '\n');
    const labels = {};
    for (let i = 0; i < args.length; i++) if (args[i] === '--label' && args[i + 1]) {
      const eq = args[i + 1].indexOf('=');
      if (eq > 0) labels[args[i + 1].slice(0, eq)] = args[i + 1].slice(eq + 1);
    }
    state.containers.push({Id: id, Name: previewName, State: {Running: true, Status: 'running', Pid: process.pid},
      Config: {Labels: labels}, Mounts: []});
    save();
    fs.writeFileSync(path.join(root, 'preview-helper-running'), id + '\n');
    // PID 1 with no init ignores TERM. Only SIGKILL, or docker kill's default, stops this helper.
    process.on('SIGTERM', () => {});
    process.on('SIGINT', () => {});
    process.on('SIGHUP', () => {});
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
      let cur;
      try { cur = JSON.parse(fs.readFileSync(file)); } catch { Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 200); continue; }
      const live = (cur.containers || []).find(item => item.Id === id);
      if (!live || live.State.Running !== true) process.exit(137);
      Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 200);
    }
    process.exit(137);
  }
  if (cidPath) fs.writeFileSync(cidPath, crypto.randomBytes(32).toString('hex') + '\n');
  if (offline) {
    const shim = path.join(root, 'git-shim');
    fs.mkdirSync(shim, {recursive: true});
    const realGit = cp.execFileSync('which', ['git'], {encoding: 'utf8'}).trim();
    fs.writeFileSync(path.join(shim, 'git'), '#!/bin/bash\ncase " $* " in\n  *" ls-remote "*|*" fetch "*) printf \'fatal: Could not resolve host: origin\\n\' >&2; exit 128 ;;\nesac\nexec ' + JSON.stringify(realGit) + ' "$@"\n', {mode: 0o755});
  }
  // uid 1654 cannot create /runner-tmp on the host root. A private root holds the
  // production paths; both volume paths are mounted read-only inside it.
  const stage = fs.mkdtempSync('/dev/shm/c1008-preview-');
  const env = {...process.env, C1008_SIMULATED_HELPER: '1', C1008_SIM_WORK: state.volumes[vol].Mountpoint, C1008_SIM_PROGRAM: program, C1008_SIM_STAGE: stage};
  const inheritedPath = process.env.PATH || '/usr/bin:/bin';
  env.PATH = offline
    ? path.join(root, 'git-shim') + ':' + pathWithChroot(inheritedPath)
    : pathWithChroot(inheritedPath);
  for (const e of envs) { const eq = e.indexOf('='); env[e.slice(0, eq)] = e.slice(eq + 1); }
  if (tmpMount) env.C1008_SIM_TMP = tmpMount;
  if (proofSource) env.C1008_SIM_PROOF = proofSource;
  const outer = [
    'set -euo pipefail',
    'mount --make-rprivate /',
    'mount -t tmpfs -o mode=0755 none "$C1008_SIM_STAGE"',
    'mkdir -p "$C1008_SIM_STAGE/bin" "$C1008_SIM_STAGE/lib" "$C1008_SIM_STAGE/lib64" "$C1008_SIM_STAGE/usr/bin" "$C1008_SIM_STAGE/usr/local" "$C1008_SIM_STAGE/proc" "$C1008_SIM_STAGE/dev" "$C1008_SIM_STAGE/tmp" "$C1008_SIM_STAGE/work" "$C1008_SIM_STAGE/runner-tmp" "$C1008_SIM_STAGE/var/tmp"',
    'mount --bind /bin "$C1008_SIM_STAGE/bin"',
    'mount --bind /lib "$C1008_SIM_STAGE/lib"',
    'mount --bind /lib64 "$C1008_SIM_STAGE/lib64"',
    'mount --bind /usr/bin "$C1008_SIM_STAGE/usr/bin"',
    'mount --bind /usr/local "$C1008_SIM_STAGE/usr/local"',
    'mount --bind /proc "$C1008_SIM_STAGE/proc"',
    'touch "$C1008_SIM_STAGE/dev/null" "$C1008_SIM_STAGE/dev/urandom" "$C1008_SIM_STAGE/dev/zero"',
    'mount --bind /dev/null "$C1008_SIM_STAGE/dev/null"',
    'mount --bind /dev/urandom "$C1008_SIM_STAGE/dev/urandom"',
    'mount --bind /dev/zero "$C1008_SIM_STAGE/dev/zero"',
    // A real helper image has /dev/fd. Process substitution in the audit uses it.
    'ln -s /proc/self/fd "$C1008_SIM_STAGE/dev/fd"',
    // Scratch /tmp is a private tmpfs. The fixture root is bind-mounted afterwards
    // at its own path, so a fixture under /tmp stays visible above that tmpfs.
    'mount -t tmpfs -o mode=1777,size=2147483648 none "$C1008_SIM_STAGE/tmp"',
    '[ -n "${C1008_FIXTURE_ROOT:-}" ] && [ "$C1008_FIXTURE_ROOT" != / ]',
    'mkdir -p -- "$C1008_SIM_STAGE$C1008_FIXTURE_ROOT"',
    'mount --bind -- "$C1008_FIXTURE_ROOT" "$C1008_SIM_STAGE$C1008_FIXTURE_ROOT"',
    'mount --bind -- "$C1008_SIM_WORK" "$C1008_SIM_STAGE$C1008_SIM_WORK"',
    'mount -o remount,bind,ro -- "$C1008_SIM_STAGE$C1008_SIM_WORK"',
    'mount --bind -- "$C1008_SIM_WORK" "$C1008_SIM_STAGE/work"',
    'mount -o remount,bind,ro -- "$C1008_SIM_STAGE/work"',
    'if [ -n "${C1008_SIM_TMP:-}" ]; then',
    '  mkdir -p -- "$C1008_SIM_STAGE$C1008_SIM_TMP"',
    '  mount --bind -- "$C1008_SIM_TMP" "$C1008_SIM_STAGE$C1008_SIM_TMP"',
    '  mount -o remount,bind,ro -- "$C1008_SIM_STAGE$C1008_SIM_TMP"',
    '  mount --bind -- "$C1008_SIM_TMP" "$C1008_SIM_STAGE/runner-tmp"',
    '  mount -o remount,bind,ro -- "$C1008_SIM_STAGE/runner-tmp"',
    'fi',
    'if [ -n "${C1008_SIM_PROOF:-}" ]; then',
    '  mkdir -p -- "$C1008_SIM_STAGE/c1008-host-proof"',
    '  mount --bind -- "$C1008_SIM_PROOF" "$C1008_SIM_STAGE/c1008-host-proof"',
    '  mount -o remount,bind,ro -- "$C1008_SIM_STAGE/c1008-host-proof"',
    'fi',
    'exec unshare --user --map-root-user -- chroot "$C1008_SIM_STAGE" /bin/bash -c "$C1008_SIM_PROGRAM"',
  ].join('\n');
  let run;
  try {
    const unshareArgs = ['--user', '--map-root-user', '--mount'];
    if (offline) unshareArgs.push('--net');
    unshareArgs.push('bash', '-c', outer);
    run = cp.spawnSync('unshare', unshareArgs, {env, encoding: 'utf8', timeout: 60000});
  } finally {
    if (stage.startsWith('/dev/shm/c1008-preview-')) fs.rmSync(stage, {recursive: true, force: true});
  }
  if (run.stdout) process.stdout.write(run.stdout);
  if (run.status !== 0) {
    const err = run.stderr || (run.error && run.error.message) || 'preview helper failed';
    fs.appendFileSync(path.join(root, 'preview-helper-stderr'), err + '\n');
  }
  process.exit(run.status === null || run.status === undefined ? 2 : run.status);
};
const name = args.at(-1), fault = state.fault || '';
if(state.dockerStderr&&args[0]==='inspect')process.stderr.write(state.dockerStderr+'\n');
if (fault === 'ps-error' && args[0] === 'ps') fail();
if (fault === 'inspect-error' && args[0] === 'inspect') fail();
if (fault === 'volume-ls-error' && args[0] === 'volume' && args[1] === 'ls') fail();
if (args[0] === 'info') { out(root+'\n'); }
else if(args[0]==='image'&&args[1]==='inspect'){if(fault==='image-inspect-error'||fault==='original-image-missing'&&name==='sha256:'+'b'.repeat(64))fail();out((fault==='image-inspect-wrong'?'sha256:'+'b'.repeat(64):name.startsWith('sha256:')?name:'sha256:'+'a'.repeat(64))+'\n');}
else if (args[0] === 'ps') {
 if(!state.containers.some(c=>['session-runner','state-init'].includes(c.Config.Labels['com.docker.compose.service']))) {
  // Census bookkeeping belongs only to the explicit attachment fault; reads do not mutate inventory.
  if(fault==='late-attachment')state.postStopPs=(state.postStopPs||0)+1;
  if(fault==='late-attachment'&&state.postStopPs===3)state.containers.push({Id:'5'.repeat(64),Image:'sha256:'+'a'.repeat(64),State:{Running:false,Status:'exited'},Config:{Labels:{}},Mounts:[{Type:'volume',Name:'antiphon-runner_work',Source:state.volumes['antiphon-runner_work'].Mountpoint,Destination:'/foreign',RW:true}]});
  if(fault==='final-ps-error')fail();
  if(fault==='c994-final-ps-partial'){out(state.containers[0].Id+'\n');fail();}
  save();
 }
 if(fault==='c994-ps-partial'){out(state.containers[0].Id+'\n');fail();}
 if(fault==='c994-ps-empty-error')fail();
 if(fault==='c994-truncated-id'){out('a'.repeat(12)+'\n');process.exit(0);}
 if(fault==='c994-nonhex-id'){out('z'.repeat(64)+'\n');process.exit(0);}
 if(fault==='c994-duplicate-id'){out(state.containers[0].Id+'\n'+state.containers[0].Id+'\n');process.exit(0);}
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
 if (fs.existsSync(path.join(root, 'preview-name-taken')) && /^c1008-preview-/.test(name)) {
  out([plantForeignPreview(name)]); process.exit(0);
 }
 const c=findContainer(name); if(!c){if(fault==='c994-id-absence-error')fail();process.stderr.write('Error: No such object: '+name+'\n');process.exit(1);}
 if(fault==='inspect-empty'){out('');process.exit(0);}
 if(fault==='inspect-malformed'){out('{');process.exit(0);}
 if(fault==='c994-wrong-id')c.Id='f'.repeat(64);
 if(fault==='c994-missing-state')delete c.State;
 if(fault==='c994-missing-image')delete c.Image;
 if(fault==='c994-missing-mounts')delete c.Mounts;
 if(fault==='c994-missing-labels')delete c.Config.Labels;
 if(fault==='c994-inspect-multirow'){out([c,c]);process.exit(0);}
 out([c]);
} else if(args[0]==='stop') {
 if(fault==='stop-failed')fail(); const c=state.containers.find(c=>c.Id===name);if(!c)fail();
 if(fault!=='stop-stays-running')c.State={Running:false,Status:'exited'};
 if(fault==='late-busy') { const p=path.join(root,'statuses.json'),s=JSON.parse(fs.readFileSync(p));s.server2.sessions=1;fs.writeFileSync(p,JSON.stringify(s)); }
 if(fault==='late-routing') { const p=path.join(root,'statuses.json'),s=JSON.parse(fs.readFileSync(p));s.server2.acceptingNewWork=true;fs.writeFileSync(p,JSON.stringify(s)); }
 if(fault==='late-land') { const p=path.join(root,'tasks.json'),s=JSON.parse(fs.readFileSync(p));(s.scopes[Object.keys(s.scopes)[0]].land||s.scopes[Object.keys(s.scopes)[0]]).items.push({id:'22222222-2222-2222-2222-222222222222',status:'Succeeded',runnerId:'server2',projectId:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1',scopeSource:'Task',landRequestedAt:'2026-10-03T09:00:00Z',landStartedAt:null});fs.writeFileSync(p,JSON.stringify(s)); }
 if(fault==='audit-drift') fs.writeFileSync(path.join(root,'work/repo/new-work'), 'retained');
 save();out(name+'\n');
} else if(args[0]==='kill') {
 const target=args.at(-1);
 const victim=findContainer(target);
 if(!victim){process.stderr.write('Error: No such container: '+target+'\n');process.exit(1);}
 let sig='KILL';
 for(let i=1;i<args.length;i++){
  if(args[i]==='-s'||args[i]==='--signal') sig=args[++i]||sig;
  else if(args[i].startsWith('--signal=')) sig=args[i].slice(9);
 }
 const normalized=String(sig).replace(/^SIG/,'').toUpperCase();
 if(normalized==='TERM'||normalized==='INT'||normalized==='HUP'){victim.State.IgnoredSignal=normalized;save();out(victim.Id+'\n');}
 else if(normalized==='KILL'){victim.State={Running:false,Status:'exited',Pid:victim.State&&victim.State.Pid};save();out(victim.Id+'\n');}
 else process.exit(1);
} else if(args[0]==='rm') {
 const force=args.includes('-f')||args.includes('--force');
 const target=args.at(-1);
 const found=findContainer(target);
 if(force){
  if(args.includes('-v'))fail();
  if(!found){process.stderr.write('Error: No such container: '+target+'\n');process.exit(1);}
  if(!found.Config||!found.Config.Labels||!found.Config.Labels['io.antiphon.preview-audit'])fail();
  state.containers=state.containers.filter(c=>c.Id!==found.Id);save();out(found.Id+'\n');
 } else {
 if(args.includes('-v'))fail();
 const c=found;if(!c||c.State.Running||fault==='helper-rm-failed'&&c.Config.Labels['io.antiphon.audit'])fail();
 if(fault==='owned-rm-failed'&&c.Config.Labels['com.docker.compose.service']==='session-runner')fail();
 state.cleanupRm=(state.cleanupRm||0)+1;
 if(fault==='c994-rm-'+state.cleanupRm){save();fail();}
 if(fault!=='c994-rm-retains')state.containers=state.containers.filter(c=>c.Id!==found.Id);
 const next=state.containers.find(c=>c.Config.Labels['com.docker.compose.project']==='antiphon-runner-temp');
 if(next&&fault==='c994-late-image')next.Image='sha256:'+'b'.repeat(64);
 if(next&&fault==='c994-late-mount')next.Mounts[0].Source='/foreign';
 if(next&&fault==='c994-late-state')next.State={Running:true,Status:'running'};
 if(next&&fault==='c994-late-id')next.Id='e'.repeat(64);
 if(fault==='c994-late-main'||fault==='c994-late-stamp') {const p=path.join(root,'statuses.json'),v=JSON.parse(fs.readFileSync(p));
  if(fault==='c994-late-main')v.server2.acceptingNewWork=false;else v['server2-temp'].retiredAt='2026-10-03T10:30:00Z';fs.writeFileSync(p,JSON.stringify(v));}
 if(fault==='c994-late-land'){const p=path.join(root,'tasks.json'),v=JSON.parse(fs.readFileSync(p));(v.scopes[Object.keys(v.scopes)[0]].land||v.scopes[Object.keys(v.scopes)[0]]).items.push({id:'22222222-2222-2222-2222-222222222222',status:'Succeeded',runnerId:'server2-temp',projectId:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1',scopeSource:'Task',landRequestedAt:'2020-01-01T00:00:00Z',landStartedAt:null});fs.writeFileSync(p,JSON.stringify(v));}
 if(fault==='c994-late-task'){const p=path.join(root,'tasks.json'),v=JSON.parse(fs.readFileSync(p));(v.scopes[Object.keys(v.scopes)[0]].open||v.scopes[Object.keys(v.scopes)[0]]).items.push({id:'22222222-2222-2222-2222-222222222222',status:'Working',runnerId:'server2-temp',projectId:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1',scopeSource:'Task',landRequestedAt:null,landStartedAt:null});fs.writeFileSync(p,JSON.stringify(v));}
 if(fault==='c994-final-replacement') {const replacement=structuredClone(c);replacement.Id='e'.repeat(64);state.containers.push(replacement);}
 save();out(name+'\n');
 }
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
 const composeDir=process.env.C1008_COMPOSE_DIR||'';
 const keyed=composeDir?project+'@'+path.basename(composeDir):'';
 const model=keyed&&Object.prototype.hasOwnProperty.call(state.models,keyed)?state.models[keyed]:state.models[project];
 if(args.includes('config')){if(model===undefined)fail();out(model);}
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
 // CARD-1105 repair 6: /work first, a private tmpfs at /tmp, and the runner-tmp volume
 // read-only at /runner-tmp with C1008_TMP_MOUNT when that volume exists.
 const mounts=args.flatMap((a,i)=>a==='--mount'?[args[i+1]]:[]),envs=args.flatMap((a,i)=>a==='--env'?[args[i+1]]:[]);
 const mount=mounts[0]||'';
 if(!mount.endsWith(',readonly')||!mount.includes('target=/work'))fail();
 const vol=mount.match(/source=([^,]+)/)[1];if(!state.volumes[vol])fail();
 if(mounts[1]!=='type=tmpfs,destination=/tmp,tmpfs-mode=1777,tmpfs-size=2147483648')fail();
 const tmpVol=vol.replace(/_work$/,'_runner-tmp'),Mounts=[{Type:'volume',Name:vol,Source:state.volumes[vol].Mountpoint,Destination:'/work'}];
 // The host always forwards the audit cap. C1008_SCRATCH_QUOTA_BYTES is optional and unset in production.
 const classified=classifyAudit(mounts,envs);if(!classified)fail();
 const auditMounts=classified.mounts,proofSource=classified.proofSource;
 const mountEnv=envs.filter(e=>e==='C1008_TMP_MOUNT=/runner-tmp');
 const extra=classified.extra;
 const forwarded=extra.every(e=>/^C1008_AUDIT_TIMEOUT_SECONDS=[1-9][0-9]{0,6}$/.test(e)||/^C1008_SCRATCH_QUOTA_BYTES=[0-9]+$/.test(e))&&new Set(extra.map(e=>e.split('=',1)[0])).size===extra.length;
 if(auditMounts.length===3){if(auditMounts[2]!=='type=volume,source='+tmpVol+',target=/runner-tmp,readonly'||!state.volumes[tmpVol]||mountEnv.length!==1||!forwarded)fail();
  Mounts.push({Type:'volume',Name:tmpVol,Source:state.volumes[tmpVol].Mountpoint,Destination:'/runner-tmp'});}
 else if(auditMounts.length!==2||mountEnv.length||!forwarded||state.volumes[tmpVol])fail();
 const id='6'.repeat(64);state.auditProgram=args[args.indexOf('-c')+1];state.auditEnv=envs;state.proofSource=proofSource;state.containers.push({Id:id,Image:'sha256:'+'a'.repeat(64),State:{Running:false,Status:'created'},Config:{Labels:{'io.antiphon.audit':'true'}},Mounts});save();out(id+'\n');
} else if(args[0]==='start') {
 const id=args.at(-1),c=state.containers.find(c=>c.Id===id);if(!c)fail();
 // Remap the mount path, preserving unrelated scratch filenames such as /worktrees.
 let program=state.auditProgram.replace(/\/work(?=[\/\s"')]|$)/g,path.join(root,'work'));
 let gitPath=process.env.PATH;
 if(state.gitFault) {
  // timeout execs Git directly; a shell function cannot receive that call.
  const shim=path.join(root,'git-fault-bin');fs.mkdirSync(shim,{recursive:true});
  const realGit=cp.execFileSync('which',['git'],{encoding:'utf8'}).trim();
  // count-exit128 fails only the per-tip count, after the object traversal succeeded.
  const match=state.gitFault==='count-exit128'?'*" rev-list --count "*':'*" rev-list "*';
  const fault=state.gitFault==='exit128'||state.gitFault==='count-exit128'?'exit 128':state.gitFault==='timeout'?'exit 124':state.gitFault==='empty'?'exit 0':state.gitFault==='nonnumeric'?'echo unknown; exit 0':'echo -1; exit 0';
  fs.writeFileSync(path.join(shim,'git'),'#!/bin/bash\ncase " $* " in '+match+') '+fault+' ;; esac\nexec '+JSON.stringify(realGit)+' "$@"\n',{mode:0o755});
  gitPath=shim+':'+gitPath;
 }
 if(state.gitStderr)process.stderr.write(state.gitStderr+'\n');
 // The runner-tmp volume is the fixture's runner-tmp directory.
 const auditEnv={};for(const e of state.auditEnv||[]){const eq=e.indexOf('=');const k=e.slice(0,eq),v=e.slice(eq+1);auditEnv[k]=(k==='C1008_TMP_MOUNT'&&v==='/runner-tmp')?path.join(root,'runner-tmp'):(k==='C1008_HOST_PROOF'&&v==='/c1008-host-proof'&&state.proofSource)?state.proofSource:v;}
 if(auditEnv.C1008_TMP_MOUNT)fs.mkdirSync(auditEnv.C1008_TMP_MOUNT,{recursive:true});
 const run=cp.spawnSync('bash',['-c',program],{env:{...process.env,PATH:gitPath,...auditEnv},encoding:'utf8',timeout:15000});out(run.stdout||'');c.State={Running:false,Status:'exited'};save();process.exit(run.status??2);
} else if(args[0]==='cp') {
 // The pinned audit helper receives only the test-materialized production program.
 const source=args[1];fs.copyFileSync(source,path.join(root,'audit.sh'));
} else if(args[0]==='exec') {
 if(args.includes('stat'))out(state.tmpMode||'1777');
 else if(args.includes('sh')){
  const script=args.includes('-c')?args[args.indexOf('-c')+1]:'';
  if(script.includes('/tmp/antiphon-pty-hosts')&&state.tmpProbe){
   if(state.tmpAssets===false)process.exit(1);
   if(state.tmpProbe==='error')fail();
   process.exit(ptyProbe(state.tmpProbe,script));
  }
  process.exit(state.tmpAssets===false?1:0);
 }
 else fail();
} else if (args[0] === 'run') previewRun();
else fail();
NODE
