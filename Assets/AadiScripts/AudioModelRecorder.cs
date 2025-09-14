using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using GLTFast;

public class AudioModelRecorder : MonoBehaviour
{
    [Header("Audio Settings")]
    [SerializeField] private int recordingDuration = 10;
    [SerializeField] private int sampleRate = 44100;
    [SerializeField] private string microphoneName;
    [SerializeField] private AudioSource audioSource;

    [SerializeField] Image notificationIcon;
    [SerializeField] GameObject notificationPanel;
     
    [Header("API Settings")]
    [SerializeField] private string apiUrl = "http://localhost:8000/process-audio";
    [SerializeField] private float modelPollingInterval = 5f;
    [SerializeField] private int maxPollingAttempts = 150;
    
    [Header("UI References")]
    [SerializeField] private Button recordButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI modelUrlText;
    
    private bool isRecording = false;
    private bool canRecord = true;
    private AudioClip recordedClip;
    private string recordedFilePath;
    private string currentTaskId = null;
    private float angularVelocity = 0f;
    private RectTransform buttonRect;
    
    private void Start()
    {
        buttonRect = recordButton.GetComponent<RectTransform>();
        // Get the default microphone if not specified
        if (string.IsNullOrEmpty(microphoneName))
            microphoneName = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;

        if (microphoneName == null)
        {
            Debug.LogError("No microphone found!");
            if (statusText) statusText.text = "No microphone found!";
            if (recordButton) recordButton.interactable = false;
            return;
        }

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (recordButton)
            recordButton.onClick.AddListener(ToggleRecording);

        if (statusText)
            statusText.text = "Ready to record";
    }

    private void Update()
    {
        if (canRecord)
        {
            angularVelocity = Mathf.Max(angularVelocity - Time.deltaTime * 20f, 0f);
        }
        else
        {
            angularVelocity = Mathf.Min(angularVelocity + Time.deltaTime * 50f, (1.15f * Mathf.Sin(3f * Time.time)) * 200f);
        }
        buttonRect.Rotate(0f, 0f, angularVelocity * Time.deltaTime);
    }
    
    public void ToggleRecording()
    {
        if (isRecording)
        {
            StopRecording();
        }
        else if (canRecord)
        {
            StartRecording();
        }
    }
    
    private void StartRecording()
    {
        isRecording = true;
        if (statusText) statusText.text = "Recording...";
        
        // Start recording using the microphone
        recordedClip = Microphone.Start(microphoneName, false, recordingDuration, sampleRate);
        
        StartCoroutine(MonitorRecordingProgress());
    }
    
    private IEnumerator MonitorRecordingProgress()
    {
        int lastSample = 0;
        
        while (isRecording && Microphone.IsRecording(microphoneName))
        {
            int currentSample = Microphone.GetPosition(microphoneName);
            if (currentSample < lastSample)
            {
                // Recording has automatically stopped because it reached maximum duration
                StopRecording();
                break;
            }
            lastSample = currentSample;
            yield return null;
        }
    }
    
    private void StopRecording()
    {
        if (!isRecording) return;
        canRecord = false;
        isRecording = false;
        if (statusText) statusText.text = "Processing...";
        
        // Stop recording
        Microphone.End(microphoneName);
        
        // Save the audio clip to a temporary file
        SaveAudioClip();
        
        // Send the audio file to the API
        StartCoroutine(SendAudioToAPI());
    }
    
    private void SaveAudioClip()
    {
        // Create a temporary file path
        recordedFilePath = Path.Combine(Application.temporaryCachePath, "recorded_audio.wav");
        
        // Convert AudioClip to WAV and save to file
        SavWav.Save(recordedFilePath, recordedClip);
        
        Debug.Log($"Audio saved to: {recordedFilePath}");
    }
    
