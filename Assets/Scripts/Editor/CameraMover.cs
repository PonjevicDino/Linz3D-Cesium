using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using System.Diagnostics;
using System;
using UnityEditor.Profiling.Memory.Experimental;
using Unity.Collections;

public class CameraMoverWindow : EditorWindow
{
    private Vector3Int startPoint = new Vector3Int(5, 5, 5);
    private Vector3Int endPoint = Vector3Int.zero;
    private bool isMoving;
    private bool isNextPressed = false;
    private EditorCoroutine movementCoroutine;
    private int totalPositions;
    private int currentPositionIndex;
    private Stopwatch movementTimer = new Stopwatch();
    private string statusMessage;

    [MenuItem("Tools/Camera Mover")]
    public static void ShowWindow()
    {
        GetWindow<CameraMoverWindow>("Scene Camera Mover");
    }

    void OnGUI()
    {
        EditorGUI.BeginDisabledGroup(isMoving);
        {
            startPoint = EditorGUILayout.Vector3IntField("Start Point", startPoint);
            endPoint = EditorGUILayout.Vector3IntField("End Point", endPoint);

            if (GUILayout.Button("Start Movement"))
            {
                StartCameraMovement();
            }
        }
        EditorGUI.EndDisabledGroup();

        if (isMoving)
        {
            if (GUILayout.Button("Stop Movement"))
            {
                StopCameraMovement();
            }
            if (GUILayout.Button("Next Step (1s TO)"))
            {
                isNextPressed = true;
            }

            // Progress display
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status:", statusMessage);

            if (totalPositions > 0)
            {
                float progress = (float)currentPositionIndex / totalPositions;
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress,
                    $"Progress: {progress * 100:0.0}%");

                EditorGUILayout.LabelField($"Positions: {currentPositionIndex}/{totalPositions}");
                EditorGUILayout.LabelField($"Elapsed: {FormatTimeSpan(movementTimer.Elapsed)}");

                if (currentPositionIndex > 0)
                {
                    TimeSpan eta = TimeSpan.FromSeconds(
                        (movementTimer.Elapsed.TotalSeconds / currentPositionIndex) *
                        (totalPositions - currentPositionIndex)
                    );
                    EditorGUILayout.LabelField($"ETA: {FormatTimeSpan(eta)}");
                }
            }
        }
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
        {
            return $"{timeSpan:%d} days {timeSpan:%h} hrs {timeSpan:%m} min";
        }
        else if (timeSpan.TotalHours >= 1)
        {
            return $"{timeSpan:%h} hrs {timeSpan:%m} min {timeSpan:%s} sec";
        }
        else if (timeSpan.TotalMinutes >= 1)
        {
            return $"{timeSpan:%m} min {timeSpan:%s} sec";
        }
        else
        {
            return $"{timeSpan:%s} sec";
        }
    }

    void StartCameraMovement()
    {
        isMoving = true;
        NativeArray<Vector3> positions = GeneratePositions(startPoint, endPoint);
        totalPositions = positions.Length;
        currentPositionIndex = 0;
        movementTimer.Restart();
        movementCoroutine = EditorCoroutineUtility.StartCoroutine(MoveCamera(positions), this);
    }

    void StopCameraMovement()
    {
        if (movementCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(movementCoroutine);
        }
        movementTimer.Stop();
        isMoving = false;
        titleContent = new GUIContent("Camera Mover");
    }

    NativeArray<Vector3> GeneratePositions(Vector3Int start, Vector3Int end)
    {
        int xStep = Math.Sign(end.x - start.x);
        int yStep = Math.Sign(end.y - start.y);
        int zStep = Math.Sign(end.z - start.z);

        // Handle case where start and end are the same in some axis
        if (xStep == 0) xStep = 1;
        if (yStep == 0) yStep = 1;
        if (zStep == 0) zStep = 1;

        int totalPositions = 0;

        for (int x = start.x; x != end.x + xStep * 250; x += xStep * 250)
        {
            for (int z = start.z; z != end.z + zStep * 250; z += zStep * 250)
            {
                for (int y = start.y; y != end.y + yStep * 250; y += yStep * 250)
                {
                    totalPositions++;
                }
            }
        }

        NativeArray<Vector3> positions = new NativeArray<Vector3>((int)(totalPositions * (1.0f + 1.0f / 3.0f)), Allocator.Persistent);

        int arrIndex = 0;

        for (int x = start.x; x != end.x + xStep * 250; x += xStep * 250)
        {
            for (int z = start.z; z != end.z + zStep * 250; z += zStep * 250)
            {
                for (int y = start.y; y != end.y + yStep * 250; y += yStep * 250)
                {
                    positions[arrIndex] = (new Vector3(x, y, z));
                    arrIndex++;
                }
            }
        }

        return positions;
    }

    IEnumerator MoveCamera(NativeArray<Vector3> positions)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null) yield break;

        // Save original camera state
        bool originalOrtho = sceneView.orthographic;
        Quaternion originalRotation = sceneView.rotation;
        Vector3 originalPivot = sceneView.pivot;

        // Setup camera
        //sceneView.orthographic = true;
        sceneView.size = 0.5f;
        Quaternion targetRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        foreach (Vector3 pos in positions)
        {
            if (!isMoving) break;

            currentPositionIndex++;
            statusMessage = $"Current Position: X:{pos.x}, Y:{pos.y}, Z:{pos.z}";

            sceneView.pivot = pos;
            sceneView.rotation = targetRotation;
            sceneView.Repaint();

            // Update window title with progress
            float progress = (float)currentPositionIndex / totalPositions;
            titleContent = new GUIContent($"Camera Mover ({progress * 100:0}%)");

            // Force UI update
            Repaint();

            yield return new EditorWaitForSeconds(0.2f);
            RaycastHit hit;
            if (!Physics.Raycast(positions[currentPositionIndex], Vector3.down, out hit, 10000.0f))
            {
                yield return new EditorWaitForSeconds(0.05f);
            }
            else
            {
                for (int seconds = 0; seconds < 60; seconds++) // <---- 60 = Timeout
                {
                    if (isNextPressed)
                    {
                        isNextPressed = false;
                        break;
                    }
                    else
                    {
                        yield return new EditorWaitForSeconds(1.0f);
                    }
                }
            }
        }

        // Restore camera state
        if (isMoving)
        {
            movementTimer.Stop();
            sceneView.orthographic = originalOrtho;
            sceneView.rotation = originalRotation;
            sceneView.pivot = originalPivot;
            sceneView.Repaint();
            titleContent = new GUIContent("Camera Mover");
        }

        isMoving = false;
    }
}