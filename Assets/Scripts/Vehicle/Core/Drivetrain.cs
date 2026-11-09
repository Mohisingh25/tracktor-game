namespace TractorSim
{
    /// <summary>
    /// Engine + friction clutch + gearbox + (optional) automatic controller.
    /// The clutch is solved semi-implicitly: it transmits whatever torque is needed to
    /// bring the flywheel to driveshaft speed, limited by the clutch's capacity, so it
    /// slips when underpowered or partly engaged, locks when fully engaged, and drags
    /// the engine into a stall if you dump it in a tall gear.
    /// </summary>
    public class Drivetrain
    {
        public readonly TractorSpec Spec;
        public readonly EngineSystem Engine;
        public readonly TransmissionSystem Gearbox;
        public readonly AutomaticController Auto;

        public TransmissionMode Mode = TransmissionMode.Manual;

        // ---- inputs ----
        public float ThrottleInput;     // accelerator pedal 0..1
        public float HandThrottle;      // sticky hand throttle 0..1
        public float ClutchPedal;       // 0 = released (engaged), 1 = floored (disengaged)
        public float BrakeInput;        // 0..1 (used by auto logic)
        public int RequestedDirection = 1; // automatic: +1 forward, -1 reverse

        // ---- outputs ----
        public float ClutchEngagement { get; private set; }
        public float ClutchTorque { get; private set; }
        public bool ClutchSlipping { get; private set; }
        public float WheelTorque { get; private set; }   // total torque at the rear axle (signed, +forward)
        public float AutoBrakeAssist { get; private set; }
        public float LoadFactor { get; private set; }     // 0..1 how hard the engine is working


        public Drivetrain(TractorSpec spec)
        {
            Spec = spec;
            Engine = new EngineSystem(spec);
            Gearbox = new TransmissionSystem(spec);
            Auto = new AutomaticController(this);
            SetupWheels();
        }

        /// <summary>Clutch pedal → plate engagement with a realistic biting zone.</summary>
        public static float PedalToEngagement(float pedal)
        {
            // fully pressed above 0.8, bites around 0.55, fully engaged below 0.2
            float x = Num.Clamp01((0.8f - pedal) / 0.6f);
            return x * x * (3f - 2f * x);
        }

        public bool ClutchFree
        {
            // with the engine stopped the gears aren't turning, so the lever moves freely
            get { return Mode == TransmissionMode.Automatic || ClutchPedal > 0.75f || (!Engine.Running && !Engine.Cranking && Engine.Rpm < 30f); }
        }

        public void SetMode(TransmissionMode m)
        {
            if (m == Mode) return;
            Mode = m;
            // hand over in neutral so neither mode inherits a gear it didn't choose
            if (m == TransmissionMode.Automatic) Auto.Reset(); else Gearbox.ForceNeutral();
        }

        public bool TryStartEngine()
        {
            if (Engine.Running) return true;
            bool safe = Gearbox.InNeutral || (Mode == TransmissionMode.Manual ? ClutchPedal > 0.75f : true);
            if (!safe)
            {
                Engine.Event = "Press the clutch or select Neutral to start";
                return false;
            }
            if (Mode == TransmissionMode.Automatic) Auto.Reset();
            Engine.BeginCrank();
            return true;
        }

        // ---- driven wheels state (owned here because they are coupled to the engine) ----
        // index 0 = rear-left, 1 = rear-right, 2 = front-left, 3 = front-right (front only with 4WD)
        public float[] WheelOmega = new float[4];         // rad/s, + = forward
        public readonly float[] WheelForce = new float[4]; // average tyre Fx over the step, N
        public float[] WheelRadius = new float[4];
        public float[] WheelInertia = new float[4];
        public int DrivenCount { get; private set; }
        public bool DiffLock;
        public int Substeps = 8;

        public float OmegaLeft { get { return WheelOmega[0]; } set { WheelOmega[0] = value; } }
        public float OmegaRight { get { return WheelOmega[1]; } set { WheelOmega[1] = value; } }
        public float[] AxleForce { get { return WheelForce; } }
        public float RearOmegaAvg { get { return 0.5f * (WheelOmega[0] + WheelOmega[1]); } }
        /// <summary>Average ground speed the driven tyres are trying to roll at, m/s.</summary>
        public float DrivenWheelSpeed
        {
            get { float s = 0f; for (int i = 0; i < DrivenCount; i++) s += WheelOmega[i] * WheelRadius[i]; return DrivenCount > 0 ? s / DrivenCount : 0f; }
        }

        void SetupWheels()
        {
            DrivenCount = Spec.fourWheelDrive ? 4 : 2;
            float rr = Spec.rearWheelRadius, rf = Spec.frontWheelRadius;
            WheelRadius[0] = WheelRadius[1] = rr;
            WheelRadius[2] = WheelRadius[3] = rf;
            WheelInertia[0] = WheelInertia[1] = Spec.rearWheelInertia;
            WheelInertia[2] = WheelInertia[3] = Spec.rearWheelInertia * (rf / rr) * (rf / rr) * 0.6f;
        }

        public void ResetWheels() { for (int i = 0; i < 4; i++) { WheelOmega[i] = 0f; WheelForce[i] = 0f; } }

        /// <summary>Backwards-compatible 2WD entry point.</summary>
        public void Step(float dt, WheelContact left, WheelContact right, float speedKmh)
        {
            contactBuf[0] = left; contactBuf[1] = right;
            contactBuf[2] = default(WheelContact); contactBuf[3] = default(WheelContact);
            Step(dt, contactBuf, speedKmh);
        }
        readonly WheelContact[] contactBuf = new WheelContact[4];

        /// <summary>
        /// Advance engine, clutch, gearbox, differentials and driven-wheel spin by dt.
        /// contacts: [RL, RR, FL, FR]; front entries are ignored for 2WD tractors.
        /// Tyre forces are evaluated every substep; averages are returned in WheelForce.
        /// </summary>
        public void Step(float dt, WheelContact[] contacts, float speedKmh)
        {
            AutoBrakeAssist = 0f;
            float engagement;
            if (Mode == TransmissionMode.Automatic)
            {
                Auto.Update(dt, speedKmh);
                engagement = Auto.Engagement;
                AutoBrakeAssist = Auto.BrakeAssist;
                Engine.Throttle = Num.Max(Auto.ThrottleOut, HandThrottle);
            }
            else
            {
                engagement = PedalToEngagement(ClutchPedal);
                Engine.Throttle = Num.Max(ThrottleInput, HandThrottle);
            }
            if (Engine.Cranking && !Gearbox.InNeutral) engagement = 0f;
            ClutchEngagement = engagement;

            int n = DrivenCount;
            if (AutoBrakeAssist > 0f)
                for (int i = 0; i < n; i++)
                    contacts[i].brakeTorque += AutoBrakeAssist * (i < 2 ? Spec.brakeTorquePerRearWheel : Spec.brakeTorquePerFrontWheel);

            float engineTorque = Engine.ComputeTorque(dt);
            float ratio = Gearbox.Ratio;
            float eff = Spec.drivelineEfficiency;
            float rRef = Spec.rearWheelRadius;
            float Ie = Engine.Inertia;
            float capacity = Spec.clutchMaxTorqueNm * engagement;
            bool connected = ratio != 0f && engagement > 0.002f;

            // k_i: gearing that makes every driven tyre want the same ground speed (front axle lead ignored)
            // k maps a wheel's omega to "rear-equivalent" omega: wEq = w * r_i / rRef
            for (int i = 0; i < n; i++) kBuf[i] = WheelRadius[i] / rRef;
            float sumK2overI = 0f;
            for (int i = 0; i < n; i++) sumK2overI += kBuf[i] * kBuf[i] / WheelInertia[i];

            int steps = Substeps < 1 ? 1 : Substeps;
            float h = dt / steps;
            float we = Engine.Omega;
            for (int i = 0; i < 4; i++) sumF[i] = 0f;
            float sumTc = 0f;
            bool anySlip = false;

            for (int s = 0; s < steps; s++)
            {
                for (int i = 0; i < n; i++) fBuf[i] = TireModel.LongForce(WheelOmega[i], WheelRadius[i], contacts[i]);
                float tc = 0f;
                if (connected)
                {
                    // Implicit dry-friction clutch over all driven wheels (see README):
                    // slip = we - ratio * avg(w_i k_i); choose plate torque that zeroes slip this substep,
                    // clamp to capacity → slips when overloaded, locks otherwise, stalls when dragged down.
                    float wEq = 0f, aw = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        wEq += WheelOmega[i] * kBuf[i];
                        aw += -fBuf[i] * WheelRadius[i] * kBuf[i] / WheelInertia[i];
                    }
                    wEq /= n; aw /= n;
                    float slipW = we - ratio * wEq;
                    float ae = engineTorque / Ie;
                    float denom = h * (1f / Ie + ratio * ratio * eff / ((float)n * n) * sumK2overI);
                    float tcStar = (slipW + h * (ae - ratio * aw)) / denom;
                    tc = Num.Clamp(tcStar, -capacity, capacity);
                    if (Num.Abs(tcStar) > capacity * 1.001f) anySlip = true;
                    we += (engineTorque - tc) / Ie * h;
                    float td = tc * ratio * eff;
                    for (int i = 0; i < n; i++)
                        WheelOmega[i] += (td / n * kBuf[i] - fBuf[i] * WheelRadius[i]) / WheelInertia[i] * h;
                }
                else
                {
                    we += engineTorque / Ie * h;
                    for (int i = 0; i < n; i++)
                        WheelOmega[i] += (-fBuf[i] * WheelRadius[i]) / WheelInertia[i] * h;
                }
                if (n == 4)
                {
                    // tractor MFWD has no centre differential: front and rear axles are geared together,
                    // so their average ground speeds are equal (momentum-conserving projection)
                    float kr = kBuf[0], kf = kBuf[2];
                    float rearAvg = 0.5f * (WheelOmega[0] + WheelOmega[1]);
                    float frontAvg = 0.5f * (WheelOmega[2] + WheelOmega[3]);
                    float Ir = 2f * WheelInertia[0] / (kr * kr), If = 2f * WheelInertia[2] / (kf * kf);
                    float common = (rearAvg * kr * Ir + frontAvg * kf * If) / (Ir + If);
                    float dr = common / kr - rearAvg, df = common / kf - frontAvg;
                    WheelOmega[0] += dr; WheelOmega[1] += dr;
                    WheelOmega[2] += df; WheelOmega[3] += df;
                }
                if (DiffLock)
                {
                    for (int a = 0; a < n; a += 2)
                    {
                        float avg = 0.5f * (WheelOmega[a] + WheelOmega[a + 1]);
                        WheelOmega[a] = avg; WheelOmega[a + 1] = avg;
                    }
                }
                for (int i = 0; i < n; i++) ApplyBrake(ref WheelOmega[i], contacts[i].brakeTorque, WheelInertia[i], h);
                if (we < 0f) we = 0f;
                for (int i = 0; i < n; i++) sumF[i] += fBuf[i];
                sumTc += tc;
            }

            for (int i = 0; i < 4; i++) WheelForce[i] = i < n ? sumF[i] / steps : 0f;
            ClutchTorque = sumTc / steps;
            ClutchSlipping = anySlip;
            Engine.SetOmegaAndUpdate(we, dt);
            WheelTorque = ClutchTorque * ratio * eff;
            LoadFactor = Engine.Running ? Engine.Rack : 0f;
        }
        readonly float[] kBuf = new float[4], fBuf = new float[4], sumF = new float[4];

        static void ApplyBrake(ref float w, float brakeTorque, float inertia, float h)
        {
            if (brakeTorque <= 0f) return;
            float dw = brakeTorque / inertia * h;
            if (Num.Abs(w) <= dw) w = 0f; else w -= Num.Sign(w) * dw;
        }
    }

    /// <summary>
    /// Automatic mode: centrifugal-style clutch, load-aware up/down shifting over all 8 forward
    /// gears, and automatic forward/reverse changes (brakes to a stop first).
    /// </summary>
    public class AutomaticController
    {
        readonly Drivetrain d;
        public float Engagement { get; private set; }
        public float ThrottleOut { get; private set; }
        public float BrakeAssist { get; private set; }
        public bool Shifting { get { return shiftTimer > 0f; } }

        float shiftTimer;
        int pendingFlat = -1;
        int pendingReverse = -1;
        float sinceShift = 10f;
        const int StartGear = 1; // L2

        public AutomaticController(Drivetrain d) { this.d = d; }

        public void Reset()
        {
            shiftTimer = 0f; pendingFlat = -1; pendingReverse = -1; sinceShift = 10f;
            Engagement = 0f;
            d.Gearbox.ForceNeutral();
        }

        void BeginShift(int flat, int reverseRange)
        {
            pendingFlat = flat; pendingReverse = reverseRange;
            shiftTimer = 0.45f;
            sinceShift = 0f;
        }

        public void Update(float dt, float speedKmh)
        {
            var gb = d.Gearbox;
            var eng = d.Engine;
            var spec = d.Spec;
            float throttle = d.ThrottleInput;
            ThrottleOut = throttle;
            BrakeAssist = 0f;
            sinceShift += dt;
            float absSpeed = Num.Abs(speedKmh);

            if (!eng.Running)
            {
                Engagement = 0f;
                return;
            }

            // --- in the middle of a shift: clutch out, throttle lifted ---
            if (shiftTimer > 0f)
            {
                shiftTimer -= dt;
                Engagement = Num.MoveTowards(Engagement, 0f, dt * 10f);
                ThrottleOut = throttle * 0.3f;
                if (shiftTimer <= 0.2f && (pendingFlat >= 0 || pendingReverse >= 0))
                {
                    if (pendingReverse >= 0) gb.ForceReverse(pendingReverse); else gb.ForceFlat(pendingFlat);
                    pendingFlat = -1; pendingReverse = -1;
                }
                return;
            }

            int dir = d.RequestedDirection >= 0 ? 1 : -1;
            int currentDir = gb.InReverse ? -1 : (gb.InNeutral ? 0 : 1);

            // --- direction change ---
            if (currentDir != dir)
            {
                bool movingAgainst = (dir > 0 && speedKmh < -1.2f) || (dir < 0 && speedKmh > 1.2f);
                if (movingAgainst)
                {
                    Engagement = Num.MoveTowards(Engagement, 0f, dt * 6f);
                    BrakeAssist = 0.8f;
                    ThrottleOut = 0f;
                    return;
                }
                if (throttle > 0.02f || currentDir != 0)
                {
                    if (dir > 0) BeginShift(StartGear, -1); else BeginShift(-1, 0);
                    Engagement = 0f;
                    return;
                }
                Engagement = 0f;
                return;
            }

            // --- clutch engagement (centrifugal behaviour) ---
            float target;
            float idle = spec.idleRpm;
            if (throttle < 0.04f)
            {
                // coasting: keep engaged for engine braking until near idle, then free-wheel to stop
                bool engineBraking = absSpeed > 3f && eng.Rpm > idle + 80f;
                target = engineBraking ? 1f : 0f;
                if (absSpeed < 2.5f && d.BrakeInput > 0.05f) target = 0f;
            }
            else
            {
                target = Num.Clamp01((eng.Rpm - (idle + 40f)) / 420f);
                if (eng.Rpm < idle * 0.8f) target *= 0.4f;   // protect against stall
            }
            float rate = target > Engagement ? 1.6f : 6f;
            Engagement = Num.MoveTowards(Engagement, target, rate * dt);

            // --- reset to starting gear when stopped ---
            if (absSpeed < 1.5f && throttle < 0.04f && sinceShift > 0.6f)
            {
                if (dir > 0 && gb.FlatIndex != StartGear) { gb.ForceFlat(StartGear); }
                else if (dir < 0 && gb.Range != 0) { gb.ForceReverse(0); }
                return;
            }

            if (sinceShift < 1.1f || Engagement < 0.92f) return;

            // don't chase revs caused by wheelspin (mud, sand): only upshift when the tyres grip
            float wheelKmh = Num.Abs(d.DrivenWheelSpeed) * 3.6f;
            bool spinning = wheelKmh > absSpeed * 1.25f + 1.0f;

            float rpm = eng.Rpm;
            float ratio = Num.Abs(gb.Ratio);

            if (dir > 0)
            {
                int cur = gb.FlatIndex;
                // upshift: pick the highest gear that still leaves healthy rpm
                if (!spinning && throttle > 0.25f && rpm > spec.ratedRpm * 0.92f && cur < gb.ForwardCount - 1)
                {
                    int best = cur;
                    for (int g = cur + 1; g < gb.ForwardCount; g++)
                    {
                        float predicted = rpm * gb.ForwardRatios[g] / ratio;
                        float minRpm = eng.Rack > 0.9f ? 1550f : 1350f;
                        if (predicted > minRpm) best = g; else break;
                    }
                    if (best != cur) { BeginShift(best, -1); return; }
                }
                // downshift: lugging under load or rpm too low
                bool lugging = eng.Rack > 0.97f && rpm < 1400f;
                bool tooLow = rpm < 1050f && throttle > 0.1f;
                bool engineBrake = throttle < 0.04f && rpm < 1000f && absSpeed > 4f;
                if ((lugging || tooLow || engineBrake) && cur > 0)
                {
                    int next = cur - 1;
                    float predicted = rpm * gb.ForwardRatios[next] / ratio;
                    if (predicted < spec.highIdleRpm * 0.97f) { BeginShift(next, -1); return; }
                }
            }
            else
            {
                int rr = gb.Range < gb.ReverseRatios.Length ? gb.Range : gb.ReverseRatios.Length - 1;
                if (!spinning && rr < gb.ReverseRatios.Length - 1 && throttle > 0.6f && rpm > spec.ratedRpm * 0.95f)
                {
                    float predicted = rpm * gb.ReverseRatios[rr + 1] / ratio;
                    if (predicted > 1400f) BeginShift(-1, rr + 1);
                }
                else if (rr > 0 && eng.Rack > 0.97f && rpm < 1300f)
                {
                    BeginShift(-1, rr - 1);
                }
            }
        }
    }
}