    private IEnumerator SendAudioToAPI()
    {
        if (string.IsNullOrEmpty(recordedFilePath) || !File.Exists(recordedFilePath))
        {
            Debug.LogError("No recorded audio file found!");
            if (statusText) statusText.text = "Error: No audio file!";
            yield break;
        }
        
        // Read the audio file
        byte[] audioData = File.ReadAllBytes(recordedFilePath);
        
        // Create form with audio file
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", audioData, "audio.wav", "audio/wav");
        
        using (UnityWebRequest www = UnityWebRequest.Post(apiUrl, form))
        {
            // Send the request
            yield return www.SendWebRequest();
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"API Error: {www.error}");
                if (statusText) statusText.text = "API Error: " + www.error;
            }
            else
            {
                Debug.Log("API Response received");
                
                // Parse the response
                string responseJson = www.downloadHandler.text;
                InitialResponse response = JsonUtility.FromJson<InitialResponse>(responseJson);
                
                // Handle the initial response (no audio playback)
                ProcessInitialResponse(response);
            }
        }
        
        // Clean up the temporary file
        if (File.Exists(recordedFilePath))
            File.Delete(recordedFilePath);
    }
    
    private void ProcessInitialResponse(InitialResponse response)
    {
        // Removed audio playback. We only handle model task polling.
        currentTaskId = response.task_id;
        if (!string.IsNullOrEmpty(currentTaskId))
        {
            if (statusText) statusText.text = "Generating 3D model...";
            StartCoroutine(PollForModelStatus());
        }
        else
        {
            canRecord = true;
            if (modelUrlText) modelUrlText.text = "No model requested";
            if (statusText) statusText.text = "Ready to record";
        }
    }
    
    private IEnumerator PollForModelStatus()
    {
        if (string.IsNullOrEmpty(currentTaskId))
            yield break;
            
        string statusUrl = $"http://localhost:8000/model-status/{currentTaskId}";
        int attempts = 0;
        
        while (attempts < maxPollingAttempts)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(statusUrl))
            {
                yield return www.SendWebRequest();
                
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Status check error: {www.error}");
                    if (statusText) statusText.text = "Error checking model status";
                    yield break;
                }
                
                string responseJson = www.downloadHandler.text;
                ModelStatusResponse status = JsonUtility.FromJson<ModelStatusResponse>(responseJson);
                
                if (status.status == "completed" && !string.IsNullOrEmpty(status.model_url))
                {
                    // Model is ready!
                    if (modelUrlText) modelUrlText.text = status.model_url;
                    if (statusText) statusText.text = "3D Model ready!";
                    canRecord = true;
                    
                    // Create a new VRItem and add it to the ShopifyProductFetcher's Items list
                    if (ShopifyProductFetcher.Instance != null)
                    {
                        // Create a unique ID for this item
                        string itemId = "audio_model_" + DateTime.Now.Ticks;
                        
                        // Create the VRItem
                        ShopifyProductFetcher.VRItem newItem = new ShopifyProductFetcher.VRItem
                        {
                            id = itemId,
                            title = "Voice Generated Model",
                            description = "", // Empty description as requested
                            modelUrl = status.model_url,
                            cost = 0,
                            isLoading = true
                        };
                        
                        // Add to items list
                        ShopifyProductFetcher.Instance.Items.Add(newItem);
                        int itemIndex = ShopifyProductFetcher.Instance.Items.Count - 1;
                        
                        // Download the thumbnail if available
                        if (!string.IsNullOrEmpty(status.thumbnail_url))
                        {
                            StartCoroutine(DownloadThumbnailImage(status.thumbnail_url, itemIndex));
                        }
                        
                        // Position to instantiate model (far from origin)
                        Vector3 farPosition = new Vector3(1000, 0, 50 + (itemIndex * 5));
                        
                        // Start coroutine to download and instantiate the model
                        StartCoroutine(InstantiateModelAtPosition(status.model_url, itemIndex, farPosition));
                        
                        notificationPanel.SetActive(true);
                        Debug.Log("SHOWING NOTIFICATION");
                        Debug.Log($"Added voice-generated model to item list: {status.model_url}");
                    }
                    else
                    {
                        Debug.LogWarning("ShopifyProductFetcher instance not found. Cannot add model to items list.");
                    }

                    yield break;
                }
                else if (status.status == "failed")
                {
                    if (statusText) statusText.text = "Model generation failed";
                    Debug.LogError($"Model generation failed: {status.error}");
                    canRecord = true;

                    yield break;
                }
                
                // Still processing, wait and try again
                attempts++;
                yield return new WaitForSeconds(modelPollingInterval);
            }
        }
        
        // Timed out
        if (statusText) statusText.text = "Model generation timed out";
        Debug.LogWarning("Model generation polling timed out");
    }

    // Class for the initial API response (no audio_content)
    [Serializable]
    private class InitialResponse
    {
        public string task_id;
        public string message;
    }
    
    // Class for the model status response
    [Serializable]
    private class ModelStatusResponse
    {
        public string status;
        public string model_url;
        public string thumbnail_url;
        public string error;
    }
    
    public void CloseNotification()
    {
        StartCoroutine(CloseNotificationCoroutine());
    }

    private IEnumerator CloseNotificationCoroutine()
    {
        notificationPanel.GetComponent<Animator>().SetTrigger("Disappear");
        yield return new WaitForSeconds(1f);
        notificationPanel.SetActive(false);
    }

    // Helper method to download thumbnail image for the item
    private IEnumerator DownloadThumbnailImage(string imageUrl, int itemIndex)
    {
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                if (texture != null && ShopifyProductFetcher.Instance != null &&
                    itemIndex < ShopifyProductFetcher.Instance.Items.Count)
                {
                    // Update the image in the VRItem
                    ShopifyProductFetcher.VRItem item = ShopifyProductFetcher.Instance.Items[itemIndex];
                    item.image = texture;
                    notificationIcon.sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                    ShopifyProductFetcher.Instance.Items[itemIndex] = item;

                    Debug.Log($"Downloaded thumbnail for voice-generated model");

                    // If StoreUI is available, add item to UI
                    if (ShopifyProductFetcher.Instance.GetComponent<ShopifyProductFetcher>().storeUI != null)
                    {
                        ShopifyProductFetcher.Instance.GetComponent<ShopifyProductFetcher>().storeUI.AddItem(item);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"Failed to download thumbnail image: {www.error}");
            }
        }
    }

    // Helper method to instantiate the model at a specific position
    private IEnumerator InstantiateModelAtPosition(string modelUrl, int itemIndex, Vector3 position)
    {
        if (ShopifyProductFetcher.Instance == null)
            yield break;
            
        GameObject modelParent = new GameObject($"VoiceModel_{itemIndex}");
        modelParent.transform.position = position;
        
        // Use GLTFast to load the model
        var gltfImport = new GLTFast.GltfImport();
        
        // Load the glTF
        bool success = false;
        var loadTask = gltfImport.Load(modelUrl);
        while (true)
        {
            try
            {
                if (loadTask.IsCompleted)
                {
                    success = loadTask.Result;
                    break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading model: {e.Message}");
                success = false;
                break;
            }
            yield return null;
        }
        
        if (success)
        {
            // Create a child GameObject for proper scaling
            GameObject modelRoot = new GameObject("ModelRoot");
            modelRoot.transform.SetParent(modelParent.transform);
            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localScale = Vector3.one * 0.5f; // Similar scale to ShopifyProductFetcher
            
            // Instantiate the model
            var instantiateTask = gltfImport.InstantiateMainSceneAsync(modelRoot.transform);
            while (!instantiateTask.IsCompleted)
                yield return null;
                
            if (instantiateTask.Result && itemIndex < ShopifyProductFetcher.Instance.Items.Count)
            {
                // Update the VRItem with the instantiated model
                ShopifyProductFetcher.VRItem item = ShopifyProductFetcher.Instance.Items[itemIndex];
                item.instantiatedModel = modelParent;
                item.isLoading = false;
                ShopifyProductFetcher.Instance.Items[itemIndex] = item;
                
                // Hide the model (it's just a template)
                modelParent.SetActive(false);
                
                Debug.Log($"Successfully instantiated voice-generated model");
            }
            else
            {
                Debug.LogError("Failed to instantiate model");
                Destroy(modelParent);
            }
        }
        else
        {
            Debug.LogError($"Failed to load model from {modelUrl}");
            Destroy(modelParent);
        }
    }
}

