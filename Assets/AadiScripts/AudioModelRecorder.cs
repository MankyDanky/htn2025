using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ModelGeneratorAI : MonoBehaviour
{
    [Header("API Keys")]
    [SerializeField] private string openAIApiKey = "YOUR_OPENAI_API_KEY";
    [SerializeField] private string meshyApiKey = "YOUR_MESHY_API_KEY";

    [Header("Audio Settings")]
    [SerializeField] private int recordingDuration = 10;
    [SerializeField] private int sampleRate = 44100;
    [SerializeField] private string microphoneName;
    [SerializeField] private AudioSource audioSource;
    
    [Header("3D Model Settings")]
    [SerializeField] private float modelPollingInterval = 3f;
    [SerializeField] private int maxModelPollingAttempts = 60;
    
    [Header("UI References")]
    [SerializeField] private Button recordButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI modelUrlText;
    
    [Header("Debug")]
    [SerializeField] private bool debugMode = true;

    private bool isRecording = false;
    private AudioClip recordedClip;
    private string recordedFilePath;
    private Dictionary<string, object> modelTasks = new Dictionary<string, object>();
    
    void Start()
    {
        ValidateSettings();
        InitializeUI();
    }
    
    private void ValidateSettings()
    {
        // Validate API keys
        if (string.IsNullOrEmpty(openAIApiKey))
        {
            LogError("OpenAI API key is missing! Set it in the inspector.");
            enabled = false;
            return;
        }
        
        if (string.IsNullOrEmpty(meshyApiKey))
        {
            LogError("Meshy API key is missing! Set it in the inspector.");
            enabled = false;
            return;
        }
        
        // Get default microphone if not specified
        if (string.IsNullOrEmpty(microphoneName))
            microphoneName = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;
        
        if (microphoneName == null)
        {
            LogError("No microphone found!");
            enabled = false;
            return;
        }
        
        // Create audio source if needed
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
            
        LogDebug("Settings validated successfully");
    }
    
    private void InitializeUI()
    {
        if (recordButton)
            recordButton.onClick.AddListener(ToggleRecording);
            
        if (statusText)
            statusText.text = "Ready to record";
            
        LogDebug("UI initialized");
    }
    
    public void ToggleRecording()
    {
        if (isRecording)
            StopRecording();
        else
            StartRecording();
    }
    
    private void StartRecording()
    {
        isRecording = true;
        if (statusText) statusText.text = "Recording...";
        
        // Start recording using the microphone
        recordedClip = Microphone.Start(microphoneName, false, recordingDuration, sampleRate);
        LogDebug($"Started recording with microphone: {microphoneName}, duration: {recordingDuration}s");
        
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
        
        isRecording = false;
        if (statusText) statusText.text = "Processing...";
        
        // Stop recording
        Microphone.End(microphoneName);
        LogDebug("Recording stopped");
        
        // Save the audio clip to a temporary file
        SaveAudioClip();
        
        // Process the audio (transcribe, call OpenAI, etc)
        StartCoroutine(ProcessAudioFlow());
    }
    
    private void SaveAudioClip()
    {
        // Create a temporary file path
        recordedFilePath = Path.Combine(Application.temporaryCachePath, "recorded_audio.wav");
        
        // Convert AudioClip to WAV and save to file
        SavWav.Save(recordedFilePath, recordedClip);
        
        LogDebug($"Audio saved to: {recordedFilePath}");
    }
    
    private IEnumerator ProcessAudioFlow()
    {
        // 1. Transcribe the audio with Whisper
        yield return StartCoroutine(TranscribeAudio((transcription) => {
            if (string.IsNullOrEmpty(transcription))
            {
                LogError("Failed to transcribe audio");
                if (statusText) statusText.text = "Transcription failed";
                return;
            }
            
            LogDebug($"Transcription: \"{transcription}\"");
            
            // 2. Send to GPT for processing and tool calling
            StartCoroutine(ProcessWithGPT(transcription));
        }));
    }
    
    private IEnumerator TranscribeAudio(Action<string> onComplete)
    {
        if (string.IsNullOrEmpty(recordedFilePath) || !File.Exists(recordedFilePath))
        {
            LogError("No recorded audio file found!");
            onComplete?.Invoke(null);
            yield break;
        }
        
        LogDebug("Transcribing audio with Whisper API...");
        
        // Read the audio file
        byte[] audioData = File.ReadAllBytes(recordedFilePath);
        
        // Create form with audio file
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", audioData, "audio.wav", "audio/wav");
        form.AddField("model", "whisper-1");
        form.AddField("response_format", "json");
        
        // Create the request
        using (UnityWebRequest www = UnityWebRequest.Post("https://api.openai.com/v1/audio/transcriptions", form))
        {
            www.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);
            
            // Send request
            yield return www.SendWebRequest();
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError($"Whisper API Error: {www.error}");
                onComplete?.Invoke(null);
            }
            else
            {
                // Parse the response
                string responseJson = www.downloadHandler.text;
                LogDebug("Whisper response: " + responseJson);
                
                // Extract the transcription from the JSON response
                try
                {
                    // Parse as JObject to handle complex structure
                    JObject transcriptionData = JObject.Parse(responseJson);
                    string transcription = transcriptionData["text"]?.ToString();
                    
                    if (string.IsNullOrEmpty(transcription))
                    {
                        LogError("No text found in transcription response");
                        onComplete?.Invoke(null);
                    }
                    else
                    {
                        LogDebug($"Successfully extracted transcription: \"{transcription}\"");
                        onComplete?.Invoke(transcription);
                    }
                }
                catch (Exception e)
                {
                    LogError($"Error parsing transcription: {e.Message}");
                    onComplete?.Invoke(null);
                }
            }
        }
    }
    
    private IEnumerator ProcessWithGPT(string userMessage)
    {
        LogDebug("Sending to GPT for processing...");
        if (statusText) statusText.text = "AI processing...";
        
        // Create the GPT request with tool calling
        var requestData = new
        {
            model = "gpt-4",
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are an assistant that helps create 3D models. If the user is describing or requesting a 3D model, use the generate_3d_model function. Otherwise, respond normally."
                },
                new
                {
                    role = "user",
                    content = userMessage
                }
            },
            tools = new[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "generate_3d_model",
                        description = "Generates a 3D model based on the user's description",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                description = new
                                {
                                    type = "string",
                                    description = "Detailed description of the 3D model to generate"
                                }
                            },
                            required = new[] { "description" }
                        }
                    }
                }
            },
            tool_choice = "auto"
        };
        
        // Convert to JSON
        string jsonRequestData = JsonConvert.SerializeObject(requestData);
        LogDebug("GPT Request: " + jsonRequestData);
        
        // Create request
        using (UnityWebRequest www = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonRequestData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);
            
            // Send request
            yield return www.SendWebRequest();
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError($"GPT API Error: {www.error}");
                if (statusText) statusText.text = "AI processing failed";
            }
            else
            {
                // Parse the response
                string responseJson = www.downloadHandler.text;
                LogDebug("GPT Response: " + responseJson);
                
                try
                {
                    // Parse GPT response
                    JObject responseData = JObject.Parse(responseJson);
                    JToken choiceToken = responseData["choices"]?[0];
                    JToken messageToken = choiceToken?["message"];
                    
                    string responseText = messageToken?["content"]?.ToString();
                    JToken toolCalls = messageToken?["tool_calls"];
                    
                    if (toolCalls != null && toolCalls.HasValues)
                    {
                        // Tool call was used - extract model description
                        string toolCallId = toolCalls[0]["id"].ToString();
                        string functionName = toolCalls[0]["function"]["name"].ToString();
                        string functionArgs = toolCalls[0]["function"]["arguments"].ToString();
                        
                        if (functionName == "generate_3d_model")
                        {
                            var args = JObject.Parse(functionArgs);
                            string modelDescription = args["description"].ToString();
                            
                            // Generate TTS response immediately
                            string ttsResponse = "I'm generating your 3D model. This might take a minute or two, but I'll have it ready for you soon!";
                            StartCoroutine(GenerateTTS(ttsResponse));
                            
                            // Start model generation in background
                            StartCoroutine(Generate3DModel(modelDescription));
                        }
                    }
                    else if (!string.IsNullOrEmpty(responseText))
                    {
                        // Regular text response - convert to speech
                        StartCoroutine(GenerateTTS(responseText));
                    }
                }
                catch (Exception e)
                {
                    LogError($"Error parsing GPT response: {e.Message}");
                    if (statusText) statusText.text = "Error processing response";
                }
            }
        }
    }
    
    private IEnumerator GenerateTTS(string text)
    {
        LogDebug($"Generating TTS for: \"{text}\"");
        if (statusText) statusText.text = "Generating speech...";
        
        // Create TTS request - using MP3 format for better compatibility
        var requestData = new
        {
            model = "tts-1",
            voice = "alloy",
            input = text,
            response_format = "mp3"
        };
        
        string jsonRequestData = JsonConvert.SerializeObject(requestData);
        LogDebug($"TTS request: {jsonRequestData}");
        
        using (UnityWebRequest www = new UnityWebRequest("https://api.openai.com/v1/audio/speech", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonRequestData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);
            
            // Send request
            yield return www.SendWebRequest();
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError($"TTS API Error: {www.error}");
                if (statusText) statusText.text = "Speech generation failed";
            }
            else
            {
                LogDebug("TTS response received successfully");
                
                // Get MP3 audio data
                byte[] audioBytes = www.downloadHandler.data;
                LogDebug($"Received {audioBytes.Length} bytes of MP3 audio data");
                
                // Save TTS response to persistent location for debugging
                string ttsFilePath = Path.Combine(Application.dataPath, "../", "tts_response.mp3");
                try
                {
                    File.WriteAllBytes(ttsFilePath, audioBytes);
                    LogDebug($"TTS response saved to: {ttsFilePath}");
                }
                catch (Exception e)
                {
                    LogError($"Failed to save TTS response: {e.Message}");
                }
                
                // Use Unity's built-in method to load MP3 data as AudioClip
                StartCoroutine(LoadAudioClipFromMP3(audioBytes));
            }
        }
    }
    
    private IEnumerator LoadAudioClipFromMP3(byte[] audioData)
    {
        LogDebug($"Loading AudioClip from {audioData.Length} bytes of MP3 data");
        
        // Validate audio data first
        if (audioData == null || audioData.Length == 0)
        {
            LogError("Audio data is null or empty");
            if (statusText && statusText.text != "Generating 3D model...")
                statusText.text = "No audio data received";
            yield break;
        }
        
        // Check if it looks like MP3 data (MP3 files typically start with ID3 tag or MP3 frame header)
        if (audioData.Length > 3)
        {
            string header = System.Text.Encoding.ASCII.GetString(audioData, 0, Math.Min(3, audioData.Length));
            LogDebug($"Audio data header: {header} (hex: {BitConverter.ToString(audioData, 0, Math.Min(10, audioData.Length))})");
        }
        
        string tempPath = null;
        UnityWebRequest audioRequest = null;
        
        try
        {
            // Save MP3 data to temporary file in a better location
            tempPath = Path.Combine(Application.dataPath, "../", "tts_temp_debug.mp3");
            File.WriteAllBytes(tempPath, audioData);
            LogDebug($"Saved MP3 to temp file: {tempPath}");
            
            // Verify file was written correctly
            if (File.Exists(tempPath))
            {
                FileInfo fileInfo = new FileInfo(tempPath);
                LogDebug($"Temp file created successfully: {fileInfo.Length} bytes");
            }
            else
            {
                LogError("Failed to create temp file");
                yield break;
            }
            
            // Use Unity's AudioClip loader with proper file URI
            string fileUri = "file://" + tempPath.Replace("\\", "/");
            LogDebug($"Loading audio from URI: {fileUri}");
            
            audioRequest = UnityWebRequestMultimedia.GetAudioClip(fileUri, AudioType.MPEG);
        }
        catch (Exception e)
        {
            LogError($"Error setting up MP3 loading: {e.Message}\n{e.StackTrace}");
            if (statusText && statusText.text != "Generating 3D model...")
                statusText.text = "Audio setup failed";
            yield break;
        }
        
        // Send the request outside the try block to avoid yield issues
        LogDebug("Sending UnityWebRequest to load audio...");
        yield return audioRequest.SendWebRequest();
        LogDebug($"UnityWebRequest completed with result: {audioRequest.result}");
        
        try
        {
            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                LogError($"Failed to load audio clip: {audioRequest.error}");
                LogError($"Response code: {audioRequest.responseCode}");
                if (statusText && statusText.text != "Generating 3D model...")
                    statusText.text = "Failed to load speech";
            }
            else
            {
                AudioClip audioClip = DownloadHandlerAudioClip.GetContent(audioRequest);
                
                if (audioClip != null && audioClip.length > 0)
                {
                    LogDebug($"Successfully created AudioClip: {audioClip.length:F2}s, {audioClip.frequency}Hz, {audioClip.channels} channels, {audioClip.samples} samples");
                    
                    // Verify AudioSource is ready
                    if (audioSource == null)
                    {
                        LogError("AudioSource is null!");
                        yield break;
                    }
                    
                    audioSource.clip = audioClip;
                    audioSource.volume = 1.0f; // Ensure volume is set
                    audioSource.Play();
                    
                    LogDebug($"AudioSource playing: {audioSource.isPlaying}, Volume: {audioSource.volume}");
                    
                    // Update status only if we're not generating a model
                    if (statusText && statusText.text != "Generating 3D model...")
                        statusText.text = "Playing response";
                }
                else
                {
                    LogError($"Created AudioClip is null or has zero length. AudioClip: {audioClip}, Length: {(audioClip != null ? audioClip.length.ToString() : "null")}");
                    if (statusText && statusText.text != "Generating 3D model...")
                        statusText.text = "Failed to create speech";
                }
            }
        }
        catch (Exception e)
        {
            LogError($"Error processing MP3 audio: {e.Message}\n{e.StackTrace}");
            if (statusText && statusText.text != "Generating 3D model...")
                statusText.text = "Audio processing failed";
        }
        finally
        {
            // Clean up resources
            if (audioRequest != null)
                audioRequest.Dispose();
                
            // Don't delete the temp file immediately for debugging purposes
            // Comment out the deletion for now
            /*
            if (!string.IsNullOrEmpty(tempPath))
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                    LogDebug("Cleaned up temp MP3 file");
                }
                catch (Exception e)
                {
                    LogDebug($"Could not delete temp file: {e.Message}");
                }
            }
            */
            LogDebug($"Keeping temp file for debugging: {tempPath}");
        }
    }
    
    private IEnumerator GenerateTTSWAV(string text)
    {
        LogDebug($"Generating TTS (WAV format) for: \"{text}\"");
        if (statusText) statusText.text = "Generating speech (WAV)...";
        
        // Create TTS request - using WAV format as fallback
        var requestData = new
        {
            model = "tts-1",
            voice = "alloy",
            input = text,
            response_format = "wav"
        };
        
        string jsonRequestData = JsonConvert.SerializeObject(requestData);
        LogDebug($"TTS WAV request: {jsonRequestData}");
        
        using (UnityWebRequest www = new UnityWebRequest("https://api.openai.com/v1/audio/speech", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonRequestData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);
            
            // Send request
            yield return www.SendWebRequest();
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError($"TTS WAV API Error: {www.error}");
                if (statusText) statusText.text = "WAV speech generation failed";
            }
            else
            {
                LogDebug("TTS WAV response received successfully");
                
                // Get WAV audio data
                byte[] audioBytes = www.downloadHandler.data;
                LogDebug($"Received {audioBytes.Length} bytes of WAV audio data");
                
                // Save TTS response to persistent location for debugging
                string ttsFilePath = Path.Combine(Application.dataPath, "../", "tts_response.wav");
                try
                {
                    File.WriteAllBytes(ttsFilePath, audioBytes);
                    LogDebug($"TTS WAV response saved to: {ttsFilePath}");
                }
                catch (Exception e)
                {
                    LogError($"Failed to save TTS WAV response: {e.Message}");
                }
                
                // Use Unity's built-in method to load WAV data as AudioClip
                StartCoroutine(LoadAudioClipFromWAV(audioBytes));
            }
        }
    }
    
    private IEnumerator LoadAudioClipFromWAV(byte[] audioData)
    {
        LogDebug($"Loading AudioClip from {audioData.Length} bytes of WAV data");
        
        // Validate audio data first
        if (audioData == null || audioData.Length == 0)
        {
            LogError("WAV audio data is null or empty");
            if (statusText && statusText.text != "Generating 3D model...")
                statusText.text = "No WAV audio data received";
            yield break;
        }
        
        string tempPath = null;
        UnityWebRequest audioRequest = null;
        
        try
        {
            // Save WAV data to temporary file
            tempPath = Path.Combine(Application.dataPath, "../", "tts_temp_debug.wav");
            File.WriteAllBytes(tempPath, audioData);
            LogDebug($"Saved WAV to temp file: {tempPath}");
            
            // Use Unity's AudioClip loader with WAV format
            string fileUri = "file://" + tempPath.Replace("\\", "/");
            LogDebug($"Loading WAV audio from URI: {fileUri}");
            
            audioRequest = UnityWebRequestMultimedia.GetAudioClip(fileUri, AudioType.WAV);
        }
        catch (Exception e)
        {
            LogError($"Error setting up WAV loading: {e.Message}\n{e.StackTrace}");
            if (statusText && statusText.text != "Generating 3D model...")
                statusText.text = "WAV audio setup failed";
            yield break;
        }
        
        // Send the request
        LogDebug("Sending UnityWebRequest to load WAV audio...");
        yield return audioRequest.SendWebRequest();
        LogDebug($"WAV UnityWebRequest completed with result: {audioRequest.result}");
        
        try
        {
            if (audioRequest.result != UnityWebRequest.Result.Success)
            {
                LogError($"Failed to load WAV audio clip: {audioRequest.error}");
                LogError($"WAV Response code: {audioRequest.responseCode}");
                if (statusText && statusText.text != "Generating 3D model...")
                    statusText.text = "Failed to load WAV speech";
            }
            else
            {
                AudioClip audioClip = DownloadHandlerAudioClip.GetContent(audioRequest);
                
                if (audioClip != null && audioClip.length > 0)
                {
                    LogDebug($"Successfully created WAV AudioClip: {audioClip.length:F2}s, {audioClip.frequency}Hz, {audioClip.channels} channels, {audioClip.samples} samples");
                    
                    if (audioSource == null)
                    {
                        LogError("AudioSource is null!");
                        yield break;
                    }
                    
                    audioSource.clip = audioClip;
                    audioSource.volume = 1.0f;
                    audioSource.Play();
                    
                    LogDebug($"WAV AudioSource playing: {audioSource.isPlaying}, Volume: {audioSource.volume}");
                    
                    if (statusText && statusText.text != "Generating 3D model...")
                        statusText.text = "Playing WAV response";
                }
                else
                {
                    LogError($"Created WAV AudioClip is null or has zero length. AudioClip: {audioClip}, Length: {(audioClip != null ? audioClip.length.ToString() : "null")}");
                    if (statusText && statusText.text != "Generating 3D model...")
                        statusText.text = "Failed to create WAV speech";
                }
            }
        }
        catch (Exception e)
        {
            LogError($"Error processing WAV audio: {e.Message}\n{e.StackTrace}");
            if (statusText && statusText.text != "Generating 3D model...")
                statusText.text = "WAV audio processing failed";
        }
        finally
        {
            if (audioRequest != null)
                audioRequest.Dispose();
            LogDebug($"Keeping WAV temp file for debugging: {tempPath}");
        }
    }

    private IEnumerator Generate3DModel(string description)
    {
        LogDebug($"Generating 3D model: \"{description}\"");
        if (statusText) statusText.text = "Generating 3D model...";
        
        // 1. Create preview task
        yield return StartCoroutine(CreateMeshyPreviewTask(description, (previewTaskId) => {
            if (string.IsNullOrEmpty(previewTaskId))
            {
                LogError("Failed to create Meshy preview task");
                if (statusText) statusText.text = "Model generation failed";
                return;
            }
            
            LogDebug($"Meshy preview task created: {previewTaskId}");
            
            // 2. Poll preview task until completed
            StartCoroutine(PollMeshyTask(previewTaskId, "preview", (previewTask) => {
                if (previewTask == null || previewTask.status != "SUCCEEDED")
                {
                    LogError("Meshy preview task failed");
                    if (statusText) statusText.text = "Model preview failed";
                    return;
                }
                
                // 3. Create refine task
                StartCoroutine(CreateMeshyRefineTask(previewTaskId, (refineTaskId) => {
                    if (string.IsNullOrEmpty(refineTaskId))
                    {
                        LogError("Failed to create Meshy refine task");
                        if (statusText) statusText.text = "Model refine failed";
                        return;
                    }
                    
                    LogDebug($"Meshy refine task created: {refineTaskId}");
                    
                    // 4. Poll refine task until completed
                    StartCoroutine(PollMeshyTask(refineTaskId, "refine", (refineTask) => {
                        if (refineTask == null || refineTask.status != "SUCCEEDED")
                        {
                            LogError("Meshy refine task failed");
                            if (statusText) statusText.text = "Model refine failed";
                            return;
                        }
                        
                        // 5. Get the model URL from the refine task
                        string modelUrl = refineTask.model_urls?.glb;
                        if (string.IsNullOrEmpty(modelUrl))
                        {
                            LogError("No model URL in refine task response");
                            if (statusText) statusText.text = "Model URL missing";
                            return;
                        }
                        
                        // Success!
                        if (modelUrlText) modelUrlText.text = modelUrl;
                        if (statusText) statusText.text = "3D Model ready!";
                        LogDebug($"3D model ready: {modelUrl}");
                        
                        // Optionally, here you could download and load the model
                    }));
                }));
            }));
        }));
    }
    
    private IEnumerator CreateMeshyPreviewTask(string description, Action<string> onComplete)
    {
        var requestData = new
        {
            mode = "preview",
            prompt = description,
            art_style = "realistic",
            should_remesh = true
        };
        
        string jsonRequestData = JsonConvert.SerializeObject(requestData);
        
        using (UnityWebRequest www = new UnityWebRequest("https://api.meshy.ai/openapi/v2/text-to-3d", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonRequestData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + meshyApiKey);
            
            yield return www.SendWebRequest();
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError($"Meshy API Error (preview): {www.error}");
                onComplete?.Invoke(null);
            }
            else
            {
                string responseJson = www.downloadHandler.text;
                LogDebug("Meshy preview response: " + responseJson);
                
                try
                {
                    var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseJson);
                    string taskId = responseData.ContainsKey("result") ? responseData["result"] : null;
                    onComplete?.Invoke(taskId);
                }
                catch (Exception e)
                {
                    LogError($"Error parsing Meshy response: {e.Message}");
                    onComplete?.Invoke(null);
                }
            }
        }
    }
    
    private IEnumerator CreateMeshyRefineTask(string previewTaskId, Action<string> onComplete)
    {
        var requestData = new
        {
            mode = "refine",
            preview_task_id = previewTaskId,
            enable_pbr = true
        };
        
        string jsonRequestData = JsonConvert.SerializeObject(requestData);
        
        using (UnityWebRequest www = new UnityWebRequest("https://api.meshy.ai/openapi/v2/text-to-3d", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonRequestData);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + meshyApiKey);
            
            yield return www.SendWebRequest();
            
            if (www.result != UnityWebRequest.Result.Success)
            {
                LogError($"Meshy API Error (refine): {www.error}");
                onComplete?.Invoke(null);
            }
            else
            {
                string responseJson = www.downloadHandler.text;
                LogDebug("Meshy refine response: " + responseJson);
                
                try
                {
                    var responseData = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseJson);
                    string taskId = responseData.ContainsKey("result") ? responseData["result"] : null;
                    onComplete?.Invoke(taskId);
                }
                catch (Exception e)
                {
                    LogError($"Error parsing Meshy response: {e.Message}");
                    onComplete?.Invoke(null);
                }
            }
        }
    }
    
    [Serializable]
    public class MeshyTaskResult
    {
        public string status;
        public ModelUrls model_urls;
        public string thumbnail_url;
        
        [Serializable]
        public class ModelUrls
        {
            public string glb;
            public string usdz;
        }
    }
    
    private IEnumerator PollMeshyTask(string taskId, string taskType, Action<MeshyTaskResult> onComplete)
    {
        int attempts = 0;
        string url = $"https://api.meshy.ai/openapi/v2/text-to-3d/{taskId}";
        
        while (attempts < maxModelPollingAttempts)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", "Bearer " + meshyApiKey);
                
                yield return www.SendWebRequest();
                
                if (www.result != UnityWebRequest.Result.Success)
                {
                    LogError($"Meshy API Error (polling {taskType}): {www.error}");
                    attempts++;
                    yield return new WaitForSeconds(modelPollingInterval);
                    continue;
                }
                
                string responseJson = www.downloadHandler.text;
                LogDebug($"Meshy {taskType} polling response: {responseJson}");
                
                try
                {
                    var taskResult = JsonConvert.DeserializeObject<MeshyTaskResult>(responseJson);
                    
                    if (taskResult.status == "SUCCEEDED")
                    {
                        LogDebug($"Meshy {taskType} task completed successfully");
                        onComplete?.Invoke(taskResult);
                        yield break;
                    }
                    else if (taskResult.status == "FAILED")
                    {
                        LogError($"Meshy {taskType} task failed");
                        onComplete?.Invoke(taskResult);
                        yield break;
                    }
                    // else still in progress
                }
                catch (Exception e)
                {
                    LogError($"Error parsing Meshy task status: {e.Message}");
                }
            }
            
            attempts++;
            yield return new WaitForSeconds(modelPollingInterval);
        }
        
        // Timed out
        LogError($"Meshy {taskType} task timed out");
        onComplete?.Invoke(null);
    }
    
    private void LogDebug(string message)
    {
        if (debugMode)
            Debug.Log($"[ModelGeneratorAI] {message}");
    }
    
    private void LogError(string message)
    {
        Debug.LogError($"[ModelGeneratorAI] {message}");
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