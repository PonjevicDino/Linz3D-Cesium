using UnityEditor;
using UnityEngine;
using System.Collections;

[InitializeOnLoad]
public class GameObjectCountCheckerEditor
{
    private static GameObjectCounterChecker checker;
    private static double lastCheckTime;
    private const float checkInterval = 0.3f; // Seconds between checks
    private static double disableTimestamp;
    private static GameObject targetToReenable;

    static GameObjectCountCheckerEditor()
    {
        EditorApplication.update += Update;
        lastCheckTime = EditorApplication.timeSinceStartup;
    }

    private static void Update()
    {
        // Throttle checks to specified interval
        if (EditorApplication.timeSinceStartup - lastCheckTime < checkInterval) return;
        lastCheckTime = EditorApplication.timeSinceStartup;

        // Find checker component
        checker = Object.FindObjectOfType<GameObjectCounterChecker>();
        if (checker == null || checker.targetGameObject == null) return;

        // Handle delayed re-enable
        if (targetToReenable != null &&
            EditorApplication.timeSinceStartup - disableTimestamp >= 0.1)
        {
            SetActiveSafe(targetToReenable, true);
            targetToReenable = null;
        }

        // Skip check while waiting for re-enable
        if (targetToReenable != null) return;

        // Perform object count check - now including all children
        int count = CountAllGameObjects();
        //int count = 0;
        if (count > checker.maxObjectsLimit)
        {
            Debug.Log($"GameObject count ({count}) exceeded limit ({checker.maxObjectsLimit}). Resetting target object.");
            targetToReenable = checker.targetGameObject;
            SetActiveSafe(targetToReenable, false);
            disableTimestamp = EditorApplication.timeSinceStartup;
        }
    }

    private static int CountAllGameObjects()
    {
        int count = 0;
        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (GameObject root in rootObjects)
        {
            count += CountChildrenRecursive(root);
        }

        return count;
    }

    private static int CountChildrenRecursive(GameObject parent)
    {
        int count = 1; // Count self

        foreach (Transform child in parent.transform)
        {
            count += CountChildrenRecursive(child.gameObject);
        }

        return count;
    }

    private static void SetActiveSafe(GameObject target, bool state)
    {
        if (target != null && target.activeSelf != state)
        {
            Undo.RecordObject(target, "Toggle GameObject");
            target.SetActive(state);
            EditorUtility.SetDirty(target);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }
}