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

        /// <summary>Is a world point inside the zone's box (ignoring height)?</summary>
        public bool Contains(Vector3 p, float margin = 0f)
        {
            var bc = GetComponent<BoxCollider>();
            if (bc == null) return false;
            Vector3 l = transform.InverseTransformPoint(p);
            return Mathf.Abs(l.x) <= bc.size.x * 0.5f - margin && Mathf.Abs(l.z) <= bc.size.z * 0.5f - margin;
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
}
