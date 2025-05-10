using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Formats.Fbx.Exporter;
using System.Collections.Concurrent;
using System.Threading;

[InitializeOnLoad]
public class AdvancedExportMonitor
{
    private const string PrefsKey = "ExportMonitorEnabled";
    private static HashSet<GameObject> _previousActiveObjects = new HashSet<GameObject>();
    private static HashSet<int> _exportedObjects = new HashSet<int>();
    private static readonly string ExportRoot = "C:\\Users\\SIM-PC-1\\Desktop\\ExportedAssets";
    private static ExportModelOptions exportModelOptions = new ExportModelOptions();

    private static ConcurrentQueue<GameObject> exportQueue = new ConcurrentQueue<GameObject>();

    private static int WorkerCount = 128;
    private static List<Thread> workers = new List<Thread>();
    private static volatile bool keepRunning = true;


    private static void StartWorkers()
    {
        for (int i = 0; i < WorkerCount; i++)
        {
            Thread t = new Thread(ExportWorker);
            t.IsBackground = true;
            t.Start();
            workers.Add(t);
        }
    }

    private static void ExportWorker()
    {
        while (keepRunning)
        {
            if (exportQueue.TryDequeue(out var go))
            {
                /*
                lock (_exportedObjects)
                {
                    if (_exportedObjects.Contains(go.GetInstanceID()))
                    {
                        continue;
                    }
                    _exportedObjects.Add(go.GetInstanceID());
                }

                EditorApplication.delayCall += () => ProcessGameObject(go);*/

                EditorApplication.delayCall += () =>
                {
                    int id = go.GetInstanceID();
                    lock (_exportedObjects)
                    {
                        if (_exportedObjects.Contains(id))
                        {
                            return;
                        }
                        _exportedObjects.Add(id);
                    }
                    ProcessGameObject(go);
                };
            }
            else
            {
                Thread.Sleep(100);
            }
        }        
    }

    private static void StopWorkers()
    {
        keepRunning = false;
        foreach (var thread in workers)
        {
            if (thread.IsAlive)
            {
                thread.Join();
            }
        }
        workers.Clear();
    }

    static AdvancedExportMonitor()
    {
        exportModelOptions.ExportFormat = ExportFormat.Binary;
        bool enabled = EditorPrefs.GetBool(PrefsKey, true);
        if (enabled) EnableMonitoring();
    }

    [MenuItem("Tools/Export Monitor/Toggle Monitoring")]
    private static void ToggleMonitoring()
    {
        bool newState = !EditorPrefs.GetBool(PrefsKey, true);
        EditorPrefs.SetBool(PrefsKey, newState);

        if (newState) EnableMonitoring();
        else DisableMonitoring();

        Menu.SetChecked("Tools/Export Monitor/Toggle Monitoring", newState);
    }

    [MenuItem("Tools/Export Monitor/Toggle Monitoring", true)]
    private static bool ToggleMonitoringValidate()
    {
        Menu.SetChecked("Tools/Export Monitor/Toggle Monitoring",
                      EditorPrefs.GetBool(PrefsKey, true));
        return true;
    }

    private static void EnableMonitoring()
    {
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EnsureExportDirectory();
        Debug.Log("Export Monitor: Enabled");
        StartWorkers();
    }

