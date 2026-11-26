// Read-only projection of the retained CARD-2082 desktop export. Node standard library only.
// Run with a path to the original JSON, or --subjects to inspect the unpriced selection.
'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');
const assert = require('node:assert/strict');

const prefix = path.join(__dirname, '2026-11-26-card-2082-');
const expectedDigest = '509f4227eb2cb474bcbe6095ea4efc1bed5a852d0c4632d3dbbeedd1a6f16a12';
const runs = new Map([
  ['b8cbfb24', '2fd61b643ea4231fa732cd4969b5284d43b33f39'],
  ['1fe763a9', '0cfc4ac1ffb41f5af159a0c1db423fe119950267'],
]);

function subjects() {
  const document = fs.readFileSync(prefix + 'invariant-inventory.md', 'utf8');
  const roster = JSON.parse(fs.readFileSync(prefix + 'candidate-roster.json', 'utf8'));
  const linked = new Map();
  for (const line of document.split('\n').filter(line => line.includes('` compiles in `'))) {
    const [type, ...projects] = Array.from(line.matchAll(/`([^`]+)`/g), match => match[1]);
    linked.set(type, projects);
  }
  const selected = new Map();
  function add(project, type, disposition, cohort, linux = null) {
    const key = project + '/' + type;
    assert(!selected.has(key), 'Duplicate project/class ' + key);
    selected.set(key, { project, type, disposition, cohort, linux });
  }
  let disposition, assembly, namespace;
  for (const line of document.split('\n')) {
    if (line === '## Proposed guard fixtures') disposition = 'MOVE';
    if (line === '## Fixtures with a current-tree assertion to extract or isolate') disposition = 'SPLIT';
    if (line === '## Product and fixture controls that stay with their owners') break;
    const heading = line.match(/^### (\S+) \/ (\S+)$/);
    if (heading) [, assembly, namespace] = heading;
    if (!disposition || !line.startsWith('| [')) continue;
    const cells = line.slice(1, -1).split(/(?<!\\)\|/).map(s => s.trim());
    const type = namespace + '.' + cells[0].match(/^\[([^\]]+)\]/)[1];
    const hosts = assembly === 'Shared' ? linked.get(type) : [assembly];
    assert(hosts?.length, 'Missing compilation owner for ' + type);
    for (const project of hosts) {
      const linux = cells[2].split('<br>').filter(s => hosts.length === 1 || s.startsWith(project.replace('Antiphon.', '') + ' '));
      add(project, type, disposition, 'original', linux.filter(s => s !== '—').join(' / ') || null);
    }
  }
  assert.equal(selected.size, 360, '352 inventory declarations plus eight additional linked compilations');
  for (const fixture of roster.fixtures.filter(f => f.disposition !== 'STAY')) {
    add(fixture.assembly, fixture.type, fixture.disposition, fixture.cohort);
  }
  return [...selected.values()].sort((a, b) => a.project.localeCompare(b.project) || a.type.localeCompare(b.type));
}

// Same-pin, same-case Linux replay evidence from the Plan's group 113, 141 and 86 investigations.
// A differently selected, passing Linux checkpoint cannot settle a Windows failure.
const reproduced = new Map([
  ['b8cbfb24/Antiphon.Tests/Antiphon.Tests.Checkpoints.PlanCoverageBindingTests', 2],
  ['1fe763a9/Antiphon.Tests/Antiphon.Tests.Scripts.LiveLaneInventoryTests', 2],
  ['1fe763a9/Antiphon.Tests/Antiphon.Tests.Application.CardCleanupSchedulingSchemaTests', 1],
]);

function compare(run, key, counts) {
  const failures = counts.Failed ?? 0;
  const observed = Object.entries(counts).filter(([label]) => label !== 'NotExecuted').reduce((sum, [, count]) => sum + count, 0);
  if (!observed) return 'no Windows execution observed';
  if (!failures) return 'no Failed row observed';
  const matches = reproduced.get(run + '/' + key) ?? 0;
  assert(matches <= failures, 'Retained class counts disagree with the same-case failure replay for ' + key);
  if (matches === failures) return 'no: all observed Failed rows reproduced on Linux at this pin';
  if (matches) return matches + ' reproduced on Linux; remaining Failed rows unknown';
  if (run === 'b8cbfb24' && key.endsWith('.NightlyNativeOwnershipTests')) return 'native Windows descendant-exit failure; corresponding Linux case excluded; other failures unresolved';
  return 'unknown: no matching Linux case verdict for each Windows failure';
}

function projectRun(run, id, selection) {
  assert.equal(run.sha, runs.get(id), 'Source pin differs for ' + id);
  const byClass = new Map();
  for (const entry of run.classes) {
    const key = entry.assembly + '/' + entry.class;
    assert(!byClass.has(key), 'Duplicate class in ' + id + ': ' + key);
    byClass.set(key, entry);
  }
  const coverage = { runId: run.runId, sha: run.sha, partialAtExport: id === '1fe763a9',
    summaryPresent: run.summaryPresent, outcome: run.outcome, coverageComplete: run.coverageComplete,
    scheduleComplete: run.scheduleComplete, knownNativeGroups: run.knownNativeGroups,
    trxFiles: run.trxFiles, incompleteGroups: run.incompleteGroups };
  const totals = { observedClasses: 0, unobservedClasses: 0, wholeParentSeconds: 0, mixedParentSeconds: 0,
    untimedCases: 0, longestTimedWholeCaseSeconds: null };
  const rows = selection.map(subject => {
    const key = subject.project + '/' + subject.type;
    const entry = byClass.get(key);
    if (!entry) {
      totals.unobservedClasses++;
      return { ...subject, windows: null, windowsOnlyFailure: 'unknown: no class row in this export' };
    }
    for (const count of Object.values(entry.outcomes)) assert(Number.isInteger(count) && count >= 0);
    assert(Number.isFinite(entry.totalSeconds) && entry.totalSeconds >= 0);
    assert(entry.maxSeconds === null || Number.isFinite(entry.maxSeconds) && entry.maxSeconds >= 0 && entry.maxSeconds <= entry.totalSeconds);
    assert(Number.isInteger(entry.untimed) && entry.untimed >= 0);
    totals.observedClasses++;
    totals.untimedCases += entry.untimed;
    if (subject.disposition === 'MOVE') {
      totals.wholeParentSeconds += entry.totalSeconds;
      if (entry.maxSeconds !== null) totals.longestTimedWholeCaseSeconds = Math.max(totals.longestTimedWholeCaseSeconds ?? 0, entry.maxSeconds);
    } else totals.mixedParentSeconds += entry.totalSeconds;
    return { ...subject, windows: { outcomes: entry.outcomes, totalSeconds: entry.totalSeconds,
      maxSeconds: entry.maxSeconds, untimed: entry.untimed }, windowsOnlyFailure: compare(id, key, entry.outcomes) };
  });
  return { coverage, totals, rows };
}

function main() {
  const selection = subjects();
  if (process.argv.length !== 3) throw new Error('Pass the retained .json export path or --subjects');
  if (process.argv[2] === '--subjects') return { selection };
  const bytes = fs.readFileSync(process.argv[2]);
  assert.equal(bytes.length, 495901, 'Desktop export length differs');
  assert.equal(createHash('sha256').update(bytes).digest('hex'), expectedDigest, 'Desktop export SHA-256 differs');
  const input = JSON.parse(bytes.toString('utf8').replace(/^\uFEFF/, ''));
  assert.equal(input.schemaVersion, 1);
  assert.equal(input.runs.length, runs.size);
  const projected = Array.from(runs.keys(), id => {
    const matches = input.runs.filter(run => run.runId.toLowerCase().startsWith(id));
    assert.equal(matches.length, 1, 'Missing or duplicate run ' + id);
    return projectRun(matches[0], id, selection);
  });
  return { sourceSha256: expectedDigest, classCount: selection.length, runs: projected };
}

if (require.main === module) {
  try { process.stdout.write(JSON.stringify(main(), null, 2) + '\n'); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
