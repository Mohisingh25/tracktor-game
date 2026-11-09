using UnityEngine;

namespace TractorSim
{
    public enum CamMode { Chase = 0, Cockpit = 1, Side = 2, Front = 3, Hitch = 4, Cinematic = 5, Menu = 6 }

    /// <summary>Six gameplay cameras + an orbiting menu camera, with smooth transitions and drag-to-look.</summary>
    public class CameraManager : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public CamMode Mode { get; private set; }
        TractorController target;
        TractorVisuals vis;

        float orbitYaw, orbitPitch = 14f, lookYaw, lookPitch;
        float dragIdle;
        float chaseDist = 8.5f;
        Vector3 velSmooth;
        float cineTimer; int cineShot; Vector3 cinePos;
        float transition;     // 0..1 blend when switching
        Vector3 fromPos; Quaternion fromRot;

        public static readonly string[] Names = { "Chase", "Driver seat", "Side", "Front", "Hitch", "Cinematic", "Menu" };

        public void Init(Camera cam) { Cam = cam; }

        public void SetTarget(TractorController t)
        {
            target = t;
            vis = t.GetComponent<TractorVisuals>();
            orbitYaw = 0f;
        }

        public void SetMode(CamMode m)
        {
            fromPos = Cam.transform.position; fromRot = Cam.transform.rotation;
            transition = 0f;
            Mode = m;
            lookYaw = lookPitch = 0f;
            Cam.nearClipPlane = m == CamMode.Cockpit ? 0.05f : 0.2f;
            Cam.fieldOfView = m == CamMode.Cockpit ? 68f : 58f;
            if (m == CamMode.Cinematic) { cineTimer = 0f; cineShot = -1; }
        }

        public void Next()
        {
            int m = ((int)Mode + 1) % 6;
            SetMode((CamMode)m);
        }

        public void Drag(Vector2 delta)
        {
            dragIdle = 0f;
            float k = 0.18f;
            if (Mode == CamMode.Chase || Mode == CamMode.Menu) { orbitYaw += delta.x * k; orbitPitch = Mathf.Clamp(orbitPitch - delta.y * k, -5f, 70f); }
            else { lookYaw = Mathf.Clamp(lookYaw + delta.x * k, -150f, 150f); lookPitch = Mathf.Clamp(lookPitch - delta.y * k, -40f, 50f); }
        }

        void LateUpdate()
        {
            if (Cam == null || target == null) return;
            float dt = Time.unscaledDeltaTime;
            dragIdle += dt;
            Transform t = target.transform;
            Vector3 pos; Quaternion rot;
            float speed = target.Physics != null ? target.Physics.SpeedMs : 0f;

            switch (Mode)
            {
                case CamMode.Menu:
                    orbitYaw += dt * 9f;
                    pos = t.position + Quaternion.Euler(18f, orbitYaw + 140f, 0f) * new Vector3(0, 0, -7.5f) + Vector3.up * 1.0f;
                    rot = Quaternion.LookRotation(t.position + Vector3.up * 1.1f - pos);
                    break;

                case CamMode.Cockpit:
                    var eye = vis != null && vis.cockpitEye != null ? vis.cockpitEye : t;
                    pos = eye.position;
                    if (dragIdle > 2.5f) { lookYaw = Mathf.MoveTowards(lookYaw, 0f, dt * 60f); lookPitch = Mathf.MoveTowards(lookPitch, 0f, dt * 30f); }
                    rot = t.rotation * Quaternion.Euler(10f + lookPitch, lookYaw, 0f);
                    break;

                case CamMode.Side:
                    pos = t.TransformPoint(new Vector3(4.2f, 1.6f, 0.2f));
                    rot = Quaternion.LookRotation(t.TransformPoint(new Vector3(0, 1.0f, 0.2f)) - pos, Vector3.up) * Quaternion.Euler(lookPitch, lookYaw, 0);
                    break;

                case CamMode.Front:
                    pos = vis != null && vis.frontCam != null ? vis.frontCam.position : t.TransformPoint(0, 1.2f, 3.6f);
                    rot = Quaternion.LookRotation(t.TransformPoint(new Vector3(0, 1.0f, 0f)) - pos, Vector3.up) * Quaternion.Euler(lookPitch, lookYaw, 0);
                    break;

                case CamMode.Hitch:
                    pos = vis != null && vis.hitchCam != null ? vis.hitchCam.position : t.TransformPoint(0, 1.7f, -1.2f);
                    rot = t.rotation * Quaternion.Euler(28f + lookPitch, 180f + lookYaw, 0f);
                    break;

                case CamMode.Cinematic:
                    cineTimer -= dt;
                    if (cineTimer <= 0f)
                    {
                        cineShot = (cineShot + 1) % 4;
                        cineTimer = 6f;
                        // place a "tripod" ahead of the tractor, off to one side
                        Vector3 ahead = t.position + t.forward * (8f + Mathf.Abs(speed) * 3f) + t.right * (cineShot % 2 == 0 ? 5f : -5f);
                        RaycastHit hit;
                        if (Physics.Raycast(ahead + Vector3.up * 30f, Vector3.down, out hit, 80f, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore)) ahead = hit.point;
                        cinePos = ahead + Vector3.up * (cineShot == 3 ? 6f : 1.2f);
                        transition = 1f;
                    }
                    pos = cinePos;
                    rot = Quaternion.LookRotation(t.position + Vector3.up * 1.2f - pos);
                    break;

                default: // Chase
                    if (dragIdle > 3f) orbitYaw = Mathf.LerpAngle(orbitYaw, speed < -0.5f ? 180f : 0f, dt * 1.2f);
                    if (dragIdle > 3f) orbitPitch = Mathf.Lerp(orbitPitch, 14f, dt);
                    float yaw = t.eulerAngles.y + orbitYaw;
                    Vector3 pivot = t.position + Vector3.up * 1.4f;
                    Vector3 desired = pivot + Quaternion.Euler(orbitPitch, yaw, 0f) * new Vector3(0, 0, -chaseDist);
                    // keep the camera out of the ground / walls
                    RaycastHit h;
                    Vector3 dir = desired - pivot;
                    if (Physics.SphereCast(pivot, 0.3f, dir.normalized, out h, dir.magnitude, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore))
                        desired = pivot + dir.normalized * Mathf.Max(1.5f, h.distance - 0.2f);
                    pos = Vector3.SmoothDamp(Cam.transform.position, desired, ref velSmooth, 0.12f, Mathf.Infinity, dt);
                    rot = Quaternion.LookRotation(pivot + t.forward * 1.2f - pos);
                    break;
            }

            if (transition < 1f)
            {
                transition = Mathf.Min(1f, transition + dt * 3.5f);
                float s = transition * transition * (3f - 2f * transition);
                pos = Vector3.Lerp(fromPos, pos, s);
                rot = Quaternion.Slerp(fromRot, rot, s);
            }
            Cam.transform.SetPositionAndRotation(pos, rot);
        }
    }
}
