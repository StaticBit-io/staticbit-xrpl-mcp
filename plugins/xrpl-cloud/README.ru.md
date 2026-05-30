>  🌐 **Язык**: [English](README.md) | **Русский**

# xrpl-cloud plugin

Лёгкий HTTP MCP-клиент к нашему cloud XRPL-серверу на `xrpl-mcp.staticbit.io`. Никаких бинарей, никаких локальных процессов — Claude Code аутентифицируется к серверу через **OAuth 2.1** (выполни `/mcp` один раз для входа).

## Когда выбирать этот плагин

- **Cowork / 24/7 routines** — серверный агент должен работать без зависимости от твоей машины.
- **Mobile / lightweight** — не хочешь скачивать ~100 MB local-сервера.
- **Multi-device** — вход через OAuth на каждом устройстве; одна XRPL-конфигурация на сервере.

Если ты privacy-sensitive (не хочешь чтобы админ cloud-сервера видел traffic к XRPL нодам), смотри в сторону `xrpl-local`.

## Установка

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
/plugin install xrpl-cloud@staticbit-xrpl-mcp
```

### Аутентификация (OAuth 2.1)

Сервер защищён OAuth; войти могут только аккаунты из **allow-list** — попроси админа `xrpl-mcp.staticbit.io` добавить твой аккаунт. Никаких bearer/ENV задавать не нужно. Затем в Claude Code:

```
/mcp
```

Пройди вход в браузере к `auth.mcp.staticbit.io` — Claude Code сделает dynamic client registration, сохранит токен и будет обновлять его автоматически.

## Проверка

```
/mcp
```
```
xrpl-cloud  https://xrpl-mcp.staticbit.io/mcp (HTTP)  ✓ Connected
```

Все <!-- toolcount:xrpl -->116<!-- /toolcount:xrpl --> tools доступны как `mcp__plugin_xrpl-cloud_xrpl-cloud__*`. Полный per-parameter reference — [`docs/TOOLS.generated.md`](../../docs/TOOLS.generated.md). Категории с типичными представителями:
- **read** (server / account / ledger): `server_info`, `server_state`, `server_definitions`, `manifest`, `fee`, `ledger`, `tx_lookup`, `account_{info,lines,tx,tx_since,offers,objects,nfts,mpts,mpt_issuances,vaults,credentials,permissioned_domains,escrows,bridges,did,loans,loan_brokers}`, `xrp_balance`, `book_offers`, `gateway_balances`, `ripple_path_find`
- **read** (XLS-aware): `amm_info`, `nft_{buy,sell}_offers`, `signer_list_status`, `tx_simulate`, `path_find_*`, `hash_credential`
- **prepare** (DEX / payments / accounts): `payment_prepare`, `trustset_prepare`, `offer_create_prepare`, `offer_cancel_prepare`, `account_set_prepare`, `set_regular_key_prepare`, `signer_list_set_prepare`, `deposit_preauth_prepare`, `ticket_create_prepare`, `account_delete_prepare`, `clawback_prepare`, `trustline_freeze_prepare`, `delegate_set_prepare`, `batch_prepare`, `tx_prepare_generic`
- **prepare** (AMM): `amm_create_prepare`, `amm_deposit_prepare`, `amm_withdraw_prepare`, `amm_vote_prepare`, `amm_bid_prepare`, `amm_clawback_prepare`, `amm_delete_prepare`
- **prepare** (NFT): `nft_mint_prepare`, `nft_burn_prepare`, `nft_modify_prepare`, `nft_create_offer_prepare`, `nft_accept_offer_prepare`, `nft_cancel_offer_prepare`
- **prepare** (Vault): `vault_create_prepare`, `vault_set_prepare`, `vault_deposit_prepare`, `vault_withdraw_prepare`, `vault_clawback_prepare`, `vault_delete_prepare`
- **prepare** (Loan + LoanBroker): `loan_set_prepare`, `loan_manage_prepare`, `loan_pay_prepare`, `loan_delete_prepare`, `loan_broker_{set,delete,cover_deposit,cover_withdraw,cover_clawback}_prepare`
- **prepare** (Credentials / DID / Oracle / MPT / PermissionedDomain): `credential_{create,accept,delete}_prepare`, `did_{set,delete}_prepare`, `oracle_{set,delete}_prepare`, `mpt_issuance_{create,destroy,set}_prepare`, `mpt_authorize_prepare`, `permissioned_domain_{set,delete}_prepare`
- **prepare** (Escrow / Checks / PaymentChannel): `escrow_{create,finish,cancel}_prepare`, `check_{create,cash,cancel}_prepare`, `payment_channel_{create,fund,claim}_prepare`
- **prepare** (xChain bridges): `xchain_{create_bridge,modify_bridge,commit,claim,create_claim_id,account_create_commit,add_account_create_attestation,add_claim_attestation}_prepare`
- **submit / decode**: `tx_submit_signed`, `tx_decode_blob`, `tx_explain`, `tx_preflight`, `subscribe`, `unsubscribe`, `path_find_close`

## Подписание транзакций

Этот плагин **только** проксирует к серверу, **не** имеет ключей. Чтобы реально отправлять транзакции, поставь рядом `xrpl-signer`:

```
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

Cloud делает `prepare` → signer (локально) делает `sign` → cloud делает `submit_signed`. Подробности — в README каждого плагина и в их skills.

## Безопасность

- Cloud-сервер **никогда** не получает seed/private key — все write-tools принимают только подписанный blob.
- Аутентификация — OAuth 2.1: сервер валидирует короткоживущие JWT от `auth.mcp.staticbit.io`; войти могут только аккаунты из allow-list. Неудачные попытки летят в Telegram админу VPS.
- HTTPS-only.
