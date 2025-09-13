using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;

/// <summary>
/// This class provides implementation for loading GLB/GLTF models using GLTFast
/// </summary>
public class GltfImporter : MonoBehaviour
{
    // This class will be empty for now and should be updated after GLTFast is installed
    
    /// <summary>
    /// Load a model from a URL
    /// </summary>
    /// <param name="url">URL to the model</param>
    /// <param name="parent">Parent transform</param>
    /// <param name="onComplete">Callback with (success, gameObject)</param>
    public static void LoadGltfModel(string url, Transform parent = null, Action<bool, GameObject> onComplete = null)
    {
        // Create a GameObject that will hold the model
        GameObject modelRoot = new GameObject("GltfModel");
        if (parent != null)
        {
            modelRoot.transform.SetParent(parent, false);
        }
        
        // Add an importer component that will handle loading
        GltfImporterComponent importer = modelRoot.AddComponent<GltfImporterComponent>();
        importer.LoadModel(url, onComplete);
    }
    
    /// <summary>
    /// Component that handles the actual loading of GLTF/GLB models
    /// </summary>
    private class GltfImporterComponent : MonoBehaviour
    {
        private Action<bool, GameObject> onComplete;
        
        public void LoadModel(string url, Action<bool, GameObject> callback)
        {
            onComplete = callback;
            StartCoroutine(LoadModelCoroutine(url));
        }
        
        private IEnumerator LoadModelCoroutine(string url)
        {
            Debug.Log($"Loading model from URL: {url} using GLTFast");
            
            // Create a loading indicator (can be removed if not needed)
            GameObject loadingIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            loadingIndicator.transform.SetParent(transform);
            loadingIndicator.transform.localPosition = Vector3.zero;
            loadingIndicator.transform.localScale = Vector3.one * 0.3f;
            
            // Set a different material color to indicate loading
            Renderer renderer = loadingIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(renderer.material);
                mat.color = new Color(1f, 0.6f, 0f, 0.8f); // Orange-ish
                renderer.material = mat;
            }
            
            // Start the actual GLTFast loading process
            var gltfImport = new GLTFast.GltfImport();
            var importTask = gltfImport.Load(url);
            
            // Wait for the import to complete
            while (!importTask.IsCompleted)
            {
                // Make the loading indicator spin
                if (loadingIndicator != null)
                {
                    loadingIndicator.transform.Rotate(0, 5f, 0);
                }
                yield return null;
            }
            
            // Check if loading succeeded
            bool success = importTask.Result;
            
            if (success)
            {
                // Now instantiate the model
                var instantiateTask = gltfImport.InstantiateMainSceneAsync(transform);
                
                // Wait for instantiation to complete
                while (!instantiateTask.IsCompleted)
                {
                    yield return null;
                }
                
                success = instantiateTask.Result;
                
                if (success)
                {
                    Debug.Log("GLB model loaded successfully");
                }
                else
                {
                    Debug.LogError("Failed to instantiate GLB model");
                }
            }
            else
            {
                Debug.LogError($"Failed to load GLB model from {url}");
            }
            
            // Remove the loading indicator
            if (loadingIndicator != null)
            {
                Destroy(loadingIndicator);
            }
            
            // Notify completion
            onComplete?.Invoke(success, gameObject);
            
            // If failed, destroy self
            if (!success)
            {
                Debug.LogError("Model loading failed, destroying game object");
                Destroy(gameObject);
            }
        }
    }
}
