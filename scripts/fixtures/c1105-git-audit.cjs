// Local-only executable contract for the actual recycle helper and resume boundary.
const fs = require('fs'), path = require('path'), cp = require('child_process'), assert = require('assert/strict');
const [mode, sourcePath] = process.argv.slice(2);
const source = fs.readFileSync(sourcePath, 'utf8');
const root = fs.mkdtempSync('/tmp/c1105-audit-');
const work = path.join(root, 'work'), repo = path.join(work, 'repo'), origin = path.join(root, 'origin.git');
const env = {...process.env, GIT_CONFIG_SYSTEM: '/dev/null', GIT_CONFIG_GLOBAL: '/dev/null', GIT_ALLOW_PROTOCOL: 'file', GIT_TERMINAL_PROMPT: '0'};
for (const key of Object.keys(env)) if (key.startsWith('GIT_') && !['GIT_CONFIG_SYSTEM', 'GIT_CONFIG_GLOBAL', 'GIT_ALLOW_PROTOCOL', 'GIT_TERMINAL_PROMPT'].includes(key)) delete env[key];
function run(exe, args, options = {}) {
    const result = cp.spawnSync(exe, args, {env, encoding: 'utf8', timeout: 45000, maxBuffer: 8e6, ...options});
    assert.equal(result.status, 0, `${exe}: ${result.stdout}\n${result.stderr}`);
    return result.stdout.trim();
}
const git = (...args) => run('git', ['-C', repo, ...args]);
function block(name) {
    const start = source.indexOf('\n' + name + '() {') + 1;
    assert.ok(start > 0, name);
    return source.slice(start, source.indexOf('\n}\n', start) + 3);
}
function remove(target) {
    assert.ok(fs.realpathSync(target).startsWith(fs.realpathSync(root) + '/'));
    fs.rmSync(target, {recursive: true});
}
function setup() {
    fs.mkdirSync(work);
    run('git', ['init', '-q', '--bare', '--initial-branch=master', origin]);
    run('git', ['-C', origin, 'config', 'uploadpack.allowFilter', 'true']);
    run('git', ['-C', origin, 'config', 'uploadpack.allowAnySHA1InWant', 'true']);
    const src = path.join(root, 'src');
    run('git', ['init', '-q', '-b', 'master', src]);
    const s = (...args) => run('git', ['-C', src, ...args]);
    s('config', 'user.name', 'Fixture'); s('config', 'user.email', 'fixture@example.invalid');
    fs.writeFileSync(path.join(src, '.gitignore'), 'ignored\n');
    fs.writeFileSync(path.join(src, 'file'), 'A\n'); s('add', '.'); s('commit', '-qm', 'A');
    fs.writeFileSync(path.join(src, 'file'), 'B\n'); s('commit', '-qam', 'B'); s('push', '-q', origin, 'master');
    const lines = source.split('\n').filter(x => x.includes('git clone --filter=blob:none --no-checkout'));
    assert.equal(lines.length, 1);
    run('bash', ['-c', lines[0], 'antiphon-seed', 'ignored', 'file://' + origin], {env: {...env, repo}});
    git('config', 'user.name', 'Fixture'); git('config', 'user.email', 'fixture@example.invalid');
    const blob = git('rev-parse', 'HEAD:file');
    const missing = run('git', ['-C', repo, 'rev-list', '--objects', '--missing=print', 'HEAD'], {env: {...env, GIT_NO_LAZY_FETCH: '1'}});
    assert.ok(missing.split('\n').includes('?' + blob), 'seed must really lack HEAD blob');
    assert.equal(cp.spawnSync('git', ['-C', repo, 'cat-file', '-e', blob], {env: {...env, GIT_NO_LAZY_FETCH: '1'}}).status, 1, 'no blob may be fetched');
}
function helper() {
    const match = source.match(/cat <<'C1008_GIT'\n([\s\S]*?)\nC1008_GIT/);
    assert.ok(match); assert.equal(match[1].split('readlink -e /work').length, 2);
    fs.writeFileSync(path.join(root, 'helper.sh'), match[1].replace('readlink -e /work', 'readlink -e "$C1105_WORK"'));
}
function audit() {
    const trace = path.join(root, 'trace.jsonl');
    const result = cp.spawnSync('bash', [path.join(root, 'helper.sh')], {env: {...env, C1105_WORK: work, GIT_TRACE2_EVENT: trace,
        GIT_TRACE2_ENV_VARS: 'GIT_NO_LAZY_FETCH,GIT_CONFIG_SYSTEM,GIT_CONFIG_GLOBAL'}, encoding: 'utf8', timeout: 45000});
    const events = fs.readFileSync(trace, 'utf8').trim().split('\n').map(JSON.parse);
    assert.ok(!events.some(x => x.argv?.includes('fetch')), 'audit must never lazy-fetch');
    const starts = events.filter(x => x.event === 'start');
    for (const start of starts) for (const [key, value] of Object.entries({GIT_NO_LAZY_FETCH:'1', GIT_CONFIG_SYSTEM:'/dev/null', GIT_CONFIG_GLOBAL:'/dev/null'}))
        assert.ok(events.some(x => x.sid === start.sid && x.event === 'def_param' && x.param === key && x.value === value), `${key} missing at git start`);
    return result;
}
function checkout() { git('reset', '--hard', 'HEAD'); }
function eraseIndexAndFiles() { for (const name of fs.readdirSync(repo)) if (name !== '.git') remove(path.join(repo, name)); remove(path.join(repo, '.git/index')); }
function privateCommit() { git('commit', '--allow-empty', '-qm', 'private'); return git('rev-parse', 'HEAD'); }
function expireLogs() { git('reflog', 'expire', '--expire=all', '--all'); }
function objectLoss(spec) {
    const oid = git('rev-parse', spec), packs = path.join(repo, '.git/objects/pack');
    for (const name of fs.readdirSync(packs).filter(x => x.endsWith('.pack'))) {
        const bytes = fs.readFileSync(path.join(packs, name));
        fs.renameSync(path.join(packs, name), path.join(root, name));
        run('git', ['-C', repo, 'unpack-objects', '-r'], {input: bytes});
    }
    for (const name of fs.readdirSync(packs)) remove(path.join(packs, name));
    remove(path.join(repo, '.git/objects', oid.slice(0, 2), oid.slice(2)));
}
const U = 'RecycleUnpublishedWork', K = 'RecycleGitAuditUnknown', D = 'RecycleWorktreeDirty', P = '';
const gd = (...parts) => path.join(repo, '.git', ...parts);
function put(rel, text) { fs.mkdirSync(path.dirname(gd(rel)), {recursive: true}); fs.writeFileSync(gd(rel), text); }
function add(rel, text) { fs.mkdirSync(path.dirname(gd(rel)), {recursive: true}); fs.appendFileSync(gd(rel), text); }
// A commit no ref, reflog or index names until the row stores it in one location.
function priv() { return git('commit-tree', 'HEAD^{tree}', '-p', 'HEAD', '-m', 'private'); }
const head = () => git('rev-parse', 'HEAD');
const entry = (oid, old) => `${old ?? head()} ${oid} Fixture <fixture@example.invalid> 1700000000 +0000\tprivate\n`;
function linked() { checkout(); git('worktree', 'add', '-q', '--detach', path.join(work, 'linked'), 'HEAD'); return 'worktrees/linked/'; }
const lg = (...args) => run('git', ['-C', path.join(work, 'linked'), ...args]);
function fetchOther() {
    const other = path.join(root, 'other.git'); run('git', ['clone', '-q', '--bare', origin, other]);
    const oid = run('git', ['-C', other, '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit-tree', 'HEAD^{tree}', '-p', 'HEAD', '-m', 'private']);
    run('git', ['-C', other, 'update-ref', 'refs/heads/master', oid]); git('fetch', '-q', 'file://' + other, 'master');
    assert.equal(git('rev-parse', 'FETCH_HEAD'), oid); assert.equal(git('for-each-ref', '--contains', oid), '');
}
function bareMirror() {
    const bare = path.join(work, 'bare.git'); run('git', ['clone', '-q', '--mirror', repo, bare]);
    run('git', ['-C', bare, 'remote', 'set-url', 'origin', origin]); return bare;
}
function reftable() {
    remove(gd('logs')); git('refs', 'migrate', '--ref-format=reftable'); assert.ok(fs.existsSync(gd('reftable')));
    const was = head(), oid = priv(); git('update-ref', '-m', 'private', 'HEAD', oid); git('update-ref', '-m', 'back', 'HEAD', was);
}
function moduleDir() {
    const sub = gd('modules/sub'); run('git', ['clone', '-q', '--bare', origin, sub]);
    run('git', ['-C', sub, 'update-ref', 'refs/heads/private', run('git', ['-C', sub, '-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit-tree', 'HEAD^{tree}', '-p', 'HEAD', '-m', 'private'])]);
}
function longLived() {
    // Deploy-seeded clone after normal use: filtered fetches, a remote branch that
    // is pruned after gc wrote info/refs, and the deploy's own fetch of FETCH_HEAD.
    const src = path.join(root, 'src'), s = (...args) => run('git', ['-C', src, ...args]);
    s('checkout', '-q', '-b', 'feature'); s('commit', '-q', '--allow-empty', '-m', 'feature'); s('push', '-q', origin, 'feature');
    git('fetch', '-q', '--filter=blob:none', 'origin'); git('gc', '-q');
    const feature = git('rev-parse', 'origin/feature');
    assert.ok(fs.readFileSync(gd('info/refs'), 'utf8').includes(feature), 'gc must record the soon-stale branch');
    run('git', ['-C', origin, 'update-ref', '-d', 'refs/heads/feature']); git('fetch', '-q', '--prune', 'origin');
    git('fetch', '-q', '--no-tags', 'origin', 'master');
    assert.equal(git('rev-parse', 'FETCH_HEAD'), git('rev-parse', 'origin/master'));
    for (const line of fs.readFileSync(gd('logs/HEAD'), 'utf8').trim().split('\n')) assert.match(line, /\tclone: /);
    assert.equal(cp.spawnSync('git', ['-C', repo, 'cat-file', '-e', git('rev-parse', 'HEAD:file')], {env: {...env, GIT_NO_LAZY_FETCH: '1'}}).status, 1, 'HEAD blob stays absent');
}
// HIDDEN-LOCATIONS-BEGIN: one row per Git-directory location (closure table in
// docs/investigations/2026-10-07-card-1105-git-audit-promisor.md). A private commit
// stored only in that location must refuse; a published one must pass.
const hidden = {
    'head': [U, () => put('HEAD', priv() + '\n')],
    'loose-ref': [U, () => put('refs/heads/private', priv() + '\n')],
    'packed-ref': [U, () => add('packed-refs', `${priv()} refs/heads/private\n`)],
    'reflog': [U, () => add('logs/HEAD', entry(priv()))],
    'stash': [U, () => { checkout(); fs.writeFileSync(path.join(repo, 'file'), 'private'); git('stash', 'push', '-qm', 'private'); }],
    'notes': [U, () => git('notes', 'add', '-m', 'private', priv())],
    'index': [D, () => { checkout(); fs.writeFileSync(path.join(repo, 'file'), 'private'); git('add', 'file'); }],
    'linked-head': [U, () => put(linked() + 'HEAD', priv() + '\n')],
    'linked-ref': [U, () => put(linked() + 'refs/worktree/private', priv() + '\n')],
    'linked-reflog': [U, () => add(linked() + 'logs/HEAD', entry(priv()))],
    'linked-reset': [U, () => { linked(); lg('commit', '-q', '--allow-empty', '-m', 'private'); lg('reset', '-q', '--hard', 'HEAD~1'); }],
    'linked-orig-head': [U, () => put(linked() + 'ORIG_HEAD', priv() + '\n')],
    'linked-fetch-head': [U, () => put(linked() + 'FETCH_HEAD', `${priv()}\t\tbranch 'master' of other\n`)],
    'linked-merge-head': [K, () => put(linked() + 'MERGE_HEAD', priv() + '\n'), 'check=lock-MERGE_HEAD'],
    'orig-head': [U, () => put('ORIG_HEAD', priv() + '\n')],
    'orig-head-missing': [K, () => put('ORIG_HEAD', 'f'.repeat(40) + '\n'), 'check=rev-list-objects'],
    'fetch-head': [U, fetchOther],
    'rebase-head': [U, () => put('REBASE_HEAD', priv() + '\n')],
    'bisect-head': [U, () => put('BISECT_HEAD', priv() + '\n')],
    'auto-merge': [U, () => put('AUTO_MERGE', priv() + '\n')],
    'merge-autostash': [U, () => put('MERGE_AUTOSTASH', priv() + '\n')],
    'merge-head': [K, () => put('MERGE_HEAD', priv() + '\n'), 'check=lock-MERGE_HEAD'],
    'cherry-pick-head': [K, () => put('CHERRY_PICK_HEAD', priv() + '\n'), 'check=lock-CHERRY_PICK_HEAD'],
    'revert-head': [K, () => put('REVERT_HEAD', priv() + '\n'), 'check=lock-REVERT_HEAD'],
    'rebase-merge': [K, () => put('rebase-merge/orig-head', priv() + '\n'), 'check=lock-rebase-merge'],
    'rebase-apply': [K, () => put('rebase-apply/orig-head', priv() + '\n'), 'check=lock-rebase-apply'],
    'sequencer': [K, () => put('sequencer/head', priv() + '\n'), 'check=lock-sequencer'],
    'bisect-state': [K, () => put('BISECT_EXPECTED_REV', priv() + '\n'), 'check=lock-BISECT_EXPECTED_REV'],
    'notes-merge': [K, () => put('NOTES_MERGE_PARTIAL', priv() + '\n'), 'check=lock-NOTES_MERGE_PARTIAL'],
    'ref-lock': [K, () => put('refs/heads/private.lock', priv() + '\n'), 'check=lock-ref'],
    'replace': [K, () => put('refs/replace/' + priv(), head() + '\n'), 'check=replace-ref'],
    'replace-packed': [K, () => add('packed-refs', `${head()} refs/replace/${priv()}\n`), 'check=replace-ref'],
    'grafts': [K, () => put('info/grafts', `${head()} ${priv()}\n`), 'check=grafts'],
    'info-entry': [K, () => put('info/private', priv() + '\n'), 'check=gitdir-info'],
    'unknown-entry': [K, () => { const oid = priv(); put('lost-found/commit/' + oid, oid + '\n'); }, 'check=gitdir-entry'],
    'modules': [K, moduleDir, 'check=gitdir-modules'],
    'reftable': [K, reftable, 'check=ref-storage'],
    'bare-index': [K, () => { const bare = bareMirror(), blob = run('git', ['-C', bare, 'hash-object', '-w', '--stdin'], {input: 'private'});
        run('git', ['-C', bare, 'update-index', '--add', '--cacheinfo', `100644,${blob},private`]); }, 'check=bare-index'],
    'published-fetch-head': [P, () => git('fetch', '-q', '--no-tags', 'origin', 'master')],
    'published-orig-head': [P, () => put('ORIG_HEAD', git('rev-parse', 'HEAD~1') + '\n')],
    'published-linked': [P, () => { linked(); lg('reset', '-q', '--hard', 'HEAD~1'); lg('reset', '-q', '--hard', 'ORIG_HEAD'); }],
    'published-gc': [P, () => { git('fetch', '-q', 'origin'); git('gc', '-q'); assert.ok(fs.existsSync(gd('info/refs'))); }],
};
// HIDDEN-LOCATIONS-END
try {
    setup(); helper();
    let expected = 'RecycleUnpublishedWork', expectedCheck;
    switch (mode) {
        case 'seed': expected = ''; break;
        case 'fetch-only': git('fetch', 'origin'); expected = ''; break;
        case 'empty-used': checkout(); eraseIndexAndFiles(); expected = ''; break;
        case 'seed-empty-ignored': {
            git('commit', '--allow-empty', '-qm', 'empty tree'); git('push', '-q', 'origin', 'master');
            remove(path.join(repo, '.git/index'));
            fs.appendFileSync(path.join(repo, '.git/info/exclude'), '\nignored\n');
            fs.writeFileSync(path.join(repo, 'ignored'), 'private'); expected = 'RecycleWorktreeDirty'; break;
        }
        case 'stash': checkout(); fs.writeFileSync(path.join(repo, 'file'), 'private'); git('stash', 'push', '-qm', 'private'); break;
        case 'reflog': checkout(); privateCommit(); git('reset', '--hard', 'HEAD~1'); break;
        case 'reflog-symlink': {
            checkout(); privateCommit(); git('reset', '--hard', 'HEAD~1');
            const log = path.join(repo, '.git/logs/HEAD');
            fs.renameSync(log, log + '.saved'); fs.symlinkSync('HEAD.saved', log);
            expected = 'RecycleGitAuditUnknown'; break;
        }
        case 'reflog-old': checkout(); privateCommit(); git('reset', '--hard', 'HEAD~1'); {
            // Retain only the final reset entry: its old side is the sole private root.
            for (const file of ['HEAD','refs/heads/master']) {
                const p = path.join(repo, '.git/logs', file), lines = fs.readFileSync(p,'utf8').trim().split('\n');
                fs.writeFileSync(p, lines.at(-1) + '\n');
            }
            break;
        }
        case 'recovery': case 'secondary': case 'worktree-ref': {
            checkout(); const oid = privateCommit();
            git('update-ref', mode === 'recovery' ? 'refs/antiphon/recovery' : mode === 'secondary' ? 'refs/remotes/other/private' : 'refs/worktree/recovery', oid);
            git('reset', '--hard', 'HEAD~1'); expireLogs(); break;
        }
        case 'linked-head': case 'linked-private': {
            checkout(); const linked = path.join(work, 'linked'); git('worktree', 'add', '-q', '--detach', linked, 'HEAD');
            const g = (...args) => run('git', ['-C', linked, ...args]);
            g('commit', '--allow-empty', '-qm', 'private');
            if (mode === 'linked-private') {g('update-ref', 'refs/worktree/recovery', g('rev-parse', 'HEAD')); g('reset', '--hard', 'HEAD~1');}
            expireLogs(); break;
        }
        case 'bare-head': {
            const oid = run('git', ['-C', repo, 'commit-tree', 'HEAD^{tree}', '-p', 'HEAD', '-m', 'private']);
            const bare = path.join(work, 'bare.git'); run('git', ['clone', '-q', '--mirror', repo, bare]);
            run('git', ['-C', bare, 'remote', 'set-url', 'origin', origin]);
            run('git', ['-C', bare, 'update-ref', '--no-deref', 'HEAD', oid]); break;
        }
        case 'seed-untracked': case 'seed-tracked': case 'seed-ignored': {
            const name = mode === 'seed-tracked' ? 'file' : mode === 'seed-ignored' ? 'ignored' : 'new';
            if (mode === 'seed-ignored') fs.appendFileSync(path.join(repo, '.git/info/exclude'), '\nignored\n');
            fs.writeFileSync(path.join(repo, name), 'private'); expected = 'RecycleWorktreeDirty'; break;
        }
        case 'seed-stash': checkout(); fs.writeFileSync(path.join(repo, 'file'), 'private'); git('stash', 'push', '-qm', 'private'); eraseIndexAndFiles(); expected = 'RecycleWorktreeDirty'; break;
        case 'staged': case 'modified': case 'assume': case 'skip': case 'intent': case 'sparse': case 'racy': {
            checkout();
            if (mode === 'assume') git('update-index', '--assume-unchanged', 'file');
            if (mode === 'skip') git('update-index', '--skip-worktree', 'file');
            if (mode === 'sparse') {git('sparse-checkout', 'init', '--cone'); git('sparse-checkout', 'set', 'absent'); git('update-index', '--skip-worktree', 'file');}
            const p = path.join(repo, 'file');
            fs.writeFileSync(p, 'X\n');
            if (mode === 'racy') {
                // Model an unchanged stat tuple after a same-size write, independently
                // of filesystem timestamp granularity. Preserve the original blob OID.
                const indexPath = path.join(repo, '.git/index'), bytes = fs.readFileSync(indexPath), stat = fs.statSync(p, {bigint:true});
                assert.equal(bytes.readUInt32BE(4), 2, 'fixture requires index v2');
                let offset = 12, found = false;
                for (let i = 0; i < bytes.readUInt32BE(8); i++) {
                    const end = bytes.indexOf(0, offset + 62), name = bytes.toString('utf8', offset + 62, end);
                    if (name === 'file') {
                        for (const [at, value] of [[0,stat.ctimeNs/1000000000n],[4,stat.ctimeNs%1000000000n],[8,stat.mtimeNs/1000000000n],[12,stat.mtimeNs%1000000000n]]) bytes.writeUInt32BE(Number(value & 0xffffffffn), offset + at);
                        found = true;
                    }
                    offset += Math.ceil((end - offset + 1) / 8) * 8;
                }
                assert.ok(found); require('crypto').createHash('sha1').update(bytes.subarray(0,-20)).digest().copy(bytes,bytes.length-20);
                fs.writeFileSync(indexPath,bytes); const future = new Date(Date.now()+2000); fs.utimesSync(indexPath,future,future);
                assert.equal(git('status','--porcelain'), '', 'fixture must defeat ordinary status');
            }
            if (mode === 'staged') git('add', 'file');
            if (mode === 'intent') {fs.writeFileSync(path.join(repo, 'new'), 'private'); git('add', '-N', 'new');}
            expected = 'RecycleWorktreeDirty'; break;
        }
        case 'missing-commit': objectLoss('HEAD~1'); expected = 'RecycleGitAuditUnknown'; break;
        case 'missing-tree': objectLoss('HEAD~1^{tree}'); expected = 'RecycleGitAuditUnknown'; break;
        case 'missing-head-tree': objectLoss('HEAD^{tree}'); expected = 'RecycleGitAuditUnknown'; break;
        case 'resume': {
            const proof = audit(); assert.equal(proof.status, 0, proof.stdout);
            const initial = {image:'sha256:'+'a'.repeat(64), volumes:{antiphon_runner_work:{}}, phase:'preflight', audit:proof.stdout.trim().split('\n').sort().join('\n')};
            const journal = path.join(root,'journal.json'); fs.writeFileSync(journal, JSON.stringify(initial));
            const start = source.indexOf('    c1008_audit_checked; audit="$C1008_AUDIT"'), end = source.indexOf('    while IFS= read -r id;', start);
            assert.ok(start > 0 && end > start);
            const wrapper = `set -euo pipefail\nC1008_PROJECT=antiphon-runner\nC1008_RESUME=1\nC1008_RECORD="$(cat "$C1105_JOURNAL")"\nc1008_volume() { echo '{}'; }\nc1008_save() { printf '%s' "$C1008_RECORD" > "$C1105_JOURNAL"; }\nc1008_refuse() { echo "$1"; exit 2; }\nc1008_git_program() { cat "$C1105_HELPER"; }\ndocker() { case "$1 $2" in 'image inspect') echo sha256:${'a'.repeat(64)} ;; 'create --user') echo ${'b'.repeat(64)} ;; 'start -a') bash "$C1105_HELPER" ;; 'rm --') return 0 ;; *) return 2 ;; esac; }\n`;
            const script = path.join(root,'resume.sh'); fs.writeFileSync(script, wrapper + block('c1008_audit') + '\n' + block('c1008_audit_checked') + '\n' + source.slice(start,end));
            const opts = {env:{...env, C1105_WORK:work, C1105_HELPER:path.join(root,'helper.sh'), C1105_JOURNAL:journal}, encoding:'utf8'};
            git('remote','set-url','origin',path.join(root,'absent'));
            const failed = cp.spawnSync('bash',[script],opts); assert.equal(failed.status,2,failed.stdout); assert.match(failed.stdout,/RecycleGitAuditUnknown/);
            const saved = JSON.parse(fs.readFileSync(journal)); assert.equal(saved.audit,initial.audit,'failure must preserve publication proof'); assert.match(saved.auditFailure,/check=ls-remote status=128/);
            git('remote','set-url','origin','file://'+origin);
            run('bash',[script],opts); console.log('PASS resume'); process.exitCode = 0; break;
        }
        case 'long-lived': longLived(); expected = ''; break;
        default: {
            if (!Object.hasOwn(hidden, mode)) throw Error('Unknown mode ' + mode);
            const [want, hide, check] = hidden[mode]; hide(); expected = want; expectedCheck = check; break;
        }
    }
    if (mode !== 'resume') {
        const result = audit();
        assert.equal(result.status, expected ? 2 : 0, `${mode}: ${result.stdout}\n${result.stderr}`);
        if (expectedCheck) assert.ok(result.stdout.includes(expectedCheck + ' '), `${mode}: ${result.stdout}`);
        if (expected) assert.ok(result.stdout.includes(expected), `${mode}: ${result.stdout}`);
        else assert.match(result.stdout, /repositories=\d+ partial=\d+/);
        console.log('PASS ' + mode);
    }
} finally { if (fs.existsSync(root)) { assert.ok(root.startsWith('/tmp/c1105-audit-')); fs.rmSync(root,{recursive:true}); } }
