using UnityEngine;

namespace ChunkLODManager
{
    [System.Serializable]
    public struct QualityGroupNew
    {
        [Tooltip("Root GameObject that holds all chunk children for this quality level")]
        public GameObject root;
        [Tooltip("Enable any child within this distance (world units)")]
        public float maxDistance;
    }

    public class ChunkLODManager : MonoBehaviour
    {
        [Tooltip("The transform we measure distance from (usually the player or camera)")]
        public Transform target;

        [Tooltip("Each quality level, with its root container and max draw distance")]
        public QualityGroupNew[] qualityGroups;

        // Cached children for each group so we don't walk the hierarchy every frame
        private Transform[][] _groupChildren;

        void Reset()
        {
            if (target == null && Camera.main != null)
                target = Camera.main.transform;
        }

        void Start()
        {
            // Sort largest ranges first (not strictly required, but can help predictability)
            System.Array.Sort(qualityGroups, (a, b) => b.maxDistance.CompareTo(a.maxDistance));

            // Cache all direct children of each root
            _groupChildren = new Transform[qualityGroups.Length][];
            for (int i = 0; i < qualityGroups.Length; i++)
            {
                var root = qualityGroups[i].root;
                if (root != null)
                {
                    var children = new Transform[root.transform.childCount];
                    for (int c = 0; c < root.transform.childCount; c++)
                        children[c] = root.transform.GetChild(c);
                    _groupChildren[i] = children;
                }
                else
                {
                    _groupChildren[i] = new Transform[0];
                }
            }
        }

        void Update()
        {
            if (target == null) return;
            Vector3 playerPos = target.position;

            // For each quality group...
            for (int i = 0; i < qualityGroups.Length; i++)
            {
                var group = qualityGroups[i];
                float maxDist = group.maxDistance;
                var children = _groupChildren[i];
                if (children == null) continue;

                // Enable each child whose own position is within range
                for (int c = 0; c < children.Length; c++)
                {
                    var tr = children[c];
                    if (tr == null) continue;

                    float d = Vector3.Distance(playerPos, tr.position);
                    bool shouldBeOn = d <= maxDist;

                    if (tr.gameObject.activeSelf != shouldBeOn)
                        tr.gameObject.SetActive(shouldBeOn);
                }
            }
        }
    }
}