namespace TractorSim
{
    public enum TransmissionMode { Manual = 0, Automatic = 1 }

    /// <summary>
    /// Sliding-mesh gearbox: main lever (R, N, 1-4) plus a Low/High range lever = 8F + 2R.
    /// In manual mode shifting is refused (grind) unless the clutch is pressed.
    /// </summary>
    public class TransmissionSystem
    {
        public readonly TractorSpec Spec;
        public readonly float[] ForwardRatios;
        public readonly float[] ReverseRatios;

        public int Gear { get; private set; }   // -1 = R, 0 = N, 1..gearsPerRange
        public int Range { get; private set; }  // 0 = Low, 1 = High
        public string Event;                    // "Grind!" etc.
        public bool JustGround;

        public TransmissionSystem(TractorSpec spec)
        {
            Spec = spec;
            ForwardRatios = new float[spec.forwardSpeedsKmh.Length];
            for (int i = 0; i < ForwardRatios.Length; i++) ForwardRatios[i] = spec.RatioForSpeed(spec.forwardSpeedsKmh[i]);
            ReverseRatios = new float[spec.reverseSpeedsKmh.Length];
            for (int i = 0; i < ReverseRatios.Length; i++) ReverseRatios[i] = spec.RatioForSpeed(spec.reverseSpeedsKmh[i]);
        }

        public int ForwardCount { get { return ForwardRatios.Length; } }
        public bool InNeutral { get { return Gear == 0; } }
        public bool InReverse { get { return Gear < 0; } }

        /// <summary>Signed overall ratio engine→wheel. Negative in reverse, 0 in neutral.</summary>
        public float Ratio
        {
            get
            {
                if (Gear == 0) return 0f;
                if (Gear < 0) return -ReverseRatios[Range < ReverseRatios.Length ? Range : 0];
                return ForwardRatios[FlatIndex];
            }
        }

        /// <summary>0..7 index of the forward gear (L1..H4).</summary>
        public int FlatIndex { get { return Gear > 0 ? Range * Spec.gearsPerRange + (Gear - 1) : -1; } }

        public string Label
        {
            get
            {
                if (Gear == 0) return "N";
                string r = Range == 0 ? "L" : "H";
                if (Gear < 0) return "R" + r;
                return r + Gear;
            }
        }

        /// <summary>Try to put the main lever into a gear. clutchFree = clutch pressed enough to disengage.</summary>
        public bool TrySetGear(int gear, bool clutchFree)
        {
            gear = gear < -1 ? -1 : (gear > Spec.gearsPerRange ? Spec.gearsPerRange : gear);
            if (gear == Gear) return true;
            if (!clutchFree)
            {
                Event = "Gears grinding – press the clutch!"; JustGround = true;
                return false;
            }
            Gear = gear;
            return true;
        }

        public bool TrySetRange(int range, bool clutchFree, float speedKmh)
        {
            range = range <= 0 ? 0 : 1;
            if (range == Range) return true;
            if (!clutchFree) { Event = "Range lever blocked – press the clutch!"; JustGround = true; return false; }
            if (Num.Abs(speedKmh) > 2.5f) { Event = "Stop the tractor to change range"; JustGround = true; return false; }
            Range = range;
            return true;
        }

        public bool ShiftUp(bool clutchFree) { return TrySetGear(Gear + 1, clutchFree); }
        public bool ShiftDown(bool clutchFree) { return TrySetGear(Gear - 1, clutchFree); }

        /// <summary>Used by the automatic controller: directly select L1..H4 (flat index) or reverse.</summary>
        public void ForceFlat(int flatIndex)
        {
            flatIndex = flatIndex < 0 ? 0 : (flatIndex >= ForwardCount ? ForwardCount - 1 : flatIndex);
            Range = flatIndex / Spec.gearsPerRange;
            Gear = flatIndex % Spec.gearsPerRange + 1;
        }
        public void ForceReverse(int range) { Gear = -1; Range = range <= 0 ? 0 : 1; }
        public void ForceNeutral() { Gear = 0; }
    }
}
