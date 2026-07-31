#!/usr/bin/env node
// Cross-platform launcher for the XRPL offline signer. Picks the right
// self-contained .NET binary by os.platform() / os.arch() and execs it,
// inheriting stdin/stdout so the MCP protocol stream passes through.
//
// Layout:
//   bin/signer.js          ← this file
//   bin/<rid>/StaticBit.Xrpl.Mcp.Signer[.exe]

const path = require('node:path');
const os = require('node:os');
const fs = require('node:fs');
const { spawn } = require('node:child_process');

const binDir = __dirname;

/** Matches the literal text Claude Code passes through, verbatim, as the value of a
 * declared `${VAR}` placeholder in `.mcp.json`'s `env` block when the backing variable is
 * not set anywhere in the host environment — e.g. `${XRPL_SIGNER_KEYSTORE_PATH}`, not an
 * empty string and not an absent key. Confirmed from this launcher's own MCP connection
 * logs: unlike xrpl-local, this one does not crash on it (see OPTIONAL_ENV_OVERRIDES doc
 * below for why), but its own startup line logs the literal placeholder text back —
 * "Keystore at ${XRPL_SIGNER_KEYSTORE_PATH}, wallets=0, audit=disabled." — proving the
 * unexpanded placeholder reaches SignerOptions unchanged. Anchored to match the *entire*
 * value, not merely contain it, so a legitimate value that happens to contain the
 * substring `${` somewhere in the middle (a path, say) is left alone. None of these three
 * settings could ever legitimately equal the literal placeholder syntax for a shell
 * variable in its entirety, so a whole-string match is safe to treat as "unset". */
const UNSET_PLACEHOLDER_RE = /^\$\{[A-Za-z_][A-Za-z0-9_]*\}$/;

/** True when `value` is the shape Claude Code produces for an unset `${VAR}` placeholder:
 * absent, the empty string, or the literal unexpanded placeholder text itself. */
function isUnsetOverrideValue(value) {
  return value === undefined || value === '' || UNSET_PLACEHOLDER_RE.test(value);
}

/** The exact optional overrides `.mcp.json`'s `env` block declares via `${VAR}`
 * placeholders. When the backing variable is not set anywhere in the host environment,
 * Claude Code has been observed producing *two* different shapes for the substituted
 * value instead of omitting the key — an empty string, and (confirmed from this
 * launcher's own logs, see UNSET_PLACEHOLDER_RE above) the unexpanded placeholder text
 * itself, e.g. `XRPL_SIGNER_KEYSTORE_PATH="${XRPL_SIGNER_KEYSTORE_PATH}"`.
 *
 * `SignerOptions.ResolveFromEnvironment` (src/StaticBit.Xrpl.Mcp.Signer/
 * Configuration/SignerOptions.cs) reads these three by hand rather than
 * through the .NET configuration binder, so both shapes degrade the same way for
 * `XRPL_SIGNER_PASSPHRASE` / `XRPL_SIGNER_PASSPHRASE_FILE` as truly unset would: neither
 * is `string.IsNullOrEmpty`/`IsNullOrWhiteSpace`, so the literal placeholder text is read
 * as *the* passphrase (or *the* passphrase-file path) rather than "not provided" — a
 * silent, wrong-but-non-crashing misconfiguration, not the exit-3 crash the keystore path
 * case below causes. Stripping those two here closes that gap; it is also included so the
 * set matches `.mcp.json`'s declared placeholders exactly.
 *
 * `XRPL_SIGNER_KEYSTORE_PATH` is the one of the three that is user-visibly affected
 * either way: it resolves as `Environment.GetEnvironmentVariable("XRPL_SIGNER_KEYSTORE_PATH")
 * ?? GetDefaultKeystorePath()` — the `??` only falls back on `null`, not on `""` or on the
 * literal placeholder text, so either shape silently replaces the sensible default path
 * (`~/.staticbit-xrpl-signer/keystore.json`) with something else. The empty-string shape
 * surfaces as `EncryptedFileKeystore` throwing `ArgumentException("Keystore path is
 * empty.")` — caught in Program.cs and reported as "Keystore initialization failed" (exit
 * 3). The literal-placeholder shape does *not* crash: `${XRPL_SIGNER_KEYSTORE_PATH}` is a
 * syntactically valid (if bizarre) relative path component on every OS this launcher
 * targets, so the keystore is silently created under a literally-named
 * `./${XRPL_SIGNER_KEYSTORE_PATH}/keystore.json` next to wherever the process's cwd
 * happens to be — the MCP connection itself succeeds, but every wallet a user creates
 * lands somewhere they never intended and would not find by reading the default-path
 * documentation. Confirmed by direct reproduction: spawning this launcher with
 * `XRPL_SIGNER_KEYSTORE_PATH` set to its own literal placeholder text connects
 * successfully and logs "Keystore at ${XRPL_SIGNER_KEYSTORE_PATH}, wallets=0,
 * audit=disabled." — the literal text, unexpanded, used as a real path.
 *
 * None of the three has a use case where either shape is meaningful, load-bearing
 * configuration, so all three are stripped unconditionally. Only these three declared
 * names are touched — never every such-shaped `XRPL_SIGNER_*` variable — so a value a
 * user sets directly and deliberately is never second-guessed. */
