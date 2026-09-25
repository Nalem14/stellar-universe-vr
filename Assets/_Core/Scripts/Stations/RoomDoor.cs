using System;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// A door between two rooms, used as a room is used: walk up and its leaves slide open, step into the frame
    /// and you are through (fade + move). A control panel beside it opens it for seated / stationary play.
    /// Door local space: the doorway is the plane z = 0, the room you stand in is +z, the other side −z.
    /// Hidden or shown by its owner (e.g. the dry dock door only exists at an orbital station).
    /// </summary>
    public sealed class RoomDoor : MonoBehaviour
    {
        const float Width = 1.3f;
        const float Height = 2.3f;
        const float OpenSpeed = 2.6f;
        /// <summary>Closer than this (m, room side) and the leaves open.</summary>
        const float WakeDistance = 1.6f;
        /// <summary>Head this close to the doorway plane (walls stop the body just before it) = passing through.</summary>
        const float PassDepth = 0.45f;

        Transform _left;
        Transform _right;
        float _open;
        bool _requested;
        float _cooldown;
        Func<bool> _canPass;
        Action _onPass;
        TextMeshPro _sign;

        public static RoomDoor Build(Transform parent, string name, Vector3 localPos, float yaw, string label,
            Color accent, CicArtKit art, Func<bool> canPass, Action onPass)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var door = root.gameObject.AddComponent<RoomDoor>();
            door._canPass = canPass;
            door._onPass = onPass;

            var frame = art.DarkPanel(0.35f);
            var leaf = art.MetalPanel(0.45f);
            var glow = art.Lit(Texture2D.whiteTexture, accent, 2.4f);

            // Recess behind the leaves: a deep, dim airlock read (the room shell stays whole behind it).
            Piece(root, "Recess", new Vector3(0f, Height * 0.5f, -0.06f), new Vector3(Width, Height, 0.02f),
                art.Lit(Texture2D.whiteTexture, new Color(0.02f, 0.05f, 0.07f), 0.2f));
            Piece(root, "RecessStrip", new Vector3(0f, Height - 0.2f, -0.05f), new Vector3(Width * 0.7f, 0.03f, 0.01f), glow);
            Piece(root, "RecessFloor", new Vector3(0f, 0.02f, -0.05f), new Vector3(Width * 0.8f, 0.02f, 0.01f), glow);

            door._left = Piece(root, "LeafL", new Vector3(-Width * 0.25f, Height * 0.5f, 0f),
                new Vector3(Width * 0.5f, Height, 0.06f), leaf).transform;
            door._right = Piece(root, "LeafR", new Vector3(Width * 0.25f, Height * 0.5f, 0f),
                new Vector3(Width * 0.5f, Height, 0.06f), leaf).transform;
            Piece(door._left, "EdgeL", new Vector3(0.49f, 0f, 0.6f), new Vector3(0.02f, 0.92f, 0.2f), glow);
            Piece(door._right, "EdgeR", new Vector3(-0.49f, 0f, 0.6f), new Vector3(0.02f, 0.92f, 0.2f), glow);

            Piece(root, "JambL", new Vector3(-Width * 0.5f - 0.09f, Height * 0.5f, 0.04f), new Vector3(0.18f, Height + 0.1f, 0.2f), frame);
            Piece(root, "JambR", new Vector3(Width * 0.5f + 0.09f, Height * 0.5f, 0.04f), new Vector3(0.18f, Height + 0.1f, 0.2f), frame);
            Piece(root, "Lintel", new Vector3(0f, Height + 0.16f, 0.04f), new Vector3(Width + 0.36f, 0.3f, 0.2f), frame);
            Piece(root, "LintelGlow", new Vector3(0f, Height + 0.02f, 0.15f), new Vector3(Width + 0.2f, 0.025f, 0.01f), glow);

            door._sign = UiKit.Label(root, "Sign", label, new Vector3(0f, Height + 0.17f, 0.145f), Width, 0.11f, accent);
            door._sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Door control, at hand height on the jamb (poke or ray).
            var panel = PokeButton.Create(root, "DoorControl", label,
                new Vector3(Width * 0.5f + 0.34f, 1.15f, 0.16f), Quaternion.Euler(0f, 180f, 0f),
                new Vector2(0.24f, 0.07f), accent, door.Request);
            panel.transform.localScale = Vector3.one;
            return door;
        }

        static GameObject Piece(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Panel press: open, then take the player through once the leaves are apart.</summary>
        void Request()
        {
            if (_canPass != null && !_canPass())
                return;
            _requested = true;
            CicCue.Ok(transform.position + Vector3.up);
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var local = transform.InverseTransformPoint(cam.transform.position);
            var near = local.z > -0.2f && local.z < WakeDistance && Mathf.Abs(local.x) < Width;
            var want = _requested || near;
            var before = _open;
            _open = Mathf.MoveTowards(_open, want ? 1f : 0f, OpenSpeed * Time.deltaTime);
            if (_open > 0f && before == 0f)
                CicCue.Hover(transform.position + Vector3.up);
            var slide = Width * 0.48f * MotionEase.Smooth01(_open);
            _left.localPosition = new Vector3(-Width * 0.25f - slide, Height * 0.5f, 0f);
            _right.localPosition = new Vector3(Width * 0.25f + slide, Height * 0.5f, 0f);

            if (_cooldown > 0f)
            {
                _cooldown -= Time.deltaTime;
                return;
            }

            var through = local.z < PassDepth && local.z > -1f && Mathf.Abs(local.x) < Width * 0.45f;
            if (_open >= 0.98f && (through || _requested))
            {
                _requested = false;
                _cooldown = 2.5f;
                if (_canPass == null || _canPass())
                    _onPass?.Invoke();
            }
        }

        void OnDisable()
        {
            _open = 0f;
            _requested = false;
        }
    }
}
