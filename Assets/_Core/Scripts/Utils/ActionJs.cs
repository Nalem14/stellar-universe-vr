using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Core.Utils
{
    public static class ActionJs
    {
        public const string Host = "https://www.stellar-universe.com";
        public const string Endpoint = Host + "/actionjs.php";

        public static Func<string> TokenProvider { get; set; }

        public static async Task<ApiResult> Get(
            string action,
            IDictionary<string, string> query = null,
            bool withToken = true)
        {
            if (string.IsNullOrEmpty(action))
                return ApiResult.Fail("no_action");

            var url = BuildUrl(action, query, withToken);
            using var req = UnityWebRequest.Get(url);
            req.timeout = 30;
            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                Debug.LogWarning($"[SU] {action} network: {req.error}");
                return ApiResult.Fail("network");
            }

            var body = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
            if (body.StartsWith("error:", StringComparison.Ordinal))
            {
                var message = body.Length > 6 ? body.Substring(6) : "error";
                Debug.LogWarning($"[SU] {action} fail: {message}");
                return ApiResult.Fail(message);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[SU] {action} ok ({DescribeBody(body)})");
#endif
            return ApiResult.Success(body);
        }

        static string BuildUrl(string action, IDictionary<string, string> query, bool withToken)
        {
            var sb = new StringBuilder(Endpoint.Length + 128);
            sb.Append(Endpoint).Append("?action=").Append(Uri.EscapeDataString(action));
            if (query != null)
            {
                foreach (var kv in query)
                {
                    // "action" is the endpoint key; the session token is appended below when withToken
                    // (LoginToken passes its own token with withToken: false and must keep it).
                    if (ShouldOmit(kv.Value) || kv.Key == "action" || (withToken && kv.Key == "token"))
                        continue;
                    sb.Append('&')
                        .Append(Uri.EscapeDataString(kv.Key))
                        .Append('=')
                        .Append(Uri.EscapeDataString(kv.Value));
                }
            }

            if (withToken)
            {
                var token = TokenProvider?.Invoke();
                if (!ShouldOmit(token))
                    sb.Append("&token=").Append(Uri.EscapeDataString(token));
            }

            // Texts the server renders itself (system mail, decision summaries, Lang() errors) follow the
            // player's language: DetectLang() reads $_GET['lang'] first.
            if (query == null || !query.ContainsKey("lang"))
                sb.Append("&lang=").Append(Trans.Lang);
            return sb.ToString();
        }

        static bool ShouldOmit(string value)
        {
            return string.IsNullOrEmpty(value);
        }

        static string DescribeBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                return "empty";
            if (body == "ok" || body.StartsWith("ok:", StringComparison.Ordinal))
                return body;
            return $"{body.Length}c";
        }
    }
}
