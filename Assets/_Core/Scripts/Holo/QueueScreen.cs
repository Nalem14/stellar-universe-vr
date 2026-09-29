using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Stations;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Holo
{
    /// <summary>
    /// The order queue board: a holo screen on a slim stand at the port rim of the table, turned between the
    /// standing captain and the chair. It lists the queue of the ship picked on the table (else the one we
    /// are aboard) in run order — 1 = running / next, the current step lit — with, per step, move up / move
    /// down (SetFleetOrderQueue with the steps still to run) and remove (RemoveFleetOrderStep); below, the
    /// Loop toggle (ToggleFleetQueueLoop) and Clear (ClearFleetOrderQueue, two presses).
    /// Steps are added from the table: pick the ship, point at a target, "Add to queue" on the lectern.
    /// Rebuilt only when the focus / fleets / table selection change (dirty flag, ≤ 2 per second); no polling
    /// of its own — each order is followed by one FleetPoller read, as everywhere on the bridge.
    /// </summary>
    public sealed class QueueScreen : MonoBehaviour
    {
        static readonly Vector2 Size = new(0.62f, 0.66f);
        /// <summary>Floor spot: port rim of the table, mirror of the tutorial stand (starboard).</summary>
        static readonly Vector3 Floor = new(-1.05f, 0f, -0.45f);
        const int MaxRows = 7;
        const float RowTop = 160f;
        const float RowStep = 58f;
        const float RowH = 50f;
        const float RowW = 560f;
        static readonly Color Current = new(0.35f, 1f, 1f, 1f);
        static readonly Color Pending = new(1f, 0.78f, 0.45f, 1f);

        FocusContext _focus;
        FleetPoller _poller;
        HoloScreen _screen;
        RectTransform _body;
        TMP_Text _status;
        TwoPress _clear;
        bool _dirty = true;
        bool _busy;
        float _next;
        string _sig;
        int _fleetId;
        TacticalCommand _tc;
        readonly List<int> _serverIndex = new();
        /// <summary>Row 1 is already running (flight / harvest / survey): it keeps its place.</summary>
        bool _underWay;

        public static QueueScreen Build(Transform room, FocusContext focus, FleetPoller poller)
        {
            var stand = new GameObject("QueueStand").transform;
            stand.SetParent(room, false);
            stand.localPosition = Floor;
            var q = stand.gameObject.AddComponent<QueueScreen>();
            q._focus = focus;
            q._poller = poller;

            // Same furniture as the tutorial stand: rounded base, post, lit trim (amber = the queue's colour).
            UiKit.MeshPiece(stand, "Base", UiMeshes.RoundedBox(new Vector3(0.34f, 0.04f, 0.26f), 0.02f),
                UiKit.Chassis, new Vector3(0f, 0.02f, 0f));
            UiKit.MeshPiece(stand, "Post", UiMeshes.RoundedBox(new Vector3(0.06f, 0.78f, 0.06f), 0.02f),
                UiKit.Chassis, new Vector3(0f, 0.43f, 0.03f));
            var trim = UiKit.MeshPiece(stand, "Trim", UiMeshes.RoundedBox(new Vector3(0.02f, 0.66f, 0.065f), 0.008f),
                UiKit.Cap, new Vector3(0f, 0.43f, 0.03f));
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, UiKit.Amber);
            block.SetFloat(UiKit.AccentMulId, 1.2f);
            trim.GetComponent<MeshRenderer>().SetPropertyBlock(block);

            // Below the eye line (never hides the port stations for long), aimed between the standing spot and
            // the chair: poked from the table rim, ray-read from the seat.
            q._screen = HoloScreen.Create(stand, "QueueScreen", Size, new Vector3(0f, 1.14f, 0f), Quaternion.identity,
                Trans.Get("orderQueue"));
            var standEye = WorldScale.CicCaptainStand + Vector3.up * WorldScale.EyeStanding;
            var chairEye = new Vector3(0f, WorldScale.EyeSeated, WorldScale.CicCaptainChairZ);
            ScreenMount.FaceViewer(q._screen.transform, room.TransformPoint(Vector3.Lerp(standEye, chairEye, 0.45f)), 1f, 6f);
            q._screen.SetAccent(UiKit.Amber, 0.5f);

            q._body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            q._body.SetParent(q._screen.Content, false);
            q._body.sizeDelta = q._screen.PixelSize;
            q._status = ScreenKit.Line(q._screen.Content, string.Empty, 0f, -232f, 17f, UiKit.TextDim, RowW,
                TextAlignmentOptions.Center);
            q._clear = new TwoPress(q.MarkDirty);

            if (focus != null)
            {
                focus.Changed += q.MarkDirty;
                focus.FleetsChanged += q.MarkDirty;
            }

            return q;
        }

        void Start()
        {
            _tc = TacticalCommand.Instance;
            if (_tc != null)
                _tc.Changed += OnSelection;
        }

        void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.Changed -= MarkDirty;
                _focus.FleetsChanged -= MarkDirty;
            }

            if (_tc != null)
                _tc.Changed -= OnSelection;
        }

        void MarkDirty() => _dirty = true;

        void OnSelection()
        {
            // Changed also fires on hover: rebuild only when the picked ship itself changes.
            if (Fleet()?.Id != _fleetId)
                MarkDirty();
        }

        void Update()
        {
            _clear.Tick();
            if (!_dirty || _busy || Time.unscaledTime < _next)
                return;
            _dirty = false;
            _next = Time.unscaledTime + 0.5f;
            Render(false);
        }

        /// <summary>The ship picked on the table when it is ours, else the one we are aboard.</summary>
        FocusFleet Fleet()
        {
            var sel = TacticalCommand.Instance != null ? TacticalCommand.Instance.SelectedFleetId : 0;
            var f = sel > 0 ? _focus?.FindFleet(sel) : null;
            if (f != null && _focus.IsMine(f))
                return f;
            return _focus?.FindViewFleet();
        }

        string Signature(FocusFleet f)
        {
            if (f == null)
                return "none";
            var h = f.Id * 31 + OrderQueue.ActiveStep(f) * 7 + (f.QueueLoop ? 1 : 0) + (f.CanIssueMove(FleetOrderGate.UnixNow()) ? 3 : 5);
            foreach (var s in f.Queue)
                h = h * 31 + (s.Type?.GetHashCode() ?? 0) + s.TargetId + (int)s.X * 3 + (int)s.Y * 5;
            return h + "|" + f.Name + "|" + _clear.IsArmed("clear");
        }

        void Render(bool force)
        {
            var fleet = Fleet();
            var sig = Signature(fleet);
            if (!force && sig == _sig)
                return;
            _sig = sig;
            _fleetId = fleet?.Id ?? 0;
            ScreenKit.Clear(_body);
            var px = _screen.PixelSize;

            if (fleet == null)
            {
                ScreenKit.Para(_body, Trans.Get("vr.table.pickShip"), 0f, 40f, 22f, UiKit.TextDim, RowW, 200f,
                    TextAlignmentOptions.Center);
                return;
            }

            var n = fleet.Queue.Count;
            var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
            var head = "<b>" + ScreenKit.Verbatim(name) + "</b>";
            if (!fleet.CanIssueMove(FleetOrderGate.UnixNow()))
                head += "  <color=#ffb866>" + Trans.Get(FleetOrderGate.BusyKey(fleet)) + "</color>";
            if (n > 0)
                head += "  <color=#9fd8ff>" + Mathf.Min(OrderQueue.ActiveStep(fleet) + 1, n) + " / " + n + "</color>";
            ScreenKit.Line(_body, head, 0f, px.y * 0.5f - 112f, 20f, UiKit.TextBright, RowW, TextAlignmentOptions.Center);

            var order = OrderQueue.RunOrder(fleet);
            _serverIndex.Clear();
            for (var k = 0; k < order.Count; k++)
                _serverIndex.Add(OrderQueue.IndexOfRun(fleet, k));
            _underWay = OrderQueue.FirstUnderWay(fleet);

            if (order.Count == 0)
            {
                ScreenKit.Para(_body, Trans.Get(n > 0 ? "queueDone" : "queueEmpty"), 0f, 60f, 22f, UiKit.TextBright,
                    RowW, 110f, TextAlignmentOptions.Center);
                ScreenKit.Para(_body, Trans.Get("vr.queue.hint"), 0f, -60f, 18f, UiKit.TextDim, RowW, 110f,
                    TextAlignmentOptions.Center);
            }
            else
            {
                var shown = order.Count > MaxRows ? MaxRows - 1 : order.Count;
                for (var k = 0; k < shown; k++)
                    Row(fleet, order, k);
                if (shown < order.Count)
                    ScreenKit.Line(_body, "…  +" + (order.Count - shown), 0f, RowTop - shown * RowStep, 20f,
                        UiKit.TextDim, RowW, TextAlignmentOptions.Center);
            }

            // Footer: loop toggle (lit when on) and clear (two presses).
            var footY = -px.y * 0.5f + 55f;
            var loop = fleet.QueueLoop;
            var loopBtn = ScreenKit.Btn(_body, Trans.Get("orderQueueLoop"), -140f, footY, 270f, 48f,
                () => AsyncTap.Run(Run(() => OrderQueue.SetLoop(fleet, !loop), loop ? "queueLoopDisabled" : "queueLoopEnabled")),
                loop ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost, !_busy);
            loopBtn.name = "LoopToggle";
            var clear = _clear.Make(_body, "clear", Trans.Get("clearQueue"), 150f, footY, 240f, 48f,
                () => Run(() => OrderQueue.Clear(fleet), "queueCleared"));
            clear.interactable = n > 0 && !_busy;
        }

        void Row(FocusFleet fleet, List<FocusQueueStep> order, int k)
        {
            var y = RowTop - k * RowStep;
            var current = k == 0;
            var bg = new GameObject("Step" + k, typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(_body, false);
            var rt = bg.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(RowW, RowH);
            rt.anchoredPosition = new Vector2(0f, y);
            var img = bg.GetComponent<Image>();
            img.sprite = current ? DiegeticUi.SprTabActive : DiegeticUi.SprField;
            img.type = Image.Type.Sliced;
            img.color = current ? Color.white : new Color(1f, 1f, 1f, 0.8f);
            img.raycastTarget = false;

            var tint = current ? Current : Pending;
            ScreenKit.Line(_body, "<b>" + (k + 1) + "</b>", -RowW * 0.5f + 26f, y, 24f, tint, 36f, TextAlignmentOptions.Center);
            var label = OrderQueue.Describe(order[k], _focus);
            if (current)
                label = "<color=#8ff6ff>" + Trans.Get("queueActive") + "</color>  " + label;
            ScreenKit.Line(_body, label, -RowW * 0.5f + 48f + 170f, y, 18f, UiKit.TextBright, 340f);

            var index = _serverIndex[k];
            var last = order.Count - 1;
            var first = _underWay ? 1 : 0;
            ScreenKit.Btn(_body, "▲", 128f, y, 46f, 40f,
                () => AsyncTap.Run(Run(() => OrderQueue.Swap(fleet, k, k - 1), "vr.table.queueRerouted")),
                DiegeticUi.BtnStyle.Ghost, k > first && !_busy);
            ScreenKit.Btn(_body, "▼", 180f, y, 46f, 40f,
                () => AsyncTap.Run(Run(() => OrderQueue.Swap(fleet, k, k + 1), "vr.table.queueRerouted")),
                DiegeticUi.BtnStyle.Ghost, k >= first && k < last && !_busy);
            ScreenKit.Btn(_body, "×", 240f, y, 50f, 40f,
                () => AsyncTap.Run(Run(() => OrderQueue.Remove(fleet, index), "queueStepRemoved")),
                DiegeticUi.BtnStyle.Danger, !_busy);
        }

        async Task Run(Func<Task<ApiResult>> call, string okKey)
        {
            if (_busy)
                return;
            _busy = true;
            _clear.Reset();
            Render(true);
            try
            {
                var r = await call();
                if (r.Ok)
                    CicCue.Ok(_screen.transform.position);
                else
                    CicCue.Fail(_screen.transform.position);
                _status.text = r.Ok ? Trans.Get(okKey) : string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error;
                _status.color = r.Ok ? UiKit.Ok : UiKit.Danger;
                if (_poller != null)
                    await _poller.PollNow();
            }
            finally
            {
                _busy = false;
                _sig = null;
                _dirty = true;
            }
        }
    }
}
