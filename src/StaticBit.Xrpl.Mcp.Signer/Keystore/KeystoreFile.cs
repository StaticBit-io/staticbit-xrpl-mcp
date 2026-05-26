using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StaticBit.Xrpl.Mcp.Signer.Keystore;

/// <summary>
/// Versioned root document persisted to <c>keystore.json</c>.
/// Field names are kept short and snake-case to keep the file readable when
/// inspected by a user — the keystore is a plain JSON file at rest.
/// </summary>
/// <remarks>
/// Version 1 — single-seed entries only.
/// Version 2 — adds the optional <c>Kind</c>/<c>Bip39*</c>/<c>DerivationPathTemplate</c>
/// fields on <see cref="KeystoreEntry"/> for HD (BIP-39) wallets. Old files are
/// read transparently; the file is upgraded to version 2 only when the first
/// mnemonic entry is added.
/// </remarks>
public sealed class KeystoreFile
{
    public const int CurrentVersion = 2;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("wallets")]
    public Dictionary<string, KeystoreEntry> Wallets { get; set; } = new Dictionary<string, KeystoreEntry>();
}

/// <summary>
/// One wallet record. The seed (or mnemonic, for HD entries) is encrypted with
/// per-record salt and IV so that compromising one entry does not weaken any
/// other entry. AES-256-GCM provides authenticated encryption — a tampered
/// ciphertext fails to decrypt with a recognizable error rather than returning
/// garbage.
/// </summary>
public sealed class KeystoreEntry
{
    /// <summary>
    /// What the <see cref="Ciphertext"/> field holds:
    /// <c>"seed"</c> — an XRPL family seed (e.g. "sEd…" / "sn…"), one address per entry.
    /// <c>"mnemonic"</c> — a BIP-39 mnemonic phrase; can derive any number of
    ///   XRPL addresses by index via <see cref="DerivationPathTemplate"/>.
    /// Absent in legacy files — defaults to <c>"seed"</c> for back-compat.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "seed";

    /// <summary>
    /// XRPL classic address (r…). For <c>kind=mnemonic</c> entries this is a
    /// preview — the address at index 0 of <see cref="DerivationPathTemplate"/>.
    /// </summary>
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Hex-encoded public key. For <c>kind=mnemonic</c> entries — preview at index 0.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Curve / signing algorithm: <c>ed25519</c> or <c>secp256k1</c>.</summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "ed25519";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>Key derivation function. Currently always <c>pbkdf2-sha256</c>.</summary>
    [JsonPropertyName("kdf")]
    public string Kdf { get; set; } = "pbkdf2-sha256";

    [JsonPropertyName("kdfParams")]
    public KdfParams KdfParams { get; set; } = new KdfParams();

    /// <summary>Symmetric cipher. Currently always <c>aes-256-gcm</c>.</summary>
    [JsonPropertyName("cipher")]
    public string Cipher { get; set; } = "aes-256-gcm";

    [JsonPropertyName("cipherParams")]
    public CipherParams CipherParams { get; set; } = new CipherParams();

    /// <summary>
    /// Encrypted primary secret (hex). For <c>kind=seed</c> decrypts to the
    /// original XRPL seed; for <c>kind=mnemonic</c> decrypts to the BIP-39
    /// mnemonic phrase.
    /// </summary>
    [JsonPropertyName("ciphertext")]
    public string Ciphertext { get; set; } = string.Empty;

    /// <summary>
    /// (HD only) Optional BIP-39 passphrase that augments the mnemonic during
    /// seed derivation. Encrypted with its own per-record salt+IV so the master
    /// passphrase compromise doesn't reveal it in plaintext on disk alongside.
    /// </summary>
    [JsonPropertyName("bip39PassphraseCiphertext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Bip39PassphraseCiphertext { get; set; }

    [JsonPropertyName("bip39PassphraseKdfParams")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KdfParams? Bip39PassphraseKdfParams { get; set; }

    [JsonPropertyName("bip39PassphraseCipherParams")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CipherParams? Bip39PassphraseCipherParams { get; set; }

    /// <summary>
    /// (HD only) BIP-44 derivation path template with <c>{i}</c> placeholder
    /// for the account index. Default — <c>"m/44'/144'/{i}'/0/0"</c> (XRPL
    /// standard from SLIP-44). Stored explicitly so future format changes
    /// don't break existing entries.
    /// </summary>
    [JsonPropertyName("derivationPathTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DerivationPathTemplate { get; set; }
}

public sealed class KdfParams
{
    [JsonPropertyName("iterations")]
    public int Iterations { get; set; } = 600_000;

    /// <summary>Per-record salt (hex). 32 bytes.</summary>
    [JsonPropertyName("salt")]
    public string Salt { get; set; } = string.Empty;
}

public sealed class CipherParams
{
    /// <summary>AES-GCM nonce (hex). 12 bytes.</summary>
    [JsonPropertyName("iv")]
    public string Iv { get; set; } = string.Empty;

    /// <summary>AES-GCM authentication tag (hex). 16 bytes.</summary>
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;
}
