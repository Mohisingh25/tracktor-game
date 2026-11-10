using System.Collections.Generic;
using UnityEngine;

namespace TractorSim
{
    /// <summary>A gate on a mission route.</summary>
    public struct Waypoint
    {
        public Vector3 pos; public float yaw; public float width;
        public Waypoint(Vector3 p, float y, float w) { pos = p; yaw = y; width = w; }
    }

    public struct SpawnPoint
    {
        public Vector3 pos; public Quaternion rot;
        public SpawnPoint(Vector3 p, Quaternion r) { pos = p; rot = r; }
    }

    /// <summary>
    /// Named places on the current map (loading yards, delivery yards, parking bays, routes, spawn points)
    /// that the mission catalog refers to by name. Filled by the map builders.
    /// </summary>
    public static class WorldAnchors
    {
        public static readonly Dictionary<string, Zone> Zones = new Dictionary<string, Zone>();
        public static readonly Dictionary<string, List<Waypoint>> Routes = new Dictionary<string, List<Waypoint>>();
        public static readonly Dictionary<string, SpawnPoint> Spawns = new Dictionary<string, SpawnPoint>();
        public static FieldArea Field;
        public static Transform Root;

        public static void Clear(Transform root)
        {
            Zones.Clear(); Routes.Clear(); Spawns.Clear(); Field = null; Root = root;
        }

        public static Zone GetZone(string id) { Zone z; return id != null && Zones.TryGetValue(id, out z) ? z : null; }
        public static List<Waypoint> GetRoute(string id) { List<Waypoint> r; return id != null && Routes.TryGetValue(id, out r) ? r : null; }
        public static bool TryGetSpawn(string id, out SpawnPoint sp) { sp = default(SpawnPoint); return id != null && Spawns.TryGetValue(id, out sp); }
    }
}
