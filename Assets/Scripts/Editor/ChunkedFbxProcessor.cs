using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEditor.Experimental.GraphView;

public class ChunkedFbxProcessor : EditorWindow
{
    private float chunkSize = 100f; // your chunk edge length
    const string manifestFolder = "Assets/FbxChunkGenerator/ChunkManifests";
    private readonly string ExportFolder = "Assets/FbxChunkGenerator/ExportedMergedFBX/";
    private string folderPath = "Assets";
    private string filterByAsset = "";
    private int finishedChunkCount = 0;
    private int processNumber = 0;
    private ExportModelOptions exportModelOptions = new ExportModelOptions();
    [MenuItem("Tools/Fbx Merger by Chunks")]
    static void Init()
    {
        ChunkedFbxProcessor window = (ChunkedFbxProcessor)GetWindow(typeof(ChunkedFbxProcessor));
        window.Show();
    }
    void OnGUI()
    {
        GUILayout.Label("FBX + Texture Importer & Merger", EditorStyles.boldLabel);
        filterByAsset = EditorGUILayout.TextField("Filter/Split by Asset Name", filterByAsset);
        chunkSize = EditorGUILayout.FloatField("Chunk Size in Meter", chunkSize);
        EditorGUILayout.LabelField("Folder Path: " + folderPath);
        if (GUILayout.Button("Select Folder"))
        {
            folderPath = EditorUtility.OpenFolderPanel("Select FBX Folder", folderPath, "");
        }
        GUILayout.Label("Step 1: Build Chunk Manifests", EditorStyles.boldLabel);
        folderPath = EditorGUILayout.TextField("FBX Root Folder", folderPath);
        if (GUILayout.Button("Build Manifests")) BuildManifests();

        GUILayout.Space(10);
        GUILayout.Label("Step 2: Process Chunks", EditorStyles.boldLabel);
        if (GUILayout.Button("Merge & Export All Chunks")) ProcessAllChunks();
    }

    // — STEP 1: build text lists per chunk by streaming every FBX one at a time —
    void BuildManifests()
    {
        if (!Directory.Exists(manifestFolder))
            Directory.CreateDirectory(manifestFolder);
        else
        {
            foreach (var file in Directory.GetFiles(manifestFolder, "*.txt"))
            {
                File.Delete(file);
            }
        }

        // Enumerate every FBX file under fbxRoot
        var allFbx = Directory.EnumerateFiles(folderPath, "*.fbx", SearchOption.AllDirectories);
        allFbx = allFbx.Where(fbxFile => {
            string[] splits = fbxFile.Replace("\\", "/").Split("/");
            int lastIndex = splits.Length - 1;
            return splits[lastIndex].StartsWith(filterByAsset);
        });
        int total = allFbx.Count(), i = 0;
        foreach (var absPath in allFbx)
        {
            float prog = (float)(i++) / total;
            EditorUtility.DisplayProgressBar("Building Manifests",
                Path.GetFileName(absPath), prog);

            // load the FBX prefab, instantiate, read its position, destroy
            string rel = ToUnityPath(absPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rel);
            if (prefab == null) continue;

            var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            Vector3 pos = inst.transform.GetChild(0).transform.position;
            GameObject.DestroyImmediate(inst);

            // compute chunk coords
            int cx = Mathf.FloorToInt(pos.x / chunkSize);
            int cy = Mathf.FloorToInt(pos.y / chunkSize);
            int cz = Mathf.FloorToInt(pos.z / chunkSize);
            string manifest = Path.Combine(manifestFolder, $"{cx}_{cy}_{cz}.txt");

            // append the FBX and texture path
            string texPath = Path.ChangeExtension(rel.Split(".")[0] + "_Texture.fbx", ".jpg");
            File.AppendAllLines(manifest,
                new[] { rel + "|" + texPath });
        }

        AssetDatabase.Refresh();
        EditorUtility.ClearProgressBar();
        Debug.Log("Built chunk manifests in " + manifestFolder);
    }

