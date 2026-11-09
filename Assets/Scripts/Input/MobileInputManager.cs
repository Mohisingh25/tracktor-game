using UnityEngine;

namespace TractorSim
{
    public enum SteeringMode { Wheel = 0, Arrows = 1, Tilt = 2 }

    /// <summary>
    /// Single source of driver input. Touch controls write into it, keyboard (editor/desktop)
    /// and gyroscope are merged here, and the tractor reads from it.
    /// </summary>
    public class MobileInputManager : MonoBehaviour
    {
        public static MobileInputManager I { get; private set; }

        // ---- analog (0..1, steer -1..1) ----
        public float Steer { get; private set; }
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float Clutch { get; private set; }
        public float HandThrottle;            // sticky lever

        // ---- touch sources (written by UI controls) ----
        [System.NonSerialized] public float touchSteerWheel;   // -1..1 from steering wheel widget
        [System.NonSerialized] public bool touchLeft, touchRight;
        [System.NonSerialized] public float touchThrottle, touchBrake, touchClutch;
        [System.NonSerialized] public bool horn;

        public SteeringMode steeringMode = SteeringMode.Wheel;
        public float sensitivity = 1f;        // 0.5 .. 2
        public float tiltCalibration;          // device x-acceleration treated as centre

        // ---- one-shot button events (consumed by the tractor / game) ----
        bool evAttach, evLift, evPto, evTip, evLoad, evGearUp, evGearDown, evRange, evDirection, evIgnition, evLights, evIndL, evIndR, evHazard, evHandbrake, evDiff, evCamera, evPause, evReset;

        float kbSteer, kbThrottle, kbBrake, kbClutch;
        float arrowSteer;

        void Awake()
        {
            I = this;
            Input.multiTouchEnabled = true;
        }

        public void Press(string id)
        {
            switch (id)
            {
                case "gearUp": evGearUp = true; break;
                case "gearDown": evGearDown = true; break;
                case "range": evRange = true; break;
                case "direction": evDirection = true; break;
                case "ignition": evIgnition = true; break;
                case "lights": evLights = true; break;
                case "indL": evIndL = true; break;
                case "indR": evIndR = true; break;
                case "hazard": evHazard = true; break;
                case "handbrake": evHandbrake = true; break;
                case "diff": evDiff = true; break;
                case "camera": evCamera = true; break;
                case "pause": evPause = true; break;
                case "reset": evReset = true; break;
                case "attach": evAttach = true; break;
                case "lift": evLift = true; break;
                case "pto": evPto = true; break;
                case "tip": evTip = true; break;
                case "load": evLoad = true; break;
            }
        }

        static bool Take(ref bool f) { bool v = f; f = false; return v; }
        public bool GearUp() { return Take(ref evGearUp); }
        public bool GearDown() { return Take(ref evGearDown); }
        public bool RangeToggle() { return Take(ref evRange); }
        public bool DirectionToggle() { return Take(ref evDirection); }
        public bool Ignition() { return Take(ref evIgnition); }
        public bool LightsToggle() { return Take(ref evLights); }
        public bool IndicatorLeft() { return Take(ref evIndL); }
        public bool IndicatorRight() { return Take(ref evIndR); }
        public bool Hazard() { return Take(ref evHazard); }
        public bool HandbrakeToggle() { return Take(ref evHandbrake); }
        public bool DiffToggle() { return Take(ref evDiff); }
        public bool CameraNext() { return Take(ref evCamera); }
        public bool PausePressed() { return Take(ref evPause); }
        public bool ResetPressed() { return Take(ref evReset); }
        public bool AttachPressed() { return Take(ref evAttach); }
        public bool LiftPressed() { return Take(ref evLift); }
        public bool PtoPressed() { return Take(ref evPto); }
        public bool TipPressed() { return Take(ref evTip); }
        public bool LoadPressed() { return Take(ref evLoad); }

