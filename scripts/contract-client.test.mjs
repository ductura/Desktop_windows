// Tests of the contract client check (DAN-958): the lock file names exactly one
// release tarball of @ductura/contracts and its sha256, and the committed
// generated code must equal a fresh generation byte for byte.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { compareTrees, parseLock, readTree, verifyTarball } from './contract-client.mjs';

const SHA = '5594110cfcf6ed1d9ec6262803f2246f8b1098b794c9bf2d5bb02fbbc6157e70';
const URL_220 = 'https://github.com/ductura/server/releases/download/contracts-v2.2.0/ductura-contracts-2.2.0.tgz';
const lock = (o) => JSON.stringify({ version: '2.2.0', url: URL_220, sha256: SHA, ...o });

const BAD_LOCKS = [
  ['sha256 in upper case', lock({ sha256: SHA.toUpperCase() })],
  ['sha256 of 63 characters', lock({ sha256: SHA.slice(1) })],
  ['sha256 of 65 characters', lock({ sha256: `${SHA}0` })],
  ['sha256 with LF', lock({ sha256: `${SHA}\n` })],
  ['sha256 with CRLF', lock({ sha256: `${SHA}\r\n` })],
  ['sha256 with tab', lock({ sha256: `${SHA}\t` })],
  ['sha256 with NBSP', lock({ sha256: `${SHA}\u00a0` })],
  ['sha256 with DEL', lock({ sha256: `${SHA}\u007f` })],
  ['version with LF', lock({ version: '2.2.0\n', url: URL_220.replaceAll('2.2.0', '2.2.0\n') })],
  ['version with a leading zero', lock({ version: '2.02.0', url: URL_220.replaceAll('2.2.0', '2.02.0') })],
  ['version not a number', lock({ version: 'latest', url: URL_220.replaceAll('2.2.0', 'latest') })],
  ['url of another version', lock({ url: URL_220.replaceAll('2.2.0', '2.1.1') })],
  ['url of another host', lock({ url: URL_220.replace('github.com', 'example.org') })],
  ['url of another repository', lock({ url: URL_220.replace('ductura/server', 'ductura/web') })],
  ['http url', lock({ url: URL_220.replace('https:', 'http:') })],
  ['url with a query', lock({ url: `${URL_220}?x=1` })],
  ['url with a trailing tab', lock({ url: `${URL_220}\t` })],
  ['Windows path', lock({ url: 'C:\\contracts\\ductura-contracts-2.2.0.tgz' })],
  ['extra key', lock({ openapi: 'x' })],
  ['__proto__ key from JSON.parse', lock({}).replace('{', '{"__proto__":{"x":1},')],
  ['missing sha256', JSON.stringify({ version: '2.2.0', url: URL_220 })],
  ['inherited toString is not a field', JSON.stringify({ version: '2.2.0', url: URL_220, toString: SHA })],
  ['constructor key', lock({ constructor: SHA })],
  ['array', `[${lock({})}]`],
  ['null', 'null'],
  ['not JSON', '{version'],
];

for (const [name, text] of BAD_LOCKS) {
  test(`lock is refused: ${name}`, () => assert.throws(() => parseLock(text)));
}

test('lock of 2.2.0 is accepted', () => {
  assert.deepEqual(parseLock(lock({})), { version: '2.2.0', url: URL_220, sha256: SHA });
});

test('tarball with another sha256 is refused, the locked one passes', () => {
  const bytes = Buffer.from('tarball');
  const sha256 = createHash('sha256').update(bytes).digest('hex');
  assert.throws(() => verifyTarball(Buffer.from('tarbalL'), { sha256 }));
  assert.throws(() => verifyTarball(Buffer.alloc(0), { sha256 }));
  assert.doesNotThrow(() => verifyTarball(bytes, { sha256 }));
});

const tree = (o) => new Map(Object.entries(o).map(([k, v]) => [k, Buffer.from(v)]));
const BASE = { 'Client.cs': 'a b\nc\n', 'Api/V1/Health.cs': 'x\n' };

const DIVERGENT = [
  ['one changed byte', { ...BASE, 'Client.cs': 'a b\nd\n' }],
  ['CRLF instead of LF', { ...BASE, 'Client.cs': 'a b\r\nc\r\n' }],
  ['NBSP instead of a space', { ...BASE, 'Client.cs': 'a\u00a0b\nc\n' }],
  ['added tab', { ...BASE, 'Client.cs': 'a b\nc\t\n' }],
  ['added DEL', { ...BASE, 'Client.cs': 'a b\nc\u007f\n' }],
  ['missing file', { 'Client.cs': BASE['Client.cs'] }],
  ['extra file in a subfolder', { ...BASE, 'Api/V1/Extra.cs': '' }],
  ['name differs only in case', { 'Client.cs': BASE['Client.cs'], 'Api/V1/health.cs': 'x\n' }],
  ['empty tree', {}],
];

for (const [name, actual] of DIVERGENT) {
  test(`trees diverge: ${name}`, () => {
    assert.notDeepEqual(compareTrees(tree(BASE), tree(actual)), []);
  });
}

test('equal trees pass', () => assert.deepEqual(compareTrees(tree(BASE), tree({ ...BASE })), []));

test('readTree reads nested files with / separators', () => {
  const dir = mkdtempSync(join(tmpdir(), 'contract-client-'));
  mkdirSync(join(dir, 'Api', 'V1'), { recursive: true });
  writeFileSync(join(dir, 'Client.cs'), BASE['Client.cs']);
  writeFileSync(join(dir, 'Api', 'V1', 'Health.cs'), BASE['Api/V1/Health.cs']);
  assert.deepEqual(compareTrees(tree(BASE), readTree(dir)), []);
});
