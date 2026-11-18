// Tests of the package SHA-256 (DAN-1074): --verify fails when one byte of the
// package changes after the digest was written and passes on the untouched
// package, and build-test uploads the package with a checked .sha256.
// Run with node --test scripts/package-sha256.test.mjs from the repository root.
import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseDocument } from 'yaml';
import { DEFAULT_PACKAGE, parseArgs, parseLine } from './package-sha256.mjs';

const SCRIPT = fileURLToPath(new URL('package-sha256.mjs', import.meta.url));
const NAME = 'Ductura-Setup-x64.exe';
// Exit code of the script; a failure must say why on stderr.
const cli = (...args) => {
  const r = spawnSync(process.execPath, [SCRIPT, ...args], { encoding: 'utf8' });
  if (r.status !== 0) assert.match(r.stderr, r.status === 2 ? /^usage: / : /^::error::\S/);
  return r.status;
};
const dirs = [];
const tempDir = () => { dirs.push(mkdtempSync(join(tmpdir(), 'pkg-sha-'))); return dirs.at(-1); };
after(() => { for (const d of dirs) rmSync(d, { recursive: true, force: true }); });

// A fresh folder with a deterministic 1 MiB package and its written digest.
function written(bytes = Buffer.from(Array.from({ length: 1 << 20 }, (_, i) => (i * 31 + 7) & 0xff))) {
  const pkg = join(tempDir(), NAME);
  writeFileSync(pkg, bytes);
  assert.equal(cli(pkg), 0);
  return pkg;
}
const flip = (at) => (b) => { b[at(b.length)] ^= 0x01; return b; };

const CHANGES = [
  ['first byte', flip(() => 0)],
  ['middle byte', flip((n) => n >> 1)],
  ['last byte', flip((n) => n - 1)],
  ['one byte appended', (b) => Buffer.concat([b, Buffer.from([0])])],
  ['last byte removed', (b) => b.subarray(0, b.length - 1)],
  ['other content of the same length', (b) => Buffer.alloc(b.length, 0x5a)],
];
test('--verify fails on a well-formed .sha256 whose last hex digit differs', () => {
  const pkg = written();
  const line = readFileSync(`${pkg}.sha256`, 'utf8');
  writeFileSync(`${pkg}.sha256`, `${line.slice(0, 63)}${line[63] === '0' ? '1' : '0'}${line.slice(64)}`);
  assert.equal(cli('--verify', pkg), 1);
});
for (const [name, change] of CHANGES) {
  test(`--verify fails when the package changes after the digest: ${name}`, () => {
    const pkg = written();
    assert.equal(cli('--verify', pkg), 0);
    writeFileSync(pkg, change(readFileSync(pkg)));
    assert.equal(cli('--verify', pkg), 1);
  });
}