        public void ClearAll()
        {
            evAttach = evLift = evPto = evTip = evLoad = false;
            evGearUp = evGearDown = evRange = evDirection = evIgnition = evLights = evIndL = evIndR = evHazard = evHandbrake = evDiff = evCamera = evPause = evReset = false;
            touchThrottle = touchBrake = touchClutch = 0f;
            touchLeft = touchRight = false; horn = false;
        }

        public void CalibrateTilt() { tiltCalibration = Input.acceleration.x; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Keyboard(dt);

            // ---- steering ----
            float s = 0f;
            switch (steeringMode)
            {
                case SteeringMode.Wheel:
                    s = touchSteerWheel;
                    break;
                case SteeringMode.Arrows:
                    float target = (touchRight ? 1f : 0f) - (touchLeft ? 1f : 0f);
                    // arrows ramp the wheel like a driver turning it hand over hand; return to centre slower
                    float rate = (Mathf.Abs(target) > 0.01f ? 1.6f : 1.2f) * sensitivity;
                    arrowSteer = Mathf.MoveTowards(arrowSteer, target, rate * dt);
                    s = arrowSteer;
                    break;
                case SteeringMode.Tilt:
                    float ax = Input.acceleration.x - tiltCalibration;
                    s = Mathf.Clamp(ax * 2.2f * sensitivity, -1f, 1f);
                    if (Mathf.Abs(s) < 0.04f) s = 0f;
                    break;
            }
            if (Mathf.Abs(kbSteer) > 0.001f) s = kbSteer;
            Steer = Mathf.Clamp(s, -1f, 1f);

            Throttle = Mathf.Max(touchThrottle, kbThrottle);
            Brake = Mathf.Max(touchBrake, kbBrake);
            Clutch = Mathf.Max(touchClutch, kbClutch);
        }

        void Keyboard(float dt)
        {
            float st = 0f;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) st -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) st += 1f;
            kbSteer = Mathf.MoveTowards(kbSteer, st, dt * (st != 0f ? 2f : 3f));

            bool th = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
            bool br = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
            bool cl = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            kbThrottle = Mathf.MoveTowards(kbThrottle, th ? 1f : 0f, dt * 2.5f);
            kbBrake = Mathf.MoveTowards(kbBrake, br ? 1f : 0f, dt * 4f);
            kbClutch = Mathf.MoveTowards(kbClutch, cl ? 1f : 0f, dt * (cl ? 6f : 1.6f));   // slow release to let you feather it

            if (Input.GetKeyDown(KeyCode.E)) evGearUp = true;
            if (Input.GetKeyDown(KeyCode.Q)) evGearDown = true;
            if (Input.GetKeyDown(KeyCode.R)) evRange = true;
            if (Input.GetKeyDown(KeyCode.T)) evDirection = true;
            if (Input.GetKeyDown(KeyCode.I)) evIgnition = true;
            if (Input.GetKeyDown(KeyCode.L)) evLights = true;
            if (Input.GetKeyDown(KeyCode.Z)) evIndL = true;
            if (Input.GetKeyDown(KeyCode.X)) evIndR = true;
            if (Input.GetKeyDown(KeyCode.Space)) evHandbrake = true;
            if (Input.GetKeyDown(KeyCode.F)) evDiff = true;
            if (Input.GetKeyDown(KeyCode.C)) evCamera = true;
            if (Input.GetKeyDown(KeyCode.Escape)) evPause = true;
            if (Input.GetKeyDown(KeyCode.G)) evAttach = true;
            if (Input.GetKeyDown(KeyCode.V)) evLift = true;
            if (Input.GetKeyDown(KeyCode.P)) evPto = true;
            if (Input.GetKeyDown(KeyCode.B)) evTip = true;
            if (Input.GetKeyDown(KeyCode.N)) evLoad = true;
            if (Input.GetKey(KeyCode.H)) horn = true;
            else if (Input.GetKeyUp(KeyCode.H)) horn = false;
            if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus)) HandThrottle = Mathf.Clamp01(HandThrottle + dt * 0.5f);
            if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus)) HandThrottle = Mathf.Clamp01(HandThrottle - dt * 0.5f);
        }
    }
}
