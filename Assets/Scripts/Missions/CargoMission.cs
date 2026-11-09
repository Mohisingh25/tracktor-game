using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    /// <summary>Trigger volume helper: tells its owner when a tractor or trolley is inside.</summary>
    public class Zone : MonoBehaviour
    {
        public System.Action<Zone> onTractorEnter;
        public int index;
        public bool TractorInside { get; private set; }
        public bool TrolleyInside { get; private set; }
        readonly HashSet<Collider> tractorCols = new HashSet<Collider>(), trolleyCols = new HashSet<Collider>();

        public static Zone Create(Transform parent, string name, Vector3 pos, Vector3 size, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true; bc.size = size; bc.center = new Vector3(0, size.y * 0.5f, 0);
            return go.AddComponent<Zone>();
        }

        void OnTriggerEnter(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null) return;
            if (rb.GetComponent<TractorController>() != null)
            {
                bool first = tractorCols.Count == 0;
                tractorCols.Add(c); TractorInside = true;
                if (first && onTractorEnter != null) onTractorEnter(this);
            }
            else if (rb.GetComponent<Trolley>() != null) { trolleyCols.Add(c); TrolleyInside = true; }
        }

        void OnTriggerExit(Collider c)
        {
            tractorCols.Remove(c); trolleyCols.Remove(c);
            TractorInside = tractorCols.Count > 0;
            TrolleyInside = trolleyCols.Count > 0;
        }
    }

    public enum MissionStep { HitchTrolley, DriveToLoading, Loading, Deliver, Unload, Complete, Failed }

    /// <summary>
    /// Haul goods from a loading point to a destination through checkpoints:
    /// hitch the trolley, load it, drive the route, tip it in the delivery yard.
    /// Paid by kilograms delivered, with a bonus for a good time. Spilled or overturned cargo costs you.
    /// </summary>
    public class CargoMission : MonoBehaviour
    {
        public string title = "Cargo run";
        public CargoType cargo = CargoType.Apples;
        public float targetKg = 4000f;
        public int basePay = 6000;
        public float parTimeSec = 900f;
        public Zone loadZone, deliveryZone;
        public List<Zone> checkpoints = new List<Zone>();
        public List<Transform> checkpointMarkers = new List<Transform>();

        public MissionStep Step { get; private set; }
        public int NextCheckpoint { get; private set; }
        public float Timer { get; private set; }
        public float DeliveredKg { get; private set; }
        public float LoadedKg { get; private set; }
        public string Objective { get; private set; }
        public Vector3 TargetPos { get; private set; }
        public bool Active { get; private set; }
        public System.Action<string, bool> OnMessage;
        public System.Action OnCheckpoint, OnFinished;

        TractorController tractor;
        AttachmentManager hitch;
        bool timing;

        public void Begin(TractorController t)
        {
            tractor = t;
            hitch = t.GetComponent<AttachmentManager>();
            Step = MissionStep.HitchTrolley;
            NextCheckpoint = 0; Timer = 0f; DeliveredKg = 0f; LoadedKg = 0f; timing = false;
            Active = true;
            for (int i = 0; i < checkpoints.Count; i++)
            {
                int idx = i;
                checkpoints[i].index = i;
                checkpoints[i].onTractorEnter = z => Passed(idx);
            }
            ShowMarkers();
            Msg(title + ": " + Mathf.RoundToInt(targetKg / 1000f) + " t of " + cargo + " – hitch a trolley", false);
        }

        public void Abort() { Active = false; ShowMarkers(); }

        void Msg(string m, bool w) { if (OnMessage != null) OnMessage(m, w); }

        void Passed(int i)
        {
            if (!Active || Step != MissionStep.Deliver || i != NextCheckpoint) return;
            NextCheckpoint++;
            if (OnCheckpoint != null) OnCheckpoint();
            Msg("Checkpoint " + NextCheckpoint + " / " + checkpoints.Count + "   " + FormatTime(Timer), false);
            ShowMarkers();
        }

        void ShowMarkers()
        {
            for (int i = 0; i < checkpointMarkers.Count; i++)
                if (checkpointMarkers[i] != null)
                    checkpointMarkers[i].gameObject.SetActive(Active && Step == MissionStep.Deliver && i == NextCheckpoint);
        }

        public static string FormatTime(float s) { int m = (int)(s / 60f); return m + ":" + ((int)s % 60).ToString("00"); }

        public bool CanLoad
        {
            get
            {
                var t = hitch != null ? hitch.CurrentTrolley : null;
                return Active && t != null && loadZone.TrolleyInside && Step == MissionStep.DriveToLoading;
            }
        }

        void Update()
        {
            if (!Active || tractor == null) return;
            if (timing) Timer += Time.deltaTime;
            var t = hitch != null ? hitch.CurrentTrolley : null;
            var inp = MobileInputManager.I;
            bool loadPressed = inp != null && tractor.InputEnabled && inp.LoadPressed();

            switch (Step)
            {
                case MissionStep.HitchTrolley:
                    Objective = "Hitch a trolley (reverse up to it, press ATTACH)";
                    TargetPos = NearestTrolley();
                    if (t != null) { Step = MissionStep.DriveToLoading; Msg("Now drive to the loading point", false); }
                    break;

                case MissionStep.DriveToLoading:
                    Objective = "Drive to the loading point and stop the trolley in the yellow box";
                    TargetPos = loadZone.transform.position;
                    if (t == null) { Step = MissionStep.HitchTrolley; break; }
                    if (loadZone.TrolleyInside && Mathf.Abs(tractor.Physics.SpeedKmh) < 1.5f)
                    {
                        Objective = "Press LOAD";
                        if (loadPressed) { Step = MissionStep.Loading; Msg("Loading " + cargo + "…", false); }
                    }
                    break;

                case MissionStep.Loading:
                    TargetPos = loadZone.transform.position;
                    if (t == null || !loadZone.TrolleyInside) { Step = MissionStep.DriveToLoading; Msg("Loading stopped – trolley left the bay", true); break; }
                    float want = Mathf.Min(targetKg, t.capacityKg);
                    float rate = want / 10f;
                    t.AddCargo(cargo, rate * Time.deltaTime);
                    Objective = "Loading… " + Mathf.RoundToInt(t.CargoKg) + " / " + Mathf.RoundToInt(want) + " kg";
                    if (t.CargoKg >= want - 1f || t.Cargo != cargo && t.CargoKg > 0f)
                    {
                        LoadedKg = t.CargoKg; t.ResetSpill();
                        Step = MissionStep.Deliver; timing = true; Timer = 0f; NextCheckpoint = 0;
                        ShowMarkers();
                        Msg("Loaded " + Mathf.RoundToInt(LoadedKg) + " kg. Follow the checkpoints – drive carefully!", false);
                        if (LoadedKg > tractor.Spec.maxTowKg * 0.8f) Msg("Heavy load for this tractor – use low gears on climbs and descents", true);
                    }
                    break;

                case MissionStep.Deliver:
                    if (NextCheckpoint < checkpoints.Count)
                    {
                        TargetPos = checkpoints[NextCheckpoint].transform.position;
                        Objective = "Checkpoint " + (NextCheckpoint + 1) + " / " + checkpoints.Count;
                    }
                    else
                    {
                        TargetPos = deliveryZone.transform.position;
                        Objective = "Reach the delivery yard and tip the trolley (TIP)";
                        if (deliveryZone.TrolleyInside && t != null) { Step = MissionStep.Unload; Msg("In the yard – press TIP to unload", false); }
                    }
                    if (t == null) Objective = "Trolley unhitched! Hitch it again";
                    else if (t.IsTipped) Fail("Trolley overturned – cargo lost");
                    else if (LoadedKg > 0f && t.CargoKg < LoadedKg * 0.4f) Fail("Too much cargo spilled on the way");
                    break;

                case MissionStep.Unload:
                    TargetPos = deliveryZone.transform.position;
                    if (t == null) break;
                    Objective = t.Tipping ? "Unloading… " + Mathf.RoundToInt(DeliveredKg) + " kg" : "Press TIP to unload";
                    if (deliveryZone.TrolleyInside) DeliveredKg += t.DumpTick(Time.deltaTime);
                    if (t.CargoKg <= 1f && DeliveredKg > 0f) Complete();
                    break;
            }
        }

        Vector3 NearestTrolley()
        {
            Vector3 best = tractor.transform.position; float bd = float.MaxValue;
            foreach (var imp in Implement.All)
            {
                if (!(imp is Trolley) || imp.Attached) continue;
                float d = (imp.transform.position - tractor.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = imp.transform.position; }
            }
            return best;
        }

        void Complete()
        {
            timing = false;
            Step = MissionStep.Complete;
            float share = Mathf.Clamp01(DeliveredKg / Mathf.Max(1f, targetKg));
            float timeBonus = Mathf.Clamp(parTimeSec / Mathf.Max(60f, Timer), 0.6f, 1.6f);
            int pay = Mathf.RoundToInt(basePay * share * timeBonus / 10f) * 10;
            Career.Pay(pay, "delivery");
            var d = SaveSystem.Data; d.deliveries++;
            if (d.bestMountainSec <= 0f || Timer < d.bestMountainSec) d.bestMountainSec = Timer;
            SaveSystem.SaveProgress();
            Msg("Delivered " + Mathf.RoundToInt(DeliveredKg) + " kg in " + FormatTime(Timer) + "  ·  Paid ₹" + pay, false);
            if (OnFinished != null) OnFinished();
            Active = false;
        }

        void Fail(string why)
        {
            timing = false;
            Step = MissionStep.Failed;
            Msg("Mission failed: " + why + ". Restart from the pause menu", true);
            if (OnFinished != null) OnFinished();
            Active = false;
        }
    }
}
