using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StaticBit.Xrpl.Mcp.Signer.Configuration;

namespace StaticBit.Xrpl.Mcp.Signer.Keystore;

/// <summary>
/// File-backed implementation of <see cref="IKeystore"/>. Encrypts each wallet's seed
/// (or mnemonic, for HD entries) with AES-256-GCM using a key derived from the
/// passphrase by PBKDF2-SHA256 with per-record salt. Writes are atomic: serialize
/// to a temp file, then rename.
/// </summary>
public sealed class EncryptedFileKeystore : IKeystore
{
    private const int SaltBytes = 32;
    private const int KeyBytes = 32;       // AES-256
    private const int IvBytes = 12;        // GCM standard nonce size
    private const int TagBytes = 16;       // GCM authentication tag

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly string _passphrase;
    private readonly object _lock = new object();

    public EncryptedFileKeystore(SignerOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrEmpty(options.KeystorePath))
        {
            throw new ArgumentException("Keystore path is empty.", nameof(options));
        }
        if (string.IsNullOrEmpty(options.Passphrase))
        {
            throw new ArgumentException(
                "Keystore passphrase is empty. Set XRPL_SIGNER_PASSPHRASE or XRPL_SIGNER_PASSPHRASE_FILE.",
                nameof(options));
        }

        _path = options.KeystorePath;
        _passphrase = options.Passphrase;

        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            TryRestrictDirectoryPermissions(dir);
        }
    }

    public IReadOnlyList<WalletMetadata> List()
    {
        KeystoreFile file = Load();
        return file.Wallets
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => ToMetadata(p.Key, p.Value))
            .ToList();
    }

    public WalletMetadata? GetMetadata(string name)
    {
        ValidateName(name);
        KeystoreFile file = Load();
        return file.Wallets.TryGetValue(name, out KeystoreEntry? entry)
            ? ToMetadata(name, entry)
            : null;
    }

    public bool Exists(string name)
    {
        ValidateName(name);
        return Load().Wallets.ContainsKey(name);
    }

    public void Add(string name, string seed, string address, string publicKey, string algorithm)
    {
        ValidateName(name);
        if (string.IsNullOrEmpty(seed)) throw new ArgumentException("Seed is empty.", nameof(seed));
        if (string.IsNullOrEmpty(address)) throw new ArgumentException("Address is empty.", nameof(address));
        if (string.IsNullOrEmpty(publicKey)) throw new ArgumentException("PublicKey is empty.", nameof(publicKey));

        lock (_lock)
        {
            KeystoreFile file = Load();
            if (file.Wallets.ContainsKey(name))
            {
                throw new InvalidOperationException($"Wallet '{name}' already exists. Remove it first or pick a different name.");
            }

            (string ciphertext, KdfParams kdfParams, CipherParams cipherParams) = EncryptString(seed);

            file.Wallets[name] = new KeystoreEntry
            {
                Kind = "seed",
                Address = address,
                PublicKey = publicKey,
                Algorithm = algorithm,
                CreatedAt = NowIso(),
                Kdf = "pbkdf2-sha256",
                KdfParams = kdfParams,
                Cipher = "aes-256-gcm",
                CipherParams = cipherParams,
                Ciphertext = ciphertext,
            };

            Save(file);
        }
    }

    public void AddMnemonic(
        string name,
        string mnemonic,
        string previewAddress,
        string previewPublicKey,
        string algorithm,
        string? bip39Passphrase,
        string derivationPathTemplate)
    {
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(mnemonic)) throw new ArgumentException("Mnemonic is empty.", nameof(mnemonic));
        if (string.IsNullOrEmpty(previewAddress)) throw new ArgumentException("Preview address is empty.", nameof(previewAddress));
        if (string.IsNullOrEmpty(previewPublicKey)) throw new ArgumentException("Preview public key is empty.", nameof(previewPublicKey));
        if (string.IsNullOrWhiteSpace(derivationPathTemplate)) throw new ArgumentException("Derivation path template is empty.", nameof(derivationPathTemplate));
        if (!derivationPathTemplate.Contains("{i}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Derivation path template '{derivationPathTemplate}' must contain the '{{i}}' placeholder for the account index.",
                nameof(derivationPathTemplate));
        }

        lock (_lock)
        {
            KeystoreFile file = Load();
            if (file.Wallets.ContainsKey(name))
            {
                throw new InvalidOperationException($"Wallet '{name}' already exists. Remove it first or pick a different name.");
            }

            (string mnemonicCiphertext, KdfParams mnemonicKdf, CipherParams mnemonicCipher) = EncryptString(mnemonic);

            string? passphraseCiphertext = null;
            KdfParams? passphraseKdf = null;
            CipherParams? passphraseCipher = null;
            if (!string.IsNullOrEmpty(bip39Passphrase))
            {
                (string c, KdfParams k, CipherParams cp) = EncryptString(bip39Passphrase);
                passphraseCiphertext = c;
                passphraseKdf = k;
                passphraseCipher = cp;
            }

            file.Wallets[name] = new KeystoreEntry
            {
                Kind = "mnemonic",
                Address = previewAddress,
                PublicKey = previewPublicKey,
                Algorithm = algorithm,
                CreatedAt = NowIso(),
                Kdf = "pbkdf2-sha256",
                KdfParams = mnemonicKdf,
                Cipher = "aes-256-gcm",
                CipherParams = mnemonicCipher,
                Ciphertext = mnemonicCiphertext,
                Bip39PassphraseCiphertext = passphraseCiphertext,
                Bip39PassphraseKdfParams = passphraseKdf,
                Bip39PassphraseCipherParams = passphraseCipher,
                DerivationPathTemplate = derivationPathTemplate,
            };

            // Bump file version on first HD-entry write — readers tolerate both.
            file.Version = Math.Max(file.Version, KeystoreFile.CurrentVersion);

            Save(file);
        }
    }

    public string GetSeed(string name)
    {
        ValidateName(name);
        KeystoreEntry entry = LoadEntryOrThrow(name);
        if (!string.Equals(entry.Kind, "seed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Wallet '{name}' is kind='{entry.Kind}', not 'seed'. Use GetMnemonic or derive an index instead.");
        }
        return DecryptString(name, entry.Ciphertext, entry.KdfParams, entry.CipherParams, "seed");
    }

    public string GetMnemonic(string name)
    {
        ValidateName(name);
        KeystoreEntry entry = LoadEntryOrThrow(name);
        if (!string.Equals(entry.Kind, "mnemonic", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Wallet '{name}' is kind='{entry.Kind}', not 'mnemonic'. Use GetSeed instead.");
        }
        return DecryptString(name, entry.Ciphertext, entry.KdfParams, entry.CipherParams, "mnemonic");
    }

    public string? GetBip39Passphrase(string name)
    {
        ValidateName(name);
        KeystoreEntry entry = LoadEntryOrThrow(name);
        if (!string.Equals(entry.Kind, "mnemonic", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Wallet '{name}' is kind='{entry.Kind}', not 'mnemonic'.");
        }
        if (string.IsNullOrEmpty(entry.Bip39PassphraseCiphertext)
            || entry.Bip39PassphraseKdfParams is null
            || entry.Bip39PassphraseCipherParams is null)
        {
            return null;
        }
        return DecryptString(name, entry.Bip39PassphraseCiphertext, entry.Bip39PassphraseKdfParams, entry.Bip39PassphraseCipherParams, "bip39_passphrase");
    }

    public bool Remove(string name)
    {
        ValidateName(name);
        lock (_lock)
        {
            KeystoreFile file = Load();
            bool removed = file.Wallets.Remove(name);
            if (removed) Save(file);
            return removed;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Encryption helpers
    // ────────────────────────────────────────────────────────────────────────

    private (string ciphertext, KdfParams kdf, CipherParams cipher) EncryptString(string plaintext)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] iv = RandomNumberGenerator.GetBytes(IvBytes);
        byte[] key = DeriveKey(_passphrase, salt, KdfParamsDefaults.Iterations);
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plain.Length];
        byte[] tag = new byte[TagBytes];

        try
        {
            using AesGcm aes = new AesGcm(key, TagBytes);
            aes.Encrypt(iv, plain, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        return (
            ToHex(ciphertext),
            new KdfParams { Iterations = KdfParamsDefaults.Iterations, Salt = ToHex(salt) },
            new CipherParams { Iv = ToHex(iv), Tag = ToHex(tag) }
        );
    }

    private string DecryptString(string walletName, string ciphertextHex, KdfParams kdf, CipherParams cipher, string fieldHint)
    {
        byte[] salt = FromHex(kdf.Salt);
        byte[] iv = FromHex(cipher.Iv);
        byte[] tag = FromHex(cipher.Tag);
        byte[] ciphertext = FromHex(ciphertextHex);
        byte[] key = DeriveKey(_passphrase, salt, kdf.Iterations);

        byte[] plaintext = new byte[ciphertext.Length];
        try
        {
            using AesGcm aes = new AesGcm(key, TagBytes);
            aes.Decrypt(iv, ciphertext, tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidOperationException(
                $"Failed to decrypt wallet '{walletName}' (field={fieldHint}). The passphrase is likely wrong (or the keystore file is corrupted).",
                ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        string result = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return result;
    }

    // ────────────────────────────────────────────────────────────────────────

    private KeystoreEntry LoadEntryOrThrow(string name)
    {
        KeystoreFile file = Load();
        if (!file.Wallets.TryGetValue(name, out KeystoreEntry? entry))
        {
            throw new KeyNotFoundException($"Wallet '{name}' not found.");
        }
        return entry;
    }

    private KeystoreFile Load()
    {
        if (!File.Exists(_path))
        {
            return new KeystoreFile();
        }

        string text = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new KeystoreFile();
        }

        KeystoreFile? parsed = JsonSerializer.Deserialize<KeystoreFile>(text, JsonOptions);
        if (parsed is null) return new KeystoreFile();

        // Legacy entries (version 1) have no "kind" — fall back to "seed".
        foreach (KeystoreEntry e in parsed.Wallets.Values)
        {
            if (string.IsNullOrEmpty(e.Kind)) e.Kind = "seed";
        }
        return parsed;
    }

    private void Save(KeystoreFile file)
    {
        string tempPath = _path + ".tmp";
        string json = JsonSerializer.Serialize(file, JsonOptions);
        File.WriteAllText(tempPath, json);
        TryRestrictFilePermissions(tempPath);

        if (File.Exists(_path))
        {
            File.Replace(tempPath, _path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, _path);
        }

        TryRestrictFilePermissions(_path);
    }

    private static WalletMetadata ToMetadata(string name, KeystoreEntry entry)
    {
        return new WalletMetadata(name, entry.Address, entry.PublicKey, entry.Algorithm, entry.CreatedAt)
        {
            Kind = string.IsNullOrEmpty(entry.Kind) ? "seed" : entry.Kind,
            DerivationPathTemplate = entry.DerivationPathTemplate,
        };
    }

    private static void TryRestrictDirectoryPermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
        }
    }

    private static void TryRestrictFilePermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
    {
        byte[] password = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                password: password,
                salt: salt,
                iterations: iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: KeyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Wallet name is empty.", nameof(name));
        }
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool ok = (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.';
            if (!ok)
            {
                throw new ArgumentException(
                    $"Wallet name '{name}' contains an invalid character '{c}'. Allowed: letters, digits, '-', '_', '.'.",
                    nameof(name));
            }
        }
    }

    private static string NowIso() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    private static byte[] FromHex(string hex) => Convert.FromHexString(hex);
}

internal static class KdfParamsDefaults
{
    public const int Iterations = 600_000;
}
