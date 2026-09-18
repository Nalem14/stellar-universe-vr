using System.Threading.Tasks;
using UnityEngine;

namespace Core.Utils
{
    /// <summary>Fire-and-forget Tasks from XR UnityEvents without assigning Task into the event-arg slot.</summary>
    public static class AsyncTap
    {
        public static void Run(Task task)
        {
            if (task == null)
                return;
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                    Debug.LogException(t.Exception.GetBaseException());
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
