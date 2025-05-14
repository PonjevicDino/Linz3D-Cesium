using UnityEditor;
using UnityEngine;
using System.IO;

public class BatchFbxRemapper : EditorWindow
{
    private string fbxRoot = "Assets/FbxChunkGenerator/ExportedMergedFBX";

    [MenuItem("Tools/Batch Search & Remap FBX Materials")]
    static void Open() => GetWindow<BatchFbxRemapper>("Batch Remap").Show();

    void OnGUI()
    {
        GUILayout.Label("Batch FBX Search & Remap", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        fbxRoot = EditorGUILayout.TextField("FBX Root Folder", fbxRoot);
        if (GUILayout.Button("…", GUILayout.Width(24)))
        {
            string pick = EditorUtility.OpenFolderPanel("Select FBX Root", fbxRoot, "");
            if (!string.IsNullOrEmpty(pick) && pick.StartsWith(Application.dataPath))
                fbxRoot = "Assets" + pick.Substring(Application.dataPath.Length);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Run Batch Remap"))
            BatchRemap();
    }

    void BatchRemap()
    {
        if (!AssetDatabase.IsValidFolder(fbxRoot))
        {
            Debug.LogError($"Folder not found: {fbxRoot}");
            return;
        }

        // gather all FBX paths under fbxRoot
        var absRoot = Application.dataPath.Replace("/Assets", "") + "/" + fbxRoot;
        var allFbx = Directory.GetFiles(absRoot, "*.fbx", SearchOption.AllDirectories);
        int total = allFbx.Length, count = 0;

        foreach (var abs in allFbx)
        {
            // compute progress
            float prog = (float)(count++) / total;
            EditorUtility.DisplayProgressBar(
                "Batch Remap FBX",
                Path.GetFileName(abs),
                prog
            );

            // make it a Unity-relative path
            string rel = "Assets" + abs.Substring(Application.dataPath.Replace("\\", "/").Length);

            // get its importer
            var importer = AssetImporter.GetAtPath(rel) as ModelImporter;
            if (importer == null) continue;

            importer.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Local);
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();
        Debug.Log($"Batch remap complete: {total} FBX processed.");
    }
}
