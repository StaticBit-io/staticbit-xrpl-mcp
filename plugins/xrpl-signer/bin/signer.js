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

/** The exact optional overrides `.mcp.json`'s `env` block declares via `${VAR}`
 * placeholders. Claude Code substitutes an unset placeholder with an *empty
 * string* rather than omitting the key, so a user who never customized one of
 * these three arrives here with e.g. `XRPL_SIGNER_KEYSTORE_PATH=""` in
 * `process.env` instead of the key being absent.
 *
 * `SignerOptions.ResolveFromEnvironment` (src/StaticBit.Xrpl.Mcp.Signer/
 * Configuration/SignerOptions.cs) reads these three by hand rather than
 * through the .NET configuration binder, so an empty `XRPL_SIGNER_PASSPHRASE`
 * / `XRPL_SIGNER_PASSPHRASE_FILE` degrades the same way whether the variable
 * is empty or truly unset: both are treated as "not provided" via explicit
 * `string.IsNullOrEmpty`/`IsNullOrWhiteSpace` checks, and the signer exits
 * cleanly with a "keystore passphrase is not configured" message — a real,
 * unavoidable requirement (there is no safe default passphrase), not a bug.
 * Stripping those two here is a no-op for behavior; it is included anyway so
 * the set matches `.mcp.json`'s declared placeholders exactly, and so a
 * future change to the C# side that starts treating "" differently from
 * "unset" does not silently reintroduce this class of bug.
 *
 * `XRPL_SIGNER_KEYSTORE_PATH` is different: it resolves as
 * `Environment.GetEnvironmentVariable("XRPL_SIGNER_KEYSTORE_PATH") ??
 * GetDefaultKeystorePath()` — the `??` only falls back on `null`, not on an
 * empty string, so an empty override silently replaces the sensible default
 * path (`~/.staticbit-xrpl-signer/keystore.json`) with `""`. That surfaces as
 * `EncryptedFileKeystore` throwing `ArgumentException("Keystore path is
 * empty.")`, caught in Program.cs and reported as "Keystore initialization
 * failed" (exit 3) — a real failure for any user who sets a passphrase but
 * never intended to relocate the keystore. This is the one setting of the
 * three where stripping changes actual behavior, not just intent.
 *
 * None of the three has a use case where `""` is meaningful, load-bearing
 * configuration, so all three are stripped unconditionally when empty. Only
 * these three declared names are touched — never every empty `XRPL_SIGNER_*`
 * variable — so a value a user sets directly and deliberately is never
 * second-guessed. */
const OPTIONAL_ENV_OVERRIDES = [
  'XRPL_SIGNER_PASSPHRASE',
  'XRPL_SIGNER_PASSPHRASE_FILE',
  'XRPL_SIGNER_KEYSTORE_PATH',
];

/** Returns a shallow copy of `sourceEnv` with any of OPTIONAL_ENV_OVERRIDES
 * removed when their value is exactly `''` — the shape Claude Code produces
 * for an unset `${VAR}` placeholder in `.mcp.json`. A real override (any
 * non-empty string, including whitespace) passes through untouched, and every
 * other environment variable is never inspected or modified. */
function stripEmptyOptionalOverrides(sourceEnv) {
  const result = { ...sourceEnv };
  for (const key of OPTIONAL_ENV_OVERRIDES) {
    if (result[key] === '') delete result[key];
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
  resolveBinaryPath,
};
