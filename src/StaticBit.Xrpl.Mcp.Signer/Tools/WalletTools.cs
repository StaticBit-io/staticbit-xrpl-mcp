using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Signer.Keystore;
using Xrpl.Wallet;

namespace StaticBit.Xrpl.Mcp.Signer.Tools;

/// <summary>
/// Wallet management tools — generate, import (seed / mnemonic / Xumm numbers /
/// normalized text), list, look up, remove, export. All operations are local;
/// nothing leaves the process aside from the MCP response on stdout.
/// </summary>
[McpServerToolType]
public sealed class WalletTools
{
    private readonly IKeystore _keystore;

    public WalletTools(IKeystore keystore)
    {
        _keystore = keystore;
    }

    [McpServerTool(Name = "xrpl_wallet_generate")]
    [Description("Generates a brand-new XRPL wallet with random entropy, encrypts the seed with the keystore passphrase, and stores it under the given name. Returns address and public key. The seed is NEVER returned by this tool — use xrpl_wallet_export to back it up.")]
    public object Generate(
        [Description("Wallet alias used in subsequent sign calls. Allowed chars: letters, digits, '-', '_', '.'.")] string name,
        [Description("Signing curve: 'ed25519' (default) or 'secp256k1'.")] string algorithm = "ed25519")
    {
        EnsureFreeName(name);
        XrplWallet wallet = XrplWallet.Generate(algorithm);
        return PersistAndDescribe(name, wallet, algorithm);
    }

    [McpServerTool(Name = "xrpl_wallet_import_seed")]
    [Description("Imports an existing XRPL seed string (e.g. 'sEd...' for Ed25519, 'sn...' for secp256k1). Encrypted and stored under the given name.")]
    public object ImportSeed(
        [Description("Wallet alias.")] string name,
        [Description("XRPL seed — typically starts with 'sEd' or 'sn'.")] string seed,
        [Description("Optional algorithm override. If omitted the SDK infers it from the seed prefix.")] string? algorithm = null)
    {
        EnsureFreeName(name);
        if (string.IsNullOrWhiteSpace(seed)) throw new ArgumentException("Seed is empty.", nameof(seed));

        XrplWallet wallet = XrplWallet.FromSeed(seed.Trim(), masterAddress: null, algorithm: algorithm);
        return PersistAndDescribe(name, wallet, ResolveAlgorithm(algorithm, wallet));
    }

    [McpServerTool(Name = "xrpl_wallet_import_mnemonic")]
    [Description("Imports a wallet from a BIP39 mnemonic phrase (12/15/18/21/24 words). Optionally a BIP44 derivation path and BIP39 passphrase. The mnemonic itself is converted to a seed and the seed is encrypted in the keystore.")]
    public object ImportMnemonic(
        [Description("Wallet alias.")] string name,
        [Description("BIP39 mnemonic — words separated by single spaces.")] string mnemonic,
        [Description("Optional BIP44 derivation path. Default: \"m/44'/144'/0'/0/0\" (XRPL standard).")] string? derivationPath = null,
        [Description("Optional BIP39 passphrase that augments the mnemonic. Empty by default.")] string? bip39Passphrase = null,
        [Description("Optional algorithm override: 'ed25519' or 'secp256k1' (secp256k1 is the BIP39 default).")] string? algorithm = null)
    {
        EnsureFreeName(name);
        if (string.IsNullOrWhiteSpace(mnemonic)) throw new ArgumentException("Mnemonic is empty.", nameof(mnemonic));

        XrplWallet wallet = XrplWallet.FromMnemonic(
            mnemonic: mnemonic.Trim(),
            masterAddress: null,
            derivationPath: derivationPath,
            encoding: null,
            algorithm: algorithm,
            passphrase: bip39Passphrase);

        if (wallet.Seed is null)
        {
            throw new InvalidOperationException(
                "Imported wallet has no seed — cannot persist. Mnemonic-derived wallets must round-trip through a seed.");
        }

        return PersistAndDescribe(name, wallet, ResolveAlgorithm(algorithm, wallet));
    }

    [McpServerTool(Name = "xrpl_wallet_import_xumm")]
    [Description("Imports a wallet from Xumm 'secret numbers' format (8 groups of 6 digits, each including a checksum). Accepts either an array of 8 strings or one space-separated string like '554872 394230 ... ...'.")]
    public object ImportXumm(
        [Description("Wallet alias.")] string name,
        [Description("Either eight 6-digit strings (newline / space separated) or a JSON-style array. Whichever is convenient — the tool normalizes it.")] string secretNumbers,
        [Description("Optional algorithm override. Default: 'secp256k1' (Xumm's native curve).")] string algorithm = "secp256k1")
    {
        EnsureFreeName(name);
        string[] numbers = ParseXummNumbers(secretNumbers);
        if (numbers.Length != 8)
        {
            throw new ArgumentException(
                $"Expected 8 secret numbers, got {numbers.Length}.",
                nameof(secretNumbers));
        }

        XrplWallet wallet = XrplWallet.FromXummNumbers(numbers, algorithm: algorithm);
        if (wallet.Seed is null)
        {
            throw new InvalidOperationException("XummNumbers-derived wallet has no seed — cannot persist.");
        }
        return PersistAndDescribe(name, wallet, ResolveAlgorithm(algorithm, wallet));
    }

