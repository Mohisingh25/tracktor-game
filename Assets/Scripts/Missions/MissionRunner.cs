using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    public enum MissionStep { HitchTrolley, DriveToLoading, Loading, Deliver, Unload, AttachImplement, FieldWork, Drive, Park, Complete, Failed }

    /// <summary>
    /// Runs one <see cref="MissionDef"/>: route drives through gates, cargo hauls (hitch → load → checkpoints →
    /// tip in the yard), field work with a given implement, and precision trolley parking. Enforces the
    /// mission rules – time limit, spill, damage, speed limit – and pays on success.
    /// </summary>
    public class MissionRunner : MonoBehaviour
    {
        public MissionDef def;
        public int index = -1;
        public string title = "Mission";
        public Zone loadZone, deliveryZone, parkZone;
        public readonly List<Zone> checkpoints = new List<Zone>();
        public readonly List<Transform> checkpointMarkers = new List<Transform>();

        public MissionStep Step { get; private set; }
        public int NextCheckpoint { get; private set; }
        public float Timer { get; private set; }
        public float DeliveredKg { get; private set; }
        public float LoadedKg { get; private set; }
        public string Objective { get; private set; }
        public string ResultText { get; private set; }
        public Vector3 TargetPos { get; private set; }
        public bool Active { get; private set; }
        public int Paid { get; private set; }
        public System.Action<string, bool> OnMessage;
        public System.Action OnCheckpoint, OnFinished;

        TractorController tractor;
        AttachmentManager hitch;
        float damage0, overspeed, parkedFor, speedWarn, overturnedFor;
        float TargetKg { get { return def != null ? def.tons * 1000f : 0f; } }

        // ------------------------------------------------------------------ factory
        /// <summary>Build a mission from its definition using the places of the current map.</summary>
        public static MissionRunner Create(MissionDef d, int index)
        {
            var go = new GameObject("Mission_" + d.id);
            if (WorldAnchors.Root != null) go.transform.SetParent(WorldAnchors.Root, false);
            var m = go.AddComponent<MissionRunner>();
            m.def = d; m.index = index; m.title = (index + 1) + ". " + d.title;
            m.loadZone = WorldAnchors.GetZone(d.load);
            m.deliveryZone = WorldAnchors.GetZone(d.deliver);
            m.parkZone = WorldAnchors.GetZone(d.parkZone);
            var route = WorldAnchors.GetRoute(d.route);
            if (route != null)
                for (int i = 0; i < route.Count; i++)
                {
                    var w = route[i];
                    var z = Zone.Create(go.transform, "CP" + (i + 1), w.pos - Vector3.up * 0.5f, new Vector3(w.width + 2f, 6f, 4f), w.yaw);
                    m.checkpoints.Add(z);
                    bool last = i == route.Count - 1 && d.kind == MissionKind.Route && m.deliveryZone == null;
                    m.checkpointMarkers.Add(WorldProps.Gate(go.transform, w.pos, w.yaw, w.width, last ? "FINISH" : "CHECKPOINT " + (i + 1)));
                }
            if (d.kind == MissionKind.Cargo && (m.loadZone == null || m.deliveryZone == null)) Debug.LogWarning("[TractorSim] Mission " + d.id + " is missing a yard on this map");
            return m;
        }

        public static string FormatTime(float s) { if (s < 0f) s = 0f; int m = (int)(s / 60f); return m + ":" + ((int)s % 60).ToString("00"); }

        // ------------------------------------------------------------------ run
        public void Begin(TractorController t)
        {
            tractor = t;
            hitch = t.GetComponent<AttachmentManager>();
            NextCheckpoint = 0; Timer = 0f; DeliveredKg = 0f; LoadedKg = 0f; overspeed = 0f; parkedFor = 0f; overturnedFor = 0f; Paid = 0;
            damage0 = t.Damage;
            ResultText = null;
            Active = true;
            for (int i = 0; i < checkpoints.Count; i++)
            {
                int idx = i;
                checkpoints[i].index = i;
                checkpoints[i].onTractorEnter = z => Passed(idx);
            }
            switch (def.kind)
            {
                case MissionKind.Route: Step = MissionStep.Drive; break;
                case MissionKind.Field: Step = MissionStep.AttachImplement; break;
                default: Step = MissionStep.HitchTrolley; break;
            }
            ShowMarkers();
            Msg(title + " – " + def.subtitle + ".  " + def.RulesText(), false);
        }

        public void Abort() { Active = false; ShowMarkers(); }

        void Msg(string m, bool w) { if (OnMessage != null) OnMessage(m, w); }

        bool RouteStep { get { return Step == MissionStep.Drive || Step == MissionStep.Deliver; } }

        void Passed(int i)
        {
            if (!Active || !RouteStep || i != NextCheckpoint) return;
            NextCheckpoint++;
            if (OnCheckpoint != null) OnCheckpoint();
            Msg((NextCheckpoint == checkpoints.Count && def.kind == MissionKind.Route && deliveryZone == null ? "Finish!  " : "Checkpoint " + NextCheckpoint + " / " + checkpoints.Count + "   ") + FormatTime(Timer), false);
            ShowMarkers();
        }

        void ShowMarkers()
        {
            for (int i = 0; i < checkpointMarkers.Count; i++)
                if (checkpointMarkers[i] != null)
                    checkpointMarkers[i].gameObject.SetActive(Active && RouteStep && i == NextCheckpoint);
        }

        public bool CanLoad
        {
            get
            {
                var t = hitch != null ? hitch.CurrentTrolley : null;
                return Active && t != null && loadZone != null && loadZone.TrolleyInside && Step == MissionStep.DriveToLoading;
            }
        }

        public float DamageAdded { get { return tractor != null ? tractor.Damage - damage0 : 0f; } }
        public float TimeLeft { get { return def.timeLimitSec - Timer; } }

        void Update()
        {
            if (!Active || tractor == null) return;
            Timer += Time.deltaTime;
            var t = hitch != null ? hitch.CurrentTrolley : null;
            var inp = MobileInputManager.I;
            bool loadPressed = inp != null && tractor.InputEnabled && inp.LoadPressed();

            if (!CheckRules(t)) return;

            switch (Step)
            {
                // ---------------- route ----------------
                case MissionStep.Drive:
                    if (NextCheckpoint < checkpoints.Count)
                    {
                        TargetPos = checkpoints[NextCheckpoint].transform.position;
                        Objective = "Gate " + (NextCheckpoint + 1) + " / " + checkpoints.Count;
                    }
                    else if (deliveryZone != null)
                    {
                        TargetPos = deliveryZone.transform.position;
                        Objective = "Drive into the green yard and stop";
                        if (deliveryZone.TractorInside && Mathf.Abs(tractor.Physics.SpeedKmh) < 2f) Complete();
                    }
                    else Complete();
                    break;

                // ---------------- cargo ----------------
                case MissionStep.HitchTrolley:
                    TargetPos = NearestTrolley();
                    if (t == null) { Objective = "Hitch a " + (def.tandem ? "TANDEM (10 t) " : "") + "trolley – reverse up to it and press ATTACH"; break; }
                    if (TargetKg > t.capacityKg + 1f) { Objective = "This trolley is too small for " + def.tons.ToString("0.#") + " t – DETACH it and hitch the tandem trolley"; break; }
                    if (def.kind == MissionKind.Park) { Step = MissionStep.Park; Msg("Now reverse the trolley into the shed", false); break; }
                    Step = MissionStep.DriveToLoading; Msg("Drive to the loading yard", false);
                    break;

                case MissionStep.DriveToLoading:
                    Objective = "Stop the trolley in the yellow loading box";
                    TargetPos = loadZone != null ? loadZone.transform.position : tractor.transform.position;
                    if (t == null) { Step = MissionStep.HitchTrolley; break; }
                    if (loadZone != null && loadZone.TrolleyInside && Mathf.Abs(tractor.Physics.SpeedKmh) < 1.5f)
                    {
                        Objective = "Press LOAD";
                        if (loadPressed) { Step = MissionStep.Loading; Msg("Loading " + def.cargo + "…", false); }
                    }
                    break;

                case MissionStep.Loading:
                    TargetPos = loadZone.transform.position;
                    if (t == null || !loadZone.TrolleyInside) { Step = MissionStep.DriveToLoading; Msg("Loading stopped – the trolley left the bay", true); break; }
                    float want = Mathf.Min(TargetKg, t.capacityKg);
                    t.AddCargo(def.cargo, want / 12f * Time.deltaTime);
                    Objective = "Loading… " + Mathf.RoundToInt(t.CargoKg) + " / " + Mathf.RoundToInt(want) + " kg";
                    if (t.CargoKg >= want - 1f || (t.Cargo != def.cargo && t.CargoKg > 0f))
                    {
                        LoadedKg = t.CargoKg; t.ResetSpill();
                        Step = MissionStep.Deliver; NextCheckpoint = 0;
                        ShowMarkers();
                        Msg("Loaded " + Mathf.RoundToInt(LoadedKg) + " kg – follow the checkpoints", false);
                        if (LoadedKg + t.emptyKg > tractor.Spec.maxTowKg * 0.8f) Msg("Very heavy for this tractor – lowest gears on climbs and descents", true);
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
                        Objective = "Reverse the trolley into the green yard and TIP";
                        if (deliveryZone.TrolleyInside && t != null) { Step = MissionStep.Unload; Msg("In the yard – stop and press TIP", false); }
                    }
                    if (t == null) Objective = "Trolley unhitched! Hitch it again";
                    break;

                case MissionStep.Unload:
                    TargetPos = deliveryZone.transform.position;
                    if (t == null) break;
                    Objective = t.Tipping ? "Unloading… " + Mathf.RoundToInt(DeliveredKg) + " kg" : "Press TIP to unload";
                    if (deliveryZone.TrolleyInside) DeliveredKg += t.DumpTick(Time.deltaTime);
                    if (t.CargoKg <= 1f && DeliveredKg > 0f) Complete();
                    break;

                // ---------------- field ----------------
                case MissionStep.AttachImplement:
                {
                    var imp = hitch != null ? hitch.Current : null;
                    TargetPos = NearestImplement(def.implement);
                    Objective = "Attach the " + def.implement.ToUpperInvariant() + " (implement yard)";
                    if (imp != null && imp.id == def.implement) { Step = MissionStep.FieldWork; Msg("Lower it, engage PTO and work the field", false); }
                    break;
                }

                case MissionStep.FieldWork:
                {
                    var imp = hitch != null ? hitch.Current : null;
                    var f = WorldAnchors.Field;
                    if (imp == null || imp.id != def.implement) { Step = MissionStep.AttachImplement; break; }
                    float pct = f != null ? f.Percent(def.fieldState) : 0f;
                    TargetPos = f != null ? f.transform.position : tractor.transform.position;
                    Objective = FieldArea.StateNames[def.fieldState] + " " + pct.ToString("0") + "% / " + def.fieldPercent.ToString("0") + "%";
                    if (pct >= def.fieldPercent) Complete();
                    break;
                }

                // ---------------- parking ----------------
                case MissionStep.Park:
                {
                    if (t == null) { Step = MissionStep.HitchTrolley; break; }
                    TargetPos = parkZone.transform.position;
                    bool inside = parkZone.Contains(t.transform.position + t.transform.forward * -1.2f, 0.4f) && parkZone.Contains(t.transform.position, 0.4f);
                    bool still = Mathf.Abs(tractor.Physics.SpeedKmh) < 0.5f;
                    Objective = inside ? (still ? "Hold still… " + (2f - parkedFor).ToString("0.0") : "Inside – stop the tractor") : "Reverse the whole trolley into the shed";
                    parkedFor = inside && still ? parkedFor + Time.deltaTime : 0f;
                    if (parkedFor >= 2f) Complete();
                    break;
                }
            }
        }

        /// <summary>Time limit, overturning, spill, damage and speed rules. Returns false when the mission failed.</summary>
        bool CheckRules(Trolley t)
        {
            if (Timer > def.timeLimitSec) { Fail("Time is up"); return false; }
            float dmg = tractor.Damage - damage0;
            if (def.maxDamage >= 0f && dmg > def.maxDamage) { Fail("Too much damage (" + Mathf.RoundToInt(dmg) + "%)"); return false; }
            // overturning only counts when it lasts (a wheel in the air or a bounce at spawn is not a rollover)
            bool over = tractor.transform.up.y < 0.3f, tOver = t != null && t.IsTipped;
            overturnedFor = (over || tOver) && Timer > 3f ? overturnedFor + Time.deltaTime : 0f;
            if (overturnedFor > 2.5f) { Fail(over ? "Tractor overturned – drive slower on slopes and bends" : "Trolley overturned"); return false; }
            if ((Step == MissionStep.Deliver || Step == MissionStep.Unload) && LoadedKg > 0f && t != null)
            {
                float lost = t.Spilled / LoadedKg;
                if (def.maxSpill >= 0f && lost > def.maxSpill) { Fail("Spilled " + Mathf.RoundToInt(lost * 100f) + "% of the cargo"); return false; }
                if (Step == MissionStep.Deliver && t.CargoKg < LoadedKg * 0.4f) { Fail("Most of the cargo fell off"); return false; }
            }
            if (def.speedLimitKmh > 0f && (Step == MissionStep.Deliver || Step == MissionStep.Drive))
            {
                float v = Mathf.Abs(tractor.Physics.SpeedKmh);
                if (v > def.speedLimitKmh + 2f)
                {
                    overspeed += Time.deltaTime;
                    speedWarn -= Time.deltaTime;
                    if (speedWarn <= 0f) { Msg("SLOW DOWN! Limit " + def.speedLimitKmh.ToString("0") + " km/h", true); speedWarn = 3f; }
                    if (overspeed > 5f) { Fail("Caught speeding – " + Mathf.RoundToInt(v) + " km/h"); return false; }
                }
            }
            return true;
        }

        Vector3 NearestTrolley()
        {
            Vector3 best = tractor.transform.position; float bd = float.MaxValue;
            foreach (var imp in Implement.All)
            {
                var tr = imp as Trolley;
                if (tr == null || imp.Attached) continue;
                if (def.tandem && tr.capacityKg < TargetKg) continue;
                float d = (imp.transform.position - tractor.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = imp.transform.position; }
            }
            return best;
        }

        Vector3 NearestImplement(string id)
        {
            foreach (var imp in Implement.All) if (imp != null && imp.id == id && !imp.Attached) return imp.transform.position;
            return tractor.transform.position;
        }

        void Complete()
        {
            Step = MissionStep.Complete;
            float share = def.kind == MissionKind.Cargo ? Mathf.Clamp01(DeliveredKg / Mathf.Max(1f, Mathf.Min(TargetKg, LoadedKg > 0f ? LoadedKg : TargetKg))) : 1f;
            float speedBonus = 1f + 0.3f * Mathf.Clamp01(1f - Timer / Mathf.Max(60f, def.timeLimitSec));
            Paid = Mathf.RoundToInt(def.reward * share * speedBonus / 10f) * 10;
            Career.Pay(Paid, "mission");
            var d = SaveSystem.Data;
            if (def.kind == MissionKind.Cargo) d.deliveries++;
            if (index >= 0) MissionCatalog.MarkDone(index, Timer);
            ResultText = "MISSION COMPLETE in " + FormatTime(Timer) + (def.kind == MissionKind.Cargo ? "  ·  " + Mathf.RoundToInt(DeliveredKg) + " kg" : "") + "  ·  Paid ₹" + Paid;
            Msg(ResultText, false);
            if (OnFinished != null) OnFinished();
            Active = false;
            ShowMarkers();
        }

        void Fail(string why)
        {
            Step = MissionStep.Failed;
            ResultText = "MISSION FAILED: " + why;
            Msg(ResultText + " – restart from the pause menu", true);
            if (OnFinished != null) OnFinished();
            Active = false;
            ShowMarkers();
        }
    }
}
