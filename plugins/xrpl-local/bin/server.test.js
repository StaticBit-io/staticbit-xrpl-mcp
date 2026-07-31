// Unit tests for the launcher's env-handling logic in server.js.
//
// Run with: node --test plugins/xrpl-local/bin/server.test.js
//
// Scope: pure/deterministic — no process spawn, no .NET runtime, no
// filesystem writes. resolveBinaryPath() is exercised only for its pure
// path-computation branches; nothing here launches the actual server.

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const srv = require('./server.js');

test('stripEmptyOptionalOverrides removes only the five declared overrides when set to the empty string', () => {
  const source = {
    StaticBitXrplMcp__DefaultNetwork: '',
    StaticBitXrplMcp__Networks__mainnet: '',
    StaticBitXrplMcp__Networks__testnet: '',
    StaticBitXrplMcp__Networks__devnet: '',
    StaticBitXrplMcp__RequestTimeoutSeconds: '',
    // Not one of the five declared names — must survive untouched even though it
    // shares the same configuration section prefix.
    StaticBitXrplMcp__ConnectionTtlMinutes: '',
    // Some unrelated environment variable.
    PATH: '/usr/bin',
  };

  const result = srv.stripEmptyOptionalOverrides(source);

  assert.equal('StaticBitXrplMcp__DefaultNetwork' in result, false);
  assert.equal('StaticBitXrplMcp__Networks__mainnet' in result, false);
  assert.equal('StaticBitXrplMcp__Networks__testnet' in result, false);
  assert.equal('StaticBitXrplMcp__Networks__devnet' in result, false);
  assert.equal('StaticBitXrplMcp__RequestTimeoutSeconds' in result, false);
  assert.equal(result.StaticBitXrplMcp__ConnectionTtlMinutes, '');
  assert.equal(result.PATH, '/usr/bin');
  assert.equal(source.StaticBitXrplMcp__DefaultNetwork, '', 'the input object itself must not be mutated');
});

test('stripEmptyOptionalOverrides leaves a genuine (non-empty) override untouched', () => {
  const source = {
    StaticBitXrplMcp__DefaultNetwork: 'testnet',
    StaticBitXrplMcp__Networks__mainnet: 'wss://custom.example.com',
    StaticBitXrplMcp__RequestTimeoutSeconds: '45',
  };

  const result = srv.stripEmptyOptionalOverrides(source);

  assert.equal(result.StaticBitXrplMcp__DefaultNetwork, 'testnet');
  assert.equal(result.StaticBitXrplMcp__Networks__mainnet, 'wss://custom.example.com');
  assert.equal(result.StaticBitXrplMcp__RequestTimeoutSeconds, '45');
});

test('stripEmptyOptionalOverrides treats whitespace-only values as a genuine override, not empty', () => {
  // Only the exact empty string '' is the shape Claude Code produces for an unset
  // placeholder; a lone space is something a user actually typed.
  const result = srv.stripEmptyOptionalOverrides({ StaticBitXrplMcp__DefaultNetwork: ' ' });
  assert.equal(result.StaticBitXrplMcp__DefaultNetwork, ' ');
});

test("OPTIONAL_ENV_OVERRIDES matches exactly the placeholders declared in .mcp.json's env block", () => {
  // Ties the stripped name list to the actual manifest, so a future .mcp.json edit
  // that adds another `${VAR}` optional-override placeholder without updating this
  // list fails loudly here instead of silently reintroducing the same empty-string
  // bug for the new variable.
  const mcpJson = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '.mcp.json'), 'utf8'));
  const declared = Object.keys(mcpJson.mcpServers['xrpl-local'].env);

  assert.deepEqual([...srv.OPTIONAL_ENV_OVERRIDES].sort(), declared.sort());
});

test('resolveBinaryPath picks the win-x64 exe on win32', (t) => {
  // resolveBinaryPath reads os.platform()/os.arch() directly rather than taking
  // parameters, so this test only asserts the branch actually reachable in this
  // process; the other platform branches are simple enough to read by inspection
  // (see server.js) and are exercised for real by CI running on each target OS's
  // release-plugin workflow matrix.
  if (process.platform !== 'win32') {
    t.skip('only meaningful on win32');
    return;
  }
  const binPath = srv.resolveBinaryPath();
  assert.ok(binPath.endsWith(path.join('win-x64', 'StaticBit.Xrpl.Mcp.Server.exe')));
});
