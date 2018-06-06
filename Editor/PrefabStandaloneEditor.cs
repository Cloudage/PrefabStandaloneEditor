using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.IO;

public class PrefabStandaloneEditor : UnityEngine.Object
{
    static string lastFileScenePath;

    static Vector3 lastSceneViewPivot = new Vector3(0, 0, 0);
    static float lastSceneViewSize = 10;
    static Quaternion lastSceneViewRotation = Quaternion.Euler(45, 45, 0);
    static bool lastSceneViewIs2D = false;

    static Scene prefabScene;
    static GameObject prefabSceneLight;
    //static GameObject editingPrefabInstance;
    static int editingPrefabInstanceID;
    static float lightRotationY;
    static bool isEditingPrefab;

    static Bounds Join(Bounds a, Bounds b)
    {
        var min = Vector3.Min(a.min, b.min);
        var max = Vector3.Max(a.max, b.max);
        var center = (min + max) / 2;
        return new Bounds(center, max - min);
    }

    static Bounds CaculateBounds(GameObject obj)
    {
        Bounds bounds = new Bounds();

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            bounds = Join(bounds, renderer.bounds);
        }

        var collider = obj.GetComponent<Collider>();
        if (collider != null)
        {
            bounds = Join(bounds, collider.bounds);
        }

        var matW2L = obj.transform.worldToLocalMatrix;

        for (int i = 0; i < obj.transform.childCount; i++)
        {
            var child = obj.transform.GetChild(i).gameObject;
            var childBounds = CaculateBounds(child);

            bounds = Join(bounds, new Bounds(matW2L * childBounds.center, matW2L * childBounds.size));
        }

        var matL2W = obj.transform.localToWorldMatrix;

        return new Bounds(matL2W * bounds.center, matL2W * bounds.size);
    }

    static PrefabStandaloneEditor()
    {
        SceneView.onSceneGUIDelegate += DrawSceneViewUI;
        EditorSceneManager.activeSceneChangedInEditMode += OnEditorActiveSceneChanged;
    }

    private static void OnEditorActiveSceneChanged(Scene newScene, Scene oldScene)
    {
        if (newScene != prefabScene)
        {
            isEditingPrefab = false;
            prefabSceneLight = null;

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.in2DMode = lastSceneViewIs2D;
                SceneView.lastActiveSceneView.pivot = lastSceneViewPivot;
                SceneView.lastActiveSceneView.size = lastSceneViewSize;
                SceneView.lastActiveSceneView.rotation = lastSceneViewRotation;                
            }
        }
        else
        {
            isEditingPrefab = true;
        }
    }

    static GameObject CreateLight(Scene targetScene)
    {
        var lightObj = new GameObject();
        var light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObj.transform.position = new Vector3(0, 100, 0);
        lightObj.transform.Rotate(Vector3.right, 60);
        
        var lightRoot = new GameObject();
        lightRoot.transform.Rotate(Vector3.up, lightRotationY);
        lightObj.transform.parent = lightRoot.transform;

        lightRoot.hideFlags = HideFlags.HideInHierarchy;

        SceneManager.MoveGameObjectToScene(lightRoot, prefabScene);

        return lightRoot;
    }

    static GameObject CreateCanvas(Scene targetScene)
    {
        var canvasObj = new GameObject("Canvas");
        var canvas = canvasObj.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;

        canvasObj.hideFlags = HideFlags.NotEditable;

        SceneManager.MoveGameObjectToScene(canvasObj, targetScene);

        return canvasObj;
    }

    [OnOpenAsset]
    public static bool OnOpenAsset(int instanceID, int line)
    {
        var assetPath = AssetDatabase.GetAssetPath(instanceID);

        if (Path.GetExtension(assetPath).ToLower() != ".prefab")
        {
            return false;
        }

        var obj = EditorUtility.InstanceIDToObject(instanceID);
        if (obj is GameObject)
        {
            editingPrefabInstanceID = instanceID;

            var editingPrefab = obj as GameObject;

            var oldScene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(oldScene.path))
            {
                lastFileScenePath = oldScene.path;

                if (SceneView.lastActiveSceneView!=null)
                {
                    lastSceneViewPivot = SceneView.lastActiveSceneView.pivot;
                    lastSceneViewSize = SceneView.lastActiveSceneView.size;
                    lastSceneViewRotation = SceneView.lastActiveSceneView.rotation;
                    lastSceneViewIs2D = SceneView.lastActiveSceneView.in2DMode;
                }

                if (oldScene.isDirty)
                {
                    var shouldSave = EditorUtility.DisplayDialog("save your scene?", "Your scene have some contents changed. Save them before you leave, or these changes will be lost.", "Save", "Ignore");
                    if (shouldSave)
                    {
                        EditorSceneManager.SaveScene(oldScene);
                    }
                }
            }

            prefabScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = PrefabUtility.InstantiatePrefab(editingPrefab, prefabScene) as GameObject;

            if (editingPrefab.GetComponent<RectTransform>() != null)
            {
                var canvas = CreateCanvas(prefabScene);
                instance.GetComponent<RectTransform>().SetParent(canvas.transform, false);

                var canvasRect = canvas.GetComponent<RectTransform>();
                
                var sceneView = SceneView.lastActiveSceneView;

                sceneView.in2DMode = true;
                sceneView.pivot = new Vector3(sceneView.position.width / 2, sceneView.position.height / 2, 1);
                sceneView.size = Mathf.Max(canvasRect.rect.width, canvasRect.rect.height) + 20;
                sceneView.rotation = Quaternion.identity;
            }
            else
            {
                prefabSceneLight = CreateLight(prefabScene);

                SceneView.lastActiveSceneView.in2DMode = false;
                SceneView.lastActiveSceneView.pivot = Vector3.zero;
                SceneView.lastActiveSceneView.size = CaculateBounds(instance).max.magnitude + 3;
                SceneView.lastActiveSceneView.rotation = Quaternion.Euler(45, 45, 0);                
            }

            isEditingPrefab = true;

            return true;
        }
        else
        {
            return false;
        }
    }

    static void DrawSceneViewUI(SceneView sceneView)
    {
        if (!isEditingPrefab) return;

        Handles.BeginGUI();

        GUI.Box(new Rect(0, 0, sceneView.position.width, 26), GUIContent.none);

        if (GUI.Button(new Rect(4, 4, 48, 18), "Close"))
        {
            CloseScene();
        }

        if (prefabSceneLight != null)
        {
            lightRotationY = GUI.HorizontalSlider(new Rect(56, 4, 128, 18), lightRotationY, 0, 360);
            prefabSceneLight.transform.rotation = Quaternion.Euler(0, lightRotationY, 0);
        }
        
        var assetPath = AssetDatabase.GetAssetPath(editingPrefabInstanceID);
        GUI.Label(new Rect(200, 4, sceneView.position.width - 200, 18), assetPath);

        Handles.EndGUI();

    }

    static void CloseScene()
    {
        if (prefabScene != SceneManager.GetActiveScene()) return;

        if (lastFileScenePath!=null)
        {
            EditorSceneManager.OpenScene(lastFileScenePath, OpenSceneMode.Single);
        }
        else
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        EditorSceneManager.CloseScene(prefabScene, true);
        //editingPrefabInstance = null;
        //editingPrefab = null;
        prefabSceneLight = null;
        lastFileScenePath = null;
    }
}

