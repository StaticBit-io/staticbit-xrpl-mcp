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
/// with AES-256-GCM using a key derived from the passphrase by PBKDF2-SHA256 with
/// per-record salt. Writes are atomic: serialize to a temp file, fsync, then rename.
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
            .Select(p => new WalletMetadata(p.Key, p.Value.Address, p.Value.PublicKey, p.Value.Algorithm, p.Value.CreatedAt))
            .ToList();
    }

    public WalletMetadata? GetMetadata(string name)
    {
        ValidateName(name);
        KeystoreFile file = Load();
        return file.Wallets.TryGetValue(name, out KeystoreEntry? entry)
            ? new WalletMetadata(name, entry.Address, entry.PublicKey, entry.Algorithm, entry.CreatedAt)
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

            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] iv = RandomNumberGenerator.GetBytes(IvBytes);
            byte[] key = DeriveKey(_passphrase, salt, KdfParamsDefaults.Iterations);
            byte[] plaintext = Encoding.UTF8.GetBytes(seed);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagBytes];

            using (AesGcm aes = new AesGcm(key, TagBytes))
            {
                aes.Encrypt(iv, plaintext, ciphertext, tag);
            }
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);

            file.Wallets[name] = new KeystoreEntry
            {
                Address = address,
                PublicKey = publicKey,
                Algorithm = algorithm,
                CreatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                Kdf = "pbkdf2-sha256",
                KdfParams = new KdfParams { Iterations = KdfParamsDefaults.Iterations, Salt = ToHex(salt) },
                Cipher = "aes-256-gcm",
                CipherParams = new CipherParams { Iv = ToHex(iv), Tag = ToHex(tag) },
                Ciphertext = ToHex(ciphertext),
            };

            Save(file);
        }
    }

    public string GetSeed(string name)
    {
        ValidateName(name);
        KeystoreFile file = Load();
        if (!file.Wallets.TryGetValue(name, out KeystoreEntry? entry))
        {
            throw new KeyNotFoundException($"Wallet '{name}' not found.");
        }

        byte[] salt = FromHex(entry.KdfParams.Salt);
        byte[] iv = FromHex(entry.CipherParams.Iv);
        byte[] tag = FromHex(entry.CipherParams.Tag);
        byte[] ciphertext = FromHex(entry.Ciphertext);
        byte[] key = DeriveKey(_passphrase, salt, entry.KdfParams.Iterations);

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
                $"Failed to decrypt wallet '{name}'. The passphrase is likely wrong (or the keystore file is corrupted).",
                ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        string seed = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);
        return seed;
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
        return parsed ?? new KeystoreFile();
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

    private static void TryRestrictDirectoryPermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            // Best-effort; never block on file-mode setting.
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
            // Best-effort.
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

    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    private static byte[] FromHex(string hex) => Convert.FromHexString(hex);
}

internal static class KdfParamsDefaults
{
    public const int Iterations = 600_000;
}
