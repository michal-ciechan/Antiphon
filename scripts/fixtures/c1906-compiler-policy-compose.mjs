import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { withComposeFixture } from './compose-fixture-root.mjs';

// Daemon-free acceptance: real Compose files, production wrappers and complete phase functions.
// Deployment side effects are replaced; seed_runner_checkout stops each phase AFTER its env write.
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
await withComposeFixture('c1906-compose-', async root => {
const key = 'ANTIPHON_LINUX_COMPILER_POLICY';
const source = fs.readFileSync(path.join(repo, 'scripts/c590-remote.sh'), 'utf8');
function extract(name) {
  const start = source.indexOf('\n' + name + '() {');
  assert.ok(start >= 0, 'missing production function ' + name);
  const end = source.indexOf('\n}', start);
  assert.ok(end > start);
  return source.slice(start + 1, end + 2);
}
const functions = ['stack_env_value', 'runner_workspace_slots_enabled',
  'runner_linux_compiler_policy', 'deployed_sha12',
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
delete env.PhoneHome__WorkspaceSlots__Enabled;
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
// A rendered value supplies the native PTY -> build-wrapper witness.
const modelValue = process.argv.find(arg => arg.startsWith('--model-value='))?.slice('--model-value='.length);
if (modelValue !== undefined) {
  assert.ok(['0', '1'].includes(modelValue));
  save(env.SERVER2_ENV, modelValue);
  save(env.SERVER2_TEMP_ENV, modelValue === '1' ? '0' : '1');
  console.log('MODEL ' + JSON.stringify(JSON.parse(bash('compose_host config --format json\n'))));
  return;
}
// Refusals exercise the complete production phases and wrappers. Every boundary that could
// start/change anything leaves a marker and exits; checking the fixture tree catches writes too.
const malformed = [
  ['empty', key + '='],
  ['space-only', key + '= '],
  ['tab-only', key + '=\t'],
  ['leading-space-key', ' ' + key + '=1'],
  ['leading-tab-key', '\t' + key + '=1'],
  ['space-before-equals', key + ' =1'],
  ['tab-before-equals', key + '\t=1'],
  ['space-after-equals', key + '= true'],
  ['tab-after-equals', key + '=\ttrue'],
  ['trailing-space', key + '=1 '],
  ['trailing-tab', key + '=0\t'],
  ['crlf', key + '=1\r'],
  ['boolean-true', key + '=true'],
  ['boolean-false', key + '=false'],
  ['numeric-two', key + '=2'],
  ['leading-zero', key + '=01'],
  ['lowercase-key', key.toLowerCase() + '=1'],
  ['mixed-case-key', 'Antiphon_Linux_Compiler_Policy=1'],
  ['export-prefix', 'export ' + key + '=1'],
  ['quoted-value', key + '="1"'],
  ['inline-comment', key + '=1 # active assignment'],
  ['comment-without-space', key + '=1#active'],
  ['indented-duplicate', key + '=1\n ' + key + '=0'],
  ['tabbed-duplicate', key + '=1\n\t' + key + '=0'],
  ['spaced-duplicate', key + '=1\n' + key + ' =0'],
  ['comment-looking-duplicate', key + '=1\n' + key + '=0 # active'],
  ['case-duplicate', key + '=1\n' + key.toUpperCase() + '=0'],
  ['same-value-duplicate', key + '=1\n' + key + '=1'],
  ['later-duplicate', key + '=1\nUNRELATED=0\n' + key + '=0'],
];
const invalid = [
  ['invalid-value', key + '=yes'],
  ['duplicate', key + '=1\n' + key + '=0'],
  ['shell-value', key + '=$(touch ' + root + '/injected)'],
  ...malformed,
];
function snapshot() {
  function entries(dir) {
    return fs.readdirSync(dir).sort().flatMap(name => {
      const file = path.join(dir, name);
      return fs.statSync(file).isDirectory() ? [[file, 'directory'], ...entries(file)] : [[file, fs.readFileSync(file)]];
    });
  }
  return entries(root);
}
const selectedShape = process.argv.find(arg => arg.startsWith('--refusal-case='))?.slice('--refusal-case='.length);
const selectedAction = process.argv.find(arg => arg.startsWith('--refusal-action='))?.slice('--refusal-action='.length);
assert.ok(!selectedShape || invalid.some(([shape]) => shape === selectedShape), 'unknown refusal case');
assert.ok(!selectedAction || ['compose_host', 'compose_temp', 'case_deploy_parent', 'case_deploy_temp_runner'].includes(selectedAction), 'unknown refusal action');
let attempts = 0;
let refusals = 0;
const failures = [];
for (const temp of (process.argv.includes('--models-only') ? [] : [false, true])) {
  const file = temp ? env.SERVER2_TEMP_ENV : env.SERVER2_ENV;
  const other = temp ? env.SERVER2_ENV : env.SERVER2_TEMP_ENV;
  for (const [shape, assignment] of invalid) {
    if (selectedShape && shape !== selectedShape) continue;
    fs.writeFileSync(file, initial + assignment + '\n');
    save(other, '1');
    for (const action of [temp ? 'compose_temp' : 'compose_host',
      temp ? 'case_deploy_temp_runner' : 'case_deploy_parent']) {
      if (selectedAction && action !== selectedAction) continue;
      attempts++;
      const before = snapshot();
      const effects = ['docker', 'sudo', 'ensure_checkout', 'ensure_runner_boot_files',
        'c1008_recycle', 'retire_c590_leftovers', 'build_server2_images', 'c849_prepare',
        'c849_require_ready', 'c849_budget_gate', 'ensure_build_slots_broker',
        'c1008_record_recreated', 'c1008_rollout_lock', 'c849_no_temp_containers',
        'c1609_temp_retirement_clearable', 'seed_runner_checkout']
        .map(name => `${name}() { printf '%s\\n' '${name}' >> "$FIXTURE_ROOT/side-effects"; exit 91; }`).join('\n');
      const result = cp.spawnSync('bash', [], {input: prelude +
        '\nrequire_lane() { [ "$1" = host ]; }\n' + effects + '\n' + action + ' config --format json\n',
        env, cwd: repo, encoding: 'utf8', timeout: 30000});
      try {
        assert.equal(result.error, undefined);
        assert.equal(result.status, 2, 'exact refusal exit code');
        assert.equal(result.stdout, 'RunnerLinuxCompilerPolicySettingInvalid\n', 'exact refusal reason');
        assert.equal(result.stderr, '', 'no settings or diagnostics leaked');
        assert.deepEqual(snapshot(), before, 'refusal changed fixture files or started a side effect');
        refusals++;
        console.log(`PASS refusal ${action} ${shape}`);
      } catch (error) {
        failures.push(`${action} ${shape}: ${error.message}`);
        console.error(`FAIL refusal ${action} ${shape}: ${error.message}`);
        // Each diagnostic case owns its fixture state; remove only its known marker.
        if (fs.existsSync(root + '/side-effects')) fs.unlinkSync(root + '/side-effects');
      }
    }
  }
}
assert.equal(failures.length, 0, failures.join('\n'));
assert.ok(attempts > 0 || process.argv.includes('--models-only'));
assert.equal(refusals, attempts);
console.log(selectedShape || selectedAction
  ? `PASS refusals=${refusals} selected-case=${selectedShape ?? '*'} selected-action=${selectedAction ?? '*'}`
  : `PASS refusals=${refusals} original-refusals=12 malformed-refusals=${malformed.length * 4}`);
if (process.argv.includes('--refusals-only') || selectedShape || selectedAction) return;
let matrices = 0;
for (const temp of [false, true]) {
  const file = temp ? env.SERVER2_TEMP_ENV : env.SERVER2_ENV;
  const other = temp ? env.SERVER2_ENV : env.SERVER2_TEMP_ENV;
  const wrapper = temp ? 'compose_temp' : 'compose_host';
  const phase = temp ? 'case_deploy_temp_runner' : 'case_deploy_parent';
  for (const value of [undefined, '', '0', '1']) {
    // Empty ambient interpolation is still false; an empty SAVED assignment is refused above.
    save(file, value === '' ? undefined : value);
    if (value === '') env[key] = ''; else delete env[key];
    fs.appendFileSync(file, '# ' + key + '=1\n  # ' + key + '=0\nUNRELATED=1\n');
    save(other, value === '1' ? '0' : '1');
    const otherBytes = fs.readFileSync(other);
    const expected = value === '1' ? '1' : '0';
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
// Direct Compose absent/empty interpolation must keep its named default OFF.
for (const mode of ['absent', 'empty']) {
  const direct = JSON.parse(bash('docker() { env ' + (mode === 'absent' ? '-u ' + key : key + '=') +
    ' COMPOSE_DISABLE_ENV_FILE=1 docker "$@"; }\ncompose_host config --format json\n'));
  assert.equal(direct.services['session-runner'].environment[key], '0', 'direct Compose ' + mode + ' default');
}
// Missing file also defaults zero; no env file is sourced.
assert.equal(bash('runner_linux_compiler_policy "$FIXTURE_ROOT/nonexistent.env"\n').trim(), '0');
save(env.SERVER2_ENV, undefined);
save(env.SERVER2_TEMP_ENV, undefined);
const current = JSON.parse(bash('compose_host config --format json\n'));
const oldPath = root + '/docker-compose.server2-runner.yml';
fs.writeFileSync(oldPath, cp.execFileSync('git', ['show',
  'd100f8fa2c679a5c74b6b235087308776a8f5d50:docker-compose.server2-runner.yml'], {cwd: repo}));
const base = JSON.parse(bash('C1008_COMPOSE_DIR="$FIXTURE_ROOT" compose_host config --format json\n'));
assert.equal(current.services['session-runner'].environment[key], '0');
assert.equal(Object.hasOwn(base.services['session-runner'].environment, key), false);
delete current.services['session-runner'].environment[key];
assert.deepEqual(current, base, 'default render changed outside the new zero flag');
assert.equal(matrices, 16);
console.log(`PASS matrices=16 refusals=${refusals} missing-file=1 base-equivalence=1`);

});
