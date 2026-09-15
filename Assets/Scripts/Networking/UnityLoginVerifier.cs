using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Server-side: proves an access token really came from Unity Authentication before a login is tied to
// an account. Without this check, a phone could send any player id and take over that account.
//
// The token is a JWT signed by Unity. The server downloads Unity's public keys, checks the signature,
// the issuer, the expiry and (when the server knows it) the project, and only then trusts the player id
// inside. Keys are cached for an hour and refetched at most once a minute.
public static class UnityLoginVerifier
{
    private const string KeysUrl = "https://player-auth.services.api.unity.com/.well-known/jwks.json";
    private const string IssuerPrefix = "https://player-auth.services.api.unity.com";

    [Serializable] private class Header { public string alg; public string kid; }
    [Serializable] private class Claims { public string sub; public string iss; public string project_id; public long exp; }
    [Serializable] private class Key { public string kid; public string n; public string e; }
    [Serializable] private class KeySet { public Key[] keys; }

    private static KeySet keySet;
    private static float keysFetchedAt = -3600f;
    private static float lastFetchAttempt = -60f;

    // Calls back with the verified Unity player id, or never calls back if the token is not genuine.
    public static IEnumerator Verify(string token, Action<string> onVerified)
    {
        string[] parts = token != null ? token.Split('.') : new string[0];

        if (parts.Length != 3)
            yield break;

        Header header = null;
        Claims claims = null;

        try
        {
            header = JsonUtility.FromJson<Header>(Encoding.UTF8.GetString(Base64Url(parts[0])));
            claims = JsonUtility.FromJson<Claims>(Encoding.UTF8.GetString(Base64Url(parts[1])));
        }
        catch (Exception) { }

        if (header == null || claims == null || header.alg != "RS256" || string.IsNullOrEmpty(claims.sub) ||
            claims.iss == null || !claims.iss.StartsWith(IssuerPrefix) ||
            claims.exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            yield break;

        // A token for some other Unity game is a genuine token, and still not a login to this one.
        if (!string.IsNullOrEmpty(claims.project_id) && !string.IsNullOrEmpty(Application.cloudProjectId) &&
            claims.project_id != Application.cloudProjectId)
            yield break;

        if (FindKey(header.kid) == null || Time.realtimeSinceStartup - keysFetchedAt > 3600f)
            yield return FetchKeys();

        Key key = FindKey(header.kid);

        if (key == null)
            yield break;

        bool genuine = false;

        try
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(new RSAParameters { Modulus = Base64Url(key.n), Exponent = Base64Url(key.e) });
                genuine = rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64Url(parts[2]),
                                         HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("UnityLoginVerifier: could not check a token signature. " + e.Message);
        }

        if (genuine)
            onVerified(claims.sub);
    }

    private static IEnumerator FetchKeys()
    {
        if (Time.realtimeSinceStartup - lastFetchAttempt < 60f)
            yield break;

        lastFetchAttempt = Time.realtimeSinceStartup;

        using (UnityWebRequest request = UnityWebRequest.Get(KeysUrl))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("UnityLoginVerifier: could not download Unity's keys. " + request.error);
                yield break;
            }

            KeySet parsed = null;

            try { parsed = JsonUtility.FromJson<KeySet>(request.downloadHandler.text); }
            catch (Exception) { }

            if (parsed != null && parsed.keys != null && parsed.keys.Length > 0)
            {
                keySet = parsed;
                keysFetchedAt = Time.realtimeSinceStartup;
            }
        }
    }

    private static Key FindKey(string kid)
    {
        if (keySet == null || keySet.keys == null)
            return null;

        foreach (Key key in keySet.keys)
            if (key != null && key.kid == kid)
                return key;

        return null;
    }

    private static byte[] Base64Url(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
