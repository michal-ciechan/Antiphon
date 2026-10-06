import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';
import { fileURLToPath } from 'node:url';

// Read-only Compose materialization. No Docker daemon access, provider homes or secrets.
export function materialize(root, project = 'antiphon-runner', temp = false) {
  if (!path.isAbsolute(root) || fs.realpathSync(root) !== root) throw Error('FixtureRootInvalid');
  const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
  const files = [path.join(repo, 'docker-compose.server2-runner.yml')];
  if (temp) files.push(path.join(repo, 'docker-compose.server2-runner.temp.yml'));
  return renderCompose(root, project, files, repo);
}
// A synthetic previous generation: the shipped Compose file with the github-token
// bind removed, rendered by real `docker compose config`. No daemon and no secret contents.
export function materializePrevious(root, project = 'antiphon-runner', temp = false) {
  if (!path.isAbsolute(root) || fs.realpathSync(root) !== root) throw Error('FixtureRootInvalid');
  const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
  const dir = path.join(root, 'previous-compose');
  fs.mkdirSync(dir, {recursive:true});
  const base = fs.readFileSync(path.join(repo, 'docker-compose.server2-runner.yml'), 'utf8');
  const stripped = base.split('\n').filter(line => !line.includes('/run/antiphon/github-token')).join('\n');
  if (stripped === base || stripped.includes('/run/antiphon/github-token')) throw Error('PreviousBindNotRemoved');
  fs.writeFileSync(path.join(dir, 'docker-compose.server2-runner.yml'), stripped);
  const files = [path.join(dir, 'docker-compose.server2-runner.yml')];
  if (temp) {
    const overlay = path.join(repo, 'docker-compose.server2-runner.temp.yml');
    fs.copyFileSync(overlay, path.join(dir, 'docker-compose.server2-runner.temp.yml'));
    files.push(path.join(dir, 'docker-compose.server2-runner.temp.yml'));
  }
  return renderCompose(root, project, files, repo);
}
function renderCompose(root, project, files, projectDirectory) {
  const paths = {deployKey:root+'/deploy-key', phoneHome:root+'/phone-home',
    claude:root+'/claude-token', git:root+'/gitconfig', codex:root+'/codex', grok:root+'/grok', githubToken:root+'/github-token'};
  for (const key of ['deployKey','phoneHome','claude','git']) {
    if (!fs.existsSync(paths[key])) fs.writeFileSync(paths[key], 'inert-fixture-'+key+'\n');
  }
  for (const key of ['codex','grok','githubToken']) fs.mkdirSync(paths[key], {recursive:true});
  const env = {SOURCE_REVISION:'a'.repeat(40), SOURCE_SHA12:'a'.repeat(12), BUILD_SLOTS_SHA12:'a'.repeat(12),
    PHONE_HOME_SERVER_ORIGIN:'http://fixture.invalid', ANTIPHON_DEPLOY_KEY_FILE:paths.deployKey,
    PHONE_HOME_SECRET_FILE:paths.phoneHome, CLAUDE_OAUTH_TOKEN_FILE:paths.claude,
    RUNNER_GIT_IDENTITY_FILE:paths.git, RUNNER_CODEX_HOME_DIR:paths.codex, RUNNER_GROK_STORE_DIR:paths.grok,
    RUNNER_GITHUB_TOKEN_DIR:paths.githubToken};
  const envFile=root+'/compose.env';
  fs.writeFileSync(envFile,Object.entries(env).map(([k,v])=>k+'='+v).join('\n')+'\n');
  const args=['compose','--env-file',envFile,'-p',project,'--project-directory',projectDirectory];
  for (const file of files) args.push('-f', file);
  args.push('config','--format','json');
  const model=JSON.parse(cp.execFileSync('docker',args,{encoding:'utf8',timeout:30000,
    env:{...process.env,...env}}));
  return {model,paths,environment:env,signature:mountSignature(model)};
}
export function mountSignature(model) {
  return Object.fromEntries(['state-init','session-runner'].map(service=>[service,{
    volumes:[...model.services[service].volumes].sort((a,b)=>a.target.localeCompare(b.target)),
    secrets:model.services[service].secrets||[],tmpfs:model.services[service].tmpfs||[],
    secretFiles:model.secrets}]));
}
// A Docker-shaped full inspect projection for offline boundaries. Tmpfs is persisted
// in HostConfig even when an exited Engine omits its ephemeral Mounts entry.
export function inspectProjection(model, volumes, service) {
  const mounts=model.services[service].volumes.map(m=>m.type==='volume'?{
    Type:'volume',Name:model.volumes[m.source].name,Source:volumes[model.volumes[m.source].name].Mountpoint,
    Destination:m.target,RW:!(m.read_only??false)}:{Type:'bind',Source:m.source,Destination:m.target,RW:!(m.read_only??false)});
  for(const s of model.services[service].secrets||[])mounts.push({Type:'bind',Source:model.secrets[s.source].file,
    Destination:s.target.startsWith('/')?s.target:'/run/secrets/'+s.target,RW:false});
  return {Mounts:mounts,HostConfig:{Tmpfs:Object.fromEntries((model.services[service].tmpfs||[]).map(t=>[t,''])),Mounts:[]}};
}
if(process.argv[1]===fileURLToPath(import.meta.url)) {
  const [root,project,temp,mode]=process.argv.slice(2);
  const rendered = mode === 'previous'
    ? materializePrevious(root,project,temp==='true')
    : materialize(root,project,temp==='true');
  process.stdout.write(JSON.stringify(rendered));
}
