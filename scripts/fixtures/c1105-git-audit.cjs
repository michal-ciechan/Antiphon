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
    fs.writeFileSync(path.join(src, '.gitignore'), 'ignored\nbin-*/\nobj/\n');
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
function helper(edit = x => x) {
    const match = source.match(/cat <<'C1008_GIT'\n([\s\S]*?)\nC1008_GIT/);
    assert.ok(match); assert.equal(match[1].split('readlink -e /work').length, 2);
    fs.writeFileSync(path.join(root, 'helper.sh'), edit(match[1].replace('readlink -e /work', 'readlink -e "$C1105_WORK"')));
}
// Every Git start carries the no-fetch, no-system/global-config and no-commit-graph values.
const required = {GIT_NO_LAZY_FETCH:'1', GIT_CONFIG_SYSTEM:'/dev/null', GIT_CONFIG_GLOBAL:'/dev/null', GIT_CONFIG_COUNT:'2',
    GIT_CONFIG_KEY_0:'core.commitGraph', GIT_CONFIG_VALUE_0:'false', GIT_CONFIG_KEY_1:'gc.writeCommitGraph', GIT_CONFIG_VALUE_1:'false'};
let events = [];
function audit(timeout = 45000) {
    const trace = path.join(root, 'trace.jsonl');
    if (fs.existsSync(trace)) fs.rmSync(trace);
    const result = cp.spawnSync('bash', [path.join(root, 'helper.sh')], {env: {...env, C1105_WORK: work, GIT_TRACE2_EVENT: trace,
        GIT_TRACE2_ENV_VARS: Object.keys(required).join(',')}, encoding: 'utf8', timeout, maxBuffer: 64e6});
    const text = fs.existsSync(trace) ? fs.readFileSync(trace, 'utf8').trim() : '';
    events = text ? text.split('\n').map(JSON.parse) : [];
    assert.ok(events.length > 0 || mode === 'audit-timeout', 'the audit ran Git');
    assert.ok(!events.some(x => x.argv?.includes('fetch')), 'audit must never lazy-fetch');
    const params = new Set(events.filter(x => x.event === 'def_param').map(x => x.sid + '\0' + x.param + '\0' + x.value));
    // A local-transport git-upload-pack serves the origin repository, and Git clears per-repository
    // configuration variables for that other repository; it never reads the audited volume.
    for (const start of events.filter(x => x.event === 'start')) for (const [key, value] of Object.entries(required))
        if (!(/^GIT_CONFIG_(COUNT|KEY_|VALUE_)/.test(key) && /(^|\/)git-upload-pack$/.test(start.argv[0]) && start.argv[1] === path.join(root, 'origin.git'))) assert.ok(params.has(start.sid + '\0' + key + '\0' + value), `${key} missing at git start`);
    return result;
}
// Git starts during the last audit whose argv holds this exact argument.
const starts = arg => events.filter(x => x.event === 'start' && x.argv.includes(arg)).length;
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
const lp = rel => path.join(work, 'linked', rel);
const sorted = text => text.trim().split('\n').sort().join('\n');
// A tree as git leaves in AUTO_MERGE: the conflicted result, named by no ref.
function conflictTree() {
    const blob = run('git', ['-C', repo, 'hash-object', '-w', '--stdin'], {input: '<<<<<<< private\n'});
    return run('git', ['-C', repo, 'mktree'], {input: `100644 blob ${blob}\tfile\n`});
}
// Staged private bytes in a linked worktree's own index; only that index names them.
function stagedLinked() {
    linked(); fs.writeFileSync(path.join(work, 'linked', 'new'), 'sole staged bytes'); lg('add', 'new');
    const blob = lg('rev-parse', ':new');
    const all = run('git', ['-C', repo, 'rev-list', '--objects', '--missing=print', '--all'], {env: {...env, GIT_NO_LAZY_FETCH: '1'}});
    assert.equal(all.includes(blob), false, 'no commit, ref or reflog names the staged blob');
}
const s = (...args) => run('git', ['-C', path.join(root, 'src'), ...args]);
// Origin gains heads this clone never fetched, as for a drained runner's mirror.
function originAhead() {
    s('push', '-q', origin, 'master:refs/heads/feature'); git('fetch', '-q', 'origin');
    s('commit', '-q', '--allow-empty', '-m', 'ahead'); s('push', '-q', origin, 'master', 'master:refs/heads/new-branch');
    const ahead = s('rev-parse', 'HEAD');
    assert.equal(cp.spawnSync('git', ['-C', repo, 'cat-file', '-e', ahead], {env: {...env, GIT_NO_LAZY_FETCH: '1'}}).status, 1, 'origin head absent here');
}
function trackingOnly() {
    s('checkout', '-q', '-b', 'gone'); s('commit', '-q', '--allow-empty', '-m', 'gone'); s('push', '-q', origin, 'gone');
    git('fetch', '-q', 'origin'); s('push', '-q', origin, ':gone'); s('checkout', '-q', 'master');
    assert.ok(git('for-each-ref', 'refs/remotes/origin/gone'));
}
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
// A whole volume shaped like the server2 replay: one blobless mirror and many
// linked worktrees, each with a few hundred tracked files and ignored build output.
function volumeScale(count, files) {
    const src = path.join(root, 'src');
    for (let i = 0; i < files; i++) {
        const dir = path.join(src, 'd' + (i % 20)); fs.mkdirSync(dir, {recursive: true});
        fs.writeFileSync(path.join(dir, 'f' + i + '.cs'), `// line ${i}\n`.repeat(40));
    }
    s('add', '.'); s('commit', '-qm', 'tree'); s('push', '-q', origin, 'master'); git('fetch', '-q', 'origin');
    for (let i = 0; i < count; i++) {
        const name = 'task-' + String(i).padStart(4, '0'), wt = path.join(work, 'worktrees', name);
        git('worktree', 'add', '-q', '-b', name, wt, 'origin/master');
        for (const rel of ['bin-c1105/a.dll', 'bin-c1105/a.pdb', 'obj/project.assets.json']) {
            fs.mkdirSync(path.dirname(path.join(wt, rel)), {recursive: true}); fs.writeFileSync(path.join(wt, rel), 'build output ' + i);
        }
    }
    git('push', '-q', 'origin', 'refs/heads/task-*:refs/heads/task-*');
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
    // Git leaves AUTO_MERGE (a tree) after a conflicted merge or rebase; replayed on server2.
    'auto-merge': [P, () => put('AUTO_MERGE', conflictTree() + '\n')],
    'auto-merge-commit': [K, () => put('AUTO_MERGE', priv() + '\n'), 'check=auto-merge-type'],
    'auto-merge-sequencer': [K, () => { put('AUTO_MERGE', conflictTree() + '\n'); put('sequencer/head', head() + '\n'); }, 'check=lock-AUTO_MERGE'],
    'auto-merge-in-progress': [K, () => {
        checkout(); git('checkout', '-q', '-b', 'side', 'HEAD~1'); fs.writeFileSync(path.join(repo, 'file'), 'side\n'); git('commit', '-qam', 'side');
        git('checkout', '-q', 'master'); fs.writeFileSync(path.join(repo, 'file'), 'main\n'); git('commit', '-qam', 'main');
        assert.equal(cp.spawnSync('git', ['-C', repo, 'merge', 'side'], {env}).status, 1); assert.ok(fs.existsSync(gd('AUTO_MERGE')));
    }, 'check=lock-MERGE_HEAD'],
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
    // Origin ahead of the clone: compare only against advertised heads present here.
    'origin-ahead': [P, originAhead],
    'origin-tracking-deleted': [U, trackingOnly],
    'origin-none-present': [K, () => { s('commit', '-q', '--allow-empty', '-m', 'ahead'); s('push', '-q', origin, 'master'); }, 'check=origin-present'],
    // update-ref refuses a tree on a branch; the file is written directly.
    'origin-non-commit': [K, () => fs.writeFileSync(path.join(origin, 'refs/heads/tree'), git('rev-parse', 'HEAD^{tree}') + '\n'), 'check=origin-type'],
    // Every worktrees/<id> is audited whether or not its checkout exists or points back.
    'stale-pointer-staged': [D, () => { stagedLinked(); put('worktrees/linked/gitdir', gd() + '\n'); remove(path.join(work, 'linked')); }, 'check=stale-index'],
    'pruned-path-staged': [D, () => { stagedLinked(); remove(path.join(work, 'linked')); }, 'check=stale-index'],
    'stale-linked-published': [P, () => { linked(); remove(path.join(work, 'linked')); }],
    'linked-outside': [K, () => { checkout(); git('worktree', 'add', '-q', '--detach', path.join(root, 'outside'), 'HEAD'); }, 'check=worktree-confine'],
    // Ignored files are not protected work; tracked content stays protected.
    'ignored-build-output': [P, () => {
        checkout();
        for (const rel of ['bin-c1105/Antiphon.dll', 'obj/project.assets.json', 'ignored']) {
            fs.mkdirSync(path.dirname(path.join(repo, rel)), {recursive: true}); fs.writeFileSync(path.join(repo, rel), 'build output');
        }
        assert.equal(git('status', '--porcelain', '--untracked-files=all'), '');
    }],
    'ignored-tracked-modified': [D, () => {
        checkout(); fs.mkdirSync(path.join(repo, 'obj')); fs.writeFileSync(path.join(repo, 'obj/tracked'), 'A\n');
        git('add', '-f', 'obj/tracked'); git('commit', '-qm', 'tracked under obj'); git('push', '-q', 'origin', 'HEAD:master');
        fs.writeFileSync(path.join(repo, 'obj/tracked'), 'private\n');
    }, 'check=status'],
    // Product and agent state under the common directory.
    'antiphon-state': [P, () => {
        put('antiphon/landing.lock', ''); put('antiphon/verification/op/task/restoration.json', '{}');
        put('antiphon/reviews/task/review.md', 'notes'); fs.mkdirSync(gd('antiphon/children'));
    }],
    'antiphon-children': [K, () => put('antiphon/children/child.json', '{}'), 'check=antiphon-children'],
    'antiphon-linked': [K, () => put(linked() + 'antiphon/landing.lock', ''), 'check=gitdir-entry'],
    'review-evidence': [P, () => put('review-evidence/4a14585d/review.md', 'evidence')],
    'review-evidence-linked': [K, () => put(linked() + 'review-evidence/review.md', 'evidence'), 'check=gitdir-entry'],
    // Content-free layout markers pass; anything that can hold bytes refuses.
    'common-empty': [P, () => fs.mkdirSync(gd('common'))],
    'common-content': [K, () => put('common/private', priv() + '\n'), 'check=gitdir-common'],
    'daemon-export': [P, () => put('git-daemon-export-ok', '')],
    'editor-swap': [K, () => put('.COMMIT_EDITMSG.swp', 'unsaved message'), 'check=gitdir-editor'],
    // Repair 5 (D1): a recorded checkout that still exists is inspected against its admin
    // directory even when its .git pointer is gone or names another directory.
    'orphan-missing-modified': [D, () => { linked(); fs.writeFileSync(lp('file'), 'private\n'); remove(lp('.git')); }, 'check=orphan-status'],
    'orphan-missing-untracked': [D, () => { linked(); fs.writeFileSync(lp('new'), 'private\n'); remove(lp('.git')); }, 'check=orphan-status'],
    'orphan-elsewhere-modified': [D, () => {
        linked(); git('worktree', 'add', '-q', '--detach', path.join(work, 'other'), 'HEAD');
        fs.writeFileSync(lp('.git'), `gitdir: ${gd('worktrees/other')}\n`); fs.writeFileSync(lp('file'), 'private\n');
    }, 'check=status'],
    'orphan-missing-clean': [P, () => { linked(); remove(lp('.git')); }],
    'orphan-missing-ignored': [P, () => {
        linked();
        for (const rel of ['bin-c1105/Antiphon.dll', 'obj/project.assets.json', 'ignored']) {
            fs.mkdirSync(path.dirname(lp(rel)), {recursive: true}); fs.writeFileSync(lp(rel), 'build output');
        }
        remove(lp('.git'));
    }],
    // Repair 5 (D2): a tip that is not a commit is published only as that very object.
    'tag-annotated': [U, () => git('tag', '-a', 'private-note', '-m', 'private annotation'), 'check=tip-object'],
    'tag-annotated-packed': [U, () => { git('tag', '-a', 'private-note', '-m', 'private annotation'); git('pack-refs', '--all'); }, 'check=tip-object'],
    'tag-annotated-published': [P, () => { git('tag', '-a', 'release', '-m', 'published annotation'); git('push', '-q', 'origin', 'release'); }],
    'tag-of-tag': [U, () => { git('tag', '-a', 'inner', '-m', 'inner'); git('push', '-q', 'origin', 'inner'); git('tag', '-a', 'outer', '-m', 'outer', 'inner'); }, 'check=tip-object'],
    'tag-of-tag-published': [P, () => { git('tag', '-a', 'inner', '-m', 'inner'); git('tag', '-a', 'outer', '-m', 'outer', 'inner'); git('tag', '-d', 'inner'); git('push', '-q', 'origin', 'outer'); }],
    'tag-lightweight': [P, () => git('tag', 'light')],
    'tag-tree': [U, () => git('tag', '-a', 'tree-note', '-m', 'tree', 'HEAD^{tree}'), 'check=tip-object'],
    'tree-ref-published': [P, () => git('update-ref', 'refs/private/tree', git('rev-parse', 'HEAD^{tree}'))],
    'tree-ref-unpublished': [U, () => git('update-ref', 'refs/private/tree', conflictTree()), 'check=tip-object'],
    'blob-ref': [U, () => git('update-ref', 'refs/private/blob', run('git', ['-C', repo, 'hash-object', '-w', '--stdin'], {input: 'private'})), 'check=tip-object'],
    // Repair 5 (D3): ignored output never refuses and never changes the receipt; symlinks
    // in a checkout are its content and are never followed.
    'ignored-head-equality': [P, () => {
        checkout(); const before = audit(); assert.equal(before.status, 0, before.stdout);
        fs.mkdirSync(path.join(repo, 'obj')); fs.writeFileSync(path.join(repo, 'obj/HEAD'), 'ref: refs/heads/master\n');
        assert.equal(git('status', '--porcelain', '--untracked-files=all'), '');
        const after = audit(); assert.equal(after.status, 0, after.stdout);
        assert.equal(sorted(after.stdout), sorted(before.stdout), 'an ignored HEAD file must not change the receipt');
    }],
    'ignored-symlink-outside': [P, () => { checkout(); fs.mkdirSync(path.join(repo, 'obj')); fs.symlinkSync(root, path.join(repo, 'obj/outside')); }],
    'ignored-symlink-dangling': [P, () => { checkout(); fs.mkdirSync(path.join(repo, 'obj')); fs.symlinkSync('gone', path.join(repo, 'obj/cache')); }],
    'untracked-symlink': [D, () => { checkout(); fs.symlinkSync('gone', path.join(repo, 'link')); }, 'check=status'],
    'tracked-symlink-outside': [P, () => {
        checkout(); fs.symlinkSync(root, path.join(repo, 'outside')); git('add', 'outside'); git('commit', '-qm', 'link'); git('push', '-q', 'origin', 'HEAD:master');
    }],
    'metadata-symlink-outside': [K, () => fs.symlinkSync(root, gd('hooks/outside')), 'check=link-confine'],
    'loose-symlink-dangling': [K, () => fs.symlinkSync('gone', path.join(work, 'dangling')), 'check=link-resolve'],
    // Repair 5 re-scan: content Git never shows, or a work tree moved by configuration.
    'dot-git-content': [K, () => {
        checkout(); fs.mkdirSync(path.join(repo, 'sub/.git'), {recursive: true}); fs.writeFileSync(path.join(repo, 'sub/.git/notes'), 'private');
        assert.equal(git('status', '--porcelain', '--untracked-files=all'), '');
    }, 'check=dot-git'],
    'linked-core-worktree': [K, () => {
        linked(); const other = path.join(work, 'elsewhere'); fs.mkdirSync(other);
        fs.writeFileSync(path.join(other, 'file'), 'B\n'); fs.writeFileSync(path.join(other, '.gitignore'), 'ignored\nbin-*/\nobj/\n');
        git('config', 'extensions.worktreeConfig', 'true');
        run('git', ['--git-dir=' + gd('worktrees/linked'), 'config', '--worktree', 'core.worktree', other]);
        fs.writeFileSync(lp('new'), 'private');
        assert.equal(lg('status', '--porcelain', '--untracked-files=all'), '');
    }, 'check=core-worktree'],
    'bare-dot-git': [K, () => { git('config', 'core.bare', 'true'); fs.writeFileSync(path.join(repo, 'new'), 'private'); }, 'check=core-bare'],
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
        case 'missing-ancestor-graph': {
            // A commit-graph still describes HEAD~1 after its object is gone.
            git('commit-graph', 'write', '--reachable'); assert.ok(fs.existsSync(gd('objects/info/commit-graph')));
            objectLoss('HEAD~1'); expected = K; expectedCheck = 'check=rev-list-objects'; break;
        }
        case 'audit-timeout': {
            // The overall budget, shortened: an audit that cannot finish refuses, never passes.
            const budget = 'timeout --kill-after=10s 1800s bash -c';
            helper(text => { assert.equal(text.split(budget).length, 2); return text.replace(budget, 'timeout --kill-after=10s 0.01s bash -c'); });
            expected = K; expectedCheck = 'check=audit-timeout'; break;
        }
        case 'volume-scale': {
            const count = 250, files = 300, budget = 240;
            volumeScale(count, files);
            const started = process.hrtime.bigint(), result = audit(2 * budget * 1000);
            const seconds = Number(process.hrtime.bigint() - started) / 1e9;
            assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
            assert.match(result.stdout, new RegExp(`repositories=${count + 1} partial=${count + 1}`));
            // One pass per common directory; one content check per worktree path.
            assert.equal(starts('ls-remote'), 1, 'origin is read once for the shared common directory');
            assert.equal(starts('status'), count + 1, 'status runs once per worktree path');
            assert.equal(starts('--stdin-paths'), count, 'one batched hash per indexed worktree');
            assert.ok(seconds < budget, `whole-volume audit took ${seconds}s, budget ${budget}s`);
            console.log(`PASS volume-scale worktrees=${count} files=${files} seconds=${seconds.toFixed(1)}`);
            expected = null; break;
        }
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
    if (mode !== 'resume' && expected !== null) {
        const result = audit();
        assert.equal(result.status, expected ? 2 : 0, `${mode}: ${result.stdout}\n${result.stderr}`);
        if (expectedCheck) assert.ok(result.stdout.includes(expectedCheck + ' '), `${mode}: ${result.stdout}`);
        if (expected) assert.ok(result.stdout.includes(expected), `${mode}: ${result.stdout}`);
        else assert.match(result.stdout, /repositories=\d+ partial=\d+/);
        console.log('PASS ' + mode);
    }
} finally { if (fs.existsSync(root)) { assert.ok(root.startsWith('/tmp/c1105-audit-')); fs.rmSync(root,{recursive:true}); } }
