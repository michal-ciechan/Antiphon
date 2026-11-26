// Compare JSONL outputs of the companion Roslyn detector to the pinned source roster.
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

function checkEarlierMixed(mixed, census) {
  assert(census, 'Pass the earlier mixed source census JSONL file as the third argument');
  assert.match(mixed.source, /^[a-f0-9]{40}$/);
  assert.deepEqual(mixed.columns, ['method', 'fileIndex', 'startLine', 'endLine', 'disposition', 'assertionLines', 'reasonIndex']);

  const inventory = fs.readFileSync(prefix + 'invariant-inventory.md', 'utf8');
  const section = inventory.split('## Fixtures with a current-tree assertion to extract or isolate')[1]
    .split('## Product and fixture controls')[0];
  const candidates = [...section.matchAll(/^\| \[([^\]]+)\]\(\.\.\/\.\.\/\.\.\/(tests\/[^)#]+)\)/gm)]
    .map(([, type, file]) => file + '#' + type);
  assert.equal(candidates.length, 271);
  assert.equal(mixed.alreadyMapped.length, 25);
  assert.equal(mixed.fixtures.length, 246);
  const keys = [...mixed.alreadyMapped];
  const source = sourcesAt(mixed.source, mixed.fixtures.flatMap(f => f.files));
  const counts = { MOVE: 0, SPLIT: 0, STAY: 0 };
  for (const fixture of mixed.fixtures) {
    const id = fixture.assembly + '/' + fixture.type;
    const declaration = census.get(id);
    assert(declaration, id + ' missing from the source census');
    assert.deepEqual(fixture.files.slice().sort(), declaration.files.slice().sort(), id + ' declaring files');
    assert(fixture.files.includes(fixture.inventoryFile), id + ' original inventory file');
    keys.push(fixture.inventoryFile + '#' + fixture.type.split('.').at(-1));
    assert.deepEqual(fixture.methods.map(row => row[0]).sort(), declaration.methods.slice().sort(), id + ' declared methods');

    for (const row of fixture.methods) {
      const [name, index, first, last, choice, oracleLines, reason] = row;
      const method = id + '/' + name;
      const lines = source.get(fixture.files[index]);
      assert.equal(row.length, mixed.columns.length, method);
      assert(lines && Number.isInteger(first) && Number.isInteger(last) && first > 0 && last >= first && last <= lines.length, method);
      assert(lines.slice(first - 1, last).join('\n').includes(name + '('), method + ' declaration source');
      assert(Object.hasOwn(counts, choice) && Number.isInteger(reason) && mixed.reasons[reason]?.trim(), method + ' disposition and reason');
      assert(oracleLines.length > 0 && new Set(oracleLines).size === oracleLines.length, method + ' unique oracle source');
      for (const line of oracleLines) {
        // A helper in the same partial file may own the assertion.
        assert(Number.isInteger(line) && line > 0 && lines[line - 1]?.trim(), method + ' oracle L' + line);
      }
      counts[choice]++;
    }
  }
  assert.deepEqual(keys.sort(), candidates.sort(), 'Earlier inventory must be covered exactly once');
  console.log(mixed.fixtures.length + ' earlier mixed fixtures at ' + mixed.source + ' (' +
    counts.MOVE + ' MOVE, ' + counts.SPLIT + ' SPLIT, ' + counts.STAY + ' STAY methods)');
}

function checkImplementation(roster, caseFile, masterFile) {
  assert(caseFile && masterFile, 'Pass candidate compiled cases JSON and the replay-base detector JSONL');
  const cases = JSON.parse(fs.readFileSync(caseFile, 'utf8'));
  const master = readCensus(masterFile);
  const keyed = new Map();
  const projectUids = new Set();
  const projects = new Set();
  for (const test of cases) {
    const qualified = test.asm + '/' + test.uid;
    assert(!projectUids.has(qualified), 'Duplicate discovered UID ' + qualified);
    projectUids.add(qualified);
    projects.add(test.asm);
    const type = test.type.endsWith(')') ? test.type.replace(/\(.+\)$/, '') : test.type;
    const name = test.asm + '/' + test.ns + '.' + type + '/' + test.method;
    if (!keyed.has(name)) keyed.set(name, []);
    keyed.get(name).push(test);
  }
  const obligations = new Map();
  for (const cohort of [roster, roster.earlierMixed]) {
    for (const fixture of cohort.fixtures) {
      for (const [method, , , , disposition] of fixture.methods) {
        if (disposition === 'STAY') continue;
        const key = fixture.assembly + '/' + fixture.type + '/' + method;
        assert(!obligations.has(key), 'Duplicate roster method ' + key);
        obligations.set(key, { project: fixture.assembly, disposition });
      }
    }
  }
  const statuses = roster.implementation;
  assert(statuses && typeof statuses === 'object', 'Missing application statuses');
  assert.deepEqual(Object.keys(statuses).sort(), [...obligations.keys()].sort(), 'Every MOVE and SPLIT needs a status');
  const isInvariant = test => test.ns.endsWith('.Invariants') && test.cats.includes('RepositoryInvariant');
  let applied = 0, deferred = 0, retired = 0;
  for (const [key, { project, disposition }] of obligations) {
    const entry = statuses[key];
    assert(projects.has(project), 'Missing compiled project ' + project);
    const original = keyed.get(key) ?? [];
    if (entry.status === 'applied') {
      const observed = keyed.get(project + '/' + entry.owner) ?? [];
      assert(observed.length > 0 && observed.every(isInvariant), key + ' has no classified compiled invariant owner');
      assert(Array.isArray(entry.uids) && entry.uids.length > 0, key + ' requires the expanded invariant UIDs');
      assert.deepEqual(observed.map(test => test.uid).sort(), entry.uids.slice().sort(), key + ' expanded arguments');
      if (disposition === 'MOVE') {
        assert(original.every(isInvariant), key + ' still compiles in an ordinary owner');
      } else {
        assert(original.length > 0 && original.every(test => !test.cats.includes('RepositoryInvariant')),
          key + ' lost its ordinary SPLIT cases');
      }
      applied++;
    } else if (entry.status === 'deferred') {
      assert(entry.phase?.endsWith('/' + project) && entry.reason?.trim(),
        key + ' needs a phase and reason');
      assert(Array.isArray(entry.pendingOwners) && entry.pendingOwners.length > 0, key + ' needs the deferred owner candidates');
      const observed = [...original, ...entry.pendingOwners.flatMap(owner => keyed.get(project + '/' + owner) ?? [])];
      assert(observed.length > 0, key + ' has no compiled ordinary owner for deferred disposition');
      assert(observed.every(test => !test.cats.includes('RepositoryInvariant') && !test.ns.endsWith('.Invariants')),
        key + ' is deferred but compiles as an invariant');
      deferred++;
    } else {
      assert(entry.status === 'retired' && entry.reason?.trim(), key + ' needs an explicit retirement reason');
      assert.equal(original.length, 0, key + ' is retired but still compiles');
      retired++;
    }
  }
  const liveProject = 'Antiphon.LiveClaude.Tests';
  const liveMaster = [...master.values()].filter(row => row.key.startsWith(liveProject + '/'));
  const liveRoster = roster.masterLiveClaude?.fixtures ?? [];
  assert(liveMaster.length > 0, 'The replay-base detector omitted the live Claude project');
  assert.deepEqual(liveRoster.map(row => row.key).sort(), liveMaster.map(row => row.key).sort(), 'Live Claude master inventory');
  for (const row of liveRoster) {
    assert(row.disposition === 'STAY' && row.reason?.trim(), row.key + ' requires dedicated-lane admission');
    assert.deepEqual(row.methods.slice().sort(), master.get(row.key).methods.slice().sort(), row.key + ' method inventory');
    for (const method of row.methods) {
      const actual = keyed.get(row.key + '/' + method) ?? [];
      assert(actual.length > 0 && actual.every(test => !test.cats.includes('RepositoryInvariant')),
        row.key + '/' + method + ' is absent or entered the invariant lane');
    }
  }
  console.log('compiled roster applied=' + applied + ' deferred=' + deferred + ' retired=' + retired + ' liveClaudeFixtures=' + liveRoster.length);
}

function main(baseFile, integratedFile, mixedFile, caseFile, masterFile) {
  assert(baseFile && integratedFile, 'Pass the inventory-pin and integrated-pin census JSONL files');
  const roster = JSON.parse(fs.readFileSync(prefix + 'candidate-roster.json'));
  checkImplementation(roster, caseFile, masterFile);
  const original = readCensus(baseFile), integrated = readCensus(integratedFile);
  if (roster.earlierMixed) checkEarlierMixed(roster.earlierMixed, mixedFile && readCensus(mixedFile));
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
