using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StaticBit.Xrpl.Mcp.Signer.Keystore;

/// <summary>
/// Versioned root document persisted to <c>keystore.json</c>.
/// Field names are kept short and snake-case to keep the file readable when
/// inspected by a user — the keystore is a plain JSON file at rest.
/// </summary>
public sealed class KeystoreFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("wallets")]
    public Dictionary<string, KeystoreEntry> Wallets { get; set; } = new Dictionary<string, KeystoreEntry>();
}

/// <summary>
/// One wallet record. The seed is encrypted with per-record salt and IV so that
/// compromising one entry does not weaken any other entry. AES-256-GCM provides
/// authenticated encryption — a tampered ciphertext fails to decrypt with a
/// recognizable error rather than returning garbage.
/// </summary>
public sealed class KeystoreEntry
{
    /// <summary>XRPL classic address (r…).</summary>
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    /// <summary>Hex-encoded public key.</summary>
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

    /// <summary>Encrypted seed bytes (hex). Decrypts to the original seed string used at import.</summary>
    [JsonPropertyName("ciphertext")]
    public string Ciphertext { get; set; } = string.Empty;
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
