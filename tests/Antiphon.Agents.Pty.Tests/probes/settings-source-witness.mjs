// Benign SessionStart fixture. The hook itself writes the session-bound marker.
import fs from 'node:fs';
import path from 'node:path';
const [destination, source, retained] = process.argv.slice(2);
if (!path.isAbsolute(destination ?? '') || !['user', 'project', 'local'].includes(source))
  throw new Error('invalid owned witness request');
let input = '';
for await (const part of process.stdin) {
  input += part;
  if (input.length > 16384) throw new Error('hook input too large');
}
const hook = JSON.parse(input);
if (typeof hook.session_id !== 'string' || !hook.session_id || typeof retained !== 'string')
  throw new Error('missing hook identity');
fs.appendFileSync(destination, JSON.stringify({source, session: hook.session_id, retained}) + '\n', {mode: 0o600});
