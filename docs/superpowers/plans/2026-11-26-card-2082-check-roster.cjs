// Compare two JSONL outputs of the companion Roslyn detector to the pinned source roster.
// Run from the repository root. Reads Git objects at the roster SHA without changing the checkout.
'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const prefix = path.join(__dirname, '2026-11-26-card-2082-');

function readCensus(file) {
  const rows = fs.readFileSync(file, 'utf8').trim().split(/\r?\n/).map(JSON.parse);
  const byKey = new Map(rows.map(row => [row.key, row]));
  assert.equal(byKey.size, rows.length, 'Duplicate declaration in ' + file);
  return byKey;
}

function originalCandidates() {
  const text = fs.readFileSync(prefix + 'invariant-inventory.md', 'utf8');
  const section = text.split('## Additional candidate sites to check at the migration pin')[1].split('## Linked declarations')[0];
  let owner, namespace;
  const keys = [];
  for (const line of section.split('\n')) {
    const heading = line.match(/^### (\S+) \/ (\S+)$/);
    if (heading) [, owner, namespace] = heading;
    const type = line.match(/^\| \[([^\]]+)\]/);
    if (type) keys.push(owner + '/' + namespace + '.' + type[1]);
  }
  assert.equal(keys.length, 513);
  return keys;
}

function sourcesAt(pin, names) {
  const files = [...new Set(names)];
  const batch = execFileSync('git', ['cat-file', '--batch'], {
    input: files.map(file => pin + ':' + file).join('\n') + '\n', maxBuffer: 128 * 1024 * 1024,
  });
  let cursor = 0;
  const result = new Map();
  for (const file of files) {
    const end = batch.indexOf(10, cursor);
    const header = batch.toString('ascii', cursor, end).match(/^[a-f0-9]+ blob (\d+)$/);
    assert(header, 'Missing Git blob ' + pin + ':' + file);
    cursor = end + 1;
    const size = Number(header[1]);
    result.set(file, batch.toString('utf8', cursor, cursor + size).split(/\r?\n/));
    cursor += size + 1;
  }
  return result;
}

function main(baseFile, integratedFile) {
  assert(baseFile && integratedFile, 'Pass the inventory-pin and integrated-pin census JSONL files');
  const roster = JSON.parse(fs.readFileSync(prefix + 'candidate-roster.json'));
  const original = readCensus(baseFile), integrated = readCensus(integratedFile);
  const keys = new Set(originalCandidates());
  for (const r of integrated.values()) if (r.signals.length && !original.get(r.key)?.signals.length) keys.add(r.key);
  assert.deepEqual([...keys].sort(), roster.fixtures.map(f => f.assembly + '/' + f.type).sort());
  const history = roster.historicalDiscovery;
  const files = roster.sourceMembers.map(row => row[0]).concat(history.file);
  for (const f of roster.fixtures) {
    files.push(...f.files);
    for (const sites of Object.values(f.checkoutOracles ?? {})) {
      for (const token of sites.split(',')) if (token.includes('#')) files.push(token.split('#')[0]);
    }
  }
  const source = sourcesAt(roster.censusPin, files);
  const catalog = JSON.parse(source.get(history.file).join('\n'));
  assert.equal(catalog.producing.sourceSha, history.sha);
  const checkedNodes = new Set(), checkedMethods = new Set();
  let splits = 0;
  for (const f of roster.fixtures) {
    const key = f.assembly + '/' + f.type;
    assert.deepEqual(f.methods.map(m => m[0]).sort(), integrated.get(key).methods.slice().sort(), key);
    const expected = new Set(f.methods.filter(m => m[4] === 'SPLIT').map(m => m[0]));
    assert.deepEqual(new Set(Object.keys(f.checkoutOracles ?? {})), expected, key + ' checkout assertions');
    const actions = new Set(f.methods.map(m => m[4]));
    assert.equal(f.disposition, actions.size === 1 ? [...actions][0] : 'SPLIT', key);
    for (const m of f.methods) {
      const [name, idx, first, last, choice, input, , oracle, reason, , nodeRanges, caseLines, ...sets] = m;
      const id = key + '/' + name, file = f.files[idx], lines = source.get(file);
      assert.equal(m.length, roster.columns.length, id);
      assert(!checkedMethods.has(id) && first > 0 && last >= first && last <= lines.length, id);
      checkedMethods.add(id);
      assert(lines.slice(first - 1, last).join('\n').includes(name + '('), id + ' source location');
      assert(['STAY', 'MOVE', 'SPLIT'].includes(choice) && input && oracle && reason, id);
      for (const line of caseLines) assert(line >= first && line <= last, id + ' case attribute');
      for (const index of sets) {
        if (index === -1) continue;
        assert(Array.isArray(f.sourceSets[index]), id + ' missing source set');
        for (const item of f.sourceSets[index].flat()) assert(roster.sourceMembers[item], id + ' source member');
      }
      if (choice === 'SPLIT') {
        splits++;
        for (const token of f.checkoutOracles[name].split(',')) {
          const [, target, position] = token.match(/^(?:(.+)#)?L(\d+)$/) ?? [];
          assert(source.get(target ?? file)?.[Number(position) - 1]?.trim(), id + ' missing assertion ' + token);
        }
      }
      for (const range of nodeRanges.split(',').filter(Boolean)) {
        const [low, high = low] = range.split('-').map(Number);
        assert(Number.isInteger(low) && Number.isInteger(high) && low >= 0 && low <= high, id);
        for (let n = low; n <= high; n++) {
          const node = catalog.nodes[n];
          assert(node && !checkedNodes.has(n), id + ' historical index ' + n);
          assert.equal(node.project + '/' + node.namespace + '.' + node.type + '/' + node.method, id);
          checkedNodes.add(n);
        }
      }
    }
  }
  console.log(roster.fixtures.length + ' fixtures, ' + checkedMethods.size + ' source methods, ' + splits +
    ' split assertion maps, ' + checkedNodes.size + ' historical case identities');
}

try { main(...process.argv.slice(2)); }
catch (error) { console.error(error.message); process.exitCode = 1; }
