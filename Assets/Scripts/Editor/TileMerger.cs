using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;

public class TileMerger : EditorWindow
{
    private string folderPath = "Assets";
    private string filterByAsset = "";
    private float radiusToMergeBy = 25f;
    [MenuItem("Tools/Merge Tiles")]
    static void Init()
    {
        TileMerger window = (TileMerger)GetWindow(typeof(TileMerger));
        window.Show();
    }

    void OnGUI()
    {
        GUILayout.Label("FBX + Texture Importer & Merger", EditorStyles.boldLabel);

        filterByAsset = EditorGUILayout.TextField("Filter by Asset", "21");
        radiusToMergeBy = EditorGUILayout.FloatField("Filter by Raidus", 25f);

        if (GUILayout.Button("Select Folder"))
        {
            folderPath = EditorUtility.OpenFolderPanel("Select Tile Folder", folderPath, "");
        }

        if (GUILayout.Button("Import and Apply Textures"))
        {
            MergeTiles();
        }
    }
    public void MergeTiles()
    {
        // Prompt for folder
        if (string.IsNullOrEmpty(folderPath)) return;
        // Get all FBX files
        EditorUtility.DisplayProgressBar("TileMerger", "Filtering FBX Files", 0);
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
            string absolutePath = fbxPath
                .Replace("\\", "/")
                .Split(new[] { "Assets/" }, System.StringSplitOptions.None)[2];
            absolutePath = "Assets/" + absolutePath;
            string texPath = Path.ChangeExtension(absolutePath.Split(".")[0] + "_Texture.fbx", ".jpg");
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { Debug.LogWarning($"Missing texture: {texPath}"); continue; }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(absolutePath);
            if (model == null) { Debug.LogWarning($"Missing FBX: {absolutePath}"); continue; }
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
                combinedMesh.CombineMeshes(combineInstances.ToArray(), false, true);

                // Create new GameObject for the combined mesh
                GameObject merged = new GameObject(filterByAsset + "-MergedTile");
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
        }

        Debug.Log($"Merged tiles into {mergedTiles.Count} combined objects.");





        // Step 2: Group into 2x2 based on transform positions
        // Build a lookup of grid coordinates to tile
        /*var coordMap = new Dictionary<Vector2Int, GameObject>();
        float tileSize = 25.0f; // assume unit grid (adjust as needed)
        float tolerance = 2.0f;

        Vector2Int SnapToGrid(Vector3 pos)
        {
            int x = Mathf.RoundToInt(pos.x / tileSize);
            int y = Mathf.RoundToInt(pos.z / tileSize);
            return new Vector2Int(x, y);
        }
        *//*
        foreach (GameObject tile in tiles)
        {
            Vector3 pos = tile.transform.GetChild(0).transform.position;
            Vector2Int gridPos = SnapToGrid(pos);
            if (!coordMap.ContainsKey(gridPos))
                coordMap[gridPos] = tile;
            else
                Debug.LogWarning($"Duplicate tile at snapped grid position {gridPos}");
            /*
            Vector3 pos = tile.transform.GetChild(0).transform.position;
            Vector2Int key = new Vector2Int(Mathf.RoundToInt(pos.x / tileSize), Mathf.RoundToInt(pos.z / tileSize));
            coordMap[key] = tile;*/
        //}
        /*
        // Iterate to find 2x2 squares
        foreach (var kv in coordMap)
        {
            Vector2Int c = kv.Key;
            if (kv.Value != null && used.Contains(kv.Value.name)) continue;
            // Check right, up, up-right neighbors
            Vector2Int right = new Vector2Int(c.x + 1, c.y);
            Vector2Int up = new Vector2Int(c.x, c.y + 1);
            Vector2Int upRight = new Vector2Int(c.x + 1, c.y + 1);
            if (!coordMap.ContainsKey(right) || !coordMap.ContainsKey(up) || !coordMap.ContainsKey(upRight))
                continue;
            var tileA = coordMap[c];
            var tileB = coordMap[right];
            var tileC = coordMap[up];
            var tileD = coordMap[upRight];
            if (!tileA|| used.Contains(tileA.name) || used.Contains(tileB.name) ||
                used.Contains(tileC.name) || used.Contains(tileD.name))
                continue;

            // Mark as used
            used.Add(tileA.name); used.Add(tileB.name);
            used.Add(tileC.name); used.Add(tileD.name);

            // Merge these four
            GameObject parent = new GameObject("MergedTile");
            MeshFilter mf = parent.AddComponent<MeshFilter>();
            MeshRenderer mr = parent.AddComponent<MeshRenderer>();

            // Pack textures into atlas
            /*
            Texture2D[] texs = { DuplicateTexture(tileA.GetComponentInChildren<Renderer>().sharedMaterial.mainTexture as Texture2D),
                                 DuplicateTexture(tileB.GetComponentInChildren<Renderer>().sharedMaterial.mainTexture as Texture2D),
                                 DuplicateTexture(tileC.GetComponentInChildren<Renderer>().sharedMaterial.mainTexture as Texture2D),
                                 DuplicateTexture(tileD.GetComponentInChildren<Renderer>().sharedMaterial.mainTexture as Texture2D) };
            Texture2D atlas = new Texture2D(2048, 2048);
            
            Rect[] rects = atlas.PackTextures(texs, 2, 2048,false);*/
        /*  Material[] materials = new Material[4];
          materials[0] = tileA.GetComponentInChildren<Renderer>().sharedMaterial;
          materials[1] = tileB.GetComponentInChildren<Renderer>().sharedMaterial;
          materials[2] = tileC.GetComponentInChildren<Renderer>().sharedMaterial;
          materials[3] = tileD.GetComponentInChildren<Renderer>().sharedMaterial;

          // Combine meshes
          var combines = new CombineInstance[4];
          GameObject[] group = { tileA, tileB, tileC, tileD };
          for (int i = 0; i < 4; i++)
          {
              var cf = group[i].GetComponentInChildren<MeshFilter>();
              combines[i].mesh = cf.sharedMesh;
              combines[i].transform = group[i].transform.GetChild(0).transform.localToWorldMatrix;
          }
          mf.sharedMesh = new Mesh();
          mf.sharedMesh.CombineMeshes(combines, false, true);

          mr.sharedMaterials = materials;
          // Adjust UVs
          /*
          Vector2[] uvs = mf.sharedMesh.uv;
          for (int i = 0; i < 4; i++)
          {
              Rect r = rects[i];
              // For simplicity assume each mesh has non-overlapping vertex index ranges of equal length
              int uvStart = i * (uvs.Length / 4);
              int count = uvs.Length / 4;
              for (int j = uvStart; j < uvStart + count; j++)
              {
                  Vector2 uv = uvs[j];
                  uv.x = r.x + uv.x * r.width;
                  uv.y = r.y + uv.y * r.height;
                  uvs[j] = uv;
              }
          }
          mf.sharedMesh.uv = uvs;*/

        // Create new material with atlas
        /*Material newMat = new Material(tileA.GetComponentInChildren<Renderer>().sharedMaterial);
        newMat.mainTexture = atlas;
        mr.sharedMaterial = newMat;*/



        // Destroy original tiles
        /*foreach (GameObject t in group)
        {
            Undo.DestroyObjectImmediate(t);
        }
    }*/

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
