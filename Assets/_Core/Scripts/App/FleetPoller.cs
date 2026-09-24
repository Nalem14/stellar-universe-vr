using System.Collections;
using System.Threading.Tasks;
using Core.Utils;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Polls fleet list so exterior ships stay in sync with the server.
    /// </summary>
    public class FleetPoller : MonoBehaviour
    {
        /// <summary>Server caches GetAllFleets for 3 s (actionjs.php); polling faster returns stale data.</summary>
        public const float DefaultInterval = 3.5f;
        /// <summary>Diplomacy refresh ~ every 45 s at the default fleet cadence.</summary>
        const int DiplomacyEvery = 13;

        FocusContext _focus;
        float _interval = DefaultInterval;
        Coroutine _loop;
        int _pollCount;

        public void Bind(FocusContext focus, float intervalSeconds = DefaultInterval)
        {
            _focus = focus;
            _interval = Mathf.Max(3.1f, intervalSeconds);
            _pollCount = 0;
            if (_loop != null)
                StopCoroutine(_loop);
            _loop = StartCoroutine(Loop());
        }

        public async Task PollNow()
        {
            if (_focus == null || !AuthManager.Ensure().IsLoggedIn)
                return;
            await PollOnce();
        }

        void OnDestroy()
        {
            if (_loop != null)
                StopCoroutine(_loop);
        }

        IEnumerator Loop()
        {
            var wait = new WaitForSeconds(_interval);
            while (true)
            {
                yield return wait;
                if (_focus == null || !AuthManager.Ensure().IsLoggedIn)
                    continue;
                var task = PollOnce();
                while (!task.IsCompleted)
                    yield return null;
            }
        }

        async Task PollOnce()
        {
            _pollCount++;
            if (_pollCount == 1 || _pollCount % DiplomacyEvery == 0)
                await DiplomacyIndex.EnsureLoaded();

            var result = await ActionJs.Get("GetAllFleets");
            if (!result.Ok || _focus == null)
                return;
            _focus.ApplyFleetsBody(result.Body);
        }
    }
}