    private static void DisableMonitoring()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        _previousActiveObjects.Clear();
        _exportedObjects.Clear();
        Debug.Log("Export Monitor: Disabled");
        StopWorkers();
    }

    private static void EnsureExportDirectory()
    {
        if (!Directory.Exists(ExportRoot))
        {
            Directory.CreateDirectory(ExportRoot);
            AssetDatabase.Refresh();
            Debug.Log($"Created export directory: {ExportRoot}");
        }
    }

    private static void OnHierarchyChanged()
    {
        if (!EditorPrefs.GetBool(PrefsKey, true)) return;

        var currentActiveObjects = new HashSet<GameObject>(
            Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(go => go.scene.IsValid() &&
                       go.activeInHierarchy &&
                       (go.hideFlags & HideFlags.HideInHierarchy) == 0)
        );

        var newlyEnabledObjects = currentActiveObjects.Except(_previousActiveObjects).ToList();

        foreach (var go in newlyEnabledObjects)
        {
            if (ShouldExport(go))
            {
                exportQueue.Enqueue(go);
            }
        }

        _previousActiveObjects = currentActiveObjects;
    }

    private static bool ShouldExport(GameObject go)
    {
        int id = go.GetInstanceID();
        if (_exportedObjects.Contains(id)) return false;

        string parentName = GetExportName(go);
        if (!parentName.Contains("http")) return false;
        string tileNumber = int.Parse(parentName.Split("/")[6].Split("_")[1]).ToString("00") + "-";
        parentName = parentName.Split("/")[9].Split(".")[0];
        parentName = parentName.Substring(0, 3) + tileNumber + parentName.Substring(3);
        string fbxPath = Path.Combine(ExportRoot, $"{parentName}.fbx");
        return !File.Exists(fbxPath);
    }

    private static string GetExportName(GameObject go)
    {
        return go.transform.parent != null ?
            go.transform.parent.name :
            go.name;
    }

    private static void ProcessGameObject(GameObject go)
    {
        
        string baseName = GetExportName(go);
        if (!baseName.Contains("http")) return;
        string tileNumber = int.Parse(baseName.Split("/")[6].Split("_")[1]).ToString("00") + "-";
        baseName = baseName.Split("/")[9].Split(".")[0];
        baseName = baseName.Substring(0, 3) + tileNumber + baseName.Substring(3);
        Renderer renderer = go.GetComponent<Renderer>();
        MeshFilter meshFilter = go.GetComponent<MeshFilter>();

        if (renderer == null || meshFilter == null) return;

        ProcessMaterial(renderer, baseName);
        ExportFbx(go, baseName, null);
        /*Transform tempParent = go.transform.parent; 
        
        if (success)
        {
           
        }*/
        _exportedObjects.Add(go.GetInstanceID());
        Debug.Log($"Exported: {baseName}");
    }

    private static Material ProcessMaterial(Renderer renderer, string baseName)
    {
        Material originalMat = renderer.sharedMaterial;
        if (originalMat == null) return null;

        Texture2D texture = originalMat.GetTexture("_baseColorTexture") as Texture2D;
        ExportTexture(texture, baseName);

        /*
        Material newMat = new Material(originalMat);
        newMat.name = $"{baseName}_Material";
        newMat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        string matPath = Path.Combine(ExportRoot, $"{baseName}_Mat.mat");
        AssetDatabase.CreateAsset(newMat, matPath);
        */
        return null;
    }

    private static string ExportTexture(Texture2D texture, string baseName)
    {
        if (texture == null) return null;

        byte[] bytes = InstantiateTexture(texture).EncodeToJPG();

        string texPath = Path.Combine(ExportRoot, $"{baseName}_Texture.jpg");


        File.WriteAllBytes(texPath, bytes);

        return null;
    }

    private static Texture2D InstantiateTexture(Texture2D source)
    {
        RenderTexture rt = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.sRGB
        );

        Graphics.Blit(source, rt);
        Texture2D readableTexture = new Texture2D(source.width, source.height);
        RenderTexture.active = rt;
        readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readableTexture.Apply();
        RenderTexture.ReleaseTemporary(rt);
        return readableTexture;
    }

    private static void ExportFbx(GameObject go, string baseName, Material material)
    {
        try
        {
            GameObject tempGo = Object.Instantiate(go.transform.parent.gameObject);
            tempGo.name = baseName;
            //tempGo.GetComponent<Renderer>().sharedMaterial = material;

            string fbxPath = Path.Combine(ExportRoot, $"{baseName}.fbx");

            ModelExporter.ExportObject(fbxPath, tempGo, exportModelOptions);

            Object.DestroyImmediate(tempGo);
            //AssetDatabase.Refresh();
            /*GameObject nestedChild = go;
            if (nestedChild != null)
            {
                Undo.DestroyObjectImmediate(nestedChild);
                Debug.Log($"Removed child '{nestedChild.name}' from '{nestedChild.name}'");
                return true;
            }*/
            
        }
        catch (System.Exception e)
        {
            Debug.LogError($"FBX Export failed: {e.Message}");
        }
    }
}