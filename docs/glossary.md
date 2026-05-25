# Глоссарий XRPL

Короткий справочник терминов, которые встречаются в описаниях MCP-tool'ов плагина. Полная официальная справка — на [xrpl.org/docs](https://xrpl.org/docs).

## Деньги и единицы

### drops
Минимальная единица XRP. **1 XRP = 1 000 000 drops**. Все суммы XRP в XRPL-протоколе передаются как строка drops (uint64). В наших prepare-tools `amount` для XRP — это строка drops (`"10000000"` = 10 XRP), для токенов — JSON-объект `{value, currency, issuer}`.

### token amount
Сумма выпущенного токена. JSON-объект формата:
```json
{ "value": "100.50", "currency": "USD", "issuer": "rIssuerAddress..." }
```
- `value` — десятичная строка (XRPL поддерживает 15 значащих цифр).
- `currency` — 3-char ISO-код (`USD`, `EUR`) или 40-char hex для нестандартных.
- `issuer` — r-адрес эмитента.

### MPT (XLS-33)
Multi-Purpose Token — новый тип токенов с расширенными политиками. Идентифицируется `mpt_issuance_id` вместо пары `currency+issuer`. Поддержка в плагине ограничена тем, что есть в XrplCSharp SDK.

## Резервы и комиссии

### base reserve
Минимум XRP, который должен лежать на каждом аккаунте, чтобы сам аккаунт существовал в ledger'е. На mainnet это **1 XRP** (актуальное значение — через `xrpl_server_state`, поле `state.validated_ledger.reserve_base`).

### owner reserve
Дополнительные XRP за каждый `owned object` (trust line, offer, escrow, NFT, signer list и т.п.). На mainnet это **0.2 XRP за объект**. Поле `state.validated_ledger.reserve_inc`.

Общий требуемый минимум: `base + owner_count × owner_reserve`. Если `balance` упадёт ниже — все transactions, кроме `AccountDelete`, начнут отклоняться с `tecINSUFFICIENT_RESERVE`. `xrpl_tx_preflight` считает это автоматически.

### Fee
Транзакционная комиссия, drops. Минимум — `10 drops` (open-ledger reference fee). При нагрузке rippled поднимает «open-ledger fee» — узнаётся через `xrpl_fee`. Опция `FeeBumpMultiplier` в `XrplMcpOptions` позволяет проактивно умножить autofilled fee.

## Идентификаторы и временные окна

### Sequence
uint32-счётчик транзакций аккаунта. Каждая успешная или fee-payment'нувшая транзакция увеличивает Sequence на 1. Tx с неверным Sequence отклоняется. Autofill подставляет правильное значение из `account_info`.

### TicketSequence
Альтернатива `Sequence` — заранее зарезервированный «слот» через `TicketCreate`. Позволяет отправлять транзакции не строго в порядке.

### LastLedgerSequence (LLS)
Номер ledger'а, после которого транзакция не должна быть валидирована. Защищает от «застрявших» транзакций. Рекомендуется `current + 20` (наш `XrplMcpOptions.LastLedgerSequenceOffset`).

### Ripple epoch
XRPL хранит времена как «секунды с 2000-01-01T00:00:00Z». **Не Unix epoch.** Конвертация: `unix_time - 946684800`. SDK конвертит автоматически при использовании типизированных prepare-tools.

## Ledger states

Поле `ledger_index` в read-only tools принимает четыре варианта:

- **`validated`** (default) — последний полностью подтверждённый ledger. Финальные данные. Используй всегда, когда не нужна свежесть.
- **`closed`** — последний закрытый, но ещё не валидированный consensus'ом. Может откатиться.
- **`current`** — текущий открытый ledger, в который сейчас попадают новые tx. Не финален.
- **`<число>`** — конкретный исторический ledger sequence (если нода его хранит — `complete_ledgers` из `xrpl_server_info`).

## Trust lines

### TrustSet
Транзакция, которая создаёт или модифицирует trust line — двустороннее обязательство держать выпущенный кем-то токен с указанным лимитом. Bilateral: каждая сторона ставит свой `limit`. Trust line — ledger object типа `RippleState`, занимает 2 XRP owner reserve.

### NoRipple
Флаг trust line, который блокирует cross-currency rippling через эту линию. Когда обе стороны trust line ставят `NoRipple`, путь через эту пару исключается из path finding.

В `xrpl_account_lines`:
- `no_ripple` — флаг с твоей стороны.
- `no_ripple_peer` — флаг с другой стороны.

