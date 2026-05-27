> 🇷🇺 [Прочесть на русском](supply-chain.ru.md)

# Supply chain — release artifacts and verification

Every plugin release (`xrpl-cloud` / `xrpl-local` / `xrpl-signer`) ships with a set of supply-chain artifacts. Some are always included; some only when Apple / Authenticode certificate-authority secrets are configured.

Tag convention: `<plugin>--v<X.Y.Z>` (e.g. `xrpl-signer--v0.1.2`).

## What ships with every Release

Built by [`.github/workflows/release-plugin.yml`](../.github/workflows/release-plugin.yml) → step by step:

| Artifact | When | How to verify |
|---|---|---|
| **CHANGELOG entry** in the release notes | always | Grouped by conventional-commit type (feat / fix / docs / refactor / test / perf / build / ci / other). See `release-plugin.sh::group_by_conventional_commit`. |
| **Per-RID tarballs** `<plugin>-v<X>-<rid>.tar.gz` | for `xrpl-signer` and `xrpl-local` | `tar -tzf` to inspect; `sha256sum -c <file>.sha256` for integrity. |
| **SHA-256 sidecars** `<tarball>.sha256` | always alongside tarballs | `sha256sum -c` compares the local hash against the build-time hash. |
| **SBOM (CycloneDX)** `<plugin>-v<X>.cdx.json` | for `xrpl-signer` and `xrpl-local` | `cyclonedx-cli analyze`, `grype sbom:<file>`, import into Dependency-Track. |
| **SLSA build provenance attestation** | for `xrpl-signer` and `xrpl-local` — **only when the repo is public or the org is on a paid plan** (see below) | `gh attestation verify <tarball> --repo StaticBit-io/staticbit-xrpl-mcp` (GitHub CLI). Confirms the binary really was built by this workflow on this commit. |
| **macOS notarization** | when `APPLE_*` secrets are set | `codesign --verify --deep --strict <binary>`, `spctl --assess --type execute <binary>` (Gatekeeper). Apple notary online lookup on first launch. |
| **Windows Authenticode** | when `WINDOWS_PFX*` secrets are set | `signtool verify /pa /v <binary>.exe`, or `osslsigncode verify -in <binary>.exe` on Linux. |

### SLSA attestation — GitHub Free / private repo limitation

The GitHub Attestations API is available only on:
- **Public repositories** (free).
- Private repos in **Team / Enterprise** org plans.

On free-plan private repos the API returns 403 *"Feature not available for the organization"*. The workflow step is marked `continue-on-error: true` — the release still publishes, just without an attestation in Sigstore. Tarball integrity in that case is confirmed by the accompanying `.sha256` sidecars plus the Git-tagged commit.

When the repo becomes public or the org upgrades — attestations start flowing automatically with no workflow changes.

## Reproducible builds

[`Directory.Build.props`](../Directory.Build.props) enables:

