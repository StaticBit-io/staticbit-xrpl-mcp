>  🌐 **Язык**: [English](../../examples/controlled-mpt-issuance.md) | **Русский**

# Пример: Controlled MPT issuance (XLS-33)

Cowork-агент управляет жизненным циклом Multi-Purpose Token'а: создаёт issuance с allow-listing + clawback, авторизует holder'а, переводит токены, при необходимости лочит и забирает обратно.

Использует ровно те последовательности, что валидируются интеграционными тестами XrplCSharp ([TestIMPTokenCreate](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIMPTokenCreate.cs), [TestIMPTokenAuthorize](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIMPTokenAuthorize.cs), [TestIMPTokenManage](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIMPTokenManage.cs)).

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_mpt_issuance_create_prepare`, `xrpl_mpt_authorize_prepare`, `xrpl_mpt_issuance_set_prepare`, `xrpl_payment_prepare`, `xrpl_mpt_issuance_destroy_prepare`, `xrpl_account_mpt_issuances`, `xrpl_account_mpts`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_tx_lookup` |
| **xrpl-signer** | `xrpl_sign` ×N |

## Архитектура и роли

```
ISSUER  ───── MPTokenIssuanceCreate ─────►  rippled
   │            (tfMPTRequireAuth + tfMPTCanClawback)
   │            ↓ returns MPTokenIssuanceID in metadata
   │
   │  HOLDER  ── MPTokenAuthorize (holder opt-in) ──►
   │  ISSUER  ── MPTokenAuthorize (issuer authorize holder) ──►
   │
   │  ISSUER  ── Payment to HOLDER with MPT amount ──►
   │
   │  (when needed)
   │  ISSUER  ── MPTokenIssuanceSet (tfMPTLock) ──►
   │  ISSUER  ── Clawback ──►
   │  ISSUER  ── MPTokenIssuanceDestroy ──►
```

**Двусторонняя авторизация при `tfMPTRequireAuth`**: holder должен submit'ить `MPTokenAuthorize` (opt-in), а issuer — отдельный `MPTokenAuthorize` с `Holder = <addr>` (одобрение).

## Pre-requisites

- 1 funded issuer-аккаунт.
- ≥ 1 funded holder-аккаунт.
- Master passphrase к `xrpl-signer` (env `XRPL_SIGNER_PASSPHRASE`).
- Оба аккаунта импортированы в keystore через `xrpl_wallet_import_seed`.

## Промт агента

```markdown
---
name: mpt-lifecycle
description: Manages a Multi-Purpose Token issuance with allow-listing — creates,
  authorizes holders bilaterally, sends payments, locks, claws back, destroys.
tools: xrpl_mpt_issuance_create_prepare, xrpl_mpt_authorize_prepare,
  xrpl_mpt_issuance_set_prepare, xrpl_payment_prepare,
  xrpl_mpt_issuance_destroy_prepare, xrpl_account_mpt_issuances,
  xrpl_account_mpts, xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_tx_lookup,
  xrpl_sign
---

You execute a single step of an MPT lifecycle.

Input is one of:
- `{"action":"create","network":"testnet","issuer":"r...","assetScale":2,
   "transferFee":0,"requireAuth":true,"canClawback":true,"canTransfer":false,
   "metadataPlain":"<optional>"}`
- `{"action":"authorize","network":"...","mptokenIssuanceId":"<48-hex>",
   "issuer":"r...","holder":"r...","role":"holder"|"issuer"}`
- `{"action":"pay","network":"...","mptokenIssuanceId":"<48-hex>",
   "issuer":"r...","destination":"r...","amount":"<decimal>"}`
- `{"action":"lock","mptokenIssuanceId":"<48-hex>","issuer":"r...","lock":true|false}`
- `{"action":"destroy","mptokenIssuanceId":"<48-hex>","issuer":"r..."}`

For every action:
1. Call the appropriate `*_prepare` tool with given parameters.
2. Pass the unsigned blob to `xrpl_tx_preflight` first; if `feasible=false`,
   return the warnings and stop.
3. Sign via `xrpl_sign(walletName, txBlobUnsigned)` (walletName == account).
4. Submit via `xrpl_tx_submit_signed(txBlobSigned, waitForValidation=true)`.
5. For `create` action: after success, call `xrpl_tx_lookup(txHash)` and
   extract `MPTokenIssuanceID` from metadata.affected_nodes.
6. Return `{txHash, engineResult, ...action-specific output}`.

Never sign without preflight; never proceed past `tec*` engine results.
```

## Пошаговая последовательность

### 1. Issuer creates the issuance

