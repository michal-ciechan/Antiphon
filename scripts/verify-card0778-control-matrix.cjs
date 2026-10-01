#!/usr/bin/env node
// CARD-0778: run one named positive control at a time under scripts/build-slot.ps1.
const fs = require('fs');
const os = require('os');
const path = require('path');
const cp = require('child_process');
const root = process.cwd();
const pty = 'src/Antiphon.Agents.Pty/GrokStartupReadiness.cs';
const adapter = 'server/Infrastructure/Agents/SessionRunner/RunnerGrokAdapter.cs';
const readyClass = 'RunnerGrokAdapterReadyTests';
const classifierClass = 'GrokStartupReadinessTests';
const methods = {
  spinner: 'Spinner_sequence_advance_does_not_prevent_positive_ready',
  settle: 'Settle_requires_two_observations_and_elapsed_time',
  reset: 'Composer_change_or_blocker_restarts_settle',
  trust: 'Post_trust_blank_or_sign_in_is_not_ready',
  deadline: 'One_deadline_covers_reads_trust_and_minimum_age',
  floor: 'Floor_modal_invalidates_stale_positive_at_minimum_age',
  utc: 'Utc_jump_does_not_advance_monotonic_settle',
  exit: 'Exit_and_cancellation_stop_without_input',
  held: 'Cancellation_during_held_read_escapes_without_more_io',
  poll: 'Cancellation_during_pending_poll_escapes_without_more_io',
  capture: 'Timeout_captures_last_frame_and_io_failure_preserves_failure'
};
const r = (file, method, assertion, change) => ({file, method, assertion, change});
const controls = {
  'PC-5': r(pty, `${classifierClass}/${methods.settle}`, 'firstZeroSettleObservation', s =>
    once(s, `            PositiveObservations = 1;\n            return false;`,
      `            PositiveObservations = 1;\n            return settle <= TimeSpan.Zero;`)),
  'PC-6': r(adapter, `${readyClass}/${methods.spinner}`, 'ready must survive', s => {
    const start = s.indexOf('    public async Task<bool> WaitForReadyAsync(CancellationToken ct)');
    const end = s.indexOf('    /// <summary>', start);
    if (start < 0 || end < 0) throw Error('PC-6 method anchors missing');
    return s.slice(0,start) + `    public async Task<bool> WaitForReadyAsync(CancellationToken ct)\n    {\n        EnsureStarted();\n        return await _terminal.WaitForQuietAfterVisibleAsync(\n            TimeSpan.FromMilliseconds(_settings.GrokReadyQuietPeriodMs),\n            TimeSpan.FromMilliseconds(_settings.GrokReadyMaxWaitMs), ct);\n    }\n\n` + s.slice(end);
  }),
  'PC-10': r(pty, `${readyClass}/${methods.deadline}`, 'lateReadReady', s =>
    once(once(s, `            if (elapsed >= options.MaxWait) return Fail(GrokStartupReason.Deadline);`,
      `            // Mutation: completed reads bypass the post-read deadline.`),
      `                    if (time.GetElapsedTime(started) >= options.MaxWait) return Fail(GrokStartupReason.Deadline);`,
      `                    // Mutation: accept a positive decision after the deadline.`)),
  'PC-11': r(pty, `${readyClass}/${methods.exit}`, 'exitedReady', s =>
    once(once(s, `            ct.ThrowIfCancellationRequested();\n            if (isExited?.Invoke() == true) return Fail(GrokStartupReason.Exited);\n            elapsed =`,
      `            ct.ThrowIfCancellationRequested();\n            // Mutation: exit after a read is ignored.\n            elapsed =`),
      `                    if (isExited?.Invoke() == true) return Fail(GrokStartupReason.Exited);`,
      `                    // Mutation: exit before success is ignored.`)),
  'PC-18': r(pty, `${classifierClass}/${methods.reset}`, 'readyAfterNegativeBeforeThreshold', s =>
    once(s, `        if (!observation.IsReady)\n        {\n            Reset();`,
      `        if (!observation.IsReady)\n        {\n            // Mutation: preserve the previous positive candidate.`)),
  'PC-20': r(adapter, `${readyClass}/${methods.spinner}`, 'snapshotReads must equal completedDecisions', s =>
    once(s, `                var snap = await _terminal.GetSnapshotAsync(token);\n                return new GrokStartupSnapshot`,
      `                var snap = await _terminal.GetSnapshotAsync(token);\n                _ = await _terminal.GetSnapshotAsync(token);\n                return new GrokStartupSnapshot`)),
  'PC-24': r(pty, `${readyClass}/${methods.trust}`, 'trustCompletionElapsed', s =>
    once(s, `                if (trustRemaining < remaining) remaining = trustRemaining;`,
      `                // Mutation: trust sub-budget does not bound the held read.`)),
  'PC-25': r(pty, `${readyClass}/${methods.deadline}`, 'readyBeforeMinimumAge', s =>
    once(s, `                if (tracker.Observe(observation, elapsed)\n                    && elapsed >= options.MinimumAgeRemaining)`,
      `                if (tracker.Observe(observation, elapsed))`)),
  'PC-26': r(pty, `${readyClass}/${methods.floor}`, 'readyWithFloorModal', s =>
    once(s, `                if (tracker.Observe(observation, elapsed)\n                    && elapsed >= options.MinimumAgeRemaining)\n                {`,
      `                if (tracker.Observe(observation, elapsed))\n                {\n                    if (elapsed < options.MinimumAgeRemaining)\n                    {\n                        await Task.Delay(options.MinimumAgeRemaining - elapsed, time, ct);\n                        return true;\n                    }`)),
  'PC-28': r(pty, `${readyClass}/${methods.deadline}`, 'completionElapsed', s =>
    once(s, `                frame = await Bounded(snapshotAsync, remaining, time, ct);`,
      `                frame = await snapshotAsync(ct);`)),
  'PC-29': r(pty, `${readyClass}/${methods.deadline}`, 'trustCompletionElapsed inside originalMax', s =>
    once(s, `                    trustAt = time.GetElapsedTime(started);`,
      `                    trustAt = time.GetElapsedTime(started);\n                    started = time.GetTimestamp();`)),
  'PC-30a': r(pty, `${readyClass}/${methods.held}`, 'OperationCanceledException', s =>
    once(s, `            catch (OperationCanceledException) when (!ct.IsCancellationRequested)\n            {\n                return Fail(trustAt`,
      `            catch (OperationCanceledException)\n            {\n                return Fail(trustAt`)),
  'PC-30b': r(pty, `${readyClass}/${methods.poll}`, 'OperationCanceledException', s =>
    once(s, `            await Task.Delay(delay < remaining ? delay : remaining, time, ct);`,
      `            try { await Task.Delay(delay < remaining ? delay : remaining, time, ct); }\n            catch (OperationCanceledException) { return Fail(GrokStartupReason.Deadline); }`)),
  'PC-31': r(pty, `${readyClass}/${methods.capture}`, 'snapshotFailureOutcome', s =>
    once(s, `            catch (Exception) when (!ct.IsCancellationRequested)\n            {\n                return Fail(GrokStartupReason.SnapshotFailure);\n            }`,
      `            catch (Exception) when (!ct.IsCancellationRequested)\n            {\n                if (last is not null && tracker.LastReason == GrokStartupReason.Ready) return true;\n                return Fail(GrokStartupReason.SnapshotFailure);\n            }`)),
  'PC-41': r(adapter, `${readyClass}/${methods.capture}`, 'logSecretSentinel', s =>
    once(once(s, `mcpSeen={McpSeen} capture={CapturePath}",`,
      `mcpSeen={McpSeen} capture={CapturePath} screen={Screen}",`),
      `frame?.Sequence, frame?.CapturedAt, positives, mcpSeen, path);`,
      `frame?.Sequence, frame?.CapturedAt, positives, mcpSeen, path, frame?.RenderedScreen);`)),
  'PC-42': r(pty, `${readyClass}/${methods.utc}`, 'readyAfterUtcJumpWithoutElapsed', s => {
    s = once(s, `        var started = time.GetTimestamp();`, `        var started = time.GetUtcNow();`);
    return s.replaceAll('time.GetElapsedTime(started)', '(time.GetUtcNow() - started)');
  }),
  'PC-44': r(adapter, `${readyClass}/${methods.capture}`, 'readsAfterFailureDecision', s =>
    once(s, `        try\n        {\n            var path = GrokStartupCaptureStore.Write(`,
      `        try\n        {\n            _ = _terminal.GetSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();\n            var path = GrokStartupCaptureStore.Write(`))
};
function once(s, from, to) {
  if (s.split(from).length !== 2) throw Error(`Mutation anchor missing or ambiguous: ${from.slice(0,80)}`);
  return s.replace(from,to);
}
const selected = process.argv[2];
if (!controls[selected]) throw Error('Choose one of: ' + Object.keys(controls).join(', '));
const control = controls[selected];
const source = path.join(root, control.file);
const original = fs.readFileSync(source);
const scratch = fs.mkdtempSync(path.join(os.tmpdir(), `c778-${selected.toLowerCase()}-`));
console.log(`SCRATCH ${scratch}`);
fs.writeFileSync(path.join(scratch,'original.cs'),original);
function command(args, name) {
  const output = cp.spawnSync(args[0], args.slice(1), {cwd:root, encoding:'utf8', maxBuffer:64*1024*1024});
  if (output.error) throw output.error;
  const text = (output.stdout || '') + (output.stderr || '');
  if (name) fs.writeFileSync(path.join(scratch,name),text);
  return {code:output.status,text};
}
if (command(['git','diff','--exit-code']).code !== 0) throw Error('Tracked source dirty before mutation');
const mutated = control.change(original.toString('utf8'));
if (mutated === original.toString('utf8')) throw Error('Mutation did not change source');
const rows = [];
function runs(label, loaded, count) {
  const burners=[];
  try {
    if (loaded) for(let i=0;i<24;i++) burners.push(cp.spawn('yes',[],{stdio:'ignore'}));
    for(let i=1;i<=count;i++) {
      const filter = `/*/*/${control.method.split('/')[0]}*/${control.method.split('/')[1]}`;
      const result = command(['dotnet','exec','tests/Antiphon.Tests/bin-c778-control/Antiphon.Tests.dll',
        '--treenode-filter',filter],`${label}-${i}.log`);
      const named = result.text.includes(control.assertion);
      const status = result.code===2 && result.text.includes('failed: 1') && named ? 'red' :
        result.code===0 ? 'green' : 'invalid';
      rows.push([selected,label,i,status,result.code,control.assertion,named,`${scratch}/${label}-${i}.log`]);
      console.log(`${selected} ${label} ${i}/${count} ${status} exit=${result.code} named=${named}`);
    }
  } finally {for(const burner of burners) burner.kill();}
}
try {
  fs.writeFileSync(source,mutated);
  const build=command(['dotnet','build','tests/Antiphon.Tests','--no-restore',
    '--property:OutputPath=bin-c778-control/','--property:UseAppHost=false','--nologo','-v:q'], 'build.log');
  console.log(`BUILD ${selected} exit=${build.code} log=${scratch}/build.log`);
  if(build.code!==0) throw Error('Mutant did not compile');
  runs('idle',false,5);
  runs('load24',true,3);
} finally {
  fs.writeFileSync(source,original);
  const diff=command(['git','diff','--exit-code']);
  if(diff.code!==0) throw Error('Tracked source restoration failed');
  console.log('RESTORED git diff empty');
  fs.writeFileSync(path.join(scratch,'matrix.csv'),
    'pc,load,iteration,status,exit,assertion,named,log\n'+rows.map(x=>x.join(',')).join('\n')+'\n');
}
if(rows.some(x=>x[3]!=='red')) throw Error(`Matrix contains non-red cells: ${scratch}/matrix.csv`);
console.log(`MATRIX ${selected} 8/8 named red ${scratch}/matrix.csv`);