const OPTIONAL_ENV_OVERRIDES = [
  'XRPL_SIGNER_PASSPHRASE',
  'XRPL_SIGNER_PASSPHRASE_FILE',
  'XRPL_SIGNER_KEYSTORE_PATH',
];

/** Returns a shallow copy of `sourceEnv` with any of OPTIONAL_ENV_OVERRIDES
 * removed when their value is the shape Claude Code produces for an unset `${VAR}`
 * placeholder in `.mcp.json` — the empty string, or the literal unexpanded placeholder
 * text itself (see isUnsetOverrideValue). A real override (any other non-empty string,
 * including whitespace) passes through untouched, and every other environment variable
 * is never inspected or modified. */
function stripEmptyOptionalOverrides(sourceEnv) {
  const result = { ...sourceEnv };
  for (const key of OPTIONAL_ENV_OVERRIDES) {
    if (isUnsetOverrideValue(result[key])) delete result[key];
  }
  return result;
}

function resolveBinaryPath() {
  const platform = os.platform();
  const arch = os.arch();

  if (platform === 'win32') {
    return path.join(binDir, 'win-x64', 'StaticBit.Xrpl.Mcp.Signer.exe');
  }
  if (platform === 'linux') {
    if (arch === 'x64')  return path.join(binDir, 'linux-x64',  'StaticBit.Xrpl.Mcp.Signer');
    if (arch === 'arm64') return path.join(binDir, 'linux-arm64', 'StaticBit.Xrpl.Mcp.Signer');
  }
  if (platform === 'darwin') {
    if (arch === 'x64')  return path.join(binDir, 'osx-x64',  'StaticBit.Xrpl.Mcp.Signer');
    if (arch === 'arm64') return path.join(binDir, 'osx-arm64', 'StaticBit.Xrpl.Mcp.Signer');
  }
  throw new Error(`Unsupported platform/architecture: ${platform}/${arch}`);
}

function main() {
  const binPath = resolveBinaryPath();

  if (!fs.existsSync(binPath)) {
    console.error(`[xrpl-signer launcher] Binary not found: ${binPath}`);
    console.error(`[xrpl-signer launcher] Was this plugin installed correctly? Expected layout: bin/<rid>/StaticBit.Xrpl.Mcp.Signer[.exe]`);
    process.exit(2);
  }

  if (os.platform() !== 'win32') {
    try { fs.chmodSync(binPath, 0o755); } catch { /* best-effort */ }
  }

  const child = spawn(binPath, process.argv.slice(2), {
    stdio: 'inherit',
    env: stripEmptyOptionalOverrides(process.env),
  });

  child.on('exit', (code, signal) => {
    if (signal) {
      process.kill(process.pid, signal);
    } else {
      process.exit(code ?? 0);
    }
  });

  child.on('error', err => {
    console.error(`[xrpl-signer launcher] Failed to spawn ${binPath}: ${err.message}`);
    process.exit(3);
  });

  for (const sig of ['SIGINT', 'SIGTERM', 'SIGHUP']) {
    process.on(sig, () => {
      if (!child.killed) child.kill(sig);
    });
  }
}

if (require.main === module) {
  main();
}

// Exported for signer.test.js. Pure/deterministic — no process spawn, no
// filesystem writes — so it can be unit-tested without a .NET runtime.
module.exports = {
  OPTIONAL_ENV_OVERRIDES,
  stripEmptyOptionalOverrides,
  isUnsetOverrideValue,
  UNSET_PLACEHOLDER_RE,
  resolveBinaryPath,
};