### DefaultRipple
Account-level флаг (`asfDefaultRipple = 8`). Если установлен — `NoRipple` по умолчанию **выключен** на новых trust line. Issuer'ы обычно его включают.

### Freeze / DeepFreeze
Замораживает trust line — счёт-холдер не может тратить токен. `tfSetFreeze`/`tfClearFreeze` в `TrustSet`. DeepFreeze (`tfSetDeepFreeze`) дополнительно блокирует получение. См. `xrpl_trustline_freeze_prepare`.

## Результаты транзакций

`engine_result` в ответе на `submit` или `tx`:

- **`tesSUCCESS`** — применена успешно.
- **`tec*`** — claimed fee, failed. Транзакция попала в ledger, fee списан, но эффект не достигнут (например, `tecUNFUNDED_OFFER`, `tecPATH_DRY`, `tecNO_PERMISSION`, `tecINSUFFICIENT_RESERVE`).
- **`tef*`** — failed before applying, no fee claimed. Не попала в ledger вообще (например, `tefMAX_LEDGER` — LLS уже прошёл).
- **`tem*`** — malformed transaction. Никогда не попадает в ledger. Если видишь `tem*` от наших prepare-tools — это баг, сообщи.
- **`ter*`** — retry, может пройти позже (например, `terPRE_SEQ` — Sequence ещё не достигнут).

Всегда проверяй `meta.TransactionResult`, не только HTTP status.

## DEX и AMM

### Offer / OfferCreate
Лимит-ордер на встроенной DEX. `TakerGets` (что отдаёшь) и `TakerPays` (что хочешь) — пара из XRP-drops или token amount. При создании ордер автоматически матчится с открытой стороной книги (частичное исполнение возможно). Флаги: `tfPassive`, `tfImmediateOrCancel`, `tfFillOrKill`, `tfSell`.

### AMM (XLS-30)
Automated Market Maker. Один pool = два актива в одном ledger object типа `AMM`. У pool'а есть **спец-аккаунт** (без master key), который владеет ассетами и выпускает **LP-tokens** (доля провайдера ликвидности). Trading fee 0..1% (basis points / 10). См. `xrpl_amm_*` tools.

### LP token
Issued token, который AMM выпускает депозиторам пропорционально внесённой ликвидности. Issuer = AMM-spec-аккаунт. При withdraw'е сжигается.

## Authorization

### Regular Key
Альтернативный ключ для подписи tx этого аккаунта. Назначается `SetRegularKey`. Можно отключить master key через `asfDisableMaster` (только если есть regular key или signer list — иначе аккаунт «замуровывается»).

### Signer List
Multi-sig: список других аккаунтов + их веса + порог (`SignerQuorum`). Аккаунт назначается через `SignerListSet`. Подписи собирают через `xrpl_sign_multi` + `xrpl_sign_combine`.

### DepositAuth
Account-флаг (`asfDepositAuth = 9`). Когда установлен — аккаунт не принимает Payment'ы кроме как от заранее авторизованных через `DepositPreauth`. Помогает регулируемым гейтвеям.

## Прочее

### Path / RipplePathFind
Для cross-currency Payment нужно явно указать `Paths` — последовательности промежуточных trust lines / order books / AMM pool'ов через которые течёт стоимость. `xrpl_ripple_path_find` находит варианты и возвращает `paths_computed` готовый к подстановке в prepare-tool.

### Clawback
Issuer'у выпущенного токена возвращает свои токены с балансов holder'ов. Требует `asfAllowTrustLineClawback` на issuer'e (установлен ДО первого выпуска токена). См. `xrpl_clawback_prepare`.

### Escrow
Условный/временной hold XRP (или с TokenEscrow amendment — токенов). Поддерживает `FinishAfter` (время), `CancelAfter` (deadline возврата отправителю), `Condition` (PREIMAGE-SHA-256 криптоусловие). См. `xrpl_escrow_*` tools.

### Payment Channel
Однонаправленный канал между sender и receiver. Sender фондирует, выписывает оффлайн-claim'ы (подписанные суммы), receiver когда-либо приходит и redeem'ит самый большой. Снижает on-chain нагрузку. См. `xrpl_payment_channel_*` tools.

### Check
Deferred payment — sender создаёт Check, receiver обязан явно cash'ить. Никаких автоматических переводов. Полезно для compliance flow. См. `xrpl_check_*` tools.

### NFT (XLS-20)
NFToken хранится в `NFTokenPage` владельца, идентифицируется 256-bit `NFTokenID`. Mint/Burn/Transfer через NFToken*Offer tx. URI хранится hex-encoded.
