namespace TractorSim
{
    /// <summary>
    /// Diesel engine with a mechanical (droop) governor, as on Indian tractors.
    /// Throttle sets a target RPM; the governor opens the fuel rack when load pulls RPM down.
    /// </summary>
    public class EngineSystem
    {
        public readonly TractorSpec Spec;

        public bool Running { get; private set; }
        public bool Cranking { get; private set; }
        public float Rpm { get; private set; }
        public float Omega { get { return Rpm * Num.RpmToRad; } }
        public float Throttle;              // 0..1 – governor lever (pedal or hand throttle)
        public float Rack { get; private set; }      // 0..1 fuel delivery
        public float GrossTorque { get; private set; }
        public float NetTorque { get; private set; }
        public float PowerKw { get; private set; }
        public float FuelL;
        public float FuelRateLph { get; private set; }
        public float TempC = 32f;
        public float AmbientC = 32f;
        public float DamageDerate = 1f;    // 1 = healthy

        /// <summary>Set for one step when something notable happens (stall, start fail…). Consumer clears it.</summary>
        public string Event;
        public bool JustStalled;
        public bool JustStarted;

        float crankTimer;
        float startGrace;
        const float CrankRpmTarget = 260f;

        public EngineSystem(TractorSpec spec)
        {
            Spec = spec;
            FuelL = spec.fuelTankL * 0.8f;
        }

        public bool Overheating { get { return TempC > 108f; } }
        public bool LowFuel { get { return FuelL < Spec.fuelTankL * 0.12f; } }
        public float Inertia { get { return Spec.engineInertia; } }

        public void BeginCrank()
        {
            if (Running || Cranking) return;
            if (FuelL <= 0.01f) { Event = "No fuel"; return; }
            Cranking = true;
            crankTimer = 0f;
        }

        public void Stop()
        {
            if (Running) Event = "Engine off";
            Running = false;
            Cranking = false;
        }

        public void ForceRunning(float rpm)
        {
            Running = true; Cranking = false; Rpm = rpm;
        }

        public float FrictionTorque(float rpm)
        {
            if (rpm <= 1f) return 0f;
            // pumping + mechanical losses; compression makes a stopped diesel hard to turn
            return 9f + rpm * 0.0115f + (Running ? 0f : 12f);
        }

        /// <summary>Torque the engine puts on its flywheel this step (before clutch load).</summary>
        public float ComputeTorque(float dt)
        {
            JustStalled = false;
            JustStarted = false;
            float friction = FrictionTorque(Rpm);

            if (Cranking)
            {
                crankTimer += dt;
                Rack = 0f;
                GrossTorque = 0f;
                float starter = Spec.starterTorqueNm * Num.Clamp01((CrankRpmTarget + 60f - Rpm) / 120f);
                if (crankTimer > 0.65f && Rpm > 150f)
                {
                    Cranking = false; Running = true; JustStarted = true; Event = "Engine started";
                    startGrace = 1.2f;
                }
                else if (crankTimer > 2.5f)
                {
                    Cranking = false; Event = "Engine won't turn over – press clutch";
                }
                NetTorque = starter - friction;
                return NetTorque;
            }

            if (!Running)
            {
                Rack = 0f; GrossTorque = 0f;
                NetTorque = -friction;
                return NetTorque;
            }

            float target = Num.Lerp(Spec.idleRpm, Spec.highIdleRpm, Throttle);
            Rack = Num.Clamp01((target - Rpm) / Spec.governorDroopRpm + 0.04f);
            float derate = DamageDerate;
            if (TempC > 115f) derate *= Num.Clamp(1f - (TempC - 115f) / 20f, 0.4f, 1f);
            if (FuelL <= 0f) { Rack = 0f; }
            GrossTorque = Spec.GrossTorque(Rpm) * Rack * derate;
            NetTorque = GrossTorque - friction;
            return NetTorque;
        }

        /// <summary>Set the flywheel speed solved by the drivetrain and update stall/fuel/thermal state.</summary>
        public void SetOmegaAndUpdate(float omega, float dt)
        {
            if (omega < 0f) omega = 0f;
            Rpm = omega * Num.RadToRpm;
            if (Rpm > Spec.maxRpm + 300f) Rpm = Spec.maxRpm + 300f;

            if (startGrace > 0f) startGrace -= dt;
            if (Running && Rpm < Spec.stallRpm && (startGrace <= 0f || Rpm < 120f))
            {
                Running = false; JustStalled = true; Event = "Engine stalled!";
            }
            if (Running && FuelL <= 0f && Rpm < Spec.idleRpm * 0.6f)
            {
                Running = false; Event = "Out of fuel";
            }

            // Fuel: brake-specific fuel consumption on produced power + idle baseline
            PowerKw = Num.Max(0f, GrossTorque) * Omega / 1000f;
            if (Running)
            {
                float kgPerH = PowerKw * Spec.bsfcKgPerKWh;
                FuelRateLph = kgPerH / 0.835f + Spec.idleFuelLph * (Rpm / Spec.idleRpm) * 0.6f;
                FuelL = Num.Max(0f, FuelL - FuelRateLph / 3600f * dt);
            }
            else FuelRateLph = 0f;

            // Coolant temperature: heat from fuel burned, cooling via fan (rpm) and radiator
            float heat = Running ? (0.9f + PowerKw * 0.09f) : 0f;
            float fan = 0.35f + Rpm / Spec.ratedRpm;
            float thermostatOpen = Num.Clamp01((TempC - 82f) / 8f);
            float cooling = (TempC - AmbientC) * (0.004f + 0.045f * thermostatOpen * fan);
            TempC += (heat - cooling) * dt * 0.9f;
        }
    }
}
