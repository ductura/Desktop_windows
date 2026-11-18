// Tests of the contract client check (DAN-958): the lock file names exactly one
// release tarball of @ductura/contracts and its sha256, and the committed
// generated code must equal a fresh generation byte for byte.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { compareTrees, parseLock, readTree, tarballUrl, verifyTarball } from './contract-client.mjs';

const SHA = '5594110cfcf6ed1d9ec6262803f2246f8b1098b794c9bf2d5bb02fbbc6157e70';
const lock = (o) => JSON.stringify({ version: '2.2.0', sha256: SHA, ...o });

const BAD_LOCKS = [
  ['sha256 in upper case', lock({ sha256: SHA.toUpperCase() })],
  ['sha256 of 63 characters', lock({ sha256: SHA.slice(1) })],
  ['sha256 of 65 characters', lock({ sha256: `${SHA}0` })],
  ['sha256 with LF', lock({ sha256: `${SHA}\n` })],
  ['sha256 with CRLF', lock({ sha256: `${SHA}\r\n` })],
  ['sha256 with tab', lock({ sha256: `${SHA}\t` })],
  ['sha256 with NBSP', lock({ sha256: `${SHA} ` })],
  ['sha256 with DEL', lock({ sha256: `${SHA}\u007f` })],
  ['version with LF', lock({ version: '2.2.0\n' })],
  ['version with a leading v', lock({ version: 'v2.2.0' })],
  ['version with a leading zero', lock({ version: '02.2.0' })],
  ['prerelease version', lock({ version: '2.2.0-rc.1' })],
  ['version of two parts', lock({ version: '2.2' })],
  ['version path', lock({ version: '2.2.0/../../x' })],
  ['version as a number', lock({ version: 2.2 })],
  ['empty version', lock({ version: '' })],
  ['extra key url', lock({ url: 'https://example.org/x.tgz' })],
  ['__proto__ key from JSON.parse', lock({}).replace('{', '{"__proto__":{"x":1},')],
  ['missing sha256', JSON.stringify({ version: '2.2.0' })],
  ['toString key', lock({ toString: SHA })],
  ['constructor key', lock({ constructor: SHA })],
  ['array', `[${lock({})}]`],
  ['null', 'null'],
  ['not JSON', '{version'],
];

test('inherited keys are not lock fields', () => {
  const inherited = Object.create({ version: '2.2.0', sha256: SHA });
  assert.throws(() => parseLock(JSON.stringify(inherited)));
});

for (const [name, text] of BAD_LOCKS) {
  test(`lock is refused: ${name}`, () => assert.throws(() => parseLock(text)));
}

test('lock of 2.2.0 is accepted', () => {
  assert.deepEqual(parseLock(lock({})), { version: '2.2.0', sha256: SHA });
  assert.equal(tarballUrl('2.2.0'), 'https://github.com/ductura/Institution_server/releases/download/contracts-v2.2.0/ductura-contracts-2.2.0.tgz');
});

test('tarball with another sha256 is refused, the locked one passes', () => {
  const bytes = Buffer.from('tarball');
  const sha256 = createHash('sha256').update(bytes).digest('hex');
  assert.throws(() => verifyTarball(Buffer.from('tarbalL'), { sha256 }));
  assert.throws(() => verifyTarball(Buffer.alloc(0), { sha256 }));
  assert.doesNotThrow(() => verifyTarball(bytes, { sha256 }));
});

const tree = (o) => new Map(Object.entries(o).map(([k, v]) => [k, Buffer.from(v)]));
const H = 'Api/V1/Health/HealthRequestBuilder.cs';
const BASE = { 'Client.cs': 'a b\nc\n', [H]: 'x\n' };

const DIVERGENT = [
  ['one changed byte', { ...BASE, 'Client.cs': 'a b\nd\n' }],
  ['CRLF instead of LF', { ...BASE, 'Client.cs': 'a b\r\nc\r\n' }],
  ['NBSP instead of a space', { ...BASE, 'Client.cs': 'a\u00a0b\nc\n' }],
  ['added tab', { ...BASE, 'Client.cs': 'a b\nc\t\n' }],
  ['added DEL', { ...BASE, 'Client.cs': 'a b\nc\u007f\n' }],
  ['missing file', { 'Client.cs': BASE['Client.cs'] }],
  ['extra file in a subfolder', { ...BASE, 'Api/V1/Extra.cs': '' }],
  ['name differs only in case', { 'Client.cs': BASE['Client.cs'], [H.toLowerCase()]: 'x\n' }],
  ['empty tree', {}],
];

test('both trees empty is not a pass (nothing generated)', () => {
  assert.notDeepEqual(compareTrees(tree({}), tree({})), []);
});

for (const [name, actual] of DIVERGENT) {
  test(`trees diverge: ${name}`, () => {
    assert.notDeepEqual(compareTrees(tree(BASE), tree(actual)), []);
  });
}

test('equal trees pass', () => assert.deepEqual(compareTrees(tree(BASE), tree({ ...BASE })), []));

test('readTree reads nested files with / separators', () => {
  const dir = mkdtempSync(join(tmpdir(), 'contract-client-'));
  mkdirSync(join(dir, 'Api', 'V1', 'Health'), { recursive: true });
  writeFileSync(join(dir, 'Client.cs'), BASE['Client.cs']);
  writeFileSync(join(dir, ...H.split('/')), BASE[H]);
  assert.deepEqual(compareTrees(tree(BASE), readTree(dir)), []);
  symlinkSync(join(dir, 'Client.cs'), join(dir, 'Link.cs'));
  assert.throws(() => readTree(dir), /not a regular file: Link.cs/);
});