    [McpServerTool(Name = "xrpl_wallet_import_text")]
    [Description("Imports a wallet derived from an arbitrary text passphrase. The text is normalized and run through SHA-256 or PBKDF2 to produce 16 bytes of entropy. Useful for brain-wallets or human-memorable seeds. WARNING: short or low-entropy text is trivially brute-forceable — only use long, unique phrases.")]
    public object ImportText(
        [Description("Wallet alias.")] string name,
        [Description("The text — any length. Longer & more unique = better.")] string text,
        [Description("Optional salt mixed into the KDF. Increases isolation between different wallets derived from similar text.")] string? salt = null,
        [Description("If true the text is lowercased before hashing — easier on humans, slightly weaker against typos vs intentional variants.")] bool caseInsensitive = true,
        [Description("KDF: 'sha256' (default, fast) or 'pbkdf2' (slow, 100K iter, much harder to brute-force).")] string kdf = "sha256",
        [Description("Optional algorithm override: 'ed25519' or 'secp256k1'. Default: secp256k1.")] string? algorithm = null)
    {
        EnsureFreeName(name);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Text is empty.", nameof(text));

        TextWalletKdf kdfEnum = ParseKdf(kdf);
        XrplWallet wallet = XrplWallet.FromNormalizedText(
            text: text,
            salt: salt,
            caseInsensitive: caseInsensitive,
            algorithm: algorithm,
            masterAddress: null,
            kdf: kdfEnum);

        if (wallet.Seed is null)
        {
            throw new InvalidOperationException("Text-derived wallet has no seed — cannot persist.");
        }
        return PersistAndDescribe(name, wallet, ResolveAlgorithm(algorithm, wallet));
    }

    [McpServerTool(Name = "xrpl_wallet_list")]
    [Description("Lists all wallets in the keystore — name, address, public key, algorithm, creation timestamp. No secrets are returned.")]
    public object List()
    {
        return new
        {
            wallets = _keystore.List(),
        };
    }

    [McpServerTool(Name = "xrpl_wallet_address")]
    [Description("Returns metadata (address, public key, algorithm, creation time) for one wallet by name. No secrets are returned.")]
    public object Address(
        [Description("Wallet alias.")] string name)
    {
        WalletMetadata? meta = _keystore.GetMetadata(name);
        if (meta is null)
        {
            throw new KeyNotFoundException($"Wallet '{name}' not found.");
        }
        return meta;
    }

    [McpServerTool(Name = "xrpl_wallet_remove")]
    [Description("Permanently deletes a wallet from the keystore. The encrypted seed is overwritten on disk via atomic rewrite — but if you have backups elsewhere they are not affected.")]
    public object Remove(
        [Description("Wallet alias.")] string name)
    {
        bool removed = _keystore.Remove(name);
        return new { name, removed };
    }

    [McpServerTool(Name = "xrpl_wallet_export")]
    [Description("Returns the plaintext seed and (optionally) the Xumm secret numbers for the wallet. Use this for BACKUP — the seed is the only way to recover the wallet later. The seed will appear in the MCP transcript, so handle the chat carefully. Requires explicit confirm=true to guard against accidental invocation.")]
    public object Export(
        [Description("Wallet alias.")] string name,
        [Description("Must be set to true to actually return the seed — a safety interlock.")] bool confirm = false)
    {
        if (!confirm)
        {
            throw new InvalidOperationException(
                "Refusing to export: pass confirm=true to acknowledge that the seed will be returned in plaintext.");
        }

        WalletMetadata? meta = _keystore.GetMetadata(name);
        if (meta is null) throw new KeyNotFoundException($"Wallet '{name}' not found.");

        string seed = _keystore.GetSeed(name);
        return new
        {
            name,
            meta.Address,
            meta.PublicKey,
            meta.Algorithm,
            seed,
        };
    }

    // ────────────────────────────────────────────────────────────────────────

    private object PersistAndDescribe(string name, XrplWallet wallet, string algorithm)
    {
        if (wallet.Seed is null)
        {
            throw new InvalidOperationException(
                "Wallet has no seed and cannot be persisted. Use an import method that goes through a seed.");
        }

        _keystore.Add(name, wallet.Seed, wallet.ClassicAddress, wallet.PublicKey, algorithm);
        return new
        {
            name,
            address = wallet.ClassicAddress,
            publicKey = wallet.PublicKey,
            algorithm,
        };
    }

    private void EnsureFreeName(string name)
    {
        if (_keystore.Exists(name))
        {
            throw new InvalidOperationException(
                $"Wallet '{name}' already exists. Use xrpl_wallet_remove first or pick a different name.");
        }
    }

    private static string ResolveAlgorithm(string? requested, XrplWallet wallet)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested!.Trim().ToLowerInvariant();
        }

        // Best-effort inference from public key prefix when SDK doesn't expose it directly.
        // Ed25519 XRPL public keys are 33 bytes hex starting with "ED".
        string pk = wallet.PublicKey ?? string.Empty;
        return pk.StartsWith("ED", StringComparison.OrdinalIgnoreCase) ? "ed25519" : "secp256k1";
    }

    private static TextWalletKdf ParseKdf(string kdf)
    {
        return kdf.Trim().ToLowerInvariant() switch
        {
            "sha256" => TextWalletKdf.Sha256,
            "pbkdf2" => TextWalletKdf.Pbkdf2,
            _ => throw new ArgumentException($"Unknown KDF '{kdf}'. Use 'sha256' or 'pbkdf2'.", nameof(kdf)),
        };
    }

    private static string[] ParseXummNumbers(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Xumm secret is empty.", nameof(raw));
        string trimmed = raw.Trim();

        // If the input looks like a JSON array, parse it.
        if (trimmed.StartsWith('['))
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(trimmed);
            return doc.RootElement
                .EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }

        // Otherwise split on any whitespace or commas.
        return trimmed
            .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}
