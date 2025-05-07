using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

public class FbxTextureImporter : EditorWindow
{
    private string folderPath = "Assets/MyFolder"; // Change to your folder path

    [MenuItem("Tools/Import FBX and Apply Textures")]
    static void Init()
    {
        FbxTextureImporter window = (FbxTextureImporter)GetWindow(typeof(FbxTextureImporter));
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("FBX + Texture Importer", EditorStyles.boldLabel);

        folderPath = EditorGUILayout.TextField("Folder Path", folderPath);

        if (GUILayout.Button("Import and Apply Textures"))
        {
            ImportAndApplyTextures(folderPath);
        }
    }

    static void ImportAndApplyTextures(string path)
    {
        string[] fbxFiles = Directory.GetFiles(path, "*.fbx", SearchOption.AllDirectories);
        string[] textureFiles = Directory.GetFiles(path, "*.jpg", SearchOption.AllDirectories);

        foreach (string fbxPath in fbxFiles)
        {
            string fbxFileName = Path.GetFileNameWithoutExtension(fbxPath);
            string fbxAssetPath = fbxPath.Replace(Application.dataPath, "Assets");

            // Try to find matching texture
            string matchedTexturePath = textureFiles.FirstOrDefault(tex =>
                Path.GetFileNameWithoutExtension(tex).Contains(fbxFileName));

            if (string.IsNullOrEmpty(matchedTexturePath))
            {
                Debug.LogWarning($"No matching texture found for: {fbxFileName}");
                continue;
            }

            // Load FBX and Texture
            GameObject fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                matchedTexturePath.Replace(Application.dataPath, "Assets"));

            if (fbxPrefab != null && texture != null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab);
                Renderer renderer = instance.GetComponentInChildren<Renderer>();

                if (renderer != null)
                {
                    Material newMat = new Material(Shader.Find("HDRP/Lit"));
                    newMat.SetTexture("_BaseColorMap", texture);
                    renderer.sharedMaterial = newMat;
                }
                else
                {
                    Debug.LogWarning($"No Renderer found on: {fbxFileName}");
                }

                Undo.RegisterCreatedObjectUndo(instance, "Import FBX");
            }
        }

        Debug.Log("FBX import and texture assignment complete.");
    }
}
