using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Thunderstorms on some worlds: lightning flickering under the clouds round one or two storm cells.
    /// Seeded by the planet id (the same worlds storm every visit, from any system view); VR-only dressing.
    /// Two pooled glow sprites per stormy world (a hot strike and the cloud it lights), no per-strike allocation.
    /// </summary>
    public sealed partial class SystemAmbience
    {
        sealed class Weather
        {
            public Transform Planet;
            public float Radius;
            public Vector3[] Cells;
            public Transform Strike;
            public Transform Wash;
            public float Next;
            public float Until;
            public float Size;
        }

        readonly List<Weather> _weather = new();
        static readonly Color StrikeTint = new(0.8f, 0.9f, 1f);
        static readonly Color WashTint = new(0.45f, 0.6f, 1f);

        void BuildWeather()
        {
            if (_focus == null || _ext == null)
                return;
            foreach (var p in _focus.Planets)
            {
                // About a third of worlds are stormy; the planet id decides, not the system.
                if (Hash01(p.Id * 48271 + 17) > 0.34f || !_ext.TryGetPlanet(p.Id, out var body))
                    continue;
                var cells = new Vector3[Hash01(p.Id * 48271 + 18) < 0.4f ? 2 : 1];
                for (var i = 0; i < cells.Length; i++)
                {
                    // Storm belts sit at low and middle latitudes.
                    var lon = Hash01(p.Id * 131 + i * 7) * Mathf.PI * 2f;
                    var lat = (Hash01(p.Id * 137 + i * 11) - 0.5f) * 1.6f;
                    cells[i] = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                }

                var w = new Weather
                {
                    Planet = body,
                    Radius = WorldScale.PlanetRadius(Mathf.Max(1, p.Slot)),
                    Cells = cells,
                    Strike = Sprite(body, "Lightning", Vector3.zero, 1f, StrikeTint),
                    Wash = Sprite(body, "StormGlow", Vector3.zero, 1f, WashTint * 0.6f),
                    Next = Time.time + Random.Range(0.5f, 4f)
                };
                Hide(w);
                _weather.Add(w);
            }
        }

        static void Hide(Weather w)
        {
            w.Strike.gameObject.SetActive(false);
            w.Wash.gameObject.SetActive(false);
        }

        void TickWeather(float t)
        {
            for (var i = 0; i < _weather.Count; i++)
            {
                var w = _weather[i];
                if (w.Planet == null)
                    continue;
                if (t < w.Until)
                {
                    // A strike flickers two or three times before it dies.
                    var k = Mathf.PerlinNoise(t * 38f, i * 3.1f);
                    w.Strike.localScale = Vector3.one * (w.Size * (0.35f + k));
                    continue;
                }

                if (w.Strike.gameObject.activeSelf)
                    Hide(w);
                if (t < w.Next)
                    continue;
                // Bursts of strikes, then a lull.
                w.Next = t + (Random.value < 0.45f ? Random.Range(0.1f, 0.4f) : Random.Range(1.5f, 5f));
                w.Until = t + Random.Range(0.08f, 0.22f);
                var cell = w.Cells[Random.Range(0, w.Cells.Length)];
                var dir = (cell + Random.insideUnitSphere * 0.22f).normalized;
                // Only on the face turned to the player: a strike near the limb would glow out in empty space
                // (the cell turns with the world, so its storms come round again).
                var eye = Camera.main;
                if (eye != null && Vector3.Dot(w.Planet.TransformDirection(dir),
                        (eye.transform.position - w.Planet.position).normalized) < 0.35f)
                    continue;
                // Just above the atmosphere shell (inside it the haze would swallow the flash).
                var at = dir * (w.Radius * 1.075f);
                w.Size = w.Radius * Random.Range(0.12f, 0.24f);
                w.Strike.localPosition = at;
                w.Wash.localPosition = at;
                w.Wash.localScale = Vector3.one * (w.Size * Random.Range(3f, 4.5f));
                w.Strike.gameObject.SetActive(true);
                w.Wash.gameObject.SetActive(true);
            }
        }
    }
}
