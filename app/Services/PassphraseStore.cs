using System;
using System.Security.Cryptography;
using Windows.Security.Credentials;

namespace signal_windows_client.Services;

/// <summary>
/// Stores the sqlcipher passphrase in the Windows Credential Locker
/// (PasswordVault). A random 256-bit passphrase is generated on first run and
/// reused thereafter, so the encrypted store opens without ever hard-coding a
/// key. Never logged.
/// </summary>
public static class PassphraseStore
{
    private const string Resource = "Sigwin";
    private const string UserName = "store-passphrase";

    public static string GetOrCreate()
    {
        var vault = new PasswordVault();
        try
        {
            var cred = vault.Retrieve(Resource, UserName);
            cred.RetrievePassword();
            return cred.Password;
        }
        catch
        {
            // Not present yet (Retrieve throws) — generate and store one.
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            var passphrase = Convert.ToBase64String(bytes);
            vault.Add(new PasswordCredential(Resource, UserName, passphrase));
            return passphrase;
        }
    }
}
