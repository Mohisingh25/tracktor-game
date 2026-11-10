using System;
using TractorSim;

// Headless 1-D longitudinal model to verify the drivetrain logic before it goes into Unity.
class Sim
{
    public Drivetrain d;
    public double v;          // m/s
    public double wheelW;     // rad/s rear
    public double extraMass;  // trolley
    public double slopeDeg;
    public double mu = 1.0, crr = 0.03;
    public float brake;
    public double t;
    const float dt = 0.01f;

    public Sim(TransmissionMode mode, string specId = "haldhar47")
    {
        d = new Drivetrain(TractorCatalog.Get(specId));
        d.SetMode(mode);
    }
    readonly WheelContact[] cs = new WheelContact[4];

    public void Run(double seconds, Action<Sim> each = null)
    {
        var s = d.Spec;
        int n = (int)(seconds / dt);
        double m = s.massKg + extraMass;
        double r = s.rearWheelRadius;
        double Iw = 2 * 70 * r * r * 0.7; // two rear wheels
        double g = 9.81;
        double slope = slopeDeg * Math.PI / 180;
        for (int i = 0; i < n; i++)
        {
            each?.Invoke(this);
            d.BrakeInput = brake;
            float speedKmh = (float)(v * 3.6);
            double Ftot = s.massKg * g * Math.Cos(slope);
            double FzR = Ftot * 0.6 + extraMass * g * 0.15, FzF = Ftot * 0.4;
            for (int k = 0; k < 4; k++)
            {
                bool rear = k < 2;
                cs[k] = new WheelContact { grounded = true, Fz = (float)((rear ? FzR : FzF) / 2), vLong = (float)v, mu = (float)mu, longCapacity = 1f,
                    brakeTorque = brake * (rear ? s.brakeTorquePerRearWheel : s.brakeTorquePerFrontWheel) };
            }
            d.Step(dt, cs, speedKmh);
            wheelW = d.RearOmegaAvg;
            double Fx = 0; for (int k = 0; k < d.DrivenCount; k++) Fx += d.WheelForce[k];
            if (!s.fourWheelDrive && s.frontBrakes && brake > 0 && Math.Abs(v) > 0.05)
                Fx -= Math.Sign(v) * Math.Min(2 * brake * s.brakeTorquePerFrontWheel / s.frontWheelRadius, mu * FzF);
            Fx -= 0.5 * 1.2 * s.dragArea * v * Math.Abs(v);
            double Froll = crr * m * g * Math.Cos(slope) * Math.Sign(v);
            if (Math.Abs(v) < 0.05) Froll = 0;
            double Fgrade = m * g * Math.Sin(slope);
            double a = (Fx - Froll - Fgrade) / m;
            v += a * dt;
            t += dt;
        }
    }

    public string State()
    {
        var e = d.Engine;
        return string.Format("t={0,5:F1}s v={1,6:F1}km/h rpm={2,5:F0} gear={3,-3} eng={4:F2} rack={5:F2} run={6} T={7:F0}C fuel={8:F2}L/h",
            t, v * 3.6, e.Rpm, d.Gearbox.Label, d.ClutchEngagement, e.Rack, e.Running, e.TempC, e.FuelRateLph);
    }
}

