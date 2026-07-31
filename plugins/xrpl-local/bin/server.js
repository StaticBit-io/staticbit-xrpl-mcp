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

/** Matches the literal text Claude Code passes through, verbatim, as the value of a
 * declared `${VAR}` placeholder in `.mcp.json`'s `env` block when the backing variable is
 * not set anywhere in the host environment — e.g. the 27-character string
 * `${XRPL_LOCAL_REQUEST_TIMEOUT}`, not an empty string and not an absent key. Confirmed
 * from this launcher's own MCP connection logs (`Failed to convert configuration value
 * '${XRPL_LOCAL_REQUEST_TIMEOUT}' at 'StaticBitXrplMcp:RequestTimeoutSeconds' to type
 * 'System.Int32'`) and reproduced exactly by spawning this launcher with these variables
 * set to their literal placeholder text. Anchored to match the *entire* value, not merely
 * contain it, so a legitimate value that happens to contain the substring `${` somewhere
 * in the middle (a URL, say) is left alone. None of these five settings could ever
 * legitimately equal the literal placeholder syntax for a shell variable in its entirety,
 * so a whole-string match is safe to treat as "unset". */
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
 * itself, e.g. `StaticBitXrplMcp__RequestTimeoutSeconds="${XRPL_LOCAL_REQUEST_TIMEOUT}"`.
 * Neither shape is ever meaningful for any of these five:
 *
 *   - `RequestTimeoutSeconds` binds to an `int`. The .NET configuration binder throws
 *     `InvalidOperationException: Failed to convert configuration value '...' ... to type
 *     'System.Int32'` for both shapes while eagerly materializing options during
 *     `ValidateOnStart`, which crashes the whole host with an unhandled exception before
 *     it serves a single request (see StaticBit.Xrpl.Mcp.Server.Program.RunStdioAsync ->
 *     AddStaticBitXrplMcp). This is the exception that made the plugin fail to connect at
 *     all — for both the empty-string shape (already fixed) and the literal-placeholder
 *     shape (this fix).
 *   - `DefaultNetwork` bound to either shape resolves to a network key that matches
 *     neither a configured network nor a built-in default, so the first tool call that
 *     needs the default network throws `Unknown XRPL network '...'` (see
 *     NetworkResolver.Resolve).
 *   - `Networks:mainnet/testnet/devnet` bound to either shape happen to be tolerated by
 *     NetworkResolver today (an unrecognized configured URL falls back to the built-in
 *     one), but that's incidental resilience elsewhere in the code, not a documented
 *     "means default" contract for this launcher to rely on. Stripping them here keeps
 *     the guarantee explicit and cheap.
 *
 * None of these five settings has a use case where either shape is meaningful,
 * load-bearing configuration (unlike, say, an optional string prefix that is
 * deliberately blank) — so unlike code-index-mcp's launcher, which has to
 * special-case `Embedding:QueryInstruction`, there is nothing to preserve
 * here and all five can be stripped unconditionally.
 *
 * Only these five declared names are touched — never every such-shaped
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
  isUnsetOverrideValue,
  UNSET_PLACEHOLDER_RE,
  resolveBinaryPath,
};
