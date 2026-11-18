// SHA-256 of the Windows package, uploaded next to it in the CI artifact (DAN-1074).
//   node scripts/package-sha256.mjs [package]           writes <package>.sha256
//   node scripts/package-sha256.mjs --verify [package]  checks the package against it
// The package is dist/Ductura-Setup-x64.exe unless given. The file holds one
// line in the sha256sum text form: 64 lower-case hex, two spaces, the file name
// without a folder, LF; anything else fails the check. Exit 0 when written or
// matching, 1 on a failed check or a missing file, 2 on wrong arguments.
import { createHash, randomBytes } from 'node:crypto';
import { createReadStream, readFileSync, realpathSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const DEFAULT_PACKAGE = 'dist/Ductura-Setup-x64.exe';
const LINE = /^([0-9a-f]{64}) {2}(.+)\n$/;

// { verify, file } or null for arguments the script does not take.
export function parseArgs(argv) {
  let verify = false;
  const files = [];
  for (const arg of argv) {
    if (arg === '--verify' && !verify) verify = true;
    else if (arg === '' || arg.startsWith('-') || files.length > 0) return null;
    else files.push(arg);
  }
  return { verify, file: files[0] ?? DEFAULT_PACKAGE };
}

export async function sha256File(path) {
  if (!statSync(path).isFile()) throw new Error(`${path} is not a file`);
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(path)) hash.update(chunk);
  return hash.digest('hex');
}

// The hex of a .sha256 file that names exactly `name`; throws otherwise.
export function parseLine(bytes, name) {
  let text;
  try {
    text = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(bytes);
  } catch {
    throw new Error('the .sha256 file is not UTF-8');
  }
  const match = LINE.exec(text);
  if (!match) throw new Error('the .sha256 file is not one line "<64 lower-case hex>  <name>" ending in LF');
  if (match[2] !== name) throw new Error(`the .sha256 file names ${JSON.stringify(match[2])}, not ${JSON.stringify(name)}`);
  return match[1];
}

// Writes only a line that --verify reads back as the same name, so a name with
// a line break (LF, CR, U+2028, U+2029) or other unreadable text is refused.
// It never writes through an existing path: the line goes to a new file with a
// random name opened exclusively (wx fails on any existing entry, links too),
// which is then renamed onto <package>.sha256, replacing a link instead of
// following it. `random` is replaceable only so a test can name the file.
export async function writeDigest(path, random = () => randomBytes(16).toString('hex')) {
  const hex = await sha256File(path);
  const line = `${hex}  ${basename(path)}\n`;
  parseLine(Buffer.from(line), basename(path));
  const target = `${path}.sha256`;
  const tmp = join(dirname(target), `.${basename(target)}.${random()}.tmp`);
  writeFileSync(tmp, line, { flag: 'wx' });
  try {
    renameSync(tmp, target);
  } catch (error) {
    rmSync(tmp, { force: true });
    throw error;
  }
  return hex;
}

export async function verify(path) {
  const expected = parseLine(readFileSync(`${path}.sha256`), basename(path));
  const actual = await sha256File(path);
  if (actual !== expected) throw new Error(`${path}: SHA-256 ${actual} differs from ${expected} in ${basename(path)}.sha256`);
  return actual;
}

async function main(argv) {
  const args = parseArgs(argv);
  if (!args) {
    console.error('usage: node scripts/package-sha256.mjs [--verify] [package]');
    return 2;
  }
  try {
    const hex = await (args.verify ? verify(args.file) : writeDigest(args.file));
    console.log(`${args.verify ? 'verified' : 'wrote'} ${hex}  ${basename(args.file)}`);
    return 0;
  } catch (error) {
    console.error(`::error::${error.message}`);
    return 1;
  }
}

// Run as a script: argv[1] is not resolved through links, and on Windows the
// drive letter may differ in case from the module URL.
function isMain() {
  try {
    const [a, b] = [realpathSync(process.argv[1]), fileURLToPath(import.meta.url)];
    return process.platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b;
  } catch {
    return false;
  }
}
if (isMain()) process.exitCode = await main(process.argv.slice(2));
