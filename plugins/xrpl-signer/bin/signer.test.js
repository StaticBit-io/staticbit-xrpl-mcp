// Unit tests for the launcher's env-handling logic in signer.js.
//
// Run with: node --test plugins/xrpl-signer/bin/signer.test.js
//
// Scope: pure/deterministic — no process spawn, no .NET runtime, no
// filesystem writes. resolveBinaryPath() is exercised only for its pure
// path-computation branches; nothing here launches the actual signer.

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const signer = require('./signer.js');

test('stripEmptyOptionalOverrides removes only the three declared overrides when set to the empty string', () => {
  const source = {
    XRPL_SIGNER_PASSPHRASE: '',
    XRPL_SIGNER_PASSPHRASE_FILE: '',
    XRPL_SIGNER_KEYSTORE_PATH: '',
    // Not one of the three declared names — must survive untouched even though it
    // shares the same XRPL_SIGNER_ prefix.
    XRPL_SIGNER_AUDIT_LOG: '',
    PATH: '/usr/bin',
  };

  const result = signer.stripEmptyOptionalOverrides(source);

  assert.equal('XRPL_SIGNER_PASSPHRASE' in result, false);
  assert.equal('XRPL_SIGNER_PASSPHRASE_FILE' in result, false);
  assert.equal('XRPL_SIGNER_KEYSTORE_PATH' in result, false);
  assert.equal(result.XRPL_SIGNER_AUDIT_LOG, '');
  assert.equal(result.PATH, '/usr/bin');
  assert.equal(source.XRPL_SIGNER_PASSPHRASE, '', 'the input object itself must not be mutated');
});

test('stripEmptyOptionalOverrides leaves a genuine (non-empty) override untouched', () => {
  const source = {
    XRPL_SIGNER_PASSPHRASE: 'correct horse battery staple',
    XRPL_SIGNER_KEYSTORE_PATH: 'C:\\custom\\keystore.json',
  };

  const result = signer.stripEmptyOptionalOverrides(source);

  assert.equal(result.XRPL_SIGNER_PASSPHRASE, 'correct horse battery staple');
  assert.equal(result.XRPL_SIGNER_KEYSTORE_PATH, 'C:\\custom\\keystore.json');
});

test('isUnsetOverrideValue recognizes undefined, "", and the literal unexpanded placeholder text', () => {
  assert.equal(signer.isUnsetOverrideValue(undefined), true);
  assert.equal(signer.isUnsetOverrideValue(''), true);
  assert.equal(signer.isUnsetOverrideValue('${XRPL_SIGNER_KEYSTORE_PATH}'), true);
  assert.equal(signer.isUnsetOverrideValue('${SOME_VAR}'), true, 'the inner name need not match the outer key');

  assert.equal(signer.isUnsetOverrideValue('correct horse battery staple'), false);
  assert.equal(signer.isUnsetOverrideValue('C:\\custom\\keystore.json'), false);
  assert.equal(signer.isUnsetOverrideValue(' '), false, 'whitespace is not the empty string');
  assert.equal(signer.isUnsetOverrideValue('prefix-${FOO}-suffix'), false);
});

test('stripEmptyOptionalOverrides removes the three declared overrides when set to the literal unexpanded placeholder text', () => {
  // Confirmed by direct reproduction: with XRPL_SIGNER_KEYSTORE_PATH set to its own literal
  // placeholder text, the signer connects (unlike xrpl-local's crash) but logs "Keystore at
  // ${XRPL_SIGNER_KEYSTORE_PATH}, wallets=0, audit=disabled." — the unexpanded text used
  // verbatim as a relative filesystem path instead of falling back to the documented default.
  const source = {
    XRPL_SIGNER_PASSPHRASE: '${XRPL_SIGNER_PASSPHRASE}',
    XRPL_SIGNER_PASSPHRASE_FILE: '${XRPL_SIGNER_PASSPHRASE_FILE}',
    XRPL_SIGNER_KEYSTORE_PATH: '${XRPL_SIGNER_KEYSTORE_PATH}',
    // Not one of the three declared names — must survive untouched even though it looks like
    // the same shape.
    XRPL_SIGNER_AUDIT_LOG: '${XRPL_SIGNER_AUDIT_LOG}',
    PATH: '/usr/bin',
  };

  const result = signer.stripEmptyOptionalOverrides(source);

  assert.equal('XRPL_SIGNER_PASSPHRASE' in result, false);
  assert.equal('XRPL_SIGNER_PASSPHRASE_FILE' in result, false);
  assert.equal('XRPL_SIGNER_KEYSTORE_PATH' in result, false);
  assert.equal(result.XRPL_SIGNER_AUDIT_LOG, '${XRPL_SIGNER_AUDIT_LOG}');
  assert.equal(result.PATH, '/usr/bin');
});

test("OPTIONAL_ENV_OVERRIDES matches exactly the placeholders declared in .mcp.json's env block", () => {
  const mcpJson = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '.mcp.json'), 'utf8'));
  const declared = Object.keys(mcpJson.mcpServers['xrpl-signer'].env);

  assert.deepEqual([...signer.OPTIONAL_ENV_OVERRIDES].sort(), declared.sort());
});

test('resolveBinaryPath picks the win-x64 exe on win32', (t) => {
  if (process.platform !== 'win32') {
    t.skip('only meaningful on win32');
    return;
  }
  const binPath = signer.resolveBinaryPath();
  assert.ok(binPath.endsWith(path.join('win-x64', 'StaticBit.Xrpl.Mcp.Signer.exe')));
});
