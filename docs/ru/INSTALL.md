> 🇬🇧 [Read in English](../INSTALL.md)

# INSTALL — установка и подключение XRPL-плагинов

Self-contained пошаговая инструкция. Передай ссылку на этот файл новому пользователю — он сможет дойти от чистой Claude Code до первой подписанной XRPL-транзакции без чтения других документов.

## Оглавление

1. [Предварительные требования](#1-предварительные-требования)
2. [Выбор сценария — какие плагины ставить](#2-выбор-сценария)
3. [Доступ к marketplace](#3-доступ-к-marketplace)
4. [Добавление marketplace](#4-добавление-marketplace)
5. [Установка плагинов](#5-установка-плагинов)
6. [Получение доступа — allow-list админа и секреты](#6-получение-доступа)
7. [Установка environment-переменных](#7-установка-environment-переменных)
8. [Перезапуск Claude Code](#8-перезапуск-claude-code)
9. [Проверка подключения — и OAuth-вход для cloud](#9-проверка-подключения)
10. [Первый кошелёк + первая транзакция (testnet)](#10-первый-кошелёк--первая-транзакция-testnet)
11. [Day-two — обновления, бэкапы, ротация](#11-day-two)
12. [Удаление](#12-удаление)
13. [Troubleshooting](#13-troubleshooting)

---

## 1. Предварительные требования

| Что | Зачем | Как проверить |
|---|---|---|
| Claude Code 2.1+ | команды `/plugin` и поддержка plugin MCP появились в этой версии | `claude --version` |
| Node.js 18+ | плагины `xrpl-local` и `xrpl-signer` запускают .NET-бинарь через Node-launcher | `node --version` |
| ~600 MB места | self-contained бинарники для 5 платформ внутри плагинов | — |

> Если у тебя Claude Code < 2.1 — обнови через `claude update` или [официальный апдейтер](https://claude.com/claude-code).

---

## 2. Выбор сценария

Marketplace содержит **три независимых** плагина. Их можно ставить в любых комбинациях. Выбирай по сценарию:

| Что хочешь | Ставь | Зачем |
|---|---|---|
| **Hosted setup**: проще, легче, есть Cowork-агенты — но cloud-сервер видит metadata запросов | `xrpl-cloud` + `xrpl-signer` | cloud делает prepare/submit, signer подписывает локально |
| **Privacy-first**: ничего не идёт через наш VPS, WebSocket к XRPL нодам открывается с твоей машины | `xrpl-local` + `xrpl-signer` | local-сервер делает то же что cloud, но локально |
| **Read-only через cloud**: дашборд, мониторинг балансов, нет подписей | `xrpl-cloud` | без signer'а только чтение |
| **Read-only локально** | `xrpl-local` | то же без cloud |
| **Только wallet management** (генерация/импорт/бэкап без сети) | `xrpl-signer` | offline keystore сам по себе |

> Если не уверен — выбери **local + signer**. Это полностью self-contained (не нужен оформленный cloud-доступ), и её можно расширить или заменить позже без потери wallet'ов в keystore.

### Зачем подписание всегда локальное

Cloud-сервер **никогда** не принимает seed или private key — все write-tools требуют уже подписанный hex blob. Подпись делает **только** `xrpl-signer` у тебя на машине, через зашифрованный паролем keystore. Это инвариант архитектуры, не сценарий.

### Что покрывают инструменты (с высоты птичьего полёта)

Walkthrough ниже использует базовые `xrpl_fee` / `xrpl_payment_prepare` / `xrpl_sign` / `xrpl_tx_submit_signed` как hello-world, но поверхность гораздо шире — **<!-- toolcount:total -->131<!-- /toolcount:total --> tools** через `xrpl-cloud` / `xrpl-local` (<!-- toolcount:xrpl -->116<!-- /toolcount:xrpl -->) и `xrpl-signer` (<!-- toolcount:xrpl-signer -->15<!-- /toolcount:xrpl-signer -->). Категории, доступные через тот же `prepare → sign → submit` flow:

| Домен | Типичные инструменты |
|---|---|
| **Payments & DEX** | `payment_prepare`, `offer_create_prepare`, `offer_cancel_prepare`, `trustset_prepare`, `book_offers`, `ripple_path_find` |
| **AMM** | `amm_create_prepare`, `amm_deposit_prepare`, `amm_withdraw_prepare`, `amm_vote_prepare`, `amm_bid_prepare`, `amm_clawback_prepare`, `amm_delete_prepare`, `amm_info` |
| **NFT** | `nft_mint_prepare`, `nft_burn_prepare`, `nft_modify_prepare`, `nft_create_offer_prepare`, `nft_accept_offer_prepare`, `nft_cancel_offer_prepare`, `nft_buy_offers`, `nft_sell_offers` |
| **Vault** (XLS-65) | `vault_create_prepare`, `vault_set_prepare`, `vault_deposit_prepare`, `vault_withdraw_prepare`, `vault_clawback_prepare`, `vault_delete_prepare` |
| **MPT** (XLS-33) | `mpt_issuance_{create,destroy,set}_prepare`, `mpt_authorize_prepare`, `account_mpts`, `account_mpt_issuances` |
| **Loan / Loan Broker** | `loan_set_prepare`, `loan_manage_prepare`, `loan_pay_prepare`, `loan_delete_prepare`, `loan_broker_*_prepare` |
| **Credentials** (XLS-70) | `credential_{create,accept,delete}_prepare`, `hash_credential` |
| **DID** (XLS-40) | `did_set_prepare`, `did_delete_prepare` |
| **Oracle** (XLS-47) | `oracle_set_prepare`, `oracle_delete_prepare` |
| **Permissioned Domains** | `permissioned_domain_set_prepare`, `permissioned_domain_delete_prepare`, `deposit_preauth_prepare` |
| **Escrow / Checks / PayChan** | `escrow_{create,finish,cancel}_prepare`, `check_{create,cash,cancel}_prepare`, `payment_channel_{create,fund,claim}_prepare` |
| **xChain bridges** (XLS-38) | `xchain_{create_bridge,modify_bridge,commit,claim,create_claim_id,account_create_commit,add_account_create_attestation,add_claim_attestation}_prepare` |
| **Account admin** | `account_set_prepare`, `set_regular_key_prepare`, `signer_list_set_prepare`, `ticket_create_prepare`, `account_delete_prepare`, `delegate_set_prepare`, `clawback_prepare`, `trustline_freeze_prepare` |
| **Batch** | `batch_prepare`, `tx_prepare_generic` |
| **Reads** (XLS-aware) | `account_{info,lines,tx,objects,nfts,mpts,vaults,credentials,permissioned_domains,escrows,bridges,did,loans,loan_brokers}`, `server_{info,state,definitions}`, `manifest`, `ledger`, `gateway_balances`, `tx_{lookup,simulate,preflight,decode_blob,explain}` |
| **Подписки** | `subscribe`, `unsubscribe`, `path_find_{create,status,close}` |
| **Signer / wallet** | `wallet_{generate,generate_mnemonic,import_seed,import_mnemonic,import_text,import_xumm,export,export_index,address,derive_address,list,remove}`, `sign`, `sign_multi`, `sign_combine` |

Полный per-parameter reference — авто-генерируемый: [`docs/TOOLS.generated.md`](../TOOLS.generated.md) (только английский). Категорийная разбивка с usage-заметками — в [`plugins/xrpl-cloud/README.ru.md`](../../plugins/xrpl-cloud/README.ru.md). Агент выбирает нужный tool из скиллов `xrpl-cloud-operations` / `xrpl-local-operations` / `xrpl-wallet-operations` — ты описываешь намерение естественным языком, скилл маппит на инструмент.

---

## 3. Доступ к marketplace

Marketplace **публичный** — `claude plugin marketplace add` не требует GitHub-токена. Переходи сразу к §4.

> Хостишь свой *приватный* форк? Тогда read-only GitHub PAT — стандартный способ, которым Claude Code аутентифицируется к приватному marketplace-репо, но для официального StaticBit-marketplace ничего не нужно.

---

## 4. Добавление marketplace

```powershell
claude plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
```

Marketplace публичный — токен не запрашивается.

**Проверка:**

```powershell
claude plugin marketplace list
```

Ожидается:
```
staticbit-xrpl-mcp  https://github.com/StaticBit-io/staticbit-xrpl-mcp  ✔ enabled
```

---

## 5. Установка плагинов

Команды по сценариям из §2:

### Cloud + signing (рекомендуемый дефолт)
```powershell
claude plugin install xrpl-cloud@staticbit-xrpl-mcp
claude plugin install xrpl-signer@staticbit-xrpl-mcp
```

### Local + signing (privacy-first)
```powershell
claude plugin install xrpl-local@staticbit-xrpl-mcp
claude plugin install xrpl-signer@staticbit-xrpl-mcp
```

### Только cloud (read-only)
```powershell
claude plugin install xrpl-cloud@staticbit-xrpl-mcp
```

### Только signer
```powershell
claude plugin install xrpl-signer@staticbit-xrpl-mcp
```

### Проверка списка
```powershell
claude plugin list
```

Каждый установленный плагин должен иметь `Status: ✔ enabled`.

---

## 6. Получение доступа

### 6.1 `xrpl-cloud` — хостится StaticBit, по договорённости

`xrpl-cloud` обращается к серверу, **который хостит StaticBit** (`xrpl.mcp.staticbit.ai`), защищённому **OAuth 2.1** против `auth.mcp.staticbit.ai`. Этот hosted-эндпоинт **не открыт для публичного self-service** — доступ оформляется со StaticBit, аккаунт должен быть в allow-list сервера.

**Для self-serve этот раздел не нужен.** Используй `xrpl-local` (никакого шага доступа — он ходит к публичным XRPL-нодам с твоей машины) или подними тот же сервер сам для своей команды (см. [DEPLOY.md](DEPLOY.md)). Поверхность инструментов идентична.

Если доступ по договорённости **уже есть** — войди интерактивно из Claude Code через `/mcp` (см. §9): браузерный flow проходит через `auth.mcp.staticbit.ai`, Claude Code делает dynamic client registration и обновляет токен автоматически — ничего копировать/вставлять не нужно.

### 6.2 `XRPL_SIGNER_PASSPHRASE` — только для `xrpl-signer`

Если ставишь `xrpl-signer`, **придумай passphrase сам** — она нужна для шифрования локального keystore. Никто кроме тебя её не знает, в том числе админ.

**Требования:**
- Минимум 16 символов (32+ лучше).
- Не словарное слово (atтак brute-force через PBKDF2 600k iter будет долгим, но не невозможным для словаря).
- Уникальная — не используй пароли от других сервисов.

**Сгенерировать сильную:**
```powershell
# Windows / PowerShell — 32 случайных base64 символа
[Convert]::ToBase64String((1..24 | ForEach-Object { Get-Random -Maximum 256 }))
```
```bash
# Linux / macOS
openssl rand -base64 24
```

**Сохрани в менеджер паролей сразу же.** Потеря passphrase = потеря всех кошельков в keystore (если нет бэкапа seed'ов отдельно).

---

## 7. Установка environment-переменных

ENV-переменная нужна только `xrpl-signer` — passphrase его keystore. `xrpl-cloud` не требует **ни одной**: он аутентифицируется через OAuth в момент входа (§9), а не через env-переменную. ENV-переменные читаются плагинами при старте Claude Code. После `setx` нужен **рестарт Claude Code**, а не только нового окна PowerShell.

### Windows (PowerShell)

```powershell
# Для xrpl-signer:
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", "<твоя-passphrase>", "User")
```

### Windows (cmd) — альтернатива
```cmd
setx XRPL_SIGNER_PASSPHRASE "<твоя-passphrase>"
```

### macOS / Linux (bash или zsh)
```bash
cat >> ~/.bashrc <<'EOF'
export XRPL_SIGNER_PASSPHRASE="<твоя-passphrase>"
EOF
source ~/.bashrc
# Для zsh пользователей — ~/.zshrc вместо ~/.bashrc
```

### Альтернатива для signer — passphrase в файле

Если не хочешь passphrase в ENV напрямую, положи её в файл:

```powershell
"<твоя-passphrase>" | Out-File -Encoding ASCII -NoNewline "$env:USERPROFILE\.staticbit-xrpl-signer\passphrase.txt"
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE_FILE", "$env:USERPROFILE\.staticbit-xrpl-signer\passphrase.txt", "User")
# И НЕ задавай XRPL_SIGNER_PASSPHRASE — signer прочитает первую строку файла
```

```bash
mkdir -p ~/.staticbit-xrpl-signer
echo -n "<твоя-passphrase>" > ~/.staticbit-xrpl-signer/passphrase.txt
chmod 600 ~/.staticbit-xrpl-signer/passphrase.txt
echo 'export XRPL_SIGNER_PASSPHRASE_FILE="$HOME/.staticbit-xrpl-signer/passphrase.txt"' >> ~/.bashrc
source ~/.bashrc
```

### Проверка
```powershell
[Environment]::GetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", "User")
```
```bash
echo $XRPL_SIGNER_PASSPHRASE
```

Если возвращается пустая строка — переменная не задана. Проверь правильность scope (`User` vs `Machine`) и перезапусти shell.

---

## 8. Перезапуск Claude Code

После изменения ENV или установки/обновления плагинов **обязательно** полностью перезапусти Claude Code:

```
/exit
```
И затем запусти заново (двойной клик / `claude` в shell).

Плагины загружаются при старте процесса. ENV-переменные тоже читаются один раз при старте signer-subprocess'а. Без рестарта `${XRPL_SIGNER_PASSPHRASE}` подставится тем значением которое было до изменения.

---

## 9. Проверка подключения

В новой сессии Claude Code:

```
/mcp
```

В зависимости от того что поставил, увидишь одну или несколько строк:

```
xrpl-cloud   https://xrpl.mcp.staticbit.ai/mcp (HTTP)   ⚠ Needs login
xrpl-local   node …/bin/server.js                       ✓ Connected
xrpl-signer  node …/bin/signer.js                       ✓ Connected
```

### OAuth-вход для `xrpl-cloud`

Если поставил `xrpl-cloud`, при первом запуске он попросит аутентифицироваться. Не выходя из `/mcp`, выбери `xrpl-cloud` и следуй приглашению ко входу — откроется окно браузера на `auth.mcp.staticbit.ai`. Войди там; Claude Code сделает dynamic client registration, сохранит токен и дальше будет обновлять его автоматически. После входа `/mcp` покажет:

```
xrpl-cloud   https://xrpl.mcp.staticbit.ai/mcp (HTTP)   ✓ Connected
```

Если вход отклонён — твоего аккаунта нет в allow-list, попроси админа добавить (§6.1).

В итоге все должны быть `✓ Connected`. Если `disconnected` или `failed` — см. §13.

### Проверка tools

В чате попроси Claude сделать read-only вызов:
```
What is the current XRPL fee on mainnet?
```

Агент выберет skill `xrpl-cloud-operations` (или `xrpl-local-operations`) и вызовет `xrpl_fee`. Должен вернуть `base_fee: 10` (drops) и ledger sequence.

---

## 10. Первый кошелёк + первая транзакция (testnet)

Полный hello-world на testnet'е — здесь ничего реально стоящего не движется, можно безопасно учиться.

### 10.1 Сгенерировать testnet кошелёк

В чате Claude Code:
```
Сгенерируй новый XRPL кошелёк с именем test1, ed25519
```

Агент вызовет `mcp__plugin_xrpl-signer_xrpl-signer__xrpl_wallet_generate` и вернёт:
```
{
  "name": "test1",
  "address": "rN7n7otQDd6FczFgLdhmKfNVrPBcA...",
  "publicKey": "ED...",
  "algorithm": "ed25519"
}
```

**Сохрани адрес.** Seed уже зашифрован в keystore и не возвращается. Если хочешь backup в plaintext (для recovery вне signer):
```
Экспортируй seed кошелька test1, я хочу сохранить в password manager. confirm=true
```

Полученный seed **сразу** скопируй в менеджер паролей и удали ту часть переписки если возможно. Seed появится в transcript'е чата.

### 10.2 Закинуть testnet XRP через faucet

Открой [xrpl.org/xrp-testnet-faucet.html](https://xrpl.org/xrp-testnet-faucet.html). Введи адрес из §10.1, нажми **Generate**. Через несколько секунд faucet положит 1000 testnet-XRP.

### 10.3 Проверить баланс

```
Сколько XRP у адреса <твой-address> на testnet?
```

Агент вызовет `xrpl_xrp_balance` (через cloud или local). Должно показать `1000` или около того.

### 10.4 Создать второй кошелёк (получатель)
```
Сгенерируй XRPL кошелёк test2
```

### 10.5 Отправить testnet платёж

```
Отправь 5 XRP с test1 на адрес <test2-address> на testnet
```

Агент:
1. Вызовет `*_payment_prepare(network=testnet, account=<test1>, destination=<test2>, amount=5000000)`.
2. Покажет тебе человекочитаемое summary:
   ```
   Payment from rTest1... to rTest2...: 5000000 drops XRP (=5 XRP). Fee 12 drops. Expires at ledger 17638xxx. Confirm?
   ```
3. Спросит подтверждение — **прочитай внимательно** (это твоя последняя возможность отловить опечатку).
4. После твоего "yes" вызовет `xrpl_sign(name=test1, transaction=<txJson>)`.
5. Затем `xrpl_tx_submit_signed(txBlobSigned=<blob>, waitForValidation=true)`.
6. Вернёт `engineResult: tesSUCCESS` и tx hash.

### 10.6 Lookup транзакции
```
Найди транзакцию <hash> на testnet
```

`xrpl_tx_lookup` вернёт полную транзакцию с metadata, `validated: true`. Поздравляю — первая XRPL-транзакция прошла.

---

## 11. Day-two

### 11.1 Обновить marketplace

Когда я (или ты) запушишь новую версию marketplace:

```
claude plugin marketplace update staticbit-xrpl-mcp
claude plugin list
```

Обновлённые плагины подсветятся как имеющие более свежую версию.

### 11.2 Обновить отдельный плагин

```
claude plugin update xrpl-signer@staticbit-xrpl-mcp
```

> ⚠️ Форма `<plugin>@<marketplace>` **обязательна**. Короткая
> `claude plugin update xrpl-signer` падает с `Plugin not found` в
> текущей версии Claude Code CLI — резолвер не достраивает имя
> marketplace'а сам.

Рестарт Claude Code после обновления.

### 11.3 Backup seed'а

```
Экспортируй seed test1, confirm=true
```

Скопируй seed в менеджер паролей. Удали этот фрагмент чата если возможно.

Альтернатива — backup всего файла keystore (он зашифрован, но без passphrase бесполезен):
```powershell
Copy-Item "$env:USERPROFILE\.staticbit-xrpl-signer\keystore.json" "<куда-нибудь-в-encrypted-storage>"
```

### 11.4 Ротация passphrase

**Текущий signer не поддерживает on-the-fly re-encrypt.** Алгоритм:
1. Через `xrpl_wallet_export` (с `confirm=true`) выгрузи **все** seed'ы из всех кошельков. Сохрани во временный безопасный список.
2. Удали `~/.staticbit-xrpl-signer/keystore.json`.
3. Установи новую `XRPL_SIGNER_PASSPHRASE`.
4. Перезапусти Claude Code.
5. Импортируй каждый seed обратно (`xrpl_wallet_import_seed`).
6. Сотри временный список seed'ов.

### 11.5 Cloud-доступ — bearer для ротации нет

С OAuth нет статического секрета, который мог бы утечь или нуждаться в ротации. Токены — короткоживущие RS256 JWT, которые Claude Code обновляет автоматически. Если подозреваешь компрометацию устройства — попроси админа отключить твой аккаунт в allow-list (это немедленно отзывает твои refresh-токены на всех устройствах), затем включить заново и войти повторно через `/mcp`. Сохранённый локально токен можно очистить: `/mcp` → выбери `xrpl-cloud` → clear authentication, и войди снова.

### 11.6 Добавить ещё клиент (новый MCP-пользователь)

Если коллега хочет подключиться к тому же cloud-серверу — попроси админа добавить **его** аккаунт в allow-list. Каждый входит через собственную браузерную сессию на `auth.mcp.staticbit.ai`; доступ выдаётся и отзывается по аккаунтам независимо. Общего секрета раздавать не нужно.

### 11.7 Установка на втором ПК / миграция

Два сценария — выбирай в зависимости от того, нужны ли тебе **те же** кошельки на новом ПК.

#### A. Новый ПК, новые кошельки (с нуля)

Просто пройди весь INSTALL.md от §1 до §10 на новом ПК. Для `xrpl-cloud` просто войди заново через `/mcp` на новой машине (тот же allow-listed аккаунт, свежий браузерный вход — копировать секрет не нужно). Passphrase для signer'а — новая (или старая, как удобнее), кошельки сгенерируешь / импортируешь через `xrpl-signer` отдельно. Каждый ПК имеет независимый keystore.

#### B. Перенос существующих кошельков (то же keystore)

Если у тебя на первом ПК уже есть кошельки (например `main`, `cold`, `dex`) и хочешь чтобы они были доступны на ноутбуке/втором ПК — копируем `keystore.json`.

**Что переносим:**
- Файл `~/.staticbit-xrpl-signer/keystore.json` (Windows: `%USERPROFILE%\.staticbit-xrpl-signer\keystore.json`).
- `XRPL_SIGNER_PASSPHRASE` — **та же самая** что на первом ПК. AES-GCM не расшифрует keystore с другим ключом — это by design.

**Что НЕ переносится:**
- ENV-переменные — задаём заново через `[Environment]::SetEnvironmentVariable(...)`.
- PAT для GitHub — у каждого устройства свой.
- Cloud-аутентификация — копировать нечего. На втором ПК просто войди через `/mcp` на `auth.mcp.staticbit.ai` тем же allow-listed аккаунтом; Claude Code зарегистрирует и сохранит токен для каждого устройства отдельно.

**Алгоритм:**

1. На первом ПК — установить `xrpl-signer` если ещё нет, и убедиться что keystore работает (`xrpl_wallet_list` показывает что нужно).

2. **На первом ПК** — экспортировать keystore:
   ```powershell
   # Windows
   Copy-Item "$env:USERPROFILE\.staticbit-xrpl-signer\keystore.json" "$env:USERPROFILE\Desktop\keystore-backup.json"
   ```
   ```bash
   # macOS / Linux
   cp ~/.staticbit-xrpl-signer/keystore.json ~/Desktop/keystore-backup.json
   ```

3. Передать файл на второй ПК **по безопасному каналу**:
   - ✅ USB-флешка (зашифрованная или сразу удалённая после копирования)
   - ✅ `scp` / `sftp` через SSH
   - ✅ Encrypted cloud (1Password Attach, Bitwarden Send)
   - ❌ Email, обычный Slack/Telegram/Discord, незашифрованный облачный диск
   
   > Файл сам по себе зашифрован AES-GCM с твоей passphrase, но защита всё-равно зависит от того насколько сильная passphrase. PBKDF2 600k iter делает brute-force очень дорогим, но не невозможным для словарных passphrase. Не давай файлу гулять по интернету в plaintext.

4. На втором ПК — пройти §1-9 INSTALL.md **до** §10 (не генерируй новые кошельки!). На §6.2 — задаёшь **ту же** passphrase что на первом ПК, не новую.

5. **Перед** или **после** установки ENV (но до рестарта Claude Code) — положить файл на место:
   ```powershell
   # Windows
   New-Item -ItemType Directory -Force "$env:USERPROFILE\.staticbit-xrpl-signer" | Out-Null
   Copy-Item "<откуда-перенёс>\keystore-backup.json" "$env:USERPROFILE\.staticbit-xrpl-signer\keystore.json"
   ```
   ```bash
   # macOS / Linux
   mkdir -p ~/.staticbit-xrpl-signer
   cp <откуда-перенёс>/keystore-backup.json ~/.staticbit-xrpl-signer/keystore.json
   chmod 600 ~/.staticbit-xrpl-signer/keystore.json
   ```

6. Рестарт Claude Code (§8) → проверить (§9):
   ```
   Покажи список XRPL кошельков
   ```
   Агент вызовет `xrpl_wallet_list` — должны вернуться **все** кошельки с первого ПК.

7. Удалить переданный файл `keystore-backup.json` с обоих ПК (он уже не нужен, оригинал на месте на каждом).

#### Что произойдёт если passphrase не совпадёт

Все wallet-tools (включая `xrpl_sign`) на втором ПК будут падать с:
```
Failed to decrypt wallet 'X'. The passphrase is likely wrong
(or the keystore file is corrupted).
```

Это не ломает keystore — просто значит signer не может расшифровать. Поставь правильную passphrase в ENV и рестартни Claude Code.

#### Синхронизация между ПК

Keystore — **локальный файл**. Изменения на одном ПК (добавил кошелёк, удалил, переименовал) **не** синхронизируются автоматически на другой ПК. Если такое нужно:

- Простой вариант — повторить перенос файла после изменений.
- Более продвинутый — положить файл в **зашифрованный** sync-folder (Cryptomator + Dropbox, например), не в plaintext-облако. Файл уже зашифрован keystore-passphrase'ой, но дополнительный слой не помешает.
- Помни про конкурентные изменения: оба ПК изменили keystore одновременно → один из наборов потеряется. Если работаешь с двух ПК активно — используй разные имена кошельков (например `main-desktop` и `main-laptop`) или просто разные keystore-файлы (через `XRPL_SIGNER_KEYSTORE_PATH`).

---

## 12. Удаление

### 12.1 Удалить один плагин

```powershell
claude plugin uninstall xrpl-cloud
claude plugin uninstall xrpl-local
claude plugin uninstall xrpl-signer
```

### 12.2 Удалить весь marketplace

```powershell
claude plugin marketplace remove staticbit-xrpl-mcp
```

Также удалит все плагины из этого marketplace.

### 12.3 Удалить keystore (полная очистка)

**Опасно** — все кошельки в нём теряются если нет бэкапа seed'ов.

```powershell
Remove-Item "$env:USERPROFILE\.staticbit-xrpl-signer" -Recurse
```
```bash
rm -rf ~/.staticbit-xrpl-signer
```

### 12.4 Удалить ENV-переменные

В ENV живёт только passphrase signer'а — у `xrpl-cloud` env-переменной для удаления нет (он использовал OAuth, а не bearer).

```powershell
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", $null, "User")
```
```bash
# В ~/.bashrc или ~/.zshrc удалить строку `export XRPL_SIGNER_PASSPHRASE=...`
unset XRPL_SIGNER_PASSPHRASE
```

### 12.5 Отозвать cloud-доступ

Для `xrpl-cloud` локального секрета для стирания нет. Чтобы полностью отрезать доступ: попроси админа отключить твой аккаунт в allow-list `xrpl.mcp.staticbit.ai` (это отзовёт твои refresh-токены) и очисти токен, сохранённый Claude Code, — `/mcp` → выбери `xrpl-cloud` → clear authentication.

---

## 13. Troubleshooting

### `/mcp` показывает `disconnected` или `needs login` для xrpl-cloud

| Что проверить | Команда |
|---|---|
| Вошёл через OAuth | `/mcp` → выбери `xrpl-cloud` → пройди браузерный вход на `auth.mcp.staticbit.ai` |
| Аккаунт в allow-list | Если вход отклонён — попроси админа `xrpl.mcp.staticbit.ai` добавить (или вернуть) твой аккаунт |
| Токен устарел | `/mcp` → выбери `xrpl-cloud` → clear authentication, затем войди заново |
| Сервер жив | `curl https://xrpl.mcp.staticbit.ai/healthz` — должно вернуть `{"status":"ok"}` |
| Рестартил Claude Code после установки | Закрыть полностью и запустить заново |

### `/mcp` показывает `disconnected` для xrpl-local или xrpl-signer

| Что проверить | Как |
|---|---|
| Node.js установлен | `node --version` — нужен 18+ |
| Бинарь существует | `claude plugin list` покажет путь к плагину; проверь что внутри `bin/<rid>/StaticBit.Xrpl.Mcp.*` есть файл под твою ОС |
| Passphrase задан (только signer) | `[Environment]::GetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", "User")` |
| Антивирус не блокирует | Self-contained .NET бинарь иногда триггерит ложное срабатывание Defender. Добавь папку плагина в exception |

### `Failed to decrypt wallet 'X'. The passphrase is likely wrong`

Изменил `XRPL_SIGNER_PASSPHRASE` после того как создал кошелёк. AES-GCM не расшифрует ciphertext с другим ключом — это by design. Варианты:
- Верни старую passphrase.
- Если потерял — кошелёк потерян (для этого keystore же шифровать). Восстанови из seed-бэкапа если был.

### Marketplace add падает с `authentication required`

PAT не сохранился или просрочился. Удали и добавь заново:
```powershell
claude plugin marketplace remove staticbit-xrpl-mcp
claude plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
```
Введи свежий PAT.

### `command not found: claude`

Claude Code не в PATH. Найди установку и добавь `bin/`:
- Windows: обычно `%LOCALAPPDATA%\Programs\Claude`
- macOS: `/Applications/Claude.app/Contents/MacOS/`
- Linux: где распаковал .deb / .AppImage

Или используй полный путь.

### Транзакция возвращает `tecPATH_DRY` / `tecUNFUNDED_PAYMENT`

Не баг плагина — это rippled-уровневая ошибка:
- `tecUNFUNDED_PAYMENT` — на отправляющем аккаунте недостаточно XRP (включая reserve = 1 XRP + 0.2 XRP за каждый owned object).
- `tecPATH_DRY` — для cross-currency payment не нашёлся ликвидный path. Попробуй через DEX напрямую.
- `tefMAX_LEDGER` — prepare сделан давно, ledger window закрылся. Сделай заново.
- `tem*` — malformed transaction (баг в плагине, сообщи).

### Хочу видеть подробные логи

Для cloud — попроси админа выгрузить из VPS:
```bash
ssh root@<vps> 'cd /opt/staticbit-xrpl-mcp && docker compose logs --tail 200 xrpl-mcp'
```

Для local / signer — Claude Code пишет stderr субпроцессов в свои логи. Открой `/logs` (или эквивалент в твоей версии).

### Получил много спама про auth failure в Telegram админ-чате

Скорее всего сторонние сканеры пробуют `.well-known/oauth-protected-resource/mcp` и подобные пути. У cloud-сервера есть фильтр который отбрасывает их с 404 без алерта (commit `45e267c`). Если всё ещё спамит — попроси админа обновить сервер.

### macOS — «"StaticBit.Xrpl.Mcp.Signer" cannot be opened because the developer cannot be verified»

Бинарь в плагине **не подписан Apple Developer ID** (см. [docs/supply-chain.md](../supply-chain.md) — подпись опциональна, владелец marketplace может её настроить или нет). Когда release-workflow собран с включёнными `APPLE_*` secrets — бинарь нотаризуется и Gatekeeper его пропускает; иначе нужно вручную снять quarantine-attribute, который macOS вешает на скачанный файл:

```bash
# Узнать путь к плагину
claude plugin list

# Снять quarantine с папки плагина целиком
xattr -dr com.apple.quarantine ~/.claude/plugins/xrpl-signer/

# Альтернатива: разрешить конкретный исполняемый файл через spctl
sudo spctl --add ~/.claude/plugins/xrpl-signer/bin/osx-arm64/StaticBit.Xrpl.Mcp.Signer
```

Если в System Settings → Privacy & Security появилось «App was blocked» — нажми «Open Anyway» сразу после неудачной попытки запуска.

### Windows — SmartScreen «Windows protected your PC» или Defender блокирует .exe

Та же история — бинарь без Authenticode-подписи. Опции:

1. **На SmartScreen-окне** нажать `More info` → `Run anyway`. Делается один раз для каждого бинаря после обновления плагина.
2. **Снять Mark-of-the-Web** с папки плагина:

   ```powershell
   $pluginDir = "$env:USERPROFILE\.claude\plugins\xrpl-signer"
   Get-ChildItem $pluginDir -Recurse | Unblock-File
   ```

3. **Добавить exclusion в Defender** (если он трогает single-file .NET бинарь):

   ```powershell
   # Запустить от админа
   Add-MpPreference -ExclusionPath "$env:USERPROFILE\.claude\plugins\xrpl-signer"
   Add-MpPreference -ExclusionPath "$env:USERPROFILE\.claude\plugins\xrpl-local"
   ```

   Self-contained AOT-style .NET бинарь иногда триггерит ложное срабатывание из-за самораспаковки нативных библиотек — это известное поведение и не баг плагина.

### Linux — SELinux/AppArmor блокирует signer

Self-contained .NET бинарь распаковывает нативные библиотеки в `/tmp` или `~/.cache/dotnet_bundle_extract` при первом запуске. На enforcing-SELinux (RHEL, Fedora) или strict AppArmor (Ubuntu server, snap-confined apps) это может быть deny.

Диагностика:

```bash
# SELinux
sudo ausearch -m AVC -ts recent | grep StaticBit
sudo setenforce 0   # временный permissive — для проверки гипотезы

# AppArmor
sudo journalctl -k | grep -E "DENIED.*StaticBit|DENIED.*dotnet"
sudo aa-status
```

Если выяснилось что блокирует:

- **SELinux** — навесить корректный context на папку плагина:
  ```bash
  sudo chcon -t bin_t ~/.claude/plugins/xrpl-signer/bin/linux-x64/StaticBit.Xrpl.Mcp.Signer
  # сделать постоянным:
  sudo semanage fcontext -a -t bin_t "$HOME/.claude/plugins/xrpl-signer/bin/linux-x64/.*"
  sudo restorecon -Rv ~/.claude/plugins/xrpl-signer/
  ```
- **AppArmor** — лучше всего исключить из confinement весь Claude Code, если он запущен под snap-профилем. Альтернатива — переместить плагины из `~/snap/claude-code/.../plugins/` в обычный `~/.claude/plugins/`.

Альтернатива во всех случаях — задать `DOTNET_BUNDLE_EXTRACT_BASE_DIR` на путь, который точно разрешён политикой:

```bash
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$HOME/.cache/staticbit-mcp"
mkdir -p "$DOTNET_BUNDLE_EXTRACT_BASE_DIR"
chmod 700 "$DOTNET_BUNDLE_EXTRACT_BASE_DIR"
```

Добавь в `~/.bashrc` / `~/.zshrc` или в env-блок плагина, чтобы persist'ило.

---

## Связанные документы

- [README.md](../../README.md) — обзор marketplace и доступных плагинов
- [plugins/xrpl-cloud/README.md](../../plugins/xrpl-cloud/README.md) — детали cloud-плагина
- [plugins/xrpl-local/README.md](../../plugins/xrpl-local/README.md) — детали local-плагина
- [plugins/xrpl-signer/README.md](../../plugins/xrpl-signer/README.md) — детали signer-плагина
- [StaticBit-io/staticbit-xrpl-mcp](https://github.com/StaticBit-io/staticbit-xrpl-mcp) — исходники cloud-сервера и signer'а
- [StaticBit-io/staticbit-xrpl-mcp/DEPLOY.md](https://github.com/StaticBit-io/staticbit-xrpl-mcp/blob/main/docs/DEPLOY.md) — для админа cloud-сервера: как развернуть свой инстанс
