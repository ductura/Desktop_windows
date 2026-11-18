// Tests of the package SHA-256 (DAN-1074): --verify fails when one byte of the
// package changes after the digest was written and passes on the untouched
// package, and build-test uploads the package with a checked .sha256.
// Run with node --test scripts/package-sha256.test.mjs from the repository root.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseDocument } from 'yaml';
import { DEFAULT_PACKAGE, parseArgs } from './package-sha256.mjs';

const SCRIPT = fileURLToPath(new URL('package-sha256.mjs', import.meta.url));
const NAME = 'Ductura-Setup-x64.exe';
const cli = (...args) => spawnSync(process.execPath, [SCRIPT, ...args], { encoding: 'utf8' }).status;

// A fresh folder with a deterministic 1 MiB package and its written digest.
function written(bytes = Buffer.from(Array.from({ length: 1 << 20 }, (_, i) => (i * 31 + 7) & 0xff))) {
  const pkg = join(mkdtempSync(join(tmpdir(), 'pkg-sha-')), NAME);
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
for (const [name, line] of BAD_LINES) {
  test(`--verify refuses the .sha256 of an empty package: ${name}`, () => {
    const pkg = written(Buffer.alloc(0));
    writeFileSync(`${pkg}.sha256`, line);
    assert.equal(cli('--verify', pkg), 1);
  });
}

test('--verify fails without the .sha256, without the package, or on a folder', () => {
  const dir = mkdtempSync(join(tmpdir(), 'pkg-sha-'));
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
    find('Inno Setup', (s) => typeof s.run === 'string' && s.run.includes('ISCC.exe')),
    find('write', (s) => same(lines(s.run), ['node --version', `node scripts/package-sha256.mjs ${PKG}`])),
    find('functional test', (s) => typeof s.run === 'string' && s.run.includes('Install-And-Launch.ps1')),
    find('verify', (s) => same(lines(s.run), [`node scripts/package-sha256.mjs --verify ${PKG}`])),
    find('sha256sum', (s) => same(lines(s.run), ['cd dist && sha256sum -c Ductura-Setup-x64.exe.sha256'])),
    find('upload', (s) => typeof s.uses === 'string' && s.uses.startsWith('actions/upload-artifact@')),
  ];
  const problems = order.filter((i) => typeof i === 'string');
  if (problems.length > 0) return problems;
  if (!order.every((i, k) => k === 0 || i > order[k - 1])) problems.push(`order ${order}`);
  const [, write, , verify, sum, upload] = order;
  if (sum !== verify + 1 || upload !== sum + 1 || upload !== steps.length - 1) problems.push('verify, sha256sum and upload are not the last three steps');
  for (const i of [write, verify, sum]) {
    for (const key of Object.keys(steps[i])) if (!['name', 'run', ...(i === sum ? ['shell'] : [])].includes(key)) problems.push(`step ${i} has ${key}`);
  }
  if (steps[sum].shell !== 'bash') problems.push('sha256sum step does not run in bash');
  const w = steps[upload].with ?? {};
  if (w.name !== 'Ductura-Setup-x64' || !same(lines(w.path).sort(), [PKG, `${PKG}.sha256`])) problems.push('upload is not the package and its .sha256');
  if (!Object.keys(steps[upload]).every((k) => ['uses', 'with'].includes(k))) problems.push('upload step has more than uses and with');
  if (!lines(wf.jobs['ci-ok-test'].steps.at(-1)?.run).some((l) => l.startsWith('node --test ') && l.includes('scripts/package-sha256.test.mjs'))) problems.push('ci-ok-test does not run this test');
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
];
for (const [name, mutate] of BROKEN) {
  test(`package step check fails: ${name}`, () => {
    const wf = load();
    mutate(wf.jobs['build-test'].steps);
    assert.notDeepEqual(packageProblems(wf), []);
  });
}
