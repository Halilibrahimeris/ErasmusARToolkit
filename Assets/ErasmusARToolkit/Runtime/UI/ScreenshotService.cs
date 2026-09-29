using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Saves a PNG of the current view without the HUD.</summary>
    public sealed class ScreenshotService : MonoBehaviour
    {
        bool capturing;

        /// <param name="setHudVisible">Called with false before the capture and true afterwards.</param>
        public void Capture(Action<bool> setHudVisible, Action<string> onSaved, Action<string> onFailed)
        {
            if (!capturing)
            {
                StartCoroutine(CaptureRoutine(setHudVisible, onSaved, onFailed));
            }
        }

        IEnumerator CaptureRoutine(Action<bool> setHudVisible, Action<string> onSaved, Action<string> onFailed)
        {
            capturing = true;
            setHudVisible?.Invoke(false);
            yield return new WaitForEndOfFrame();

            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                var directory = ScreenshotPaths.GetDirectory(Application.isMobilePlatform, Application.dataPath,
                    Application.persistentDataPath);
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, ScreenshotPaths.CreateFileName(DateTime.Now));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Debug.Log("[Erasmus AR Toolkit] Screenshot saved: " + path);
                onSaved?.Invoke(path);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Erasmus AR Toolkit] Screenshot failed: " + exception);
                onFailed?.Invoke(exception.Message);
            }
            finally
            {
                ObjectUtility.SafeDestroy(texture);
                setHudVisible?.Invoke(true);
                capturing = false;
            }
        }
    }
}
