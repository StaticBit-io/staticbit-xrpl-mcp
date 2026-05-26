# Supply chain — release artifacts и верификация

Каждый release плагина (`xrpl-cloud` / `xrpl-local` / `xrpl-signer`) сопровождается набором supply-chain-артефактов. Часть включена всегда, часть — только когда настроены секреты Apple / Authenticode certificate authority.

Конвенция тегов: `<plugin>--v<X.Y.Z>` (например `xrpl-signer--v0.1.2`).

## Что прикладывается к каждому Release

Создаётся [`.github/workflows/release-plugin.yml`](../.github/workflows/release-plugin.yml) → шаги последовательно:

| Артефакт | Когда | Как верифицировать |
|---|---|---|
| **CHANGELOG-entry** в release notes | всегда | Сгруппирован по conventional-commit type (feat / fix / docs / refactor / test / perf / build / ci / other). См. `release-plugin.sh::group_by_conventional_commit`. |
| **Per-RID tarballs** `<plugin>-v<X>-<rid>.tar.gz` | для `xrpl-signer` и `xrpl-local` | `tar -tzf` чтобы посмотреть; `sha256sum -c <file>.sha256` для целостности. |
| **SHA-256 sidecars** `<tarball>.sha256` | всегда вместе с tarballs | `sha256sum -c` сравнит локальный хеш с тем, что был на момент сборки. |
| **SBOM (CycloneDX)** `<plugin>-v<X>.cdx.json` | для `xrpl-signer` и `xrpl-local` | `cyclonedx-cli analyze`, `grype sbom:<file>`, импорт в Dependency-Track. |
| **SLSA build provenance attestation** | для `xrpl-signer` и `xrpl-local` — **только если репо public или org на paid плане** (см. ниже) | `gh attestation verify <tarball> --repo StaticBit-io/staticbit-xrpl-mcp` (GitHub CLI). Подтверждает что бинарь действительно собран этим workflow на этом коммите. |
| **macOS notarization** | если настроены `APPLE_*` secrets | `codesign --verify --deep --strict <binary>`, `spctl --assess --type execute <binary>` (Gatekeeper). Apple notary lookup онлайн при первом запуске. |
| **Windows Authenticode** | если настроены `WINDOWS_PFX*` secrets | `signtool verify /pa /v <binary>.exe`, или `osslsigncode verify -in <binary>.exe` на Linux. |

### SLSA attestation — ограничение GitHub Free / private repos

GitHub Attestations API доступен только для:
- **Public repositories** (бесплатно).
- Приватных репо в **Team / Enterprise** плане организации.

На приватных репо free-плана API возвращает 403 *"Feature not available for the organization"*. Workflow step помечен `continue-on-error: true` — релиз всё равно публикуется, просто без attestation в Sigstore. Целостность tarball'ов в этом случае подтверждается прилагаемыми `.sha256` sidecars + Git-тегом коммита.

Когда репо станет public или org апгрейднется — attestations начнут флоуить автоматически без изменений в workflow.

## Reproducible builds

[`Directory.Build.props`](../Directory.Build.props) включает:

- `<Deterministic>true</Deterministic>` — C# компилятор эмитит байт-идентичные сборки для идентичных входов (без эмбеддед timestamps, без GUID'ов из PE-метаданных).
- `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` (когда `CI=true`/`GITHUB_ACTIONS=true`) — нормализует пути в PDB'ах (не утечёт `C:\Users\runner\…` в Source Link).

Это даёт **bit-identity для managed-сборок** между двумя запусками одного коммита на одной версии SDK. Native AOT publish (single-file бинари) включает в себя host runtime, который **не bit-identical** между разными версиями .NET SDK — поэтому для верификации указывайте SDK-версию из `actions/setup-dotnet` соответствующего release-workflow.

## Как пользователю проверить установленный плагин

