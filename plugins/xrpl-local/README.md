>  🌐 **Language**: **English** | [Русский](README.ru.md)

# xrpl-local plugin

A local stdio MCP with the same set of <!-- toolcount:xrpl -->116<!-- /toolcount:xrpl --> XRPL tools as `xrpl-cloud`, but running **entirely on your machine**. The WebSocket to public XRPL nodes (`xrplcluster.com`, `s.altnet.rippletest.net`, etc.) goes directly from your process — no intermediary. Full reference: [`docs/TOOLS.generated.md`](../../docs/TOOLS.generated.md); category overview is in `xrpl-cloud`'s [`plugins/xrpl-cloud/README.md`](../xrpl-cloud/README.md).

## When to choose this plugin

- **Privacy-sensitive** — you don't want the cloud server admin to see traffic to XRPL nodes.
- **No-server-dependency** — our VPS went down? Local keeps working.
- **Custom nodes** — you want to reach your own rippled, the Hooks testnet v3, or a Sidechain — set the URL via ENV.
- **Air-gapped (almost)** — only public XRPL nodes are needed; no centralized point in our infrastructure.

If you need Cowork agents, mobile access, or you don't want to download a ~110 MB binary — see `xrpl-cloud`.

## Installation

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
/plugin install xrpl-local@staticbit-xrpl-mcp
```

No bearer tokens, no cloud dependency. It just works.

### Optional configuration via ENV

| Variable | Default | What |
|---|---|---|
| `XRPL_LOCAL_DEFAULT_NETWORK` | `mainnet` | network when the caller didn't specify one |
| `XRPL_LOCAL_MAINNET_URL` | `wss://xrplcluster.com` | mainnet WS endpoint |
| `XRPL_LOCAL_TESTNET_URL` | `wss://s.altnet.rippletest.net:51233` | testnet WS endpoint |
| `XRPL_LOCAL_DEVNET_URL` | `wss://s.devnet.rippletest.net:51233` | devnet WS endpoint |
| `XRPL_LOCAL_REQUEST_TIMEOUT` | `30` | timeout for a single rippled request (sec) |

For example, to switch mainnet to the Ripple provider:

```powershell
[Environment]::SetEnvironmentVariable("XRPL_LOCAL_MAINNET_URL", "wss://s1.ripple.com", "User")
```

After changing it — restart Claude Code.

## Verification

```
/mcp
```
```
xrpl-local  node bin/server.js  ✓ Connected
```

Tools are registered as `mcp__plugin_xrpl-local_xrpl-local__*` — the same `xrpl_*` names, just a different prefix. If you have both `xrpl-cloud` and `xrpl-local` installed at the same time, the agent sees both sets and can call either; they are distinguished by the plugin namespace.

## Signing transactions

This plugin holds **no** keys. To sign — install `xrpl-signer` alongside it:

```
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

Local does `prepare` → signer (offline, locally) does `sign` → local does `submit_signed`. No external service in the chain.

## Platforms

The plugin ships self-contained .NET binaries for:
- `win-x64` (~111 MB)
- `linux-x64` (~108 MB)
- `linux-arm64` (~119 MB)
- `osx-x64` (~108 MB)
- `osx-arm64` (~118 MB)

The Node.js launcher `bin/server.js` picks the right one by `os.platform()/os.arch()`.

## Security

- No network communication with our VPS — Claude Code launches a local subprocess.
- The WebSocket goes only to public XRPL nodes whose URLs you control via ENV.
- No keys — write tools accept only a signed blob (the same contract as the cloud).
