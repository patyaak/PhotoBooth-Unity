using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Unity.CoordinateSystem;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Mediapipe.Unity.Tutorial
{
    public class FaceLandmarkerRunner : MonoBehaviour
    {
        [SerializeField] private RawImage screen;
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private int fps;

        [SerializeField] private TextAsset modelAsset;

        private WebCamTexture webCamTexture;

        // Eye and iris landmarks
        private int[] leftEyeIndices = { 33, 133, 160, 159, 158, 157, 173, 144, 145, 153, 154, 155 };

        private int[] rightEyeIndices = { 362, 263, 387, 386, 385, 384, 398, 373, 374, 380, 381, 382 };
        private int[] leftIrisIndices = { 468, 469, 470, 471, 472 };
        private int[] rightIrisIndices = { 473, 474, 475, 476, 477 };

        private IEnumerator Start()
        {
            if (WebCamTexture.devices.Length == 0)
                throw new System.Exception("Web Camera devices are not found");

            var webCamDevice = WebCamTexture.devices[0];
            webCamTexture = new WebCamTexture(webCamDevice.name, width, height, fps);
            webCamTexture.Play();

            yield return new WaitUntil(() => webCamTexture.width > 16);

            screen.rectTransform.sizeDelta = new Vector2(width, height);
            screen.texture = webCamTexture;

            var options = new FaceLandmarkerOptions(
                baseOptions: new Tasks.Core.BaseOptions(
                    Tasks.Core.BaseOptions.Delegate.CPU,
                    modelAssetBuffer: modelAsset.bytes
                ),
                runningMode: Tasks.Vision.Core.RunningMode.VIDEO
            );

            using var faceLandmarker = FaceLandmarker.CreateFromOptions(options);

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var waitForEndOfFrame = new WaitForEndOfFrame();
            using var textureFrame = new Experimental.TextureFrame(webCamTexture.width, webCamTexture.height, TextureFormat.RGBA32);
            var screenRect = screen.rectTransform.rect;

            // Create spheres for visualization
            GameObject CreateSphere()
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.transform.SetParent(screen.transform);
                s.transform.localScale = Vector3.one * 10f;
                s.SetActive(false);
                return s;
            }

            GameObject[] leftEyeSpheres = new GameObject[leftEyeIndices.Length];
            GameObject[] rightEyeSpheres = new GameObject[rightEyeIndices.Length];
            GameObject[] leftIrisSpheres = new GameObject[leftIrisIndices.Length];
            GameObject[] rightIrisSpheres = new GameObject[rightIrisIndices.Length];

            for (int i = 0; i < leftEyeIndices.Length; i++) leftEyeSpheres[i] = CreateSphere();
            for (int i = 0; i < rightEyeIndices.Length; i++) rightEyeSpheres[i] = CreateSphere();
            for (int i = 0; i < leftIrisIndices.Length; i++) leftIrisSpheres[i] = CreateSphere();
            for (int i = 0; i < rightIrisIndices.Length; i++) rightIrisSpheres[i] = CreateSphere();

            while (true)
            {
                textureFrame.ReadTextureOnCPU(webCamTexture, flipHorizontally: false, flipVertically: true);
                using var image = textureFrame.BuildCPUImage();

                var result = faceLandmarker.DetectForVideo(image, stopwatch.ElapsedMilliseconds);

                if (result.faceLandmarks?.Count > 0)
                {
                    var landmarks = result.faceLandmarks[0].landmarks;

                    // Left eye points
                    for (int i = 0; i < leftEyeIndices.Length; i++)
                    {
                        var landmark = landmarks[leftEyeIndices[i]];
                        var pos = screenRect.GetPoint(landmark); // remove 'in'
                        pos.z = 0;
                        leftEyeSpheres[i].transform.localPosition = pos;
                        leftEyeSpheres[i].SetActive(true);
                    }

                    // Right eye points
                    for (int i = 0; i < rightEyeIndices.Length; i++)
                    {
                        var landmark = landmarks[rightEyeIndices[i]];
                        var pos = screenRect.GetPoint(landmark);
                        pos.z = 0;
                        rightEyeSpheres[i].transform.localPosition = pos;
                        rightEyeSpheres[i].SetActive(true);
                    }

                    // Left iris points
                    for (int i = 0; i < leftIrisIndices.Length; i++)
                    {
                        var landmark = landmarks[leftIrisIndices[i]];
                        var pos = screenRect.GetPoint(landmark);
                        pos.z = 0;
                        leftIrisSpheres[i].transform.localPosition = pos;
                        leftIrisSpheres[i].SetActive(true);
                    }

                    // Right iris points
                    for (int i = 0; i < rightIrisIndices.Length; i++)
                    {
                        var landmark = landmarks[rightIrisIndices[i]];
                        var pos = screenRect.GetPoint(landmark);
                        pos.z = 0;
                        rightIrisSpheres[i].transform.localPosition = pos;
                        rightIrisSpheres[i].SetActive(true);
                    }
                }
                else
                {
                    foreach (var s in leftEyeSpheres) s.SetActive(false);
                    foreach (var s in rightEyeSpheres) s.SetActive(false);
                    foreach (var s in leftIrisSpheres) s.SetActive(false);
                    foreach (var s in rightIrisSpheres) s.SetActive(false);
                }

                yield return waitForEndOfFrame;
            }
        }

        private void OnDestroy()
        {
            if (webCamTexture != null)
                webCamTexture.Stop();
        }
    }
}