```bash
# 1. Скачать tarball с release-страницы.
TAG=xrpl-signer--v0.1.2
gh release download "$TAG" --repo StaticBit-io/staticbit-xrpl-mcp \
  --pattern '*.tar.gz' --pattern '*.sha256' --pattern '*.cdx.json'

# 2. Проверить SHA-256.
sha256sum -c xrpl-signer-v0.1.2-linux-x64.tar.gz.sha256

# 3. Проверить SLSA build provenance.
gh attestation verify xrpl-signer-v0.1.2-linux-x64.tar.gz \
  --repo StaticBit-io/staticbit-xrpl-mcp

# 4. (опц.) Проинспектировать SBOM на известные CVE.
grype sbom:xrpl-signer-v0.1.2.cdx.json

# 5. (опц., macOS) Проверить notarization.
codesign --verify --deep --strict /path/to/StaticBit.Xrpl.Mcp.Signer
spctl --assess --type execute /path/to/StaticBit.Xrpl.Mcp.Signer

# 6. (опц., Windows) Проверить Authenticode.
signtool verify /pa /v StaticBit.Xrpl.Mcp.Signer.exe
```

## Настройка секретов (для релизера)

Все секреты хранятся в **GitHub Actions secrets** репозитория, не в коде. Workflow условно пропускает соответствующий шаг, если секрет не задан — release всё равно соберётся, просто без подписи / нотаризации.

### macOS Developer ID + notarization

Нужен **активный Apple Developer Program** (US$99/год).

Шаги:

1. **Создать Developer ID Application certificate** в [developer.apple.com → Certificates](https://developer.apple.com/account/resources/certificates/list):
   - Тип: *Developer ID Application*.
   - Скачать `.cer`, импортнуть в Keychain Access, экспортнуть как `.p12` с паролем.
2. **Создать App Store Connect API Key** в [App Store Connect → Users and Access → Keys](https://appstoreconnect.apple.com/access/api):
   - Роль: *Developer* (минимум для notary).
   - Скачать `AuthKey_XXXX.p8` (один раз!) — это private key.
   - Запомнить `Key ID` и `Issuer ID` из той же страницы.
3. **Закодировать в base64 и сохранить в GitHub secrets**:

   ```bash
   # P12 с сертификатом
   base64 -w0 < DeveloperID.p12 | pbcopy   # → APPLE_DEVELOPER_ID_P12
   # пароль от P12
   #                                        → APPLE_DEVELOPER_ID_P12_PASSWORD

   # Private key из App Store Connect
   base64 -w0 < AuthKey_XXXX.p8 | pbcopy   # → APPLE_NOTARY_API_KEY
   #                                        → APPLE_NOTARY_API_KEY_ID         (например, ABCD1234EF)
   #                                        → APPLE_NOTARY_API_KEY_ISSUER     (UUID issuer'а)
   ```

Workflow использует [`rcodesign`](https://github.com/indygreg/apple-platform-rs) — pure-Rust signer, который работает прямо на Linux runner'е без macOS-хоста.

### Windows Authenticode

Нужен **code-signing certificate** от CA (DigiCert, Sectigo, SSL.com, etc).

EV-сертификаты обычно требуют HSM-токен (USB Smart Card / Azure Key Vault) — для них workflow придётся расширить отдельно. OV/IV-сертификаты выдаются как `.pfx`, что подходит для `osslsigncode`.

Шаги:

1. Получить `.pfx` файл от CA.
2. Сохранить в GitHub secrets:

   ```bash
   base64 -w0 < codesign.pfx | pbcopy   # → WINDOWS_PFX
   #                                     → WINDOWS_PFX_PASSWORD
   ```

Workflow использует `osslsigncode` на Linux runner'е с SHA-256 digest и DigiCert timestamp server (`http://timestamp.digicert.com`). Если выбран другой CA — поменять URL в [release-plugin.yml](../.github/workflows/release-plugin.yml).

## Что ещё не реализовано

- **Notary stapling** для macOS — невозможно для plain Mach-O (только `.app` / `.dmg` / `.pkg`). При первом запуске Gatekeeper делает online-lookup в Apple notary. Если плагины переедут на распаковку из `.dmg` — стапинг можно будет добавить.
- **EV code-signing** (Windows) с HSM-токеном — потребует `signtool` с провайдером `DigiCert KeyLocker` или аналогичным. Workflow сейчас рассчитан на soft-PFX.
- **Vulnerability scanning** в CI (grype / trivy) — SBOM генерится, но скан не запускается. Можно добавить в `dotnet-test.yml` отдельным шагом.
- **Reproducible builds для single-file native бинарей** — `Deterministic=true` управляет только managed-сборками. Bit-identity native published binary потребует pinned SDK-версии + `SOURCE_DATE_EPOCH` поддержки в `dotnet publish` (планируется в .NET 11+).
