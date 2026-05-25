using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
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

    public SignTools(IKeystore keystore)
    {
        _keystore = keystore;
    }

    [McpServerTool(Name = "xrpl_sign")]
    [Description("Signs a transaction with the named wallet's private key. The transaction can be supplied as a JSON object or as an unsigned hex blob (the same value xrpl_payment_prepare returns as 'txBlobUnsigned'). Returns the signed hex blob and its hash — ready to feed to xrpl_tx_submit_signed.")]
    public object Sign(
        [Description("Wallet alias from the keystore.")] string name,
        [Description("Transaction to sign. Either a JSON object with 'TransactionType', 'Account', etc., OR a hex blob string from a *_prepare tool.")] string transaction)
    {
        Dictionary<string, object> tx = ParseTransaction(transaction);
        XrplWallet wallet = LoadWallet(name);
        SignatureResult result = wallet.Sign(tx);

        return new
        {
            name,
            address = wallet.ClassicAddress,
            txBlob = result.TxBlob,
            hash = result.Hash,
        };
    }

    [McpServerTool(Name = "xrpl_sign_multi")]
    [Description("Produces a single-slot multi-sign for a transaction. Each authorized signer runs this once; the resulting partial-signed transactions are then aggregated with xrpl_sign_combine before submission. Returns the signed hex blob containing this signer's Signers entry.")]
    public object SignMulti(
        [Description("Wallet alias from the keystore — used as one of the multi-sign authorized accounts.")] string name,
        [Description("Transaction to sign — JSON object or hex blob. The transaction's SigningPubKey must be empty string for multi-sign.")] string transaction,
        [Description("Optional: the account this signature is FOR (defaults to the wallet's own classic address). Use this only if the wallet is a Regular Key signing on behalf of a different master account.")] string? signingFor = null)
    {
        Dictionary<string, object> tx = ParseTransaction(transaction);
        XrplWallet wallet = LoadWallet(name);
        SignatureResult result = wallet.Sign(tx, multisign: true, signingFor: signingFor);

        return new
        {
            name,
            signerAccount = string.IsNullOrEmpty(signingFor) ? wallet.ClassicAddress : signingFor,
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

        return new
        {
            txBlob = combinedBlob,
            hash,
            signerCount = blobs.Length,
        };
    }

    // ────────────────────────────────────────────────────────────────────────

    private XrplWallet LoadWallet(string name)
    {
        WalletMetadata? meta = _keystore.GetMetadata(name);
        if (meta is null)
        {
            throw new KeyNotFoundException(
                $"Wallet '{name}' not found in keystore. Use xrpl_wallet_list to see available wallets " +
                $"or xrpl_wallet_generate / xrpl_wallet_import_* to add one.");
        }

        string seed = _keystore.GetSeed(name);
        return XrplWallet.FromSeed(seed, masterAddress: null, algorithm: meta.Algorithm);
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
