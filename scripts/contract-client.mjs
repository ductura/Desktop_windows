// C# contract client (DAN-958). `generate` writes src/Ductura.Contracts/Generated
// from the OpenAPI of the locked @ductura/contracts release; `check` generates
// into a temporary folder and fails when the committed code differs by one byte.
// Only /api/v1/health is generated. The generator is the Kiota version pinned in
// .config/dotnet-tools.json.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync, cpSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = fileURLToPath(new URL('..', import.meta.url));
const PROJECT = join(ROOT, 'src', 'Ductura.Contracts');
const GENERATED = join(PROJECT, 'Generated');
const INCLUDE_PATH = '/api/v1/health';
const VERSION = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/;
const SHA256 = /^[0-9a-f]{64}$/;
const KEYS = ['sha256', 'url', 'version'];

/** Parses contract-lock.json: exactly version, url and sha256; the url follows from the version. */
export function parseLock(text) {
  const lock = JSON.parse(text);
  if (typeof lock !== 'object' || lock === null || Array.isArray(lock)) throw new Error('lock must be an object');
  const keys = Object.keys(lock).sort();
  if (keys.join() !== KEYS.join()) throw new Error(`lock keys must be exactly ${KEYS.join(', ')}`);
  const { version, url, sha256 } = lock;
  if (typeof version !== 'string' || !VERSION.test(version)) throw new Error('version must be major.minor.patch');
  const expected = `https://github.com/ductura/server/releases/download/contracts-v${version}/ductura-contracts-${version}.tgz`;
  if (url !== expected) throw new Error(`url must be ${expected}`);
  if (typeof sha256 !== 'string' || !SHA256.test(sha256)) throw new Error('sha256 must be 64 lower-case hex digits');
  return { version, url, sha256 };
}

/** Throws unless the tarball bytes have the locked sha256. */
export function verifyTarball(bytes, { sha256 }) {
  const actual = createHash('sha256').update(bytes).digest('hex');
  if (actual !== sha256) throw new Error(`tarball sha256 ${actual} is not the locked ${sha256}`);
}

/** Every file under dir as relative path with / separators -> bytes. */
export function readTree(dir) {
  const files = new Map();
  for (const entry of readdirSync(dir, { recursive: true, withFileTypes: true })) {
    if (!entry.isFile()) continue;
    const path = join(entry.parentPath, entry.name);
    files.set(relative(dir, path).split(sep).join('/'), readFileSync(path));
  }
  return files;
}

/** Differences between the committed (expected) and the generated (actual) tree; empty when equal. */
export function compareTrees(expected, actual) {
  const problems = [];
  for (const [path, bytes] of expected) {
    if (!actual.has(path)) problems.push(`not generated: ${path}`);
    else if (!bytes.equals(actual.get(path))) problems.push(`differs: ${path}`);
  }
  for (const path of actual.keys()) if (!expected.has(path)) problems.push(`not committed: ${path}`);
  if (expected.size === 0 && actual.size === 0) problems.push('no generated files');
  return problems.sort();
}

async function generate(out) {
  const lock = parseLock(readFileSync(join(PROJECT, 'contract-lock.json'), 'utf8'));
  const response = await fetch(lock.url);
  if (!response.ok) throw new Error(`download failed: ${response.status} ${lock.url}`);
  const tarball = Buffer.from(await response.arrayBuffer());
  verifyTarball(tarball, lock);
  const work = mkdtempSync(join(tmpdir(), 'ductura-contracts-'));
  try {
    writeFileSync(join(work, 'contracts.tgz'), tarball);
    execFileSync('tar', ['-xzf', 'contracts.tgz', 'package/openapi/v1.json'], { cwd: work });
    execFileSync('dotnet', ['tool', 'run', 'kiota', 'generate', '--language', 'CSharp',
      '--openapi', join(work, 'package', 'openapi', 'v1.json'), '--include-path', INCLUDE_PATH,
      '--class-name', 'DucturaApiClient', '--namespace-name', 'Ductura.Contracts',
      '--exclude-backward-compatible', '--clean-output', '--output', join(work, 'out')],
    { cwd: ROOT, stdio: ['ignore', 'ignore', 'inherit'] });
    // The lock and log of Kiota hold the absolute path of the temporary folder.
    rmSync(join(work, 'out', 'kiota-lock.json'));
    rmSync(join(work, 'out', '.kiota.log'), { force: true });
    rmSync(out, { recursive: true, force: true });
    cpSync(join(work, 'out'), out, { recursive: true });
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
}

async function main(mode) {
  if (mode === 'generate') return generate(GENERATED);
  if (mode !== 'check') throw new Error('usage: node scripts/contract-client.mjs generate|check');
  const out = mkdtempSync(join(tmpdir(), 'ductura-generated-'));
  try {
    await generate(out);
    const problems = compareTrees(readTree(GENERATED), readTree(out));
    for (const p of problems) console.error(`::error::${p} (run node scripts/contract-client.mjs generate)`);
    if (problems.length > 0) process.exitCode = 1;
    else console.log('contract client matches the locked OpenAPI');
  } finally {
    rmSync(out, { recursive: true, force: true });
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) await main(process.argv[2]);