// Helper class to save AudioClip as WAV file
public static class SavWav
{
    const int HEADER_SIZE = 44;
    
    public static bool Save(string filepath, AudioClip clip)
    {
        if (!filepath.ToLower().EndsWith(".wav"))
        {
            filepath = filepath + ".wav";
        }
        
        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        
        Int16[] intData = new Int16[samples.Length];
        
        Byte[] bytesData = new Byte[samples.Length * 2 + HEADER_SIZE];
        
        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * 32767);
            byte[] byteArr = BitConverter.GetBytes(intData[i]);
            byteArr.CopyTo(bytesData, i * 2 + HEADER_SIZE);
        }
        
        WriteWavHeader(bytesData, clip);
        
        try
        {
            using (var fileStream = new FileStream(filepath, FileMode.Create))
            {
                fileStream.Write(bytesData, 0, bytesData.Length);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error saving WAV: {e.Message}");
            return false;
        }
    }
    
    private static void WriteWavHeader(byte[] data, AudioClip clip)
    {
        var hz = clip.frequency;
        var channels = clip.channels;
        var samples = clip.samples;
        
        byte[] riff = System.Text.Encoding.UTF8.GetBytes("RIFF");
        byte[] wave = System.Text.Encoding.UTF8.GetBytes("WAVE");
        byte[] fmt = System.Text.Encoding.UTF8.GetBytes("fmt ");
        byte[] dataStr = System.Text.Encoding.UTF8.GetBytes("data");
        
        var fileSize = samples * 2 * channels + HEADER_SIZE - 8;
        var fmtSize = 16;
        var dataSize = samples * 2 * channels;
        
        Array.Copy(riff, 0, data, 0, 4);
        Array.Copy(BitConverter.GetBytes(fileSize), 0, data, 4, 4);
        Array.Copy(wave, 0, data, 8, 4);
        Array.Copy(fmt, 0, data, 12, 4);
        Array.Copy(BitConverter.GetBytes(fmtSize), 0, data, 16, 4);
        Array.Copy(BitConverter.GetBytes((ushort)1), 0, data, 20, 2); // Audio format (1 = PCM)
        Array.Copy(BitConverter.GetBytes((ushort)channels), 0, data, 22, 2); // Channels
        Array.Copy(BitConverter.GetBytes(hz), 0, data, 24, 4); // Sample rate
        Array.Copy(BitConverter.GetBytes(hz * channels * 2), 0, data, 28, 4); // Byte rate
        Array.Copy(BitConverter.GetBytes((ushort)(channels * 2)), 0, data, 32, 2); // Block align
        Array.Copy(BitConverter.GetBytes((ushort)16), 0, data, 34, 2); // Bits per sample
        Array.Copy(dataStr, 0, data, 36, 4);
        Array.Copy(BitConverter.GetBytes(dataSize), 0, data, 40, 4);
    }
}