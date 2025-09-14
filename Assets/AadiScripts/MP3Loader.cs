using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class Mp3Loader : MonoBehaviour
{
    public AudioSource target;

    public IEnumerator PlayFromBytes(byte[] mp3Bytes, bool deleteAfter = true)
    {
        if (mp3Bytes == null || mp3Bytes.Length == 0) { Debug.LogError("mp3Bytes empty"); yield break; }

        var tempPath = Path.Combine(Application.persistentDataPath, $"rt_{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(tempPath, mp3Bytes);
        Debug.Log($"Wrote MP3: {tempPath} ({mp3Bytes.Length} bytes)");

        // Build correct file:// URL (Windows needs the extra slash)
        var url = (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
            ? $"file:///{tempPath.Replace("\\", "/")}"
            : $"file://{tempPath}";

        using var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
        var dh = (DownloadHandlerAudioClip)req.downloadHandler;
        // Important: fully decode so we can safely delete the temp file immediately after
        dh.streamAudio = false;

        yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
        if (req.result != UnityWebRequest.Result.Success)
#else
        if (req.isNetworkError || req.isHttpError)
#endif
        {
            Debug.LogError($"MP3 load error: {req.error} (url={url})");
            yield break;
        }

        var clip = DownloadHandlerAudioClip.GetContent(req);
        if (clip == null) { Debug.LogError("Decoded clip is null"); yield break; }

        Debug.Log($"Decoded MP3 → clip: len={clip.length:F3}s, freq={clip.frequency}, channels={clip.channels}, samples={clip.samples}");

        // Optional: verify the clip actually has non-zero data
        var inspect = new float[Mathf.Min(clip.samples * clip.channels, 48000)];
        if (inspect.Length > 0)
        {
            clip.GetData(inspect, 0);
            bool anyNonZero = false;
            for (int i = 0; i < inspect.Length; i++) { if (inspect[i] != 0f) { anyNonZero = true; break; } }
            Debug.Log(anyNonZero ? "PCM check: non-zero samples present ✅" : "PCM check: all zeros ❌");
        }

        target.spatialBlend = 0f;  // make 2D to avoid distance attenuation during debugging
        target.volume = 1f;
        target.clip = clip;
        target.Play();
        Debug.Log("AudioSource.Play() called.");

        // Wait a frame to ensure file handles are released; then delete
        yield return null;
        if (deleteAfter)
        {
            try { File.Delete(tempPath); Debug.Log("Temp MP3 deleted."); }
            catch (Exception e) { Debug.LogWarning($"Could not delete temp: {e.Message}"); }
        }
    }
}