    // — STEP 2: for each chunk, read its small list, merge/export, then clean up —
    void ProcessAllChunks()
    {
        finishedChunkCount = 0;
        if (!Directory.Exists(manifestFolder))
        {
            Debug.LogError("No manifests found. Build them first.");
            return;
        }
        Directory.CreateDirectory(ExportFolder);

        var manifests = Directory.GetFiles(manifestFolder, "*.txt");
        int totalChunks = manifests.Length, ci = 0;

        processNumber = 0;
        AssetDatabase.StartAssetEditing();

        foreach (var mf in manifests)
        {
            float prog = (float)(ci++) / totalChunks;
            EditorUtility.DisplayProgressBar("Merging Chunks",
                Path.GetFileNameWithoutExtension(mf), prog);

            // read only this chunk’s lines
            var lines = File.ReadAllLines(mf);
            var tiles = new List<GameObject>();

            string safeName = SanitizeFileName(filterByAsset + "_MergedTile_" + (finishedChunkCount + 1));
            string folderPath = Path.Combine(ExportFolder + filterByAsset + "_FBXFolderAssets/");
            string fbxPath = GetAbsolutePath(Path.Combine(folderPath, $"{safeName}.fbx"));
            if (File.Exists(fbxPath))
            {
                Debug.Log("Skipped chunk, because it already exists.");
                finishedChunkCount++;
                File.Delete(mf);
                continue;
            }
            // instantiate & texture each entry
            foreach (var ln in lines)
            {
                var parts = ln.Split('|');
                var fbxRel = parts[0];
                var texRel = parts[1];

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxRel);
                if (prefab == null) continue;
                var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

                var r = inst.GetComponentInChildren<Renderer>();
                var m = new Material(Shader.Find("HDRP/Lit"));
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texRel);
                if (tex != null) m.SetTexture("_BaseColorMap", tex);
                r.sharedMaterial = m;

                var mfilt = inst.GetComponentInChildren<MeshFilter>();
                var mesh = mfilt.sharedMesh;

                tiles.Add(mfilt.gameObject);
            }

            // now call your merge/export routine on just this chunk’s tiles
            MergeAndExportChunk(tiles);

