using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Signer.Keystore;
using Xrpl.Wallet;

namespace StaticBit.Xrpl.Mcp.Signer.Tools;

/// <summary>
/// Wallet management tools — generate, import (seed / mnemonic / Xumm numbers /
/// normalized text), list, look up, remove, export. HD (BIP-39) wallets store
/// the mnemonic encrypted and derive any number of XRPL addresses via BIP-44.
/// All operations are local; nothing leaves the process aside from the MCP
/// response on stdout.
/// </summary>
[McpServerToolType]
public sealed class WalletTools
{
    /// <summary>
    /// XRPL standard BIP-44 path template (SLIP-44 coin type 144).
    /// <c>{i}</c> is the account index.
    /// </summary>
    public const string XrplDerivationPathTemplate = "m/44'/144'/{i}'/0/0";

    private readonly IKeystore _keystore;

    public WalletTools(IKeystore keystore)
    {
        _keystore = keystore;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Generate / import (seed-kind, single address)
    // ────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "xrpl_wallet_generate")]
    [Description("Generates a brand-new single-address XRPL wallet with random entropy, encrypts the seed with the keystore passphrase, and stores it under the given name. For an HD (multi-address) wallet generate a mnemonic instead — xrpl_wallet_generate_mnemonic.")]
    public object Generate(
        [Description("Wallet alias used in subsequent sign calls. Allowed chars: letters, digits, '-', '_', '.'.")] string name,
        [Description("Signing curve: 'ed25519' (default) or 'secp256k1'.")] string algorithm = "ed25519")
    {
        EnsureFreeName(name);
        XrplWallet wallet = XrplWallet.Generate(algorithm);
        return PersistSeedAndDescribe(name, wallet, algorithm);
    }

    [McpServerTool(Name = "xrpl_wallet_import_seed")]
    [Description("Imports an existing XRPL seed string (e.g. 'sEd...' for Ed25519, 'sn...' for secp256k1). Encrypted and stored as a single-address (seed-kind) wallet.")]
    public object ImportSeed(
        [Description("Wallet alias.")] string name,
        [Description("XRPL seed — typically starts with 'sEd' or 'sn'.")] string seed,
        [Description("Optional algorithm override. If omitted the SDK infers it from the seed prefix.")] string? algorithm = null)
    {
        EnsureFreeName(name);
        if (string.IsNullOrWhiteSpace(seed)) throw new ArgumentException("Seed is empty.", nameof(seed));

        XrplWallet wallet = XrplWallet.FromSeed(seed.Trim(), masterAddress: null, algorithm: algorithm);
        return PersistSeedAndDescribe(name, wallet, ResolveAlgorithm(algorithm, wallet));
    }

    [McpServerTool(Name = "xrpl_wallet_import_xumm")]
    [Description("Imports a wallet from Xumm 'secret numbers' format (8 groups of 6 digits, each including a checksum). Accepts either an array of 8 strings or one space-separated string like '554872 394230 ... ...'. Stored as single-address (seed-kind).")]
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
        return PersistSeedAndDescribe(name, wallet, ResolveAlgorithm(algorithm, wallet));
    }

    [McpServerTool(Name = "xrpl_wallet_import_text")]
    [Description("Imports a wallet derived from an arbitrary text passphrase. The text is normalized and run through SHA-256 or PBKDF2 to produce 16 bytes of entropy. Useful for brain-wallets or human-memorable seeds. WARNING: short or low-entropy text is trivially brute-forceable — only use long, unique phrases. Stored as single-address (seed-kind).")]
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
        return PersistSeedAndDescribe(name, wallet, ResolveAlgorithm(algorithm, wallet));
    }

    // ────────────────────────────────────────────────────────────────────────
    // HD (mnemonic-kind, multi-address)
    // ────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "xrpl_wallet_generate_mnemonic")]
    [Description("Generates a random BIP-39 mnemonic (12/15/18/21/24 words) and stores it encrypted as an HD wallet. Returns the mnemonic in plaintext ONCE — write it down for backup, it will not be shown again unless you call xrpl_wallet_export. Sets up XRPL standard BIP-44 path m/44'/144'/{i}'/0/0; address at index 0 is returned as preview.")]
    public object GenerateMnemonic(
        [Description("Wallet alias.")] string name,
        [Description("Mnemonic length in words: 12 (default, 128-bit entropy), 15, 18, 21 or 24 (256-bit).")] int wordCount = 12,
        [Description("Optional BIP-39 passphrase that augments the mnemonic. Stored encrypted separately. Empty by default.")] string? bip39Passphrase = null,
        [Description("Signing curve for derived accounts: 'secp256k1' (default, XRPL BIP-44 convention) or 'ed25519'.")] string algorithm = "secp256k1")
    {
        EnsureFreeName(name);
        string[] words = XrplWallet.GenerateMnemonic(wordCount);
        string mnemonic = string.Join(" ", words);

        XrplWallet preview = DeriveFromMnemonic(mnemonic, index: 0, bip39Passphrase, algorithm);
        _keystore.AddMnemonic(
            name: name,
            mnemonic: mnemonic,
            previewAddress: preview.ClassicAddress,
            previewPublicKey: preview.PublicKey,
            algorithm: algorithm,
            bip39Passphrase: bip39Passphrase,
            derivationPathTemplate: XrplDerivationPathTemplate);

        return new
        {
            name,
            kind = "mnemonic",
            algorithm,
            derivationPathTemplate = XrplDerivationPathTemplate,
            previewIndex = 0,
            previewAddress = preview.ClassicAddress,
            previewPublicKey = preview.PublicKey,
            mnemonic,
            mnemonicNote = "BACK UP THIS PHRASE NOW. It is shown once. Use xrpl_wallet_export to retrieve later (requires confirm=true).",
        };
    }

    [McpServerTool(Name = "xrpl_wallet_import_mnemonic")]
    [Description("Imports a BIP-39 mnemonic (12/15/18/21/24 words) as an HD wallet — the mnemonic itself is stored encrypted, and any number of XRPL addresses can be derived later via xrpl_wallet_derive_address. Set storeAsHd=false to use the legacy behaviour where only the seed for a single derivation path is stored (mnemonic discarded after import).")]
    public object ImportMnemonic(
        [Description("Wallet alias.")] string name,
        [Description("BIP-39 mnemonic — words separated by single spaces.")] string mnemonic,
        [Description("Optional BIP-44 derivation path TEMPLATE with '{i}' placeholder for index. Default: 'm/44'/144'/{i}'/0/0' (XRPL standard).")] string? derivationPathTemplate = null,
        [Description("Optional BIP-39 passphrase that augments the mnemonic. Stored encrypted separately. Empty by default.")] string? bip39Passphrase = null,
        [Description("Signing curve: 'secp256k1' (default, XRPL BIP-44 convention) or 'ed25519'.")] string algorithm = "secp256k1",
        [Description("If true (default), store the mnemonic encrypted as an HD wallet — multi-address. If false, derive ONLY index 0's seed and store that as a seed-kind wallet (mnemonic discarded).")] bool storeAsHd = true)
    {
        EnsureFreeName(name);
        if (string.IsNullOrWhiteSpace(mnemonic)) throw new ArgumentException("Mnemonic is empty.", nameof(mnemonic));

        string template = ResolveTemplate(derivationPathTemplate);

        if (storeAsHd)
        {
            XrplWallet preview = DeriveFromMnemonic(mnemonic.Trim(), index: 0, bip39Passphrase, algorithm, template);
            _keystore.AddMnemonic(
                name: name,
                mnemonic: mnemonic.Trim(),
                previewAddress: preview.ClassicAddress,
                previewPublicKey: preview.PublicKey,
                algorithm: algorithm,
                bip39Passphrase: bip39Passphrase,
                derivationPathTemplate: template);

            return new
            {
                name,
                kind = "mnemonic",
                algorithm,
                derivationPathTemplate = template,
                previewIndex = 0,
                previewAddress = preview.ClassicAddress,
                previewPublicKey = preview.PublicKey,
            };
        }

        // Legacy single-seed path: derive once at the resolved template+index=0,
        // grab the seed, persist as seed-kind. Mnemonic NOT retained.
        XrplWallet derived = DeriveFromMnemonic(mnemonic.Trim(), index: 0, bip39Passphrase, algorithm, template);
        if (derived.Seed is null)
        {
            throw new InvalidOperationException(
                "Mnemonic-derived wallet has no seed exposed by the SDK — cannot persist as seed-kind. " +
                "Use storeAsHd=true (default).");
        }
        return PersistSeedAndDescribe(name, derived, ResolveAlgorithm(algorithm, derived));
    }

    [McpServerTool(Name = "xrpl_wallet_derive_address")]
    [Description("Derives an XRPL address at the given index for an HD (mnemonic-kind) wallet. Decrypts the mnemonic, derives via BIP-44 template, returns address + public key. No seed material in the response. Throws for seed-kind wallets.")]
    public object DeriveAddress(
        [Description("Wallet alias.")] string name,
        [Description("Account index (0..2^31-1). Default 0.")] int index = 0)
    {
        WalletMetadata meta = RequireMetadata(name);
        if (!string.Equals(meta.Kind, "mnemonic", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Wallet '{name}' is kind='{meta.Kind}', not 'mnemonic'. Only HD wallets support derivation by index.");
        }
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be non-negative.");
        }

        string mnemonic = _keystore.GetMnemonic(name);
        string? bip39Passphrase = _keystore.GetBip39Passphrase(name);
        string template = meta.DerivationPathTemplate ?? XrplDerivationPathTemplate;

        XrplWallet derived = DeriveFromMnemonic(mnemonic, index, bip39Passphrase, meta.Algorithm, template);
        return new
        {
            name,
            algorithm = meta.Algorithm,
            derivationPath = template.Replace("{i}", index.ToString(CultureInfo.InvariantCulture)),
            index,
            address = derived.ClassicAddress,
            publicKey = derived.PublicKey,
        };
    }

    // ────────────────────────────────────────────────────────────────────────
    // List / lookup / remove / export
    // ────────────────────────────────────────────────────────────────────────

    [McpServerTool(Name = "xrpl_wallet_list")]
    [Description("Lists all wallets in the keystore — name, kind (seed | mnemonic), address (preview for HD), public key, algorithm, derivationPathTemplate (HD only), creation timestamp. No secrets are returned.")]
    public object List()
    {
        return new
        {
            wallets = _keystore.List(),
        };
    }

    [McpServerTool(Name = "xrpl_wallet_address")]
    [Description("Returns metadata (kind, address, public key, algorithm, derivationPathTemplate for HD, creation time) for one wallet by name. For HD wallets the address shown is the preview at index 0 — use xrpl_wallet_derive_address for other indices. No secrets are returned.")]
    public object Address(
        [Description("Wallet alias.")] string name)
    {
        return RequireMetadata(name);
    }

    [McpServerTool(Name = "xrpl_wallet_remove")]
    [Description("Permanently deletes a wallet from the keystore. The encrypted ciphertext is overwritten on disk via atomic rewrite — but if you have backups elsewhere they are not affected.")]
    public object Remove(
        [Description("Wallet alias.")] string name)
    {
        bool removed = _keystore.Remove(name);
        return new { name, removed };
    }

    [McpServerTool(Name = "xrpl_wallet_export")]
    [Description("Returns the plaintext secret for backup. For seed-kind: the XRPL family seed. For HD (mnemonic-kind): the BIP-39 mnemonic + optional BIP-39 passphrase + derivation path template + algorithm. The secret will appear in the MCP transcript — handle the chat carefully. Requires confirm=true to guard against accidental invocation.")]
    public object Export(
        [Description("Wallet alias.")] string name,
        [Description("Must be set to true to actually return the secret — a safety interlock.")] bool confirm = false)
    {
        if (!confirm)
        {
            throw new InvalidOperationException(
                "Refusing to export: pass confirm=true to acknowledge that the secret will be returned in plaintext.");
        }

        WalletMetadata meta = RequireMetadata(name);

        if (string.Equals(meta.Kind, "mnemonic", StringComparison.Ordinal))
        {
            string mnemonic = _keystore.GetMnemonic(name);
            string? bip39Passphrase = _keystore.GetBip39Passphrase(name);
            return new
            {
                name,
                kind = "mnemonic",
                meta.Algorithm,
                meta.Address,
                meta.PublicKey,
                meta.DerivationPathTemplate,
                mnemonic,
                bip39Passphrase,
            };
        }

        string seed = _keystore.GetSeed(name);
        return new
        {
            name,
            kind = "seed",
            meta.Address,
            meta.PublicKey,
            meta.Algorithm,
            seed,
        };
    }

    [McpServerTool(Name = "xrpl_wallet_export_index")]
    [Description("Derives an XRPL family seed for a specific index of an HD (mnemonic-kind) wallet. Useful for backing up a derived account independently, or importing it into other XRPL tools that only accept a seed. Requires confirm=true. Throws for seed-kind wallets.")]
    public object ExportIndex(
        [Description("Wallet alias.")] string name,
        [Description("Account index to derive.")] int index,
        [Description("Must be set to true to actually return the seed.")] bool confirm = false)
    {
        if (!confirm)
        {
            throw new InvalidOperationException(
                "Refusing to export: pass confirm=true to acknowledge that the seed will be returned in plaintext.");
        }

        WalletMetadata meta = RequireMetadata(name);
        if (!string.Equals(meta.Kind, "mnemonic", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Wallet '{name}' is kind='{meta.Kind}', not 'mnemonic'. Only HD wallets have per-index seeds; use xrpl_wallet_export instead.");
        }
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must be non-negative.");
        }

        string mnemonic = _keystore.GetMnemonic(name);
        string? bip39Passphrase = _keystore.GetBip39Passphrase(name);
        string template = meta.DerivationPathTemplate ?? XrplDerivationPathTemplate;

        XrplWallet derived = DeriveFromMnemonic(mnemonic, index, bip39Passphrase, meta.Algorithm, template);
        return new
        {
            name,
            index,
            algorithm = meta.Algorithm,
            derivationPath = template.Replace("{i}", index.ToString(CultureInfo.InvariantCulture)),
            address = derived.ClassicAddress,
            publicKey = derived.PublicKey,
            seed = derived.Seed,
        };
    }

    // ────────────────────────────────────────────────────────────────────────

    private object PersistSeedAndDescribe(string name, XrplWallet wallet, string algorithm)
    {
        if (wallet.Seed is null)
        {
            throw new InvalidOperationException(
                "Wallet has no seed and cannot be persisted as seed-kind. Use an import method that goes through a seed or storeAsHd=true.");
        }

        _keystore.Add(name, wallet.Seed, wallet.ClassicAddress, wallet.PublicKey, algorithm);
        return new
        {
            name,
            kind = "seed",
            address = wallet.ClassicAddress,
            publicKey = wallet.PublicKey,
            algorithm,
        };
    }

    private WalletMetadata RequireMetadata(string name)
    {
        WalletMetadata? meta = _keystore.GetMetadata(name);
        if (meta is null) throw new KeyNotFoundException($"Wallet '{name}' not found.");
        return meta;
    }

    private void EnsureFreeName(string name)
    {
        if (_keystore.Exists(name))
        {
            throw new InvalidOperationException(
                $"Wallet '{name}' already exists. Use xrpl_wallet_remove first or pick a different name.");
        }
    }

    private static XrplWallet DeriveFromMnemonic(string mnemonic, int index, string? bip39Passphrase, string algorithm, string? template = null)
    {
        string path = (template ?? XrplDerivationPathTemplate)
            .Replace("{i}", index.ToString(CultureInfo.InvariantCulture));
        return XrplWallet.FromMnemonic(
            mnemonic: mnemonic,
            masterAddress: null,
            derivationPath: path,
            encoding: null,
            algorithm: algorithm,
            passphrase: bip39Passphrase);
    }

    private static string ResolveTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return XrplDerivationPathTemplate;
        string t = template.Trim();
        if (!t.Contains("{i}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"derivationPathTemplate '{template}' must contain the '{{i}}' placeholder for the account index.",
                nameof(template));
        }
        return t;
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

        return trimmed
            .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}
