import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

test('checkpoint launch refuses low disk before starting dotnet', () => {
    const fixture = mkdtempSync(join(tmpdir(), 'checkpoint-disk-guard-'));
    try {
        const scripts = join(fixture, 'scripts');
        mkdirSync(scripts);
        copyFileSync(new URL('./antiphon', import.meta.url), join(scripts, 'antiphon'));
        writeFileSync(join(fixture, 'df'), [
            '#!/bin/sh',
            'printf "Filesystem 1024-blocks Used Available Capacity Mounted on\\n"',
            'printf "test 42000000 10000000 %s 90%% /\\n" "$TEST_DISK_FREE"',
        ].join('\n'), { mode: 0o755 });
        writeFileSync(join(fixture, 'dotnet'), '#!/bin/sh\nprintf "dotnet started\\n" >&2\nexit 1\n', { mode: 0o755 });
        const invoke = (free, ...args) => spawnSync('sh', [join(scripts, 'antiphon'), ...args], {
            cwd: fixture,
            encoding: 'utf8',
            env: { ...process.env, PATH: fixture + ':' + process.env.PATH, TEST_DISK_FREE: free },
        });

        const low = invoke('20971519', 'checkpoints', 'row');
        assert.equal(low.status, 9);
        assert.match(low.stderr, /workspace_disk_low.*20 GiB/);
        assert.doesNotMatch(low.stderr, /launcher did not build/);

        const atFloor = invoke('20971520', 'checkpoints', 'start');
        assert.match(atFloor.stderr, /launcher did not build/);

        const recovery = invoke('0', 'checkpoints', 'clean');
        assert.match(recovery.stderr, /launcher did not build/);
    } finally {
        rmSync(fixture, { recursive: true });
    }
});
