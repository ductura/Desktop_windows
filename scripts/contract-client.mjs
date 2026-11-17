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
const KEYS = ['sha256', 'version'];
const REQUIRED = 'Api/V1/Health/HealthRequestBuilder.cs';

/** Release tarball of @ductura/contracts on the public GitHub Releases of ductura/server. */
export const tarballUrl = (version) =>
  `https://github.com/ductura/server/releases/download/contracts-v${version}/ductura-contracts-${version}.tgz`;

/** Parses contract-lock.json: exactly version and sha256 (own keys only). */
export function parseLock(text) {
  const lock = JSON.parse(text);
  if (typeof lock !== 'object' || lock === null || Array.isArray(lock)) throw new Error('lock must be an object');
  const keys = Object.keys(lock).sort();
  if (keys.join() !== KEYS.join()) throw new Error(`lock keys must be exactly ${KEYS.join(', ')}`);
  const { version, sha256 } = lock;
  if (typeof version !== 'string' || !VERSION.test(version)) throw new Error('version must be major.minor.patch');
  if (typeof sha256 !== 'string' || !SHA256.test(sha256)) throw new Error('sha256 must be 64 lower-case hex digits');
  return { version, sha256 };
}

/** Throws unless the tarball bytes have the locked sha256. */
export function verifyTarball(bytes, { sha256 }) {
  const actual = createHash('sha256').update(bytes).digest('hex');
  if (actual !== sha256) throw new Error(`tarball sha256 ${actual} is not the locked ${sha256}`);
}

/** Every file under dir as relative path with / separators -> bytes; a link or other entry throws. */
export function readTree(dir) {
  const files = new Map();
  for (const entry of readdirSync(dir, { recursive: true, withFileTypes: true })) {
    const path = join(entry.parentPath, entry.name);
    if (entry.isDirectory()) continue;
    if (!entry.isFile()) throw new Error(`not a regular file: ${relative(dir, path)}`);
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
  if (!actual.has(REQUIRED)) problems.push(`generation has no ${REQUIRED}`);
  return problems.sort();
}

// spec replaces the locked OpenAPI only in the CI self-test of a changed contract.
async function generate(out, spec) {
  const work = mkdtempSync(join(tmpdir(), 'ductura-contracts-'));
  try {
    if (!spec) {
      const lock = parseLock(readFileSync(join(PROJECT, 'contract-lock.json'), 'utf8'));
      const response = await fetch(tarballUrl(lock.version));
      if (!response.ok) throw new Error(`download failed: ${response.status} ${tarballUrl(lock.version)}`);
      const tarball = Buffer.from(await response.arrayBuffer());
      verifyTarball(tarball, lock);
      writeFileSync(join(work, 'contracts.tgz'), tarball);
      execFileSync('tar', ['-xzf', 'contracts.tgz', 'package/openapi/v1.json'], { cwd: work });
      spec = join(work, 'package', 'openapi', 'v1.json');
    }
    execFileSync('dotnet', ['tool', 'run', 'kiota', 'generate', '--language', 'CSharp',
      '--openapi', spec, '--include-path', INCLUDE_PATH,
      '--class-name', 'DucturaApiClient', '--namespace-name', 'Ductura.Contracts',
      '--exclude-backward-compatible', '--clean-output', '--output', join(work, 'out')],
    { cwd: ROOT, stdio: ['ignore', 'ignore', 'inherit'] });
    // Kiota's lock holds a path relative to the temporary folder; its log holds only warnings.
    rmSync(join(work, 'out', 'kiota-lock.json'));
    rmSync(join(work, 'out', '.kiota.log'), { force: true });
    rmSync(out, { recursive: true, force: true });
    cpSync(join(work, 'out'), out, { recursive: true });
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
}

async function main(mode, spec) {
  // Byte comparison assumes the LF checkout and the generator output of Linux, where CI runs it.
  if (process.platform === 'win32') throw new Error('run the contract client script on Linux (CI or WSL)');
  if (mode === 'generate') return generate(GENERATED);
  if (mode !== 'check') throw new Error('usage: node scripts/contract-client.mjs generate | check [openapi.json]');
  const out = mkdtempSync(join(tmpdir(), 'ductura-generated-'));
  try {
    await generate(out, spec);
    const problems = compareTrees(readTree(GENERATED), readTree(out));
    for (const p of problems) console.error(`::error::${p} (run node scripts/contract-client.mjs generate)`);
    if (problems.length > 0) process.exitCode = 1;
    else console.log('contract client matches the locked OpenAPI');
  } finally {
    rmSync(out, { recursive: true, force: true });
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) await main(process.argv[2], process.argv[3]);
