using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Signer.Audit;
using StaticBit.Xrpl.Mcp.Signer.Keystore;
using Xrpl.BinaryCodec;
using Xrpl.Wallet;

namespace StaticBit.Xrpl.Mcp.Signer.Tools;

/// <summary>
/// Transaction signing tools — single-sign, multi-sign (single slot) and
/// combine (aggregate). All purely-local crypto operations; no network calls.
/// </summary>
[McpServerToolType]
public sealed class SignTools
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
    };

    private readonly IKeystore _keystore;
    private readonly IAuditLogger _audit;

    public SignTools(IKeystore keystore, IAuditLogger audit)
    {
        _keystore = keystore;
        _audit = audit;
    }

    [McpServerTool(Name = "xrpl_sign")]
    [Description("Signs a transaction with the named wallet's private key. The transaction can be supplied as a JSON object or as an unsigned hex blob (the same value xrpl_payment_prepare returns as 'txBlobUnsigned'). For HD (mnemonic-kind) wallets, pass 'index' to select which derived account to sign with (default 0). Returns the signed hex blob and its hash — ready to feed to xrpl_tx_submit_signed.")]
    public object Sign(
        [Description("Wallet alias from the keystore.")] string name,
        [Description("Transaction to sign. Either a JSON object with 'TransactionType', 'Account', etc., OR a hex blob string from a *_prepare tool.")] string transaction,
        [Description("HD account index (0..2^31-1) for mnemonic-kind wallets. Ignored for seed-kind wallets. Default 0.")] int index = 0)
    {
        Dictionary<string, object> tx = ParseTransaction(transaction);
        XrplWallet wallet = LoadWalletOrAuditFail(name, index);

        SignatureResult result;
        try
        {
            result = wallet.Sign(tx);
        }
        catch (Exception ex)
        {
            _audit.LogSignError(name, ex.Message);
            throw;
        }

        _audit.LogSign(name, IndexForAudit(name, index), signMode: "single",
            txHash: result.Hash, txType: TryReadTxType(tx));

        return new
        {
            name,
            address = wallet.ClassicAddress,
            index,
            txBlob = result.TxBlob,
            hash = result.Hash,
        };
    }

    [McpServerTool(Name = "xrpl_sign_multi")]
    [Description("Produces a single-slot multi-sign for a transaction. Each authorized signer runs this once; the resulting partial-signed transactions are then aggregated with xrpl_sign_combine before submission. Returns the signed hex blob containing this signer's Signers entry.")]
    public object SignMulti(
        [Description("Wallet alias from the keystore — used as one of the multi-sign authorized accounts.")] string name,
        [Description("Transaction to sign — JSON object or hex blob. The transaction's SigningPubKey must be empty string for multi-sign.")] string transaction,
        [Description("Optional: the account this signature is FOR (defaults to the wallet's own classic address). Use this only if the wallet is a Regular Key signing on behalf of a different master account.")] string? signingFor = null,
        [Description("HD account index for mnemonic-kind wallets. Ignored for seed-kind. Default 0.")] int index = 0)
    {
        Dictionary<string, object> tx = ParseTransaction(transaction);
        XrplWallet wallet = LoadWalletOrAuditFail(name, index);

        SignatureResult result;
        try
        {
            result = wallet.Sign(tx, multisign: true, signingFor: signingFor);
        }
        catch (Exception ex)
        {
            _audit.LogSignError(name, ex.Message);
            throw;
        }

        _audit.LogSign(name, IndexForAudit(name, index), signMode: "multi",
            txHash: result.Hash, txType: TryReadTxType(tx));

        return new
        {
            name,
            signerAccount = string.IsNullOrEmpty(signingFor) ? wallet.ClassicAddress : signingFor,
            index,
            txBlob = result.TxBlob,
            hash = result.Hash,
        };
    }

    [McpServerTool(Name = "xrpl_sign_combine")]
    [Description("Aggregates several multi-signed partial-blobs into one fully-signed transaction blob. Pass the array of hex blobs produced by xrpl_sign_multi from each authorized signer. Returns the combined signed blob and its hash — ready for xrpl_tx_submit_signed.")]
    public object SignCombine(
        [Description("Array of multi-signed transaction blobs (hex strings) — output of xrpl_sign_multi from each signer. Accept as JSON array string '[\"blob1\",\"blob2\",...]' or as newline-separated blobs.")] string signedBlobs)
    {
        string[] blobs = ParseBlobArray(signedBlobs);
        if (blobs.Length < 2)
        {
            throw new ArgumentException(
                $"Multi-sign combine requires at least 2 partial signatures, got {blobs.Length}.",
                nameof(signedBlobs));
        }

        string combinedBlob = global::Xrpl.Wallet.Signer.Multisign(blobs);

        // Decode to compute the hash from the canonical form. The combined blob
        // already contains all Signers entries — decoding lets us return the hash
        // without re-signing.
        JsonNode? decoded = XrplBinaryCodec.Decode(combinedBlob);
        string? hash = TryGetTransactionHash(decoded);
        string? txType = decoded is JsonObject obj && obj["TransactionType"] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

        _audit.LogSign(wallet: $"<combine:{blobs.Length}>", index: null, signMode: "combine", txHash: hash, txType: txType);

        return new
        {
            txBlob = combinedBlob,
            hash,
            signerCount = blobs.Length,
        };
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decrypts the wallet for signing. For seed-kind wallets the index must be 0.
    /// For mnemonic-kind wallets, derives at the given index using the entry's
    /// derivation path template. Audit-logs decrypt failures with a reason.
    /// </summary>
    private XrplWallet LoadWalletOrAuditFail(string name, int index)
    {
        WalletMetadata? meta = _keystore.GetMetadata(name);
        if (meta is null)
        {
            _audit.LogDecryptFail(name, "not_found");
            throw new KeyNotFoundException(
                $"Wallet '{name}' not found in keystore. Use xrpl_wallet_list to see available wallets " +
                $"or xrpl_wallet_generate / xrpl_wallet_import_* to add one.");
        }

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be non-negative.");
        }

        if (string.Equals(meta.Kind, "mnemonic", StringComparison.Ordinal))
        {
            string mnemonic;
            string? bip39Passphrase;
            try
            {
                mnemonic = _keystore.GetMnemonic(name);
                bip39Passphrase = _keystore.GetBip39Passphrase(name);
            }
            catch (Exception ex)
            {
                _audit.LogDecryptFail(name, ex.GetType().Name);
                throw;
            }

            string template = meta.DerivationPathTemplate ?? "m/44'/144'/{i}'/0/0";
            string path = template.Replace("{i}", index.ToString(CultureInfo.InvariantCulture));

            return XrplWallet.FromMnemonic(
                mnemonic: mnemonic,
                masterAddress: null,
                derivationPath: path,
                encoding: null,
                algorithm: meta.Algorithm,
                passphrase: bip39Passphrase);
        }

        // seed-kind
        if (index != 0)
        {
            throw new ArgumentException(
                $"Wallet '{name}' is kind='seed' (single address); 'index' must be 0, got {index}. " +
                $"Only mnemonic-kind (HD) wallets support multiple indices.",
                nameof(index));
        }

        string seed;
        try
        {
            seed = _keystore.GetSeed(name);
        }
        catch (Exception ex)
        {
            _audit.LogDecryptFail(name, ex.GetType().Name);
            throw;
        }
        return XrplWallet.FromSeed(seed, masterAddress: null, algorithm: meta.Algorithm);
    }

    /// <summary>
    /// Audit log records the index only for HD wallets — leaves null for
    /// seed-kind to avoid implying multi-address semantics that aren't there.
    /// </summary>
    private int? IndexForAudit(string name, int index)
    {
        WalletMetadata? meta = _keystore.GetMetadata(name);
        return meta is not null && string.Equals(meta.Kind, "mnemonic", StringComparison.Ordinal)
            ? index
            : null;
    }

    private static string? TryReadTxType(Dictionary<string, object> tx)
    {
        if (tx.TryGetValue("TransactionType", out object? v) && v is not null)
        {
            return v.ToString();
        }
        return null;
    }

    private static Dictionary<string, object> ParseTransaction(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("Transaction is empty.", nameof(raw));
        }

        string trimmed = raw.Trim();
        if (LooksLikeHex(trimmed))
        {
            JsonNode? decoded = XrplBinaryCodec.Decode(trimmed)
                ?? throw new ArgumentException(
                    "Failed to decode hex blob — value is not a valid XRPL serialized transaction.",
                    nameof(raw));
            string json = decoded.ToJsonString();
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions)
                ?? throw new InvalidOperationException("Decoded transaction is not a JSON object.");
        }

        return JsonSerializer.Deserialize<Dictionary<string, object>>(trimmed, JsonOptions)
            ?? throw new ArgumentException(
                "Transaction is neither valid JSON nor a hex blob.",
                nameof(raw));
    }

    private static string[] ParseBlobArray(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Blob list is empty.", nameof(raw));
        string trimmed = raw.Trim();

        if (trimmed.StartsWith('['))
        {
            using JsonDocument doc = JsonDocument.Parse(trimmed);
            return doc.RootElement
                .EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }

        return trimmed
            .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static bool LooksLikeHex(string s)
    {
        if (s.Length == 0 || s.Length % 2 != 0) return false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }

    private static string? TryGetTransactionHash(JsonNode? decoded)
    {
        if (decoded is null) return null;
        try
        {
            JsonObject? obj = decoded.AsObject();
            if (obj.TryGetPropertyValue("hash", out JsonNode? hashNode) && hashNode is not null)
            {
                return hashNode.GetValue<string>();
            }
            if (obj.TryGetPropertyValue("Hash", out JsonNode? upperHashNode) && upperHashNode is not null)
            {
                return upperHashNode.GetValue<string>();
            }
        }
        catch
        {
            // Combined blob doesn't always carry a hash field in decoded form —
            // tx hash is computed at submission time. Return null and let the
            // caller request it from xrpl-cloud after submit.
        }
        return null;
    }
}