const HEX = createHash('sha256').update(Buffer.alloc(0)).digest('hex');
const BAD_LINES = [
  ['upper-case hex', `${HEX.toUpperCase()}  ${NAME}\n`],
  ['63 hex', `${HEX.slice(1)}  ${NAME}\n`],
  ['65 hex', `${HEX}0  ${NAME}\n`],
  ['one space', `${HEX} ${NAME}\n`],
  ['three spaces', `${HEX}   ${NAME}\n`],
  ['tab', `${HEX}\t${NAME}\n`],
  ['NBSP', `${HEX}  ${NAME}\n`],
  ['DEL', `${HEX}  ${NAME}\u007f\n`],
  ['NUL in the hex', `${HEX.slice(1)}\0  ${NAME}\n`],
  ['NUL in the name', `${HEX}  ${NAME}\0\n`],
  ['BOM', `﻿${HEX}  ${NAME}\n`],
  ['CRLF', `${HEX}  ${NAME}\r\n`],
  ['lone CR at the end', `${HEX}  ${NAME}\r`],
  ['lone CR inside', `${HEX}  Ductura\r-Setup-x64.exe\n`],
  ['no final LF', `${HEX}  ${NAME}`],
  ['two LF', `${HEX}  ${NAME}\n\n`],
  ['second line', `${HEX}  ${NAME}\n${HEX}  ${NAME}\n`],
  ['trailing space', `${HEX}  ${NAME} \n`],
  ['binary marker', `${HEX} *${NAME}\n`],
  ['GNU escaped line', `\\${HEX}  ${NAME}\n`],
  ['name with a folder', `${HEX}  dist/${NAME}\n`],
  ['Windows path', `${HEX}  dist\\${NAME}\n`],
  ['parent folder', `${HEX}  ../${NAME}\n`],
  ['other letter case', `${HEX}  ductura-setup-x64.exe\n`],
  ['other name', `${HEX}  Ductura-Setup-arm64.exe\n`],
  ['empty file', ''],
  ['invalid UTF-8', Buffer.concat([Buffer.from(`${HEX}  `), Buffer.from([0xc3, 0x28]), Buffer.from('\n')])],
];
// The parser itself refuses each line, not only the digest comparison after it.
const REASON = { 'invalid UTF-8': /not UTF-8/, 'three spaces': /names/, 'name with a folder': /names/, 'Windows path': /names/, 'parent folder': /names/, 'other letter case': /names/, 'other name': /names/, 'NUL in the name': /names/, 'DEL': /names/, 'trailing space': /names/ };
for (const [name, line] of BAD_LINES) {
  test(`--verify refuses the .sha256 of an empty package: ${name}`, () => {
    assert.throws(() => parseLine(Buffer.from(line), NAME), REASON[name] ?? /not one line/);
    const pkg = written(Buffer.alloc(0));
    writeFileSync(`${pkg}.sha256`, line);
    assert.equal(cli('--verify', pkg), 1);
  });
}

test('--verify fails without the .sha256, without the package, or on a folder', () => {
  const dir = tempDir();
  const pkg = join(dir, NAME);
  writeFileSync(pkg, 'x');
  assert.equal(cli('--verify', pkg), 1);
  mkdirSync(`${pkg}.sha256`);
  assert.equal(cli('--verify', pkg), 1);
  assert.equal(cli('--verify', join(dir, 'missing.exe')), 1);
  mkdirSync(join(dir, 'folder.exe'));
  assert.equal(cli(join(dir, 'folder.exe')), 1);
  assert.equal(cli('--verify', join(dir, 'folder.exe')), 1);
  assert.equal(cli(join(dir, 'missing.exe')), 1);
  assert.throws(() => readFileSync(join(dir, 'missing.exe.sha256')));
});

test('writing refuses a name that --verify could not read back', () => {
  const dir = tempDir();
  for (const name of ['a\u2028b.exe', 'a\u2029b.exe', 'a\nb.exe', 'a\rb.exe', 'a.exe\r']) {
    const pkg = join(dir, name);
    writeFileSync(pkg, 'x');
    assert.equal(cli(pkg), 1, JSON.stringify(name));
    assert.throws(() => readFileSync(`${pkg}.sha256`), JSON.stringify(name));
  }
  const fine = join(dir, 'Ductura Setup šđčćž.exe');
  writeFileSync(fine, 'x');
  assert.equal(cli(fine), 0);
  assert.equal(cli('--verify', fine), 0);
});

// A valid .sha256 stays, so only the check of the package itself can fail.
test('--verify fails when the package is gone or a folder next to a valid .sha256', () => {
  const gone = written();
  rmSync(gone);
  assert.equal(cli('--verify', gone), 1);
  const folder = written(Buffer.alloc(0));
  rmSync(folder);
  mkdirSync(folder);
  assert.equal(cli('--verify', folder), 1);
});

test('wrong arguments exit with 2', () => {
  for (const args of [['--check'], ['-v'], ['a.exe', 'b.exe'], ['--verify', '--verify'], ['--verify', 'a', 'b'], ['']]) {
    assert.equal(parseArgs(args), null, JSON.stringify(args));
    assert.equal(cli(...args), 2, JSON.stringify(args));
  }
  assert.deepEqual(parseArgs([]), { verify: false, file: DEFAULT_PACKAGE });
  assert.deepEqual(parseArgs(['--verify']), { verify: true, file: DEFAULT_PACKAGE });
  assert.deepEqual(parseArgs(['x.exe', '--verify']), { verify: true, file: 'x.exe' });
});

