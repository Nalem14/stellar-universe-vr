using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.Entity;
using Core.Utils;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Core.App
{
    public class AuthManager : MonoBehaviour
    {
        const string TokenKey = "su.vr.token";

        public static AuthManager Instance { get; private set; }

        public UserSession User { get; private set; }
        public JObject Empire { get; private set; }

        /// <summary>
        /// GetMeEmpire answers even without an empire (no id). Creating one has no API action yet
        /// (docs/PARITY.md, CreateEmpire spec): the bridge cannot boot until the server exposes it.
        /// </summary>
        public bool HasEmpire => Empire != null && FocusContext.AsInt(Empire["id"]) > 0;
        public bool IsLoggedIn => User != null && !string.IsNullOrEmpty(User.token);
        public string Token => User?.token;
        public bool HasSavedToken => !string.IsNullOrEmpty(PlayerPrefs.GetString(TokenKey, string.Empty));

        public event Action LoggedIn;
        public event Action LoggedOut;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Edit Mode: Destroy() is deferred and leaves Boot hierarchy polluted.
                if (!Application.isPlaying)
                    DestroyImmediate(gameObject);
                else
                    Destroy(gameObject);
                return;
            }

            Instance = this;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
            ActionJs.TokenProvider = () => Token;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                if (ActionJs.TokenProvider != null)
                    ActionJs.TokenProvider = null;
            }
        }

        public static AuthManager Ensure()
        {
            if (Instance != null)
                return Instance;

            // Reclaim a leftover from Edit Mode / prior Play before spawning another.
            var existing = UnityEngine.Object.FindFirstObjectByType<AuthManager>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                if (Application.isPlaying)
                    DontDestroyOnLoad(existing.gameObject);
                ActionJs.TokenProvider = () => Instance.Token;
                return existing;
            }

            var go = new GameObject("AuthManager");
            return go.AddComponent<AuthManager>();
        }

        public async Task<ApiResult> Login(string email, string password)
        {
            var result = await ActionJs.Get("Login", new Dictionary<string, string>
            {
                { "email", email },
                { "password", password }
            }, withToken: false);
            return ApplyAuth(result);
        }

        public async Task<ApiResult> Register(string email, string password, string username)
        {
            var result = await ActionJs.Get("Register", new Dictionary<string, string>
            {
                { "email", email },
                { "password", password },
                { "username", username }
            }, withToken: false);
            return ApplyAuth(result);
        }

        public async Task<ApiResult> LoginToken(string token = null)
        {
            var value = string.IsNullOrEmpty(token) ? PlayerPrefs.GetString(TokenKey, string.Empty) : token;
            if (string.IsNullOrEmpty(value))
                return ApiResult.Fail("no_token");

            var result = await ActionJs.Get("LoginToken", new Dictionary<string, string>
            {
                { "token", value }
            }, withToken: false);
            var applied = ApplyAuth(result);
            // IP-bound token: clear stale local session so Menu "Continuer" does not loop.
            if (!applied.Ok)
            {
                PlayerPrefs.DeleteKey(TokenKey);
                PlayerPrefs.Save();
                User = null;
                Empire = null;
            }

            return applied;
        }

        /// <summary>Last GetMeEmpire said this account has no empire yet (airlock founding flow).</summary>
        public bool NoEmpire { get; private set; }

        public async Task<ApiResult> FetchMe()
        {
            if (!IsLoggedIn)
                return ApiResult.Fail("not_logged");
            var result = await ActionJs.Get("GetMeEmpire");
            NoEmpire = false;
            if (!result.Ok)
            {
                // Account without an empire: error:<Lang(noEmpire)> — a state to found one, not a failure.
                if (!string.IsNullOrEmpty(result.Error) && (result.Error == Trans.Get("noEmpire") || result.Error == "noEmpire"))
                {
                    Empire = null;
                    NoEmpire = true;
                }

                return result;
            }
            try
            {
                Empire = JObject.Parse(result.Body);
            }
            catch (Exception)
            {
                return ApiResult.Fail("bad_json");
            }

            return result;
        }

        public void Logout()
        {
            User = null;
            Empire = null;
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.Save();
            BridgeViewAnchor.Clear();
            DiplomacyIndex.Clear();
            LoggedOut?.Invoke();
        }

        ApiResult ApplyAuth(ApiResult result)
        {
            if (!result.Ok)
                return result;

            UserSession parsed;
            try
            {
                parsed = JsonUtility.FromJson<UserSession>(result.Body);
            }
            catch (Exception)
            {
                return ApiResult.Fail("bad_json");
            }

            if (parsed == null || string.IsNullOrEmpty(parsed.token))
                return ApiResult.Fail("no_token");

            User = parsed;
            PlayerPrefs.SetString(TokenKey, parsed.token);
            PlayerPrefs.Save();
            LoggedIn?.Invoke();
            return result;
        }
    }
}
