using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

//[InitializeOnLoad]
public class RemoveChildObjects : MonoBehaviour
{

    private static double lastCheckTime;
    private const float checkInterval = 0.3f;

    static RemoveChildObjects()
    {
        EditorApplication.update += OnEditorUpdate;
        lastCheckTime = EditorApplication.timeSinceStartup;
    }

    private static void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup - lastCheckTime < checkInterval)
            return;

        lastCheckTime = EditorApplication.timeSinceStartup;

        GameObject celsiumMainHolder = GameObject.FindGameObjectWithTag("CelsiumOSM");
        if (celsiumMainHolder == null) return;

        foreach (Transform child in celsiumMainHolder.transform)
        {
            if (!child.gameObject.activeSelf && child.childCount == 1)
            {
                GameObject nestedChild = child.GetChild(0).gameObject;
                if (nestedChild != null)
                {
                    Undo.DestroyObjectImmediate(nestedChild);
                    Debug.Log($"Removed child '{nestedChild.name}' from '{child.name}'");
                }
            }
        }
    }
}
