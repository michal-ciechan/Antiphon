import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';
import { fileURLToPath } from 'node:url';

// Daemon-free acceptance: real Compose files, production wrappers and complete phase functions.
// Deployment side effects are replaced; seed_runner_checkout stops each phase AFTER its env write.
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const root = fs.mkdtempSync(path.join(repo, '.antiphon/c346-compose-'));
console.log('evidence=' + root);
const key = 'PhoneHome__WorkspaceSlots__Enabled';
const source = fs.readFileSync(path.join(repo, 'scripts/c590-remote.sh'), 'utf8');
function extract(name) {
  const start = source.indexOf('\n' + name + '() {');
  assert.ok(start >= 0, 'missing production function ' + name);
  const end = source.indexOf('\n}', start);
  assert.ok(end > start);
  return source.slice(start + 1, end + 2);
}
const functions = ['stack_env_value', 'runner_workspace_slots_enabled', 'deployed_sha12',
  'broker_sha12', 'compose_host', 'compose_temp', 'case_deploy_parent', 'case_deploy_temp_runner']
  .map(extract).join('\n');
const env = { ...process.env, CHECKOUT: repo, CASE_DIR: root, SERVER2_ENV: root + '/stack.env',
  SERVER2_TEMP_ENV: root + '/stack.temp.env', HOST_PROJECT: 'antiphon-runner',
  TEMP_PROJECT: 'antiphon-runner-temp', SHA: 'a'.repeat(40), C604_SERVER_ORIGIN: 'http://fixture.invalid',
  DEPLOY_KEY: root + '/deploy-key', PHONE_HOME_SECRET: root + '/phone-home',
  CLAUDE_OAUTH_TOKEN_PATH: root + '/claude-token', GIT_IDENTITY_PATH: root + '/gitconfig',
  CODEX_HOME_PATH: root + '/codex', GITHUB_TOKEN_DIR_PATH: root + '/github-token',
  RUNNER_GROK_STORE_DIR: root + '/grok', RUNNER_GIT_USER_NAME: 'fixture',
  RUNNER_GIT_USER_EMAIL: 'fixture@example.invalid', FIXTURE_ROOT: root };
// Absence must be real: neither an inherited flag nor Compose's ambient file selection may mask it.
delete env[key];
delete env.C1008_CONTEXT;
delete env.C1008_COMPOSE_DIR;
delete env.C1008_RESUME;
delete env.COMPOSE_FILE;
delete env.COMPOSE_ENV_FILES;
assert.equal(Object.hasOwn(env, key), false);
for (const name of ['deploy-key', 'phone-home', 'claude-token', 'gitconfig'])
  fs.writeFileSync(path.join(root, name), 'inert-fixture\n');
