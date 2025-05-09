using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Formats.Fbx.Exporter;
using System.Runtime.Serialization;

public class TileMerger : EditorWindow
{
    private readonly string ExportFolder = "Assets/ExportedMergedFBX/";
    private string folderPath = "Assets";
    private string filterByAsset = "";
    private float radiusToMergeBy = 25f;
    private ExportModelOptions exportModelOptions = new ExportModelOptions();
    [MenuItem("Tools/Merge Tiles")]
    static void Init()
    {
        TileMerger window = (TileMerger)GetWindow(typeof(TileMerger));
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("FBX + Texture Importer & Merger", EditorStyles.boldLabel);

        filterByAsset = EditorGUILayout.TextField("Filter/Split by Asset Name", filterByAsset);
        radiusToMergeBy = EditorGUILayout.FloatField("Merge Radius", radiusToMergeBy);
        EditorGUILayout.LabelField("Folder Path: " + folderPath);
        if (GUILayout.Button("Select Folder"))
        {
            folderPath = EditorUtility.OpenFolderPanel("Select FBX Folder", folderPath, "");

        }

        if (GUILayout.Button("Import and Apply Textures"))
        {
            MergeTiles();
        }
    }
    string GetUnityRelativePath(string fullPath)
    {
        string projectPath = UnityEngine.Application.dataPath.Replace("\\", "/");
        fullPath = fullPath.Replace("\\", "/");

        if (fullPath.StartsWith(projectPath))
        {
            // Add "Assets" back since Application.dataPath ends in "/Assets"
            return "Assets" + fullPath.Substring(projectPath.Length);
        }

        return null;
    }
    public void MergeTiles()
    {
        // Prompt for folder
        if (string.IsNullOrEmpty(folderPath)) return;
        // Get all FBX files
        EditorUtility.DisplayProgressBar("FBX Merger", "Filtering FBX Files", 0);
        //EditorUtility.DisplayProgressBar("Importing FBX Files");
        var fbxFiles = Directory.EnumerateFiles(folderPath, "*.fbx");
        var used = new HashSet<string>();
        List<GameObject> tiles = new List<GameObject>();
        fbxFiles = fbxFiles.Where(fbxFile => {
            string[] splits = fbxFile.Replace("\\", "/").Split("/");
            int lastIndex = splits.Length - 1;
            return splits[lastIndex].StartsWith(filterByAsset);
        });
        EditorUtility.ClearProgressBar();
        int totalFiles = fbxFiles.Count();
        int count = 0;
        // Step 1: Instantiate and texture each tile (caching for grouping)
        foreach (string fbxPath in fbxFiles)
        {
            EditorUtility.DisplayProgressBar("Importing FBX Files", $"Processing {Path.GetFileName(fbxPath)}", (float)count / totalFiles);
            string relativeUnityPath = GetUnityRelativePath(fbxPath);

            Texture2D tex = null;
            string texPath = Path.ChangeExtension(relativeUnityPath.Split(".")[0] + "_Texture.fbx", ".jpg");
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { Debug.LogWarning($"Missing texture: {texPath}"); continue; }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(relativeUnityPath);
            if (model == null) { Debug.LogWarning($"Missing FBX: {relativeUnityPath}"); continue; }
            GameObject inst = PrefabUtility.InstantiatePrefab(model) as GameObject;

            // Apply texture
            var renderer = inst.GetComponentInChildren<Renderer>();
            //Material mat = new Material(renderer.sharedMaterial);
            //mat.mainTexture = tex;
            Material newMat = new Material(Shader.Find("HDRP/Lit"));
            newMat.SetTexture("_BaseColorMap", tex);
            renderer.sharedMaterial = newMat;


            // Add to list for grouping
            MeshFilter mf = inst.GetComponentInChildren<MeshFilter>();
            if (mf != null)
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh != null)
                {
                    BoxCollider bc = inst.transform.GetChild(0).gameObject.AddComponent<BoxCollider>();
                    bc.center = mesh.bounds.center;
                    bc.size = mesh.bounds.size;
                }
            }
            tiles.Add(inst);
        }
        EditorUtility.ClearProgressBar();

        List<GameObject> mergedTiles = new List<GameObject>();
        int total = tiles.Count;
        int processedCount = 0;

        try
        {
            while (tiles.Count > 0)
            {
                GameObject tile = tiles[0];
                tiles.RemoveAt(0);
                if (tile == null) continue;

                processedCount++;
                float progress = (float)processedCount / total;
                EditorUtility.DisplayProgressBar("Merging Tiles", $"Processing {processedCount}/{total}", progress);

                // Find neighbors within radius
                Collider[] hits = Physics.OverlapSphere(tile.transform.GetChild(0).transform.position, radiusToMergeBy);
                List<GameObject> group = new List<GameObject> { tile.transform.GetChild(0).gameObject };

                foreach (var hit in hits)
                {
                    GameObject hitObj = hit.gameObject;
                    if (hitObj != null && tiles.Contains(hitObj.transform.parent.gameObject))
                    {
                        group.Add(hitObj);
                    }
                }

                // Prepare CombineInstance list
                List<CombineInstance> combineInstances = new List<CombineInstance>();
                List<Material> newMaterials = new List<Material>();
                foreach (GameObject go in group)
                {
                    MeshFilter mf = go.GetComponent<MeshFilter>();
                    MeshRenderer mr = go.GetComponent<MeshRenderer>();
                    if (mf == null || mr == null || mf.sharedMesh == null) continue;
                    Mesh mesh = mf.sharedMesh;
                    Material[] mats = mr.sharedMaterials;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        CombineInstance ci = new CombineInstance();
                        ci.mesh = mesh;
                        ci.subMeshIndex = sub;
                        ci.transform = go.transform.localToWorldMatrix;
                        combineInstances.Add(ci);
                        if (sub < mats.Length) newMaterials.Add(mats[sub]);
                    }
                }

                // Combine into one mesh
                Mesh combinedMesh = new Mesh();
                combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                combinedMesh.CombineMeshes(combineInstances.ToArray(), false, true);

                // Create new GameObject for the combined mesh
                GameObject merged = new GameObject(filterByAsset + "-MergedTile-" + processedCount);
                MeshFilter newMf = merged.AddComponent<MeshFilter>();
                newMf.sharedMesh = combinedMesh;
                MeshRenderer newMr = merged.AddComponent<MeshRenderer>();
                newMr.sharedMaterials = newMaterials.ToArray();
                merged.SetActive(false);
                mergedTiles.Add(merged);

                // Destroy original tiles
                foreach (GameObject go in group)
                {
                    tiles.Remove(go);
                    if (go != null)
                    {
                        Object.DestroyImmediate(go.transform.parent.gameObject);
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            count = 0;
            totalFiles = mergedTiles.Count;
            if (mergedTiles != null && mergedTiles.Count > 0)
            {
                EnsureExportDirectory();
                foreach (GameObject go in mergedTiles)
                {
                    EditorUtility.DisplayProgressBar("Exporting Merged FBX Files", $"Processing {go}", (float)count / totalFiles);
                    ExportGameObjectToFbx(go);
                    count++;
                }
                Debug.Log($"Merged tiles into {mergedTiles.Count} combined objects.");
            }
            AssetDatabase.Refresh();

        }
        EditorUtility.ClearProgressBar();
        Debug.Log("Finished Process");
    }
    private void ExportGameObjectToFbx(GameObject go)
    {
        string safeName = SanitizeFileName(go.name);
        string folderPath = Path.Combine(ExportFolder + filterByAsset + "_FBXFolderAssets/");
        string fbxPath = Path.Combine(folderPath, $"{safeName}.fbx");

        EnsureExportDirectory();
        if (File.Exists(fbxPath))
        {
            Object.DestroyImmediate(go);
            return;
        }

        GameObject temp = go;
        temp.name = safeName;

        // Set export options
        exportModelOptions.ExportFormat = ExportFormat.Binary;
        exportModelOptions.KeepInstances = true;
        exportModelOptions.ExportUnrendered = false;
        exportModelOptions.EmbedTextures = false;

        // Export without modifying materials
        ModelExporter.ExportObject(fbxPath, temp, exportModelOptions);

        Object.DestroyImmediate(temp);
    }

    private void ExportMaterialsTextures(Renderer renderer, string folderPath, string baseName)
    {
        if (renderer == null || renderer.sharedMaterials == null) return;

        for (int i = 0; i < renderer.sharedMaterials.Length; i++)
        {
            var mat = renderer.sharedMaterials[i];
            if (mat == null) continue;

            Texture2D texture = mat.mainTexture as Texture2D;
            if (texture == null) continue;

            string texFileName = $"{baseName}_Texture{i}.jpg";
            string texPath = Path.Combine(folderPath, texFileName);

            SaveTextureToFile(texture, texPath);
        }
    }
    private void SaveTextureToFile(Texture2D texture, string path)
    {
        RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(texture, rt);
        RenderTexture.active = rt;

        Texture2D readableTex = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        readableTex.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        readableTex.Apply();

        byte[] bytes = readableTex.EncodeToJPG(90);
        File.WriteAllBytes(path, bytes);

        RenderTexture.ReleaseTemporary(rt);
        RenderTexture.active = null;
        Object.DestroyImmediate(readableTex);
    }
    private void ExportMainTexture(GameObject go, string folderPath, string baseName)
    {
        Renderer renderer = go.GetComponentInChildren<Renderer>();
        if (renderer == null || renderer.sharedMaterial == null) return;
        Debug.Log("BEFORE TEXTURE BYLAT");
        Texture2D texture = renderer.sharedMaterial.mainTexture as Texture2D;
        if (texture == null) return;
        Debug.Log("Not here blyat");

        string texturePath = Path.Combine(folderPath, $"{baseName}_Texture.jpg");

        // Create readable copy
        RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height);
        Graphics.Blit(texture, rt);
        RenderTexture.active = rt;

        Texture2D readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        readable.Apply();
        RenderTexture.ReleaseTemporary(rt);
        RenderTexture.active = null;

        File.WriteAllBytes(texturePath, readable.EncodeToJPG(90));
        Object.DestroyImmediate(readable);
    }
    private void EnsureExportDirectory()
    {
        if (!Directory.Exists(ExportFolder))
        {
            Directory.CreateDirectory(ExportFolder);
            AssetDatabase.Refresh();
        }
        string targetFolder = Path.Combine(ExportFolder, filterByAsset + "_FBXFolderAssets/");
        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
            AssetDatabase.Refresh();
        }
    }

    private string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
    private Texture2D DuplicateTexture(Texture2D source)
    {
        RenderTexture renderTex = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.Default,
                    RenderTextureReadWrite.Linear);

        Graphics.Blit(source, renderTex);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTex;
        Texture2D readableText = new Texture2D(source.width, source.height);
        readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
        readableText.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTex);
        return readableText;
    }
}