```text
USER → agent: {"action":"create","network":"testnet","issuer":"rIssuer...",
              "assetScale":2,"requireAuth":true,"canClawback":true}
```

Что делает агент:
1. `xrpl_mpt_issuance_create_prepare(network, account=issuer, assetScale=2, requireAuth=true, canClawback=true)` → unsigned blob.
2. `xrpl_tx_preflight(txJson)` — проверка резерва, что AssetScale ≤ 10, что transferFee требует canTransfer.
3. `xrpl_sign(walletName=issuer, txBlobUnsigned)` через xrpl-signer.
4. `xrpl_tx_submit_signed(txBlobSigned, waitForValidation=true)`.
5. После `tesSUCCESS` → `xrpl_tx_lookup(txHash)` → parse metadata → `MPTokenIssuanceID` (48-hex).

Возврат: `{mptokenIssuanceId, txHash, ledgerIndex}`.

### 2. Bilateral authorization

При `tfMPTRequireAuth` нужно **две** транзакции:

```text
agent: {"action":"authorize", ..., "holder":"rHolder...", "role":"holder"}
agent: {"action":"authorize", ..., "holder":"rHolder...", "role":"issuer"}
```

- **role=holder**: `account=holder`, без `holder`-параметра (это opt-in от самого holder'а).
- **role=issuer**: `account=issuer`, `holder=<addr>` (issuer одобряет конкретного holder'а).

Без allow-listing (`requireAuth=false`) нужен только holder opt-in.

### 3. Payment with MPT amount

```text
agent: {"action":"pay", ..., "destination":"rHolder...", "amount":"500"}
```

Внутри prepare формируется amount в shape:
```json
{ "value": "500", "mpt_issuance_id": "<48-hex>" }
```

`xrpl_payment_prepare` принимает это через `amount` parameter в JSON-формате. Holder должен быть **уже** authorized (см. шаг 2), иначе `tecNO_PERMISSION`.

### 4. Lock balances (optional)

```text
agent: {"action":"lock","lock":true}
```

`xrpl_mpt_issuance_set_prepare(lockBalance=true)` → `tfMPTLock`. Все балансы MPT'а становятся frozen — payments фейлятся с `tecLOCKED`. Снять: `lock=false` → `tfMPTUnlock`.

### 5. Per-holder lock

Лочить отдельного holder'а без глобального lock:

```text
agent: {"action":"lock","lock":true,"holder":"rBadActor..."}
```

`xrpl_mpt_issuance_set_prepare(lockBalance=true, holder="rBadActor")`. Остальные holder'ы продолжают торговать.

### 6. Clawback

При `tfMPTCanClawback` issuer может вернуть свои tokens:

```text
agent: {"action":"clawback","holder":"rBadActor...","amount":"500"}
```

Использует existing `xrpl_clawback_prepare` с MPT-shape amount.

### 7. Destroy

```text
agent: {"action":"destroy"}
```

Доступен только при `OutstandingAmount=0`. Если balances остались — `tecHAS_OBLIGATIONS`. Сначала clawback'нуть всё, потом destroy.

## Verification checklist

После каждого шага:

- [ ] `engine_result == "tesSUCCESS"`.
- [ ] `meta.TransactionResult == "tesSUCCESS"`.
- [ ] Для create: `xrpl_account_mpt_issuances(account=issuer)` показывает новый issuance с правильными флагами.
- [ ] Для holder authorize: `xrpl_account_mpts(account=holder)` показывает holding с `accepted=true` (если allow-listing — после issuer-side authorize).
- [ ] Для pay: `xrpl_account_mpts(account=holder)` показывает увеличение `amount`.
- [ ] Для lock: `xrpl_account_mpts(account=holder)` → `locked=true`.

## Тестовые предпосылки и known issues

- На стандартном testnet (`s.altnet.rippletest.net:51233`) XLS-33 **активирован** — рецепт работает out of the box.
- Если работаете против custom rippled — проверьте `xrpl_server_state` → `feature` массив содержит `MPTokensV1` enabled.
- Holder без opt-in'а получит `tecNO_AUTH` при попытке Payment.
- TransferFee > 0 требует `canTransfer=true` (preflight это поймает).

## Возможные расширения

- **Метадата по XLS-89**: `metadataPlain` сериализуется в JSON `{"n":"My Token","w":"example.com"}` и hex-кодируется — SDK предоставляет `MPTokenMetadataSchema`. У нас аналог — передать готовый hex в `metadataHex`.
- **Auction-style minting**: использовать `tfMPTCanTrade` + AMM-пул из этих MPT'ов.
- **Multi-issuer cooperation**: issuer authorizes signer-list-set, передаёт control нескольким accounts.