for (const name of ['codex', 'github-token', 'grok']) fs.mkdirSync(path.join(root, name));
const prelude = `set -euo pipefail
${functions}
write_result() { printf '%s\\n' "$2"; exit "$3"; }
`;
function bash(body, expected = 0) {
  const result = cp.spawnSync('bash', [], {input: prelude + body, env, cwd: repo,
    encoding: 'utf8', timeout: 30000});
  assert.equal(result.error, undefined);
  assert.equal(result.status, expected, result.stdout + result.stderr);
  return result.stdout;
}
const initial = 'SOURCE_SHA12=' + 'a'.repeat(12) + '\nBUILD_SLOTS_SHA12=' + 'a'.repeat(12) + '\n';
function save(file, value) {
  fs.writeFileSync(file, initial + (value === undefined ? '' : key + '=' + value + '\n'));
  assert.equal(fs.readFileSync(file, 'utf8').includes(key + '='), value !== undefined);
}
const phaseStubs = `
require_lane() { [ "$1" = host ]; }
ensure_checkout() { :; }
ensure_runner_boot_files() { :; }
retire_c590_leftovers() { :; }
build_server2_images() { :; }
c849_prepare() { :; }
c849_require_ready() { :; }
c849_budget_gate() { :; }
ensure_build_slots_broker() { :; }
c1008_record_recreated() { :; }
c1008_rollout_lock() { :; }
c849_no_temp_containers() { :; }
c1609_temp_retirement_clearable() { :; }
seed_runner_checkout() { exit 77; }
docker() {
  [ "$1:$2" = volume:inspect ] || exit 91
  printf '%s\\n' "$FIXTURE_ROOT"
}
sudo() {
  [ "$1:$2" = -n:test ] && return 0
  [ "$1:$2" = -n:df ] || exit 92
  printf 'Filesystem blocks used available capacity mount\\nfixture 99999999 0 99999999 0%% /\\n'
}
`;
let matrices = 0;
for (const temp of [false, true]) {
  const file = temp ? env.SERVER2_TEMP_ENV : env.SERVER2_ENV;
  const other = temp ? env.SERVER2_ENV : env.SERVER2_TEMP_ENV;
  const wrapper = temp ? 'compose_temp' : 'compose_host';
  const phase = temp ? 'case_deploy_temp_runner' : 'case_deploy_parent';
  for (const value of [undefined, '', 'false', 'true']) {
    save(file, value);
    save(other, value === 'true' ? 'false' : 'true');
    const otherBytes = fs.readFileSync(other);
    const expected = value === 'true' ? 'true' : 'false';
    for (const rewritten of [false, true]) {
      if (rewritten) {
        bash(phaseStubs + '\n' + phase + '\n', 77);
        assert.match(fs.readFileSync(file, 'utf8'), new RegExp('^' + key + '=' + expected + '$', 'm'));
      }
      const model = JSON.parse(bash(wrapper + ' config --format json\n'));
      assert.equal(model.services['session-runner'].environment[key], expected);
      assert.equal(model.services['session-runner'].environment.PhoneHome__RunnerId,
        temp ? 'server2-temp' : 'server2');
      assert.deepEqual(fs.readFileSync(other), otherBytes, 'other project setting changed');
      matrices++;
      console.log(`PASS ${wrapper} input=${value === undefined ? 'absent' : JSON.stringify(value)} rewritten=${rewritten} rendered=${expected}`);
    }
  }
}
let refusals = 0;
for (const temp of [false, true]) {
  const file = temp ? env.SERVER2_TEMP_ENV : env.SERVER2_ENV;
  for (const value of ['yes', 'true\n' + key + '=false', '$(touch ' + root + '/injected)']) {
    save(file, value);
    const before = fs.readFileSync(file);
    for (const action of [temp ? 'compose_temp' : 'compose_host',
      temp ? 'case_deploy_temp_runner' : 'case_deploy_parent']) {
      const output = bash('require_lane() { [ "$1" = host ]; }\ndocker() { exit 91; }\n' + action + ' config --format json\n', 2);
      assert.match(output, /RunnerWorkspaceSlotsSettingInvalid/);
      assert.deepEqual(fs.readFileSync(file), before, 'refusal rewrote settings');
      refusals++;
    }
    assert.equal(fs.existsSync(root + '/injected'), false);
  }
}
// Missing file also defaults false; no env file is sourced.
assert.equal(bash('runner_workspace_slots_enabled "$FIXTURE_ROOT/nonexistent.env"\n').trim(), 'false');
save(env.SERVER2_ENV, undefined);
save(env.SERVER2_TEMP_ENV, undefined);
const current = JSON.parse(bash('compose_host config --format json\n'));
const oldPath = root + '/docker-compose.server2-runner.yml';
fs.writeFileSync(oldPath, cp.execFileSync('git', ['show',
  '303c2476acc3c26538b98717e01776d2385ca450:docker-compose.server2-runner.yml'], {cwd: repo}));
const base = JSON.parse(bash('C1008_COMPOSE_DIR="$FIXTURE_ROOT" compose_host config --format json\n'));
assert.equal(current.services['session-runner'].environment[key], 'false');
assert.equal(Object.hasOwn(base.services['session-runner'].environment, key), false);
delete current.services['session-runner'].environment[key];
assert.deepEqual(current, base, 'default render changed outside the new false flag');
assert.equal(matrices, 16);
assert.equal(refusals, 12);
console.log('PASS matrices=16 refusals=12 missing-file=1 base-equivalence=1');