static class Program
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "  PASS " : "  FAIL ") + what); if (!ok) fails++; }

    static Sim StartedSim(TransmissionMode m, string id = "haldhar47")
    {
        var s = new Sim(m, id);
        s.d.ClutchPedal = 1f;
        s.d.TryStartEngine();
        s.Run(3);
        s.d.ClutchPedal = 0f;
        s.Run(2);
        return s;
    }

    static void Main()
    {
        Console.WriteLine("1. Start engine in neutral");
        var s = new Sim(TransmissionMode.Manual);
        s.d.TryStartEngine(); s.Run(4);
        Console.WriteLine("  " + s.State());
        Check(s.d.Engine.Running && Math.Abs(s.d.Engine.Rpm - 850) < 60, "idles near 850 rpm");

        Console.WriteLine("2. Start refused in gear without clutch");
        var s2 = new Sim(TransmissionMode.Manual);
        s2.d.ClutchPedal = 1f; s2.d.Gearbox.TrySetGear(1, true); s2.d.ClutchPedal = 0f;
        Check(!s2.d.TryStartEngine(), "starter interlock: " + s2.d.Engine.Event);

        Console.WriteLine("3. Manual: L1 gentle clutch release pulls away");
        s = StartedSim(TransmissionMode.Manual);
        s.d.ClutchPedal = 1f; s.Run(0.3);
        Check(s.d.Gearbox.TrySetGear(1, s.d.ClutchFree), "select gear 1 with clutch");
        s.d.ThrottleInput = 0.3f;
        s.Run(3, x => x.d.ClutchPedal = (float)Math.Max(0, x.d.ClutchPedal - 0.004));
        s.Run(3);
        Console.WriteLine("  " + s.State());
        Check(s.d.Engine.Running && s.v * 3.6 > 1.0, "moving in L1, engine alive");

        Console.WriteLine("4. Manual: dump clutch in H4 at idle -> stall");
        s = StartedSim(TransmissionMode.Manual);
        s.d.ClutchPedal = 1f;
        s.d.Gearbox.TrySetRange(1, true, 0); s.d.Gearbox.TrySetGear(4, true);
        s.Run(0.2);
        s.d.ClutchPedal = 0f;
        s.Run(3);
        Console.WriteLine("  " + s.State());
        Check(!s.d.Engine.Running, "engine stalled (" + s.d.Engine.Event + ")");

        Console.WriteLine("5. Manual: shift without clutch -> grind/refused");
        s = StartedSim(TransmissionMode.Manual);
        Check(!s.d.Gearbox.TrySetGear(1, s.d.ClutchFree), "refused: " + s.d.Gearbox.Event);

        Console.WriteLine("6. Manual: drive up through all gears to top speed");
        s = StartedSim(TransmissionMode.Manual);
        s.d.ClutchPedal = 1f; s.d.Gearbox.TrySetGear(1, true); s.Run(0.2);
        s.d.ThrottleInput = 0.6f;
        s.Run(2.5, x => x.d.ClutchPedal = (float)Math.Max(0, x.d.ClutchPedal - 0.006));
        s.d.ThrottleInput = 1f;
        for (int range = 0; range < 2; range++)
        {
            for (int gear = (range == 0 ? 2 : 1); gear <= 4; gear++)
            {
                s.Run(4);
                s.d.ClutchPedal = 1f; s.d.ThrottleInput = 0.2f; s.Run(0.3);
                if (range == 1 && gear == 1) { s.d.Gearbox.TrySetRange(1, true, (float)(s.v * 3.6)); }
                s.d.Gearbox.TrySetGear(gear, true);
                s.d.ThrottleInput = 1f;
                s.Run(1.2, x => x.d.ClutchPedal = (float)Math.Max(0, x.d.ClutchPedal - 0.012));
                Console.WriteLine("  -> " + s.State());
                if (!s.d.Engine.Running) break;
            }
            if (range == 0)
            {
                // range change requires a stop on a sliding-mesh box
                s.d.ClutchPedal = 1f; s.d.ThrottleInput = 0; s.brake = 1; s.Run(4); s.brake = 0;
                s.d.Gearbox.TrySetRange(1, true, (float)(s.v * 3.6));
                s.d.Gearbox.TrySetGear(1, true);
                s.d.ThrottleInput = 0.5f;
                s.Run(3, x => x.d.ClutchPedal = (float)Math.Max(0, x.d.ClutchPedal - 0.005));
            }
        }
        s.Run(15);
        Console.WriteLine("  " + s.State());
        Check(s.v * 3.6 > s.d.Spec.TopSpeedKmh * 0.85 && s.v * 3.6 < s.d.Spec.TopSpeedKmh * 1.12, "top speed in H4 ~" + s.d.Spec.TopSpeedKmh + " km/h");
        Check(s.d.Gearbox.Label == "H4", "in H4");

        Console.WriteLine("7. Engine braking: lift off in H4");
        double v0 = s.v; s.d.ThrottleInput = 0; s.Run(4);
        Console.WriteLine("  " + s.State());
        Check(s.v < v0 - 1.0 && s.d.Engine.Running, "slows on engine braking, engine still running");

        Console.WriteLine("8. Brake distance from top speed");
        s.d.ThrottleInput = 1f; s.Run(10);
        double startV = s.v; double dist = 0; s.d.ClutchPedal = 1f; s.d.ThrottleInput = 0; s.brake = 1f;
        s.Run(10, x => dist += x.v * 0.01);
        Console.WriteLine("  from " + (startV * 3.6).ToString("F1") + " km/h stopped in " + dist.ToString("F1") + " m");
        Check(dist > 4 && dist < 20 && s.v < 0.1, "plausible stopping distance");

        Console.WriteLine("9. Automatic: full throttle on flat");
        s = StartedSim(TransmissionMode.Automatic);
        s.d.ThrottleInput = 1f;
        string lastGear = "";
        s.Run(45, x => { if (x.d.Gearbox.Label != lastGear) { lastGear = x.d.Gearbox.Label; Console.WriteLine("    shift -> " + x.State()); } });
        Console.WriteLine("  " + s.State());
        Check(s.d.Gearbox.Label == "H4" && s.v * 3.6 > s.d.Spec.TopSpeedKmh * 0.85, "auto reaches H4 top speed");
        Check(s.d.Engine.Running, "no stall in auto");

        Console.WriteLine("10. Automatic: brake to stop -> clutch frees, engine idles");
        s.d.ThrottleInput = 0; s.brake = 1f; s.Run(8);
        Console.WriteLine("  " + s.State());
        Check(s.d.Engine.Running && s.v < 0.2 && s.d.ClutchEngagement < 0.05, "stopped, idling, clutch out");

        Console.WriteLine("11. Automatic: reverse selection");
        s.brake = 0; s.d.RequestedDirection = -1; s.d.ThrottleInput = 0.6f; s.Run(8);
        Console.WriteLine("  " + s.State());
        Check(s.v * 3.6 < -2 && s.d.Gearbox.InReverse, "moving backwards in reverse");
        s.d.RequestedDirection = 1; s.d.ThrottleInput = 0.6f; s.Run(10);
        Console.WriteLine("  " + s.State());
        Check(s.v * 3.6 > 2, "auto braked, changed to forward and drove off");

        Console.WriteLine("12. Hill 12 deg, empty, automatic");
        s = StartedSim(TransmissionMode.Automatic);
        s.slopeDeg = 12; s.d.ThrottleInput = 1f; s.Run(30);
        Console.WriteLine("  " + s.State());
        double emptyHill = s.v * 3.6;
        Check(s.v > 0.5 && s.d.Engine.Running, "climbs 12 deg");

        Console.WriteLine("13. Same hill with 6 t loaded trolley");
        s = StartedSim(TransmissionMode.Automatic);
        s.slopeDeg = 12; s.extraMass = 6000; s.d.ThrottleInput = 1f; s.Run(30);
        Console.WriteLine("  " + s.State());
        Check(s.v * 3.6 < emptyHill * 0.6, "much slower when loaded (" + (s.v * 3.6).ToString("F1") + " vs " + emptyHill.ToString("F1") + " km/h)");

        Console.WriteLine("14. Mud (mu 0.35, crr 0.15) with load: struggles");
        s = StartedSim(TransmissionMode.Automatic);
        s.mu = 0.35; s.crr = 0.15; s.extraMass = 5000; s.d.ThrottleInput = 1f; s.Run(20);
        Console.WriteLine("  " + s.State() + " wheel slip speed=" + (s.wheelW * 0.68 * 3.6).ToString("F1"));
        Check(s.wheelW * 0.68 > s.v * 1.3 + 0.2, "rear wheels spinning faster than ground speed");

        Console.WriteLine("15. Manual: run warm at full load 3 minutes");
        s = StartedSim(TransmissionMode.Automatic);
        s.slopeDeg = 6; s.extraMass = 3000; s.d.ThrottleInput = 1f; s.Run(180);
        Console.WriteLine("  " + s.State());
        Check(s.d.Engine.TempC > 80 && s.d.Engine.TempC < 112, "coolant temp in working range");

        Console.WriteLine("16. Every tractor in the showroom: automatic top speed on flat asphalt");
        double prevTop = 0; double mohitTop = 0; var tops = new System.Collections.Generic.Dictionary<string, double>();
        foreach (var id in TractorCatalog.Ids)
        {
            var t = StartedSim(TransmissionMode.Automatic, id);
            t.crr = 0.016; t.d.ThrottleInput = 1f; t.Run(150);
            double top = t.v * 3.6; tops[id] = top;
            Console.WriteLine("  " + t.d.Spec.displayName.PadRight(26) + " " + t.d.Spec.ratedPowerHp + " HP  top " + top.ToString("F1") + " km/h (spec " + t.d.Spec.TopSpeedKmh + ")  gear " + t.d.Gearbox.Label + "  rpm " + t.d.Engine.Rpm.ToString("F0"));
            Check(t.d.Engine.Running && top > t.d.Spec.TopSpeedKmh * 0.88, id + " reaches ~its rated top speed");
            if (id == "mohit150") mohitTop = top;
        }
        Check(mohitTop > 130, "Mohit Singh does 130+ km/h (" + mohitTop.ToString("F0") + ")");
        bool fastest = true; foreach (var kv in tops) if (kv.Key != "mohit150" && kv.Value >= mohitTop) fastest = false;
        Check(fastest, "Mohit Singh is the fastest tractor");

        Console.WriteLine("17. Mohit Singh: 0-100 km/h and braking from top speed");
        var m = StartedSim(TransmissionMode.Automatic, "mohit150");
        m.crr = 0.016; m.d.ThrottleInput = 1f; double t100 = -1; double t0 = m.t;
        m.Run(120, x => { if (t100 < 0 && x.v * 3.6 >= 100) t100 = x.t - t0; });
        Console.WriteLine("  0-100 in " + t100.ToString("F1") + " s, now " + (m.v * 3.6).ToString("F0") + " km/h");
        double bd = 0; m.d.ThrottleInput = 0; m.brake = 1f; m.Run(15, x => bd += Math.Max(0, x.v) * 0.01);
        Console.WriteLine("  stopped in " + bd.ToString("F0") + " m");
        Check(t100 > 0 && t100 < 60, "reaches 100 km/h");
        Check(bd > 40 && bd < 160 && m.v < 0.2, "stops from top speed in a plausible distance");

        Console.WriteLine("18. 4WD vs 2WD: muddy 6 deg slope with a 2 t trolley");
        var two = StartedSim(TransmissionMode.Automatic, "anshu50"); two.mu = 0.36; two.crr = 0.08; two.slopeDeg = 6; two.extraMass = 2000; two.d.ThrottleInput = 1f; two.Run(25);
        var four = StartedSim(TransmissionMode.Automatic, "bharatveer60"); four.mu = 0.36; four.crr = 0.08; four.slopeDeg = 6; four.extraMass = 2000; four.d.ThrottleInput = 1f; four.Run(25);
        Console.WriteLine("  2WD Anshu Singh: " + (two.v * 3.6).ToString("F1") + " km/h   4WD Bharat Veer: " + (four.v * 3.6).ToString("F1") + " km/h");
        Check(four.v > two.v + 0.3, "4WD climbs the muddy slope better");

        Console.WriteLine(fails == 0 ? "\nALL PASS" : "\nFAILURES: " + fails);
    }
}
