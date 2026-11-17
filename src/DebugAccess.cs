using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LanPerfMon
{
    /// <summary>
    /// Access control for /api/debug.
    ///
    /// The server itself is an unauthenticated LAN tool, and /api/debug exposes
    /// machine-level detail (absolute paths, token integrity, session id). Anything
    /// on the same network can reach it, so callers must present a per-process token
    /// before the sensitive fields are returned.
    ///
    /// The token is intentionally only obtainable on the machine itself: the main
    /// window's "copy diagnostic token" button and the tray menu. It never appears
    /// in a response body, so a remote LAN peer cannot bootstrap into it.
    /// </summary>
    internal static class DebugAccess
    {
        private static readonly string TokenValue = Generate();

        /// <summary>Current process token (shown only by the local UI).</summary>
        internal static string Token
        {
            get { return TokenValue; }
        }

        private static string Generate()
        {
            // 128 bits of entropy, rendered as 32 hex chars. RNGCryptoServiceProvider
            // is available on .NET Framework 4.8 and needs no extra reference.
            byte[] bytes = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(bytes);

            StringBuilder sb = new StringBuilder(32);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// True when the supplied token matches. Comparison is length-safe and
        /// fixed-time: it always walks the full token length and accumulates the
        /// difference, so a wrong token cannot be narrowed down by timing.
        /// </summary>
        internal static bool IsValid(string supplied)
        {
            if (string.IsNullOrEmpty(supplied))
                return false;

            string expected = TokenValue;
            // Compare against the expected length regardless of the input length.
            int diff = supplied.Length ^ expected.Length;
            int max = expected.Length;

            for (int i = 0; i < max; i++)
            {
                char a = i < expected.Length ? expected[i] : '\0';
                char b = i < supplied.Length ? supplied[i] : '\0';
                diff |= a ^ b;
            }

            return diff == 0;
        }

        /// <summary>
        /// Reduces an absolute path to its last component, so an unauthenticated
        /// caller learns nothing about the machine's directory layout.
        /// </summary>
        internal static string RedactPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            try
            {
                string trimmed = path.TrimEnd('\\', '/');
                int slash = trimmed.LastIndexOfAny(new char[] { '\\', '/' });
                if (slash >= 0 && slash + 1 < trimmed.Length)
                    return "…\\" + trimmed.Substring(slash + 1);
            }
            catch { }

            return "…";
        }

        /// <summary>Redacts a free-form diagnostic string that may embed a path.</summary>
        internal static string RedactText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Replace anything that looks like an absolute drive path with its
            // last component, keeping the rest of the message (e.g. "OK", "FAIL").
            StringBuilder sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                bool isDriveStart = (i + 2 < text.Length) &&
                                    char.IsLetter(text[i]) &&
                                    text[i + 1] == ':' &&
                                    (text[i + 2] == '\\' || text[i + 2] == '/');
                if (isDriveStart)
                {
                    int end = i;
                    while (end < text.Length &&
                           text[end] != ' ' && text[end] != '\r' && text[end] != '\n' &&
                           text[end] != '"' && text[end] != '\'')
                        end++;
                    sb.Append(RedactPath(text.Substring(i, end - i)));
                    i = end;
                }
                else
                {
                    sb.Append(text[i]);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}
