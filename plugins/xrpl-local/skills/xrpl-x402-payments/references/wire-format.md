# x402 (t54 XRPL exact scheme) — wire format reference

Authoritative contract for the `xrpl-x402-payments` skill. Verified byte-for-byte against the released
`Xrpl.X402` v1.0.0 package (`X402PaymentBuilder`, `Wire/X402Headers`, `Wire/PaymentRequirement`,
`Wire/PaymentSignatureEnvelope`, `Wire/X402Base64Json`). When a field here disagrees with that package,
the package wins — update this file.

## HTTP headers

| Header | Direction | Carries (base64 of UTF-8 JSON) |
|---|---|---|
| `PAYMENT-REQUIRED` | server → client (on HTTP 402) | `PaymentRequiredChallenge` |
| `PAYMENT-SIGNATURE` | client → server (retry) | `PaymentSignatureEnvelope` |
| `PAYMENT-RESPONSE` | server → client (on 200) | settlement receipt |

Encoding is **standard base64 of UTF-8 JSON**; `null` fields are omitted from the JSON.

## Challenge: `PAYMENT-REQUIRED`

```json
{
  "x402Version": 2,
  "accepts": [
    {
      "scheme": "exact",
      "network": "xrpl:1",
      "asset": "XRP",
      "payTo": "rMerchant...",
      "amount": "1000000",
      "maxTimeoutSeconds": 60,
      "extra": {
        "invoiceId": "inv-abc",
        "sourceTag": 804681468,
        "issuer": "rIssuer...",
        "destinationTag": 12345
      }
    }
  ]
}
```

- `amount` — XRP drops as a decimal string, OR IOU value string (e.g. `"2.5"`).
- `asset` — `"XRP"`, or a token code: 3-char (`"USD"`) or 40-hex (RLUSD = `524C555344000000000000000000000000000000`).
- `extra.invoiceId` — **required** (any non-empty string).
- `extra.sourceTag` — t54 sets `804681468` and **enforces** it on the tx.
- `extra.issuer` — **required for IOU** assets (the token issuer).
- `extra.destinationTag` — optional.

### Selecting a requirement

Pick the entry with `scheme == "exact"` AND `network == <your target>` AND `asset` ∈ {XRP, your RLUSD hex}.

## Networks (CAIP-2)

| id | meaning |
|---|---|
| `xrpl:1` | t54 testnet — confirmed live against `xrpl-facilitator-testnet.t54.ai` (`/verify` isValid + `/settle` on-chain, XRP + RLUSD) |
| `xrpl:0` | mainnet (real funds) |

## Building the XRPL Payment

Set on the `Payment` (binding mode = `Both`, the t54 reference-payer default):

| Field | Value |
|---|---|
| `Account` | payer (classic r-address) |
| `Destination` | `requirement.payTo` |
| `Amount` | XRP: drops string. IOU: `{"value":<amount>,"currency":<asset>,"issuer":<extra.issuer>}` |
| `SendMax` | **IOU only** — identical `{value,currency,issuer}` to `Amount` |
| `SourceTag` | `extra.sourceTag` (pass it **explicitly** — see note) |
| `DestinationTag` | `extra.destinationTag` if present, else omit |
| `InvoiceID` | `SHA-256(UTF-8(invoiceId))`, **uppercase hex** (64 chars) |
| `Memos` | exactly one: `[{"Memo":{"MemoData":"<hexUpper(UTF-8(invoiceId))>"}}]` — **no** `MemoType`/`MemoFormat` |

> **SourceTag note:** always pass `extra.sourceTag` explicitly into the prepare call. If you omit it,
> `TransactionPreparer.ApplyDefaultSourceTag` stamps the MCP default `100010011`, which the t54
> facilitator will reject (it enforces the x402 protocol tag `804681468`).

## Envelope: `PAYMENT-SIGNATURE`

```json
{
  "x402Version": 2,
  "accepted": { /* the chosen requirement, verbatim */ },
  "payload": { "signedTxBlob": "<hex>", "invoiceId": "<raw invoiceId string>" }
}
```

`payload.invoiceId` is the **raw** string (not the SHA-256), and is **required** by t54.

## Deterministic shell snippets

The crypto fields are computed with exact commands, never assembled by hand.

```bash
INV='inv-abc'                         # raw invoiceId from extra.invoiceId

# InvoiceID field = SHA-256(UTF-8(invoiceId)), uppercase hex
INV_ID=$(printf %s "$INV" | sha256sum | cut -d' ' -f1 | tr 'a-f' 'A-F')

# Memo MemoData = hex(UTF-8(invoiceId)), uppercase
MEMO_HEX=$(printf %s "$INV" | xxd -p -c0 | tr 'a-f' 'A-F')

# Decode the challenge header value
CH=$(printf %s "$HDR" | base64 -d)

# Select the requirement (exact + target network); $NET e.g. "xrpl:1"
REQ=$(printf %s "$CH" | jq -c --arg net "$NET" \
  '.accepts[] | select(.scheme=="exact" and .network==$net)' | head -1)

# Build the Payment txJson — XRP
TX=$(jq -cn --arg acc "$PAYER" --argjson r "$REQ" --arg id "$INV_ID" --arg m "$MEMO_HEX" '
  { TransactionType:"Payment", Account:$acc, Destination:($r.payTo),
    Amount:($r.amount), SourceTag:($r.extra.sourceTag), InvoiceID:$id,
    Memos:[{Memo:{MemoData:$m}}] }
  + (if $r.extra.destinationTag then {DestinationTag:$r.extra.destinationTag} else {} end)')

# Build the Payment txJson — IOU (adds Amount object + SendMax)
TX=$(jq -cn --arg acc "$PAYER" --argjson r "$REQ" --arg id "$INV_ID" --arg m "$MEMO_HEX" '
  ($r.extra.issuer) as $iss |
  { value:($r.amount), currency:($r.asset), issuer:$iss } as $amt |
  { TransactionType:"Payment", Account:$acc, Destination:($r.payTo),
    Amount:$amt, SendMax:$amt, SourceTag:($r.extra.sourceTag), InvoiceID:$id,
    Memos:[{Memo:{MemoData:$m}}] }
  + (if $r.extra.destinationTag then {DestinationTag:$r.extra.destinationTag} else {} end)')

# Encode the PAYMENT-SIGNATURE envelope after signing → $BLOB is the signed tx_blob
ENV=$(jq -cn --arg b "$BLOB" --arg i "$INV" --argjson r "$REQ" \
  '{x402Version:2, accepted:$r, payload:{signedTxBlob:$b, invoiceId:$i}}' | base64 -w0)
```

> `jq` emits `sourceTag` as a number — correct for `xrpl_tx_prepare_generic` (it normalizes JSON
> numbers to native integers). Do not quote it into a string.
