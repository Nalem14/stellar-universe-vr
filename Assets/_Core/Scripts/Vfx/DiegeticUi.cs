using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// Production diegetic controls — textured plates, readable TMP, hover emissive.
    /// Not naked grey cubes.
    /// </summary>
    public static class DiegeticUi
    {
        public static TMP_Text Label(Transform parent, string name, string text, Vector3 localPos,
            float worldScale, float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * worldScale;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment = align;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.text = text ?? string.Empty;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Truncate;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static XRSimpleInteractable Plate(Transform parent, string name, Vector3 localPos,
            Vector3 size, Material idle, Material hover, System.Action onSelect, out MeshRenderer rend)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            rend = go.GetComponent<MeshRenderer>();
            rend.sharedMaterial = idle;
            var mesh = rend;
            var interact = go.AddComponent<XRSimpleInteractable>();
            interact.hoverEntered.AddListener(_ =>
            {
                if (hover != null)
                    mesh.sharedMaterial = hover;
                go.transform.localScale = size * 1.06f;
                CicCue.Hover(go.transform.position);
            });
            interact.hoverExited.AddListener(_ =>
            {
                mesh.sharedMaterial = idle;
                go.transform.localScale = size;
            });
            if (onSelect != null)
            {
                interact.selectEntered.AddListener(_ =>
                {
                    CicCue.Ok(go.transform.position);
                    onSelect();
                });
            }

            return interact;
        }

        public static void FaceCaptain(Transform t)
        {
            // Content faces -Z in parent space (toward captain seat when board faces table).
            t.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }
    }
}
