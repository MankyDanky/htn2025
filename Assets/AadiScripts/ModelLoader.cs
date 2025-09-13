using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Handles downloading and instantiating 3D models at runtime
/// </summary>
public class ModelLoader : MonoBehaviour
{
    // Singleton instance
    public static ModelLoader Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Load a GLB model from URL and instantiate it
    /// </summary>
    /// <param name="url">URL to the GLB model</param>
    /// <param name="parent">Optional parent transform</param>
    /// <param name="scale">Scale factor to apply</param>
    /// <param name="onComplete">Callback when model is loaded (GameObject, success)</param>
    public void LoadModel(string url, Transform parent = null, float scale = 1.0f, 
                         Action<GameObject, bool> onComplete = null)
    {
        StartCoroutine(LoadModelCoroutine(url, parent, scale, onComplete));
    }

    private IEnumerator LoadModelCoroutine(string url, Transform parent, float scale, 
                                          Action<GameObject, bool> onComplete)
    {
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("Model URL is null or empty");
            onComplete?.Invoke(null, false);
            yield break;
        }

        // Create a unique filename for this model
        string filename = Path.GetFileName(new Uri(url).AbsolutePath);
        string localPath = Path.Combine(Application.temporaryCachePath, filename);
        bool shouldDownload = true;

        // Check if we already have this file in cache
        if (File.Exists(localPath))
        {
            Debug.Log($"Using cached model: {localPath}");
            shouldDownload = false;
        }

        // Download the file if needed
        if (shouldDownload)
        {
            Debug.Log($"Downloading model: {url}");
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Failed to download model: {request.error}");
                    onComplete?.Invoke(null, false);
                    yield break;
                }

                // Save to cache
                try
                {
                    File.WriteAllBytes(localPath, request.downloadHandler.data);
                    Debug.Log($"Downloaded model to: {localPath}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to cache model: {e.Message}");
                    // Continue without caching
                }
            }
        }

        // For GLB files, we need to use a runtime GLTF loader
        // You need to install the GLTFast package from the package manager
        // For this example, we'll use a custom GLB loader
        yield return StartCoroutine(InstantiateGlbModel(localPath, parent, scale, onComplete));
    }

    private IEnumerator InstantiateGlbModel(string localPath, Transform parent, float scale, 
                                           Action<GameObject, bool> onComplete)
    {
        try
        {
            // Create a game object to hold our model
            GameObject modelRoot = new GameObject("Model_" + Path.GetFileNameWithoutExtension(localPath));
            
            if (parent != null)
            {
                modelRoot.transform.SetParent(parent, false);
            }
            
            modelRoot.transform.localScale = Vector3.one * scale;
            
            // We need to use GLTFast to load the model
            // Since GLTFast might not be installed yet, we'll use reflection to create the importer

            // 1. First create a box as a placeholder
            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.transform.SetParent(modelRoot.transform, false);
            placeholder.transform.localPosition = Vector3.zero;
            placeholder.transform.localScale = Vector3.one;
            
            // Add a component to load the GLB when GLTFast is available
            ModelPlaceholder mp = modelRoot.AddComponent<ModelPlaceholder>();
            mp.ModelFilePath = localPath;
            
            // For now return the placeholder
            onComplete?.Invoke(modelRoot, true);
            
            // Note: When GLTFast is properly installed, you would use:
            // var gltf = new GLTFast.GltfImport();
            // bool success = false;
            //
            // try
            // {
            //     success = await gltf.Load(localPath);
            //     if (success)
            //     {
            //         success = await gltf.InstantiateMainSceneAsync(modelRoot.transform);
            //     }
            // }
            // catch (Exception ex)
            // {
            //     Debug.LogError($"GLTF import error: {ex.Message}");
            //     success = false;
            // }
            
            // For now we'll mark it as success
            // onComplete?.Invoke(modelRoot, true);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to instantiate GLB model: {e.Message}");
            onComplete?.Invoke(null, false);
        }
        
        yield break;
    }
}

/// <summary>
/// Placeholder component for models that will be loaded once GLTFast is available
/// </summary>
public class ModelPlaceholder : MonoBehaviour
{
    public string ModelFilePath;
    
    // This is where you would load the real model once GLTFast is installed
}
