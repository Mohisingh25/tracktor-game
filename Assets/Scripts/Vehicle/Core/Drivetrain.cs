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

        // ---- rear axle state (owned here because it is coupled to the engine) ----
        public float OmegaLeft, OmegaRight;           // rad/s, + = forward
        public readonly float[] AxleForce = new float[2];   // average tyre Fx over the step, N
        public bool DiffLock;
        public float RearWheelInertia = 32f;          // kg m^2 per rear wheel (tyre + rim + half-shaft)
        public int Substeps = 8;

        public float RearOmegaAvg { get { return 0.5f * (OmegaLeft + OmegaRight); } }

        /// <summary>
        /// Advance engine, clutch, gearbox, differential and rear wheel spin by dt.
        /// The wheels' tyre forces are evaluated every substep; the averaged force is
        /// returned in AxleForce for the physics engine to apply at the contact patches.
        /// </summary>
        public void Step(float dt, WheelContact left, WheelContact right, float speedKmh)
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

            if (AutoBrakeAssist > 0f)
            {
                left.brakeTorque += AutoBrakeAssist * Spec.brakeTorquePerRearWheel;
                right.brakeTorque += AutoBrakeAssist * Spec.brakeTorquePerRearWheel;
            }

            float engineTorque = Engine.ComputeTorque(dt);
            float ratio = Gearbox.Ratio;
            float eff = Spec.drivelineEfficiency;
            float r = Spec.rearWheelRadius;
            float Iw = RearWheelInertia;
            float Ie = Engine.Inertia;
            float capacity = Spec.clutchMaxTorqueNm * engagement;
            bool connected = ratio != 0f && engagement > 0.002f;

            int n = Substeps < 1 ? 1 : Substeps;
            float h = dt / n;
            float we = Engine.Omega;
            float wl = OmegaLeft, wr = OmegaRight;
            float sumFl = 0f, sumFr = 0f, sumTc = 0f;
            bool anySlip = false;

            for (int i = 0; i < n; i++)
            {
                float fl = TireModel.LongForce(wl, r, left);
                float fr = TireModel.LongForce(wr, r, right);
                float tc;

                if (connected)
                {
                    // Implicit dry-friction clutch: find the plate torque that would make engine and
                    // driveshaft turn at the same speed after this substep; if the plates can't hold
                    // that much (capacity = clamp force x engagement) they slip at full capacity.
                    // Stable at any gear ratio, locks naturally, drags the engine into a stall when overloaded.
                    float wAvg = 0.5f * (wl + wr);
                    float slipW = we - ratio * wAvg;
                    float ae = engineTorque / Ie;
                    float aw = -(fl + fr) * r * 0.5f / Iw;
                    float denom = h * (1f / Ie + ratio * ratio * eff / (2f * Iw));
                    float tcStar = (slipW + h * (ae - ratio * aw)) / denom;
                    tc = Num.Clamp(tcStar, -capacity, capacity);
                    if (Num.Abs(tcStar) > capacity * 1.001f) anySlip = true;
                    we += (engineTorque - tc) / Ie * h;
                    float td = tc * ratio * eff;
                    float wlNew = wl + (td * 0.5f - fl * r) / Iw * h;
                    float wrNew = wr + (td * 0.5f - fr * r) / Iw * h;
                    if (DiffLock) { float a = 0.5f * (wlNew + wrNew); wlNew = a; wrNew = a; }
                    ApplyBrake(ref wlNew, left.brakeTorque, Iw, h);
                    ApplyBrake(ref wrNew, right.brakeTorque, Iw, h);
                    wl = wlNew; wr = wrNew;
                    sumTc += tc;
                }
                else
                {
                    tc = 0f;
                    we += engineTorque / Ie * h;
                    float wlNew = wl + (-fl * r) / Iw * h;
                    float wrNew = wr + (-fr * r) / Iw * h;
                    if (DiffLock) { float a = 0.5f * (wlNew + wrNew); wlNew = a; wrNew = a; }
                    ApplyBrake(ref wlNew, left.brakeTorque, Iw, h);
                    ApplyBrake(ref wrNew, right.brakeTorque, Iw, h);
                    wl = wlNew; wr = wrNew;
                }
                if (we < 0f) we = 0f;
                sumFl += fl; sumFr += fr;
            }

            OmegaLeft = wl; OmegaRight = wr;
            AxleForce[0] = sumFl / n;
            AxleForce[1] = sumFr / n;
            ClutchTorque = sumTc / n;
            ClutchSlipping = anySlip;
            Engine.SetOmegaAndUpdate(we, dt);
            WheelTorque = ClutchTorque * ratio * eff;
            LoadFactor = Engine.Running ? Engine.Rack : 0f;
        }

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
            float wheelKmh = Num.Abs(d.RearOmegaAvg) * spec.rearWheelRadius * 3.6f;
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
                if (!spinning && gb.Range == 0 && throttle > 0.6f && rpm > spec.ratedRpm * 0.95f)
                {
                    float predicted = rpm * gb.ReverseRatios[1] / ratio;
                    if (predicted > 1400f) BeginShift(-1, 1);
                }
                else if (gb.Range == 1 && eng.Rack > 0.97f && rpm < 1300f)
                {
                    BeginShift(-1, 0);
                }
            }
        }
    }
}
