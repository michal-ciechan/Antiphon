const fs = require('node:fs');
const path = require('node:path');
const target = process.env.ANTIPHON_ARGV_CAPTURE;
if (!target) throw new Error('ANTIPHON_ARGV_CAPTURE is required');
const temporary = path.join(path.dirname(target), `.${path.basename(target)}.${process.pid}.tmp`);
fs.writeFileSync(temporary, JSON.stringify(process.argv.slice(2)), 'utf8');
fs.renameSync(temporary, target);
process.stdout.write('ARGV_CAPTURED\n');
setInterval(() => {}, 1000);
