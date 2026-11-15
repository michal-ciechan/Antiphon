import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { fixtureBase } from './compose-fixture-root.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const checkoutRoots = () => fs.existsSync(path.join(repo, '.antiphon'))
  ? fs.readdirSync(path.join(repo, '.antiphon')).filter(name => /^(c1906|c346)-compose-/.test(name)).sort() : [];
const before = checkoutRoots();
const env = { ...process.env };
for (const key of ['ANTIPHON_COMPOSE_FIXTURE_ROOT', 'ANTIPHON_COMPOSE_FIXTURE_TOKEN', 'ANTIPHON_COMPOSE_FIXTURE_WORKER']) delete env[key];
function disposed(output, outcome) {
  const root = output.match(/^evidence=(.+)$/m)?.[1];
  assert.ok(root, 'the fixture must report its exact owned root');
  assert.equal(fs.existsSync(root), false, 'fixture disposal must remove its exact root after ' + outcome);
  assert.equal(path.dirname(root), fixtureBase());
  assert.deepEqual(checkoutRoots(), before, 'fixture disposal left checkout residue');
}
let cases = 0;
for (const name of ['c1906-compiler-policy-compose.mjs', 'c346-workspace-slot-compose.mjs']) {
  const script = path.join(repo, 'scripts/fixtures', name);
  for (const [outcome, selection, expected] of [
    ['success', ['--refusal-case=empty', '--refusal-action=compose_host'], 0],
    ['assertion failure', ['--refusal-case=unknown'], 1],
    ['setup failure', ['--cleanup-probe=setup-failure'], 1]
  ]) {
    const child = cp.spawnSync(process.execPath, [script, ...selection], { cwd: repo, env, encoding: 'utf8', timeout: 30000 });
    assert.equal(child.error, undefined);
    assert.equal(child.status, expected, child.stdout + child.stderr);
    if (outcome === 'success') assert.match(child.stdout, /PASS refusals=1/);
    if (outcome === 'assertion failure') assert.match(child.stderr, /unknown refusal case/);
    if (outcome === 'setup failure') assert.match(child.stderr, /fixture setup failure probe/);
    disposed(child.stdout, outcome);
    cases++;
  }
  const child = cp.spawn(process.execPath, [script, '--cleanup-probe=wait'], { cwd: repo, env, stdio: ['ignore', 'pipe', 'pipe'] });
  let output = '';
  let error = '';
  child.stderr.on('data', bytes => { error += bytes; });
  const exited = new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('close', code => resolve(code));
  });
  const timeout = setTimeout(() => child.kill('SIGTERM'), 10000);
  let shutdownTimer;
  let probePid;
  let rejectShutdown;
  const shutdownExpired = new Promise((_, reject) => { rejectShutdown = reject; });
  let cancelled = false;
  child.stdout.on('data', bytes => {
    output += bytes;
    const match = output.match(/fixture-child=(\d+)/);
    if (match) probePid = Number(match[1]);
    if (!cancelled && output.includes('fixture-ready')) {
      cancelled = true;
      child.kill('SIGTERM');
      shutdownTimer = setTimeout(() => rejectShutdown(new Error('worker descendants survived cancellation')), 2000);
    }
  });
  try {
    assert.equal(await Promise.race([exited, shutdownExpired]), 143, error);
    assert.match(output, /fixture-ready/, 'cancel only after worker setup');
    disposed(output, 'cancellation');
    cases++;
  } finally {
    clearTimeout(timeout);
    clearTimeout(shutdownTimer);
    // Rescue only the exact child reported by this local probe, including on the
    // deliberate red: never leave a failed cleanup assertion's process running.
    if (probePid) {
      try { process.kill(probePid, 'SIGTERM'); }
      catch (failure) { if (failure.code !== 'ESRCH') throw failure; }
    }
    if (child.exitCode === null && child.signalCode === null) child.kill('SIGTERM');
    await exited;
  }
}
assert.deepEqual(checkoutRoots(), []);
console.log(`PASS compose fixture cleanup cases=${cases} checkout-roots=0`);