test('the written line is the sha256sum form of the bytes and --verify passes on the untouched package', () => {
  for (const bytes of [undefined, Buffer.alloc(0), Buffer.from('abc')]) {
    const pkg = written(bytes);
    const hex = createHash('sha256').update(readFileSync(pkg)).digest('hex');
    assert.equal(readFileSync(`${pkg}.sha256`, 'utf8'), `${hex}  ${NAME}\n`);
    assert.equal(cli('--verify', pkg), 0);
  }
  const abc = readFileSync(`${written(Buffer.from('abc'))}.sha256`, 'utf8');
  assert.equal(abc.slice(0, 64), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
});

test('writing again replaces a stale .sha256', () => {
  const pkg = written();
  writeFileSync(pkg, 'new');
  assert.equal(cli('--verify', pkg), 1);
  assert.equal(cli(pkg), 0);
  assert.equal(cli('--verify', pkg), 0);
});

// build-test of ci.yml, read with a real YAML parser like ci-ok.test.mjs.
const ciText = readFileSync(new URL('../.github/workflows/ci.yml', import.meta.url), 'utf8');
const load = () => {
  const doc = parseDocument(ciText, { uniqueKeys: true, merge: true });
  assert.equal(doc.errors.length, 0);
  return doc.toJS({ maxAliasCount: 100 });
};
const PKG = 'dist/Ductura-Setup-x64.exe';
const ISCC = '& "C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe" /DSourceDir=..\\publish installer\\Ductura.iss';
const UPLOAD = 'actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02';
const NODE_TEST = 'node --test scripts/ci-ok.test.mjs scripts/contract-client.test.mjs scripts/package-sha256.test.mjs';
const lines = (s) => (typeof s === 'string' ? s.split('\n').map((l) => l.trim()).filter(Boolean) : []);
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

// Problems of the package steps in a parsed workflow; empty when the package is
// built, hashed, installed, checked twice and uploaded with its .sha256 last.
function packageProblems(wf) {
  const steps = wf.jobs['build-test'].steps;
  const find = (what, pred) => {
    const at = steps.flatMap((s, i) => (pred(s) ? [i] : []));
    return at.length === 1 ? at[0] : `${what}: ${at.length} steps`;
  };
  const order = [
    find('Inno Setup', (s) => same(lines(s.run), [ISCC])),
    find('write', (s) => same(lines(s.run), ['node --version', `node scripts/package-sha256.mjs ${PKG}`])),
    find('functional test', (s) => same(lines(s.run), ['./tests/functional/Install-And-Launch.ps1'])),
    find('verify', (s) => same(lines(s.run), [`node scripts/package-sha256.mjs --verify ${PKG}`])),
    find('sha256sum', (s) => same(lines(s.run), ['cd dist && sha256sum -c Ductura-Setup-x64.exe.sha256'])),
    find('upload', (s) => s.uses === UPLOAD),
  ];
  const problems = order.filter((i) => typeof i === 'string');
  if (problems.length > 0) return problems;
  // Inno Setup, write, functional test, verify, sha256sum and upload are
  // adjacent and last, so nothing can change or rehash the package in between.
  if (!order.every((i, k) => k === 0 || i === order[k - 1] + 1) || order.at(-1) !== steps.length - 1) problems.push(`steps ${order} are not adjacent and last`);
  const [, write, , verify, sum, upload] = order;
  const job = wf.jobs['build-test'];
  if (Object.hasOwn(wf, 'defaults')) problems.push('workflow has defaults');
  for (const key of Object.keys(job)) if (!['runs-on', 'steps'].includes(key)) problems.push(`build-test has ${key}`);
  if (job['runs-on'] !== 'windows-latest') problems.push('build-test does not run on windows-latest');
  // No step of the job may be skipped, allowed to fail or moved elsewhere
  // (if, continue-on-error, working-directory, env, ...); only sha256sum sets a shell.
  steps.forEach((step, i) => {
    for (const key of Object.keys(step)) if (!['name', 'uses', 'with', 'run', ...(i === sum ? ['shell'] : [])].includes(key)) problems.push(`step ${i} has ${key}`);
  });
  if (steps[sum].shell !== 'bash') problems.push('sha256sum step does not run in bash');
  const w = steps[upload].with ?? {};
  if (w.name !== 'Ductura-Setup-x64' || !same(lines(w.path).sort(), [PKG, `${PKG}.sha256`])) problems.push('upload is not the package and its .sha256');
  if (!Object.keys(steps[upload]).every((k) => ['uses', 'with'].includes(k))) problems.push('upload step has more than uses and with');
  const tests = wf.jobs['ci-ok-test'].steps.at(-1);
  if (!same(Object.keys(tests ?? {}), ['name', 'run']) || !same(lines(tests.run), [NODE_TEST])) problems.push('ci-ok-test does not run this test as its last step');
  return problems;
}

test('build-test writes the digest after Inno Setup, checks it twice and uploads it with the package', () => {
  assert.deepEqual(packageProblems(load()), []);
});

const BROKEN = [
  ['write step removed', (s) => s.splice(s.findIndex((x) => lines(x.run).includes(`node scripts/package-sha256.mjs ${PKG}`)), 1)],
  ['verify step removed', (s) => s.splice(s.findIndex((x) => String(x.run).includes('--verify')), 1)],
  ['sha256sum step removed', (s) => s.splice(s.findIndex((x) => String(x.run).includes('sha256sum')), 1)],
  ['verify before the functional test', (s) => {
    const v = s.splice(s.findIndex((x) => String(x.run).includes('--verify')), 1)[0];
    s.splice(s.findIndex((x) => String(x.run).includes('Install-And-Launch')), 0, v);
  }],
  ['upload without the .sha256', (s) => { s.at(-1).with.path = PKG; }],
  ['upload with a glob', (s) => { s.at(-1).with.path = 'dist/*'; }],
  ['continue-on-error on verify', (s) => { s.find((x) => String(x.run).includes('--verify'))['continue-on-error'] = true; }],
  ['if on the sha256sum step', (s) => { s.find((x) => String(x.run).includes('sha256sum')).if = 'false'; }],
  ['exit code swallowed', (s) => { s.find((x) => String(x.run).includes('--verify')).run += '\nexit 0'; }],
  ['a step after the upload', (s) => { s.push({ run: 'echo' }); }],
  ['a rehash after the functional test', (s) => { s.splice(s.findIndex((x) => String(x.run).includes('--verify')), 0, { run: `node scripts\\package-sha256.mjs ${PKG}` }); }],
  ['a step between Inno Setup and the write', (s) => { s.splice(s.findIndex((x) => String(x.run).includes('node --version')), 0, { run: 'echo' }); }],
  ['upload by tag', (s) => { s.at(-1).uses = 'actions/upload-artifact@v4'; }],
  ['workflow defaults', (s, wf) => { wf.defaults = { run: { shell: 'pwsh -command ". \'{0}\'; exit 0"' } }; }],
  ['job defaults', (s, wf) => { wf.jobs['build-test'].defaults = { run: { shell: 'bash' } }; }],
  ['job continue-on-error', (s, wf) => { wf.jobs['build-test']['continue-on-error'] = true; }],
  ['job on Linux', (s, wf) => { wf.jobs['build-test']['runs-on'] = 'ubuntu-latest'; }],
  ['ci-ok-test swallows the result', (s, wf) => { wf.jobs['ci-ok-test'].steps.at(-1).run = `${NODE_TEST} || true`; }],
  ['ci-ok-test continue-on-error', (s, wf) => { wf.jobs['ci-ok-test'].steps.at(-1)['continue-on-error'] = true; }],
  ['if on the functional test', (s) => { s.find((x) => String(x.run).includes('Install-And-Launch')).if = '${{ false }}'; }],
  ['continue-on-error on Inno Setup', (s) => { s.find((x) => String(x.run).includes('ISCC.exe'))['continue-on-error'] = true; }],
  ['if on the .NET tests', (s) => { s.find((x) => String(x.run).includes('dotnet test')).if = 'false'; }],
];
test('the package step check has 22 broken workflows', () => assert.equal(BROKEN.length, 22));
for (const [name, mutate] of BROKEN) {
  test(`package step check fails: ${name}`, () => {
    const wf = load();
    mutate(wf.jobs['build-test'].steps, wf);
    assert.notDeepEqual(packageProblems(wf), []);
  });
}