            // destroy originals & delete manifest
            foreach (var t in tiles)
                GameObject.DestroyImmediate(t.transform.parent.gameObject);
            File.Delete(mf);
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }

        AssetDatabase.StopAssetEditing();
        AssetDatabase.Refresh();

        EditorUtility.ClearProgressBar();
        Debug.Log($"Processed {totalChunks} chunks; imports/exports complete.");
    }

    void MergeAndExportChunk(List<GameObject> tiles)
    {
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

                // Add all hits to chunk
                /*Collider[] hits = Physics.OverlapSphere(tile.transform.position, chunkSize);
                List<GameObject> group = new List<GameObject> { tile.gameObject };
                */
                List<GameObject> group = new List<GameObject> { tile.gameObject };
                foreach (var add in tiles)
                {
                    group.Add(add.gameObject);
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
                finishedChunkCount++;
                GameObject merged = new GameObject(filterByAsset + "_MergedTile_" + finishedChunkCount);
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
                        UnityEngine.Object.DestroyImmediate(go.transform.parent.gameObject);
                    }
                }

                Object.DestroyImmediate(merged);
                Object.DestroyImmediate(combinedMesh);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            processedCount = 0;
            total = mergedTiles.Count;
            if (mergedTiles != null && mergedTiles.Count > 0)
            {
                EnsureExportDirectory();
                foreach (GameObject go in mergedTiles)
                {
                    EditorUtility.DisplayProgressBar("Exporting Merged FBX Files", $"Processing {go}", (float)processedCount / total);
                    ExportGameObjectToFbx(go);
                    processedCount++;
                    processNumber++;
                }
                Debug.Log($"Merged tiles into {mergedTiles.Count} combined objects.");
            }

        }
        EditorUtility.ClearProgressBar();
        Debug.Log("Finished Process");
    }
    private void ExportGameObjectToFbx(GameObject go)
    {
        string safeName = SanitizeFileName(go.name);
        string folderPath = Path.Combine(ExportFolder + filterByAsset + "_FBXFolderAssets/");
        string fbxPath = Path.Combine(folderPath, $"{safeName}.fbx");
        string materialsFolder = Path.Combine(folderPath, "Materials/");
        string texturesFolder = Path.Combine(folderPath, "Textures/");


        Directory.CreateDirectory(materialsFolder);
        Directory.CreateDirectory(texturesFolder);
        if (File.Exists(fbxPath))
        {
            Object.DestroyImmediate(go);
            return;
        }

        GameObject temp = go;
        temp.name = safeName;
        Renderer renderer = temp.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material[] originalMaterials = renderer.sharedMaterials;
            Material[] exportedMaterials = new Material[originalMaterials.Length];

            for (int i = 0; i < originalMaterials.Length; i++)
            {
                Material original = originalMaterials[i];
                if (original == null) continue;

                // Clone material
                Material exportMat = new Material(Shader.Find("HDRP/Lit"));
                exportMat.name = $"_{safeName}_Material_{i}";

                // Copy main texture
                Texture2D tex = original.mainTexture as Texture2D;
                tex.name = $"_{safeName}_Texture_{i}";
                if (tex != null)
                {
                    exportMat.mainTexture = tex;

                    // Get texture path
                    string texturePath = AssetDatabase.GetAssetPath(tex);
                    string texExt = Path.GetExtension(texturePath);
                    string newTexPath = Path.Combine(texturesFolder, tex.name + texExt);

                    // Copy texture to export folder
                    File.Copy(texturePath, newTexPath, true);
                }

                // Save material asset
                string matPath = Path.Combine(materialsFolder, exportMat.name + ".mat");
                string relativeMatPath = matPath.Substring(matPath.IndexOf("Assets"));
                AssetDatabase.CreateAsset(exportMat, relativeMatPath);

                exportedMaterials[i] = exportMat;
            }

            renderer.sharedMaterials = exportedMaterials;
        }

        exportModelOptions.ExportFormat = ExportFormat.Binary;
        exportModelOptions.KeepInstances = true;
        exportModelOptions.ExportUnrendered = false;
        exportModelOptions.EmbedTextures = false; // Now exporting textures separately
        ModelExporter.ExportObject(fbxPath, temp, exportModelOptions);

        // Export textures from each material (single renderer, multiple materials)
        //ExportMaterialsTextures(temp.GetComponent<Renderer>(), folderPath, safeName);

        Object.DestroyImmediate(temp);

    }
    private void EnsureExportDirectory()
    {
        if (!Directory.Exists(ExportFolder))
        {
            Directory.CreateDirectory(ExportFolder);
        }
        if (!Directory.Exists(ExportFolder + filterByAsset + "_FBXFolderAssets/"))
        {
            Directory.CreateDirectory(ExportFolder + filterByAsset + "_FBXFolderAssets/");
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
    string ToUnityPath(string abs)
    {
        string projectPath = UnityEngine.Application.dataPath.Replace("\\", "/");
        abs = abs.Replace("\\", "/");

        if (abs.StartsWith(projectPath))
        {
            // Add "Assets" back since Application.dataPath ends in "/Assets"
            return "Assets" + abs.Substring(projectPath.Length);
        }

        return null;
    }
    public static string GetAbsolutePath(string relativePath)
    {
        // Ensure consistent separators
        relativePath = relativePath.Replace("\\", "/");

        if (relativePath.StartsWith("Assets"))
        {
            string basePath = Application.dataPath; // e.g., C:/MyProject/Assets
            string subPath = relativePath.Substring("Assets/".Length);
            //subPath = subPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.Combine(basePath, subPath).Replace("/", Path.DirectorySeparatorChar.ToString());
            return fullPath;
        }
        else
        {
            // If it's not under Assets, treat it as already absolute or invalid
            Debug.LogWarning($"Path doesn't start with 'Assets': {relativePath}");
            return Path.GetFullPath(relativePath);
        }
    }
}