using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Stratum.Core
{
    // Keeps a login's secrets off the wire. The server has a key pair and sends the public half with a one-time
    // number when a game says hello. The game seals the password (or remember-me token) and the invite code so
    // that only that server can read them, only this once: a fresh key for this login, with the one-time number,
    // is encrypted to the server's public key (RSA-OAEP), and the secrets are encrypted with the fresh key (AES)
    // and signed (HMAC). The server uses the same fresh key to hide the remember-me token it sends back.
    // Someone listening learns neither the password nor the token, and can't replay what they recorded.
    public static class Secure
    {
        const string Mark = "enc:";

        public static string Fingerprint(byte[] modulus)
        {
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(modulus);
            var sb = new StringBuilder();
            for (int i = 0; i < 16; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }

        static byte[] Random(int n) { var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
        public static byte[] Nonce() => Random(16);

        static byte[] Mac(byte[] key, byte[] data, int n)
        {
            using var h = new HMACSHA256(key);
            return h.ComputeHash(data, 0, n);
        }

        static byte[] AesRun(byte[] key, byte[] iv, byte[] data, bool encrypt)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = key; aes.IV = iv;
            using var t = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
            return t.TransformFinalBlock(data, 0, data.Length);
        }

        // iv | ciphertext | mac
        static byte[] Box(byte[] key, byte[] plain)
        {
            var iv = Random(16);
            var ct = AesRun(key, iv, plain, true);
            var all = new byte[16 + ct.Length + 32];
            Array.Copy(iv, all, 16); Array.Copy(ct, 0, all, 16, ct.Length);
            Array.Copy(Mac(key, all, 16 + ct.Length), 0, all, 16 + ct.Length, 32);
            return all;
        }

        static byte[] Unbox(byte[] key, byte[] all)
        {
            if (all == null || all.Length < 16 + 16 + 32) return null;
            var mac = Mac(key, all, all.Length - 32);
            int diff = 0; for (int i = 0; i < 32; i++) diff |= mac[i] ^ all[all.Length - 32 + i];
            if (diff != 0) return null;
            var iv = new byte[16]; Array.Copy(all, iv, 16);
            var ct = new byte[all.Length - 48]; Array.Copy(all, 16, ct, 0, ct.Length);
            try { return AesRun(key, iv, ct, false); } catch (CryptographicException) { return null; }
        }

        // The game's side: seal a login for the server whose public key this is. `key` is kept to read the answer.
        public static byte[] Seal(byte[] modulus, byte[] exponent, byte[] nonce, string secret, string invite, out byte[] key)
        {
            key = Random(32);
            var head = new byte[nonce.Length + 32];
            Array.Copy(nonce, head, nonce.Length); Array.Copy(key, 0, head, nonce.Length, 32);
            byte[] wrapped;
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
                wrapped = rsa.Encrypt(head, RSAEncryptionPadding.OaepSHA1);
            }
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8)) { w.Write(secret ?? ""); w.Write(invite ?? ""); }
            var box = Box(key, ms.ToArray());
            var all = new byte[2 + wrapped.Length + box.Length];
            all[0] = (byte)(wrapped.Length & 255); all[1] = (byte)(wrapped.Length >> 8);
            Array.Copy(wrapped, 0, all, 2, wrapped.Length); Array.Copy(box, 0, all, 2 + wrapped.Length, box.Length);
            return all;
        }

        // The server's side. False if it wasn't sealed for this server and this one-time number, or was tampered with.
        public static bool Open(RSA rsa, byte[] nonce, byte[] sealedLogin, out string secret, out string invite, out byte[] key)
        {
            secret = invite = null; key = null;
            try
            {
                if (sealedLogin == null || nonce == null || sealedLogin.Length < 2) return false;
                int n = sealedLogin[0] | sealedLogin[1] << 8;
                if (n <= 0 || 2 + n > sealedLogin.Length) return false;
                var wrapped = new byte[n]; Array.Copy(sealedLogin, 2, wrapped, 0, n);
                var head = rsa.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA1);
                if (head.Length != nonce.Length + 32) return false;
                int diff = 0; for (int i = 0; i < nonce.Length; i++) diff |= head[i] ^ nonce[i];
                if (diff != 0) return false;
                key = new byte[32]; Array.Copy(head, nonce.Length, key, 0, 32);
                var box = new byte[sealedLogin.Length - 2 - n]; Array.Copy(sealedLogin, 2 + n, box, 0, box.Length);
                var plain = Unbox(key, box);
                if (plain == null) return false;
                using var r = new BinaryReader(new MemoryStream(plain), Encoding.UTF8);
                secret = r.ReadString(); invite = r.ReadString();
                return true;
            }
            catch (Exception) { return false; }
        }

        // A short text (the remember-me token) hidden with the login's key.
        public static string SealText(byte[] key, string text) => Mark + Convert.ToBase64String(Box(key, Encoding.UTF8.GetBytes(text ?? "")));
        public static bool IsSealed(string s) => s != null && s.StartsWith(Mark, StringComparison.Ordinal);
        public static string OpenText(byte[] key, string s)
        {
            if (!IsSealed(s) || key == null) return null;
            try { var p = Unbox(key, Convert.FromBase64String(s.Substring(Mark.Length))); return p == null ? null : Encoding.UTF8.GetString(p); }
            catch (FormatException) { return null; }
        }
    }
}