- `<Deterministic>true</Deterministic>` — the C# compiler emits byte-identical builds for identical inputs (no embedded timestamps, no GUIDs in PE metadata).
- `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` (when `CI=true`/`GITHUB_ACTIONS=true`) — normalises paths in PDBs (won't leak `C:\Users\runner\…` into Source Link).

This gives **bit-identity for managed builds** between two runs of the same commit on the same SDK version. Native AOT publish (single-file binaries) includes the host runtime, which is **not bit-identical** across different .NET SDK versions — so for verification pin the SDK version from the `actions/setup-dotnet` of the relevant release workflow.

## How a user verifies an installed plugin

```bash
# 1. Download the tarball from the release page.
TAG=xrpl-signer--v0.1.2
gh release download "$TAG" --repo StaticBit-io/staticbit-xrpl-mcp \
  --pattern '*.tar.gz' --pattern '*.sha256' --pattern '*.cdx.json'

# 2. Verify SHA-256.
sha256sum -c xrpl-signer-v0.1.2-linux-x64.tar.gz.sha256

# 3. Verify SLSA build provenance.
gh attestation verify xrpl-signer-v0.1.2-linux-x64.tar.gz \
  --repo StaticBit-io/staticbit-xrpl-mcp

# 4. (optional) Inspect the SBOM for known CVEs.
grype sbom:xrpl-signer-v0.1.2.cdx.json

# 5. (optional, macOS) Verify notarization.
codesign --verify --deep --strict /path/to/StaticBit.Xrpl.Mcp.Signer
spctl --assess --type execute /path/to/StaticBit.Xrpl.Mcp.Signer

# 6. (optional, Windows) Verify Authenticode.
signtool verify /pa /v StaticBit.Xrpl.Mcp.Signer.exe
```

## Secret setup (for the release manager)

All secrets live in the **GitHub Actions secrets** of the repository, not in code. The workflow conditionally skips the related step when a secret is missing — the release still builds, just without signing / notarization.

### macOS Developer ID + notarization

Requires an **active Apple Developer Program** (US$99/year).

Steps:

1. **Create a Developer ID Application certificate** at [developer.apple.com → Certificates](https://developer.apple.com/account/resources/certificates/list):
   - Type: *Developer ID Application*.
   - Download `.cer`, import into Keychain Access, export as `.p12` with a password.
2. **Create an App Store Connect API Key** at [App Store Connect → Users and Access → Keys](https://appstoreconnect.apple.com/access/api):
   - Role: *Developer* (minimum for notary).
   - Download `AuthKey_XXXX.p8` (once!) — this is the private key.
   - Note the `Key ID` and `Issuer ID` from the same page.
3. **Base64-encode and store in GitHub secrets**:

   ```bash
   # P12 with the certificate
   base64 -w0 < DeveloperID.p12 | pbcopy   # → APPLE_DEVELOPER_ID_P12
   # P12 password
   #                                        → APPLE_DEVELOPER_ID_P12_PASSWORD

   # Private key from App Store Connect
   base64 -w0 < AuthKey_XXXX.p8 | pbcopy   # → APPLE_NOTARY_API_KEY
   #                                        → APPLE_NOTARY_API_KEY_ID         (e.g. ABCD1234EF)
   #                                        → APPLE_NOTARY_API_KEY_ISSUER     (issuer UUID)
   ```

The workflow uses [`rcodesign`](https://github.com/indygreg/apple-platform-rs) — a pure-Rust signer that runs directly on a Linux runner without a macOS host.

### Windows Authenticode

Requires a **code-signing certificate** from a CA (DigiCert, Sectigo, SSL.com, etc.).

EV certificates usually require an HSM token (USB Smart Card / Azure Key Vault) — for those the workflow would need a separate extension. OV/IV certificates come as `.pfx` files, which suits `osslsigncode`.

Steps:

1. Obtain the `.pfx` file from the CA.
2. Store in GitHub secrets:

   ```bash
   base64 -w0 < codesign.pfx | pbcopy   # → WINDOWS_PFX
   #                                     → WINDOWS_PFX_PASSWORD
   ```

The workflow uses `osslsigncode` on a Linux runner with SHA-256 digest and the DigiCert timestamp server (`http://timestamp.digicert.com`). If you pick another CA, change the URL in [release-plugin.yml](../.github/workflows/release-plugin.yml).

## Not yet implemented

- **Notary stapling** for macOS — not possible for plain Mach-O (only `.app` / `.dmg` / `.pkg`). On first launch, Gatekeeper does an online lookup against Apple's notary. If plugins migrate to `.dmg` packaging, stapling can be added.
- **EV code-signing** (Windows) with an HSM token — would require `signtool` with the `DigiCert KeyLocker` provider or similar. The workflow currently assumes a soft-PFX.
- **Vulnerability scanning in CI** (grype / trivy) — the SBOM is generated but a scan is not run. Could be added to `dotnet-test.yml` as a separate step.
- **Reproducible builds for single-file native binaries** — `Deterministic=true` only controls managed builds. Bit-identity native published binaries would need a pinned SDK version + `SOURCE_DATE_EPOCH` support in `dotnet publish` (planned in .NET 11+).
