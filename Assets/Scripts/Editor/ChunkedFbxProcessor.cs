using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

public class ChunkedFbxProcessor : EditorWindow
{
    private float chunkSize = 100f;
    const string manifestFolder = "Assets/FbxChunkGenerator/ChunkManifests";
    private readonly string ExportFolder = "Assets/FbxChunkGenerator/ExportedMergedFBX/";
    private string folderPath = "Assets";
    private string filterByAsset = "";
    private int finishedChunkCount = 0;
    private ExportModelOptions exportModelOptions = new ExportModelOptions();

    // Background processing state
    private bool isProcessing;
    private EditorCoroutine processingCoroutine;
    private int totalChunks;
    private int processedChunks;
    private string currentOperation;

    [MenuItem("Tools/Fbx Merger by Chunks")]
    static void Init()
    {
        var window = GetWindow<ChunkedFbxProcessor>();
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("FBX + Texture Importer & Merger", EditorStyles.boldLabel);
        filterByAsset = EditorGUILayout.TextField("Filter/Split by Asset Name", filterByAsset);
        chunkSize = EditorGUILayout.FloatField("Chunk Size in Meter", chunkSize);

        EditorGUILayout.Space();
        folderPath = EditorGUILayout.TextField("FBX Root Folder", folderPath);
        if (GUILayout.Button("Select Folder"))
        {
            folderPath = EditorUtility.OpenFolderPanel("Select FBX Folder", folderPath, "");
        }

        EditorGUILayout.Space();
        GUI.enabled = !isProcessing;
        if (GUILayout.Button("Build Manifests")) StartCoroutine(BuildManifestsAsync());
        if (GUILayout.Button("Merge & Export All Chunks")) StartCoroutine(ProcessAllChunksAsync());
        GUI.enabled = true;

        if (isProcessing)
        {
            EditorGUILayout.Space();
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(),
                (float)processedChunks / totalChunks,
                $"{currentOperation} ({processedChunks}/{totalChunks})");

            if (GUILayout.Button("Cancel"))
                CancelProcessing();
        }
    }

    void StartCoroutine(IEnumerator routine)
    {
        if (isProcessing) return;
        processingCoroutine = EditorCoroutineUtility.StartCoroutine(routine, this);
    }

    void CancelProcessing()
    {
        if (processingCoroutine != null)
            EditorCoroutineUtility.StopCoroutine(processingCoroutine);

        isProcessing = false;
        EditorUtility.ClearProgressBar();
        Resources.UnloadUnusedAssets();
        GC.Collect();
    }

    IEnumerator BuildManifestsAsync()
    {
        isProcessing = true;
        currentOperation = "Building Manifests";

        if (!Directory.Exists(manifestFolder))
            Directory.CreateDirectory(manifestFolder);
        else
            Directory.GetFiles(manifestFolder).ToList().ForEach(File.Delete);

        var allFbx = Directory.EnumerateFiles(folderPath, "*.fbx", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).StartsWith(filterByAsset))
            .ToList();

        totalChunks = allFbx.Count;
        processedChunks = 0;

        foreach (var absPath in allFbx)
        {
            if (!isProcessing) yield break;

            processedChunks++;
            string rel = ToUnityPath(absPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rel);

            if (prefab != null)
            {
                var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                Vector3 pos = inst.transform.GetChild(0).position;
                DestroyImmediate(inst);

                int cx = Mathf.FloorToInt(pos.x / chunkSize);
                int cy = Mathf.FloorToInt(pos.y / chunkSize);
                int cz = Mathf.FloorToInt(pos.z / chunkSize);
                string manifest = Path.Combine(manifestFolder, $"{cx}_{cy}_{cz}.txt");

                File.AppendAllText(manifest, $"{rel}|{Path.ChangeExtension(rel, ".jpg")}\n");
            }

            if (processedChunks % 10 == 0)
            {
                yield return null;
                GC.Collect();
            }
        }

        AssetDatabase.Refresh();
        isProcessing = false;
    }

    IEnumerator ProcessAllChunksAsync()
    {
        isProcessing = true;
        currentOperation = "Processing Chunks";

        if (!Directory.Exists(manifestFolder))
        {
            Debug.LogError("No manifests found. Build them first.");
            isProcessing = false;
            yield break;
        }

        var manifests = Directory.GetFiles(manifestFolder, "*.txt");
        totalChunks = manifests.Length;
        processedChunks = 0;

        Directory.CreateDirectory(ExportFolder);
        AssetDatabase.StartAssetEditing();

        foreach (var manifest in manifests)
        {
            if (!isProcessing) yield break;

            processedChunks++;
            yield return ProcessSingleChunk(manifest);

            File.Delete(manifest);

            if (processedChunks % 5 == 0)
            {
                Resources.UnloadUnusedAssets();
                GC.Collect();
                yield return null;
            }
        }

        AssetDatabase.StopAssetEditing();
        AssetDatabase.Refresh();
        isProcessing = false;
    }

    IEnumerator ProcessSingleChunk(string manifestPath)
    {
        var lines = File.ReadAllLines(manifestPath);
        var tiles = new List<GameObject>();

        // Load tiles
        foreach (var line in lines)
        {
            var parts = line.Split('|');
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(parts[0]);
            if (prefab == null) continue;

            var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            var r = inst.GetComponentInChildren<Renderer>();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(parts[1]);

            if (r != null && tex != null)
            {
                var tempMat = new Material(r.sharedMaterial);
                tempMat.mainTexture = tex;
                r.sharedMaterial = tempMat;
            }

            tiles.Add(inst);
            if (tiles.Count % 5 == 0) yield return null;
        }

        // Merge and export
        yield return MergeAndExportChunk(tiles);

        // Cleanup
        foreach (var tile in tiles)
            DestroyImmediate(tile);
    }

    IEnumerator MergeAndExportChunk(List<GameObject> tiles)
    {
        var combineInstances = new List<CombineInstance>();
        var materials = new List<Material>();

        foreach (var tile in tiles)
        {
            var mf = tile.GetComponentInChildren<MeshFilter>();
            var mr = tile.GetComponentInChildren<MeshRenderer>();

            if (mf == null || mr == null) continue;

            for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
            {
                combineInstances.Add(new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    subMeshIndex = i,
                    transform = mf.transform.localToWorldMatrix
                });
            }

            materials.AddRange(mr.sharedMaterials);
        }

        var combinedMesh = new Mesh();
        combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        combinedMesh.CombineMeshes(combineInstances.ToArray(), false);

        var mergedGO = new GameObject($"Merged_{finishedChunkCount++}");
        mergedGO.AddComponent<MeshFilter>().sharedMesh = combinedMesh;
        mergedGO.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();

        ExportMergedObject(mergedGO);
        DestroyImmediate(mergedGO);
        DestroyImmediate(combinedMesh);

        yield return null;
    }

    void ExportMergedObject(GameObject go)
    {
        var safeName = SanitizeFileName($"{filterByAsset}_Chunk_{processedChunks}");
        var exportPath = Path.Combine(ExportFolder, $"{safeName}.fbx");

        exportModelOptions = new ExportModelOptions
        {
            EmbedTextures = true,
            ExportFormat = ExportFormat.Binary
        };

        ModelExporter.ExportObject(exportPath, go, exportModelOptions);
    }

    string SanitizeFileName(string name) =>
        Path.GetInvalidFileNameChars().Aggregate(name, (current, c) => current.Replace(c, '_'));

    string ToUnityPath(string absPath) =>
        "Assets" + absPath.Replace("\\", "/").Replace(Application.dataPath, "");

    void Update()
    {
        if (isProcessing)
            Repaint();
    }

    void OnDestroy() => CancelProcessing();
}