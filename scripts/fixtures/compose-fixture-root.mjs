import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import cp from 'node:child_process';
import { randomUUID } from 'node:crypto';

const markerName = '.compose-fixture-owner';
export const fixtureBase = () => path.resolve(process.platform === 'linux' ? '/var/tmp' : os.tmpdir());

function validate(root, token, prefix) {
  assert.equal(path.dirname(root), fixtureBase(), 'fixture must be directly beneath the platform temp base');
  assert.equal(path.basename(root), prefix + token, 'fixture must own this exact unique root');
  assert.equal(fs.lstatSync(root).isSymbolicLink(), false, 'fixture root must not be a link');
  const marker = path.join(root, markerName);
  assert.equal(fs.lstatSync(marker).isSymbolicLink(), false, 'ownership marker must not be a link');
  assert.equal(fs.readFileSync(marker, 'utf8'), token, 'fixture ownership must match');
}

// The supervisor owns the root while the synchronous Compose worker runs. Its
// finally also runs after a signal or worker failure, without relying on a worker
// that may be blocked in spawnSync to process cancellation. TUnit additionally
// owns its supplied root, so a forced child-tree kill still has an outer finally.
export async function withComposeFixture(prefix, body) {
  const supplied = process.env.ANTIPHON_COMPOSE_FIXTURE_ROOT;
  const token = supplied ? process.env.ANTIPHON_COMPOSE_FIXTURE_TOKEN : randomUUID();
  const root = supplied ?? path.join(fixtureBase(), prefix + token);
  let markerWritten = false;
  if (!supplied) fs.mkdirSync(root);
  try {
    if (!supplied) fs.writeFileSync(path.join(root, markerName), token, { flag: 'wx' });
    markerWritten = true;
    validate(root, token, prefix);
    if (process.env.ANTIPHON_COMPOSE_FIXTURE_WORKER === '1') {
      if (process.argv.includes('--cleanup-probe=setup-failure')) throw new Error('fixture setup failure probe');
      if (process.argv.includes('--cleanup-probe=wait')) {
        const probe = cp.spawn(process.execPath, ['-e',
          "process.title='c1953-cleanup-child'; console.log('fixture-child='+process.pid); console.log('fixture-ready'); setTimeout(()=>{},30000)"],
          { stdio: 'inherit' });
        await new Promise((resolve, reject) => { probe.once('error', reject); probe.once('close', resolve); });
      }
      return await body(root);
    }
    console.log('evidence=' + root);
    const child = cp.spawn(process.execPath, process.argv.slice(1), {
      stdio: 'inherit', env: { ...process.env, ANTIPHON_COMPOSE_FIXTURE_ROOT: root,
        ANTIPHON_COMPOSE_FIXTURE_TOKEN: token, ANTIPHON_COMPOSE_FIXTURE_WORKER: '1' }
    });
    let cancelled;
    const interrupt = () => { cancelled = 130; child.kill('SIGINT'); };
    const terminate = () => { cancelled = 143; child.kill('SIGTERM'); };
    process.once('SIGINT', interrupt);
    process.once('SIGTERM', terminate);
    try {
      const exit = await new Promise((resolve, reject) => {
        child.once('error', reject);
        child.once('close', (code, signal) => resolve(code ?? (signal ? 1 : 0)));
      });
      process.exitCode = cancelled ?? exit;
    } finally {
      process.removeListener('SIGINT', interrupt);
      process.removeListener('SIGTERM', terminate);
    }
  } finally {
    if (!supplied && fs.existsSync(root)) {
      if (markerWritten) {
        validate(root, token, prefix);
        fs.rmSync(root, { recursive: true });
      } else fs.rmdirSync(root); // A failed marker write only owns an empty new directory.
    }
  }
}
