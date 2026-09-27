using UnityEngine;

namespace Core.UI
{
    /// <summary>
    /// TMP_InputField only opens the Quest system keyboard when <c>SystemInfo.deviceModel</c> is exactly
    /// "Oculus Quest" (Quest 1/2 era). Quest 3 / 3S / Pro report "Oculus Quest 3S" etc., so TMP believes in-place
    /// editing is available (a hardware keyboard) and never opens the soft keyboard: fields select but nothing
    /// shows. Before the first scene loads, mark any Quest-family headset as a Quest for TMP.
    /// </summary>
    static class QuestKeyboardFix
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            var model = SystemInfo.deviceModel ?? string.Empty;
            if (!model.StartsWith("Oculus") && !model.StartsWith("Meta") && model.IndexOf("Quest", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;
            const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var type = typeof(TMPro.TMP_InputField);
            var quest = type.GetField("s_IsQuestDevice", Flags);
            var evaluated = type.GetField("s_IsQuestDeviceEvaluated", Flags);
            if (quest == null || evaluated == null)
            {
                Debug.LogWarning("[QuestKeyboardFix] TMP_InputField Quest flags not found; system keyboard may not open.");
                return;
            }

            quest.SetValue(null, true);
            evaluated.SetValue(null, true);
        }
#endif
    }
}
