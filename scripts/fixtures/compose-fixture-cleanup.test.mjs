import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import cp from 'node:child_process';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const checkoutRoots = () => fs.readdirSync(path.join(repo, '.antiphon'))
  .filter(name => /^(c1906|c346)-compose-/.test(name)).sort();
const before = checkoutRoots();
for (const name of ['c1906-compiler-policy-compose.mjs', 'c346-workspace-slot-compose.mjs']) {
  const child = cp.spawnSync(process.execPath, [path.join(repo, 'scripts/fixtures', name),
    '--refusal-case=empty', '--refusal-action=compose_host'], { cwd: repo, encoding: 'utf8', timeout: 30000 });
  assert.equal(child.error, undefined);
  assert.equal(child.status, 0, child.stdout + child.stderr);
  const root = child.stdout.match(/^evidence=(.+)$/m)?.[1];
  assert.ok(root, 'the fixture must report its exact owned root');
  assert.equal(fs.existsSync(root), false, 'fixture disposal must remove its exact root after success');
  assert.deepEqual(checkoutRoots(), before, 'fixture disposal left checkout residue');
}
console.log('PASS compose fixture cleanup');
