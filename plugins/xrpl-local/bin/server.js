#!/usr/bin/env node
// Cross-platform launcher for the local XRPL MCP server. Picks the right
// self-contained .NET binary by os.platform() / os.arch() and execs it with
// `--transport stdio`, inheriting stdin/stdout so the MCP protocol stream
// passes through transparently.
//
// Layout:
//   bin/server.js          ← this file
//   bin/<rid>/StaticBit.Xrpl.Mcp.Server[.exe]

const path = require('node:path');
const os = require('node:os');
const fs = require('node:fs');
const { spawn } = require('node:child_process');

const binDir = __dirname;

/** The exact optional overrides `.mcp.json`'s `env` block declares via `${VAR}`
 * placeholders. Claude Code substitutes an unset placeholder with an *empty
 * string* rather than omitting the key, so a user who never customized any of
 * these five arrives here with e.g. `StaticBitXrplMcp__RequestTimeoutSeconds=""`
 * in `process.env` — a value that is never meaningful for any of them:
 *
 *   - `RequestTimeoutSeconds` binds to an `int`. The .NET configuration binder
 *     throws `InvalidOperationException: Failed to convert configuration value
 *     '' ... to type 'System.Int32'` while eagerly materializing options during
 *     `ValidateOnStart`, which crashes the whole host with an unhandled
 *     exception before it serves a single request (see
 *     StaticBit.Xrpl.Mcp.Server.Program.RunStdioAsync -> AddStaticBitXrplMcp).
 *     This is the exception that made the plugin fail to connect at all.
 *   - `DefaultNetwork` bound to `""` resolves to network key `""`, which
 *     matches neither a configured network nor a built-in default, so the
 *     first tool call that needs the default network throws `Unknown XRPL
 *     network ''` (see NetworkResolver.Resolve).
 *   - `Networks:mainnet/testnet/devnet` bound to `""` happen to be tolerated
 *     by NetworkResolver today (a blank configured URL falls back to the
 *     built-in one), but that's incidental resilience elsewhere in the code,
 *     not a documented "empty means default" contract for this launcher to
 *     rely on. Stripping them here keeps the guarantee explicit and cheap.
 *
 * None of these five settings has a use case where `""` is meaningful,
 * load-bearing configuration (unlike, say, an optional string prefix that is
 * deliberately blank) — so unlike code-index-mcp's launcher, which has to
 * special-case `Embedding:QueryInstruction`, there is nothing to preserve
 * here and all five can be stripped unconditionally when empty.
 *
 * Only these five declared names are touched — never every empty
 * `StaticBitXrplMcp__*` variable — so a value a user sets directly and
 * deliberately (including via a wrapping tool that sets real environment
 * variables, not `.mcp.json` placeholders) is never second-guessed. */
const OPTIONAL_ENV_OVERRIDES = [
  'StaticBitXrplMcp__DefaultNetwork',
  'StaticBitXrplMcp__Networks__mainnet',
  'StaticBitXrplMcp__Networks__testnet',
  'StaticBitXrplMcp__Networks__devnet',
  'StaticBitXrplMcp__RequestTimeoutSeconds',
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
    return path.join(binDir, 'win-x64', 'StaticBit.Xrpl.Mcp.Server.exe');
  }
  if (platform === 'linux') {
    if (arch === 'x64')  return path.join(binDir, 'linux-x64',  'StaticBit.Xrpl.Mcp.Server');
    if (arch === 'arm64') return path.join(binDir, 'linux-arm64', 'StaticBit.Xrpl.Mcp.Server');
  }
  if (platform === 'darwin') {
    if (arch === 'x64')  return path.join(binDir, 'osx-x64',  'StaticBit.Xrpl.Mcp.Server');
    if (arch === 'arm64') return path.join(binDir, 'osx-arm64', 'StaticBit.Xrpl.Mcp.Server');
  }
  throw new Error(`Unsupported platform/architecture: ${platform}/${arch}`);
}

function main() {
  const binPath = resolveBinaryPath();

  if (!fs.existsSync(binPath)) {
    console.error(`[xrpl-local launcher] Binary not found: ${binPath}`);
    console.error(`[xrpl-local launcher] Was this plugin installed correctly? Expected layout: bin/<rid>/StaticBit.Xrpl.Mcp.Server[.exe]`);
    process.exit(2);
  }

  if (os.platform() !== 'win32') {
    try { fs.chmodSync(binPath, 0o755); } catch { /* best-effort */ }
  }

  // Forward CLI args plus pin the transport to stdio. Server-side CLI parsing
  // accepts --transport stdio and falls back to it by default, but we make it
  // explicit so the choice cannot drift via accidental env-var leakage.
  const args = ['--transport', 'stdio', ...process.argv.slice(2)];

  const child = spawn(binPath, args, {
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
    console.error(`[xrpl-local launcher] Failed to spawn ${binPath}: ${err.message}`);
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

// Exported for server.test.js. Pure/deterministic — no process spawn, no
// filesystem writes — so it can be unit-tested without a .NET runtime.
module.exports = {
  OPTIONAL_ENV_OVERRIDES,
  stripEmptyOptionalOverrides,
  resolveBinaryPath,
};
