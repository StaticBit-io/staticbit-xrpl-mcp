# xrpl-signer plugin

Offline stdio MCP — управление XRPL кошельками и подписание транзакций. Запускается локально как subprocess, **никаких** сетевых вызовов: пакет `Xrpl.Wallet` использует только криптографию, без `Xrpl.Client`.

## Зачем

Парный плагин к `xrpl-cloud` или `xrpl-local`. Тот делает `prepare` и `submit_signed`, этот — `sign`. Cloud/local-серверы **никогда** не видят твоего seed: они получают на выход только подписанный hex blob.

## Установка

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

### Обязательная ENV — passphrase для keystore

```powershell
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", "<длинный-пароль>", "User")
```

```bash
echo 'export XRPL_SIGNER_PASSPHRASE="<длинный-пароль>"' >> ~/.bashrc
source ~/.bashrc
```

Альтернатива (если не хочешь passphrase прямо в ENV):

```powershell
# Файл с passphrase в первой строке, mode 0600 (на Unix)
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE_FILE", "C:\path\to\passphrase.txt", "User")
```

> **Потеря passphrase = потеря всех кошельков в keystore** (если нет бэкапа seed'ов).

После задания ENV — **рестарт Claude Code**.

### Опционально — путь до keystore

По умолчанию:
- Windows: `%USERPROFILE%\.staticbit-xrpl-signer\keystore.json`
- Linux/macOS: `~/.staticbit-xrpl-signer/keystore.json` (mode 0600)

Переопределить:
```powershell
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_KEYSTORE_PATH", "C:\my\keystore.json", "User")
```

## Проверка

```
/mcp
```
```
xrpl-signer  node bin/signer.js  ✓ Connected
```

## Возможности

### Импорт / создание кошелька — 5 способов

| Tool | Формат входа |
|---|---|
| `xrpl_wallet_generate` | случайная entropy → новый кошелёк |
| `xrpl_wallet_import_seed` | XRPL seed `sEd...` или `sn...` |
| `xrpl_wallet_import_mnemonic` | BIP39 фраза (12/15/18/21/24 слов), опц. derivation path + BIP39 passphrase |
| `xrpl_wallet_import_xumm` | 8 групп по 6 цифр (Xumm secret numbers) |
| `xrpl_wallet_import_text` | произвольный текст + KDF (`sha256` или `pbkdf2`, опц. salt) |

### Управление

- `xrpl_wallet_list` — список кошельков (без секретов)
- `xrpl_wallet_address` — метаданные одного кошелька
- `xrpl_wallet_remove` — удалить
- `xrpl_wallet_export` — backup: вернуть seed в plaintext (требует `confirm=true`)

### Подпись

- `xrpl_sign` — single-sign транзакции
- `xrpl_sign_multi` — partial signature для multi-sign (одна из N подписей)
- `xrpl_sign_combine` — агрегация нескольких partial signatures в финальный blob

## Криптография keystore

| Параметр | Значение |
|---|---|
| KDF | PBKDF2-SHA256, 600 000 iterations |
| Шифр | AES-256-GCM (authenticated) |
| Соль | 32 байта, **per-wallet** |
| IV | 12 байт, **per-wallet** |
| Tag | 16 байт (GCM auth tag) |

Per-wallet соль/IV означает: даже если два кошелька с одинаковым seed и одинаковым passphrase зашифрованы в одном keystore, их ciphertext'ы разные. Утечка одной записи не помогает декодировать другую.

## Платформы

5 self-contained .NET бинарей:
- `win-x64` (~81 MB)
- `linux-x64` (~80 MB)
- `linux-arm64` (~88 MB)
- `osx-x64` (~80 MB)
- `osx-arm64` (~87 MB)

Node.js launcher `bin/signer.js` выбирает нужный по `os.platform()/os.arch()`.

## Совместное использование

С cloud-flow:
```
/plugin install xrpl-cloud@staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

С local-flow (никакого внешнего сервиса):
```
/plugin install xrpl-local@staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

В обоих случаях агент сам разводит вызовы между prepare-сервером и signer'ом.

## Безопасность

- Signer **никогда** не делает сетевых вызовов — изоляция через выбор зависимостей в .csproj (нет `Xrpl.Client`).
- Passphrase живёт **только** в RAM signer-процесса. Не пишется на диск.
- Seeds зашифрованы в keystore — без passphrase не извлечь.
- Скомпрометирован keystore-файл? Атакующему нужно угадать твой passphrase, и PBKDF2 600k iter делает это очень дорого даже на GPU.
