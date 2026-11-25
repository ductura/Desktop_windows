// DAN-1549: every action in the workflows is pinned to a full commit SHA, the
// Inno Setup version is fixed, and Dependabot covers Actions and NuGet. Run with
// node --test scripts/ci-pins.test.mjs from the repository root.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { parseDocument } from 'yaml';

const root = fileURLToPath(new URL('..', import.meta.url));
const dir = `${root}.github/workflows/`;
const files = readdirSync(dir).filter((f) => /\.ya?ml$/.test(f));

function uses(text) {
  return [...text.matchAll(/^\s*(?:-\s*)?uses:\s*(\S+)/gm)].map((m) => m[1]);
}

test('there are workflow files', () => assert.ok(files.length > 0));

for (const f of files) {
  test(`${f}: every uses: is pinned to a 40-character SHA`, () => {
    for (const ref of uses(readFileSync(dir + f, 'utf8'))) {
      if (ref.startsWith('./')) continue;
      assert.match(ref, /^[\w.-]+\/[\w./-]+@[0-9a-f]{40}$/, `not pinned: ${ref}`);
    }
  });
}

test('Inno Setup is installed with a fixed version', () => {
  const ci = readFileSync(`${dir}ci.yml`, 'utf8');
  const installs = ci.split('\n').filter((l) => /choco install/.test(l));
  assert.ok(installs.length > 0);
  for (const l of installs) assert.match(l, /--version \d+\.\d+\.\d+\b/, `unpinned: ${l.trim()}`);
});

test('dependabot.yml covers github-actions and nuget on a schedule', () => {
  const doc = parseDocument(readFileSync(`${root}.github/dependabot.yml`, 'utf8'));
  assert.equal(doc.errors.length, 0);
  const updates = doc.toJS().updates;
  for (const eco of ['github-actions', 'nuget']) {
    const u = updates.find((x) => x['package-ecosystem'] === eco);
    assert.ok(u, `missing ${eco}`);
    assert.ok(u.schedule?.interval, `${eco} has no schedule`);
  }
  const nuget = updates.find((x) => x['package-ecosystem'] === 'nuget');
  assert.ok((nuget.directories ?? [nuget.directory]).includes('/src/Ductura.Core'));
});
