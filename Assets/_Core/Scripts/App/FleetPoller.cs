using System.Collections;
using System.Threading.Tasks;
using Core.Utils;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Polls GetAllFleetsAround so exterior ships stay in sync with the server.
    /// </summary>
    public class FleetPoller : MonoBehaviour
    {
        FocusContext _focus;
        float _interval = 2.5f;
        Coroutine _loop;

        public void Bind(FocusContext focus, float intervalSeconds = 2.5f)
        {
            _focus = focus;
            _interval = Mathf.Max(1f, intervalSeconds);
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
            var result = await ActionJs.Get("GetAllFleetsAround");
            if (!result.Ok || _focus == null)
                return;
            _focus.ApplyFleetsBody(result.Body);
        }
    }
}
