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
    env: process.env,
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

main();
