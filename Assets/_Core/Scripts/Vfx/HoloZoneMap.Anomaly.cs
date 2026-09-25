using Core.App;
using Core.Utils;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Cosmic anomalies on the system map: a gyroscope of two holo rings round a glowing core, tinted by
    /// type. Drop one of our scanner-equipped ships on it → ScanAnomaly (<see cref="HoloFleetOrders"/>).
    /// </summary>
    public partial class HoloZoneMap
    {
        public static Color AnomalyTint(string type) => type switch
        {
            "derelict_ship" => new Color(1f, 0.7f, 0.35f, 1f),
            "precursor_cache" => new Color(0.75f, 0.5f, 1f, 1f),
            "crystal_monolith" => new Color(0.4f, 1f, 0.85f, 1f),
            "nebula_rift" => new Color(1f, 0.35f, 0.8f, 1f),
            _ => new Color(0.7f, 0.85f, 1f, 1f)
        };

        /// <summary>The anomaly list of the system on the table changed (fetch or scan).</summary>
        public void OnAnomaliesChanged(int systemId)
        {
            if (_focus != null && _focus.SystemId == systemId)
                RequestRebuild();
        }

        void PlaceAnomalies(int systemId)
        {
            var service = AnomalyService.Instance;
            if (service == null)
                return;
            foreach (var a in service.For(systemId))
                PlaceAnomaly(a);
        }

        void PlaceAnomaly(Anomaly a)
        {
            // Between planet orbits, on a stable bearing: the server gives anomalies no position.
            var slot = 2 + a.Id % 4;
            var r = OrbitR(slot + 0.5f);
            var ang = StableAngle(a.Id * 71 + 3);
            var pos = new Vector3(Mathf.Cos(ang) * r, DioramaLift + 0.03f, Mathf.Sin(ang) * r);
            var tint = AnomalyTint(a.Type);
            var go = new GameObject("TokenAnomaly_" + a.Id);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            _tokenRoots.Add(go);

            var ringTex = _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture;
            var ringMat = _art.RadarIcon(ringTex, new Color(tint.r, tint.g, tint.b, 0.9f));
            var s = 0.055f;
            var gyro = new GameObject("Gyro").transform;
            gyro.SetParent(go.transform, false);
            for (var i = 0; i < 2; i++)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ring.name = "Ring" + i;
                ring.transform.SetParent(gyro, false);
                ring.transform.localRotation = Quaternion.Euler(i == 0 ? 0f : 90f, i * 90f, 0f);
                ring.transform.localScale = Vector3.one * s * (i == 0 ? 1f : 0.8f);
                DropCollider(ring);
                ring.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
            }

            var spin = gyro.gameObject.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 40f;
            spin.BobMeters = 0.006f;

            var core = GameObject.CreatePrimitive(PrimitiveType.Quad);
            core.name = "CoreGlow";
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = Vector3.one * s * 0.7f;
            DropCollider(core);
            core.AddComponent<BillboardFace>();
            var glowTex = _art.ProjectorGlow != null ? _art.ProjectorGlow : Texture2D.whiteTexture;
            core.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(glowTex, new Color(tint.r, tint.g, tint.b, 0.8f));

            // Signal beam down to the plate and a flat ring where it lands: reads from across the table.
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "SignalBeam";
            beam.transform.SetParent(go.transform, false);
            beam.transform.localPosition = new Vector3(0f, -0.015f, 0f);
            beam.transform.localScale = new Vector3(0.004f, 0.015f, 0.004f);
            DropCollider(beam);
            beam.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(Texture2D.whiteTexture,
                new Color(tint.r, tint.g, tint.b, 0.55f));
            var foot = GameObject.CreatePrimitive(PrimitiveType.Quad);
            foot.name = "SignalFoot";
            foot.transform.SetParent(go.transform, false);
            foot.transform.localPosition = new Vector3(0f, -0.029f, 0f);
            foot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foot.transform.localScale = Vector3.one * s * 1.3f;
            DropCollider(foot);
            foot.GetComponent<MeshRenderer>().sharedMaterial = _art.RadarIcon(ringTex, new Color(tint.r, tint.g, tint.b, 0.45f));
            var footSpin = foot.AddComponent<HoloSpin>();
            footSpin.DegreesPerSecond = -12f;
            footSpin.BobMeters = 0f;

            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one * s * 1.1f;
            col.isTrigger = true;

            var label = Trans.Get(a.NameKey);
            AddTokenLabel(go.transform, label, s * 0.9f, tint, plate: false, startVisible: false);
            Tag(go, HoloTokenKind.Anomaly, a.Id, slot, owned: false, busy: false, label);
        }
    }
}
