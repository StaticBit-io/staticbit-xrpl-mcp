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
    /// Stores a wallet under <paramref name="name"/>. Encrypts <paramref name="seed"/>
    /// with the configured passphrase and persists the keystore atomically.
    /// </summary>
    void Add(string name, string seed, string address, string publicKey, string algorithm);

    /// <summary>
    /// Decrypts and returns the seed for <paramref name="name"/>. Throws when the
    /// wallet does not exist or the passphrase is wrong.
    /// </summary>
    string GetSeed(string name);

    /// <summary>Removes the wallet from the keystore. Idempotent on missing names.</summary>
    bool Remove(string name);
}

public sealed record WalletMetadata(
    string Name,
    string Address,
    string PublicKey,
    string Algorithm,
    string CreatedAt);
