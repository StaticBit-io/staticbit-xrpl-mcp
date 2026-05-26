using System.Collections.Generic;

namespace StaticBit.Xrpl.Mcp.Signer.Keystore;

/// <summary>
/// Contract for the encrypted wallet store. Operations are synchronous because
/// the file is small and the only I/O is local; an async surface would only add
/// noise without buying anything.
/// </summary>
public interface IKeystore
{
    /// <summary>Returns metadata for all stored wallets (no secret material).</summary>
    IReadOnlyList<WalletMetadata> List();

    /// <summary>Returns the metadata for a single wallet, or <c>null</c> if not present.</summary>
    WalletMetadata? GetMetadata(string name);

    /// <summary>True if a wallet with this <paramref name="name"/> exists.</summary>
    bool Exists(string name);

    /// <summary>
    /// Stores a seed-kind wallet under <paramref name="name"/>. Encrypts
    /// <paramref name="seed"/> with the configured passphrase and persists the
    /// keystore atomically.
    /// </summary>
    void Add(string name, string seed, string address, string publicKey, string algorithm);

    /// <summary>
    /// Stores an HD (BIP-39) wallet under <paramref name="name"/>. Encrypts the
    /// mnemonic plus (optionally) the BIP-39 passphrase separately. The preview
    /// address / public key correspond to index 0 of <paramref name="derivationPathTemplate"/>.
    /// </summary>
    void AddMnemonic(
        string name,
        string mnemonic,
        string previewAddress,
        string previewPublicKey,
        string algorithm,
        string? bip39Passphrase,
        string derivationPathTemplate);

    /// <summary>
    /// Decrypts and returns the seed for a seed-kind wallet. Throws when the
    /// wallet does not exist, is not a seed-kind, or the passphrase is wrong.
    /// </summary>
    string GetSeed(string name);

    /// <summary>
    /// Decrypts and returns the mnemonic for an HD (mnemonic-kind) wallet.
    /// Throws when the wallet does not exist, is not an HD wallet, or the
    /// passphrase is wrong.
    /// </summary>
    string GetMnemonic(string name);

    /// <summary>
    /// Decrypts and returns the BIP-39 passphrase if the HD entry has one;
    /// <c>null</c> otherwise. Throws when the wallet does not exist or is not
    /// an HD wallet.
    /// </summary>
    string? GetBip39Passphrase(string name);

    /// <summary>Removes the wallet from the keystore. Idempotent on missing names.</summary>
    bool Remove(string name);
}

public sealed record WalletMetadata(
    string Name,
    string Address,
    string PublicKey,
    string Algorithm,
    string CreatedAt)
{
    /// <summary>
    /// <c>"seed"</c> for legacy/single-address entries or <c>"mnemonic"</c> for
    /// HD entries. HD entries' <see cref="Address"/> / <see cref="PublicKey"/>
    /// are the preview at index 0 of <see cref="DerivationPathTemplate"/>.
    /// </summary>
    public string Kind { get; init; } = "seed";

    /// <summary>(HD only) BIP-44 path template with <c>{i}</c> placeholder.</summary>
    public string? DerivationPathTemplate { get; init; }
}
