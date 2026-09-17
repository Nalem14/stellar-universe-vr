using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.App
{
    public class BridgeDirector : MonoBehaviour
    {
        async void Awake()
        {
            AuthManager.Ensure();
            await Trans.EnsureLoaded();
        }

        void Start()
        {
            var env = gameObject.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();
            var readout = CreateReadout(env.Table != null ? env.Table.transform : transform);
            readout.text = Trans.Get("Loading");
            var boot = gameObject.AddComponent<SessionBoot>();
            boot.BindReadout(readout);
            boot.Run();
        }

        static TMP_Text CreateReadout(Transform parent)
        {
            var go = new GameObject("HoloReadout");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * 0.012f;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 8f;
            tmp.color = new Color(0.55f, 0.95f, 1f, 0.9f);
            tmp.text = string.Empty;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
