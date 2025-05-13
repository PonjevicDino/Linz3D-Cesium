using UnityEngine;
using System;
using System.Threading.Tasks;


namespace ChunkLODManager { 

    [System.Serializable]
    public struct QualityGroup
    {
        [Tooltip("Root GameObject that holds all chunk children for this quality level")]
        public GameObject root;
        [Tooltip("Enable any child within this distance (world units)")]
        public float maxDistance;
    }

    public class ChunkLODManagerMultiThread: MonoBehaviour
    {
        public Transform target;
        public QualityGroup[] qualityGroups;

        // Cached children per group
        private Transform[][] _groupChildren;
        // Temp buffers for threading
        private Vector3[][] _groupPositions;
        private bool[][] _groupResults;
        private Task[] _groupTasks;
        private bool _tasksScheduled;

        void Reset()
        {
            if (target == null && Camera.main != null)
                target = Camera.main.transform;
        }

        void Start()
        {
            Array.Sort(qualityGroups, (a, b) => b.maxDistance.CompareTo(a.maxDistance));

            int G = qualityGroups.Length;
            _groupChildren = new Transform[G][];
            _groupPositions = new Vector3[G][];
            _groupResults = new bool[G][];
            _groupTasks = new Task[G * 2]; // two tasks per group

            for (int i = 0; i < G; i++)
            {
                var root = qualityGroups[i].root;
                int c = root ? root.transform.childCount : 0;
                _groupChildren[i] = new Transform[c];
                for (int j = 0; j < c; j++)
                    _groupChildren[i][j] = root.transform.GetChild(j);
            }
        }

        void Update()
        {
            if (target == null) return;

            // If no tasks running, schedule them
            if (!_tasksScheduled)
            {
                ScheduleDistanceTasks();
                _tasksScheduled = true;
                return;
            }

            // If tasks done, apply results
            bool allDone = true;
            foreach (var t in _groupTasks)
                if (t != null && !t.IsCompleted) { allDone = false; break; }

            if (!allDone) return;

            ApplyResults();
            _tasksScheduled = false;
        }

        private void ScheduleDistanceTasks()
        {
            Vector3 playerPos = target.position;
            int taskIndex = 0;

            for (int gi = 0; gi < qualityGroups.Length; gi++)
            {
                var children = _groupChildren[gi];
                int n = children.Length;
                if (n == 0) continue;

                // snapshot positions on main thread
                var posArr = new Vector3[n];
                for (int j = 0; j < n; j++)
                    posArr[j] = children[j].position;
                _groupPositions[gi] = posArr;
                _groupResults[gi] = new bool[n];

                int mid = n / 2;
                float maxD = qualityGroups[gi].maxDistance;

                // Task 1: indices [0..mid)
                _groupTasks[taskIndex++] = Task.Run(() =>
                {
                    for (int j = 0; j < mid; j++)
                        _groupResults[gi][j] = (posArr[j] - playerPos).sqrMagnitude <= maxD * maxD;
                });

                // Task 2: indices [mid..n)
                _groupTasks[taskIndex++] = Task.Run(() =>
                {
                    for (int j = mid; j < n; j++)
                        _groupResults[gi][j] = (posArr[j] - playerPos).sqrMagnitude <= maxD * maxD;
                });
            }

            // If fewer than allocated tasks, null out the rest
            for (; taskIndex < _groupTasks.Length; taskIndex++)
                _groupTasks[taskIndex] = null;
        }

        private void ApplyResults()
        {
            // Toggle each child according to the computed result
            for (int gi = 0; gi < qualityGroups.Length; gi++)
            {
                var children = _groupChildren[gi];
                var results = _groupResults[gi];
                if (children == null || results == null) continue;

                int n = children.Length;
                for (int j = 0; j < n; j++)
                {
                    var go = children[j].gameObject;
                    bool shouldOn = results[j];
                    if (go.activeSelf != shouldOn)
                        go.SetActive(shouldOn);
                }
            }
        }
    }
}