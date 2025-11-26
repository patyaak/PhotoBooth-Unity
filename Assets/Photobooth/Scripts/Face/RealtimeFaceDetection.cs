using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Unity.CoordinateSystem;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Mediapipe.Unity.Tutorial
{
    public class RealtimeFaceDetection : MonoBehaviour
    {
        [SerializeField] private RawImage screen;
        [SerializeField] private WebCamTexture webCamTexture;
        [SerializeField] private TextAsset modelAsset;

        [Header("Webcam Settings")]
        [SerializeField] private string webcamName = "";

        [SerializeField] private int requestedWidth = 1280;
        [SerializeField] private int requestedHeight = 720;
        [SerializeField] private int requestedFPS = 30;

        [Header("Sphere Settings")]
        [SerializeField, Range(0f, 10f)] private float sphereSize = 5f;

        [SerializeField] private bool showSpheres = true;

        [Header("Face Effects")]
        [SerializeField] private FaceEffectsController faceEffectsController;

        [Header("Performance")]
        [SerializeField] private bool showFPS = true;

        [SerializeField] private Text fpsText;

        // Face oval landmarks (jawline and face contour)
        private int[] faceOvalIndices = { 10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288,
                                          397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136,
                                          172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109 };

        private GameObject[] faceOvalSpheres;
        private FaceLandmarker faceLandmarker;
        private bool isRunning = false;

        // FPS calculation
        private float deltaTime = 0.0f;

        private IEnumerator Start()
        {
            // Initialize webcam
            if (string.IsNullOrEmpty(webcamName))
            {
                webCamTexture = new WebCamTexture(requestedWidth, requestedHeight, requestedFPS);
            }
            else
            {
                webCamTexture = new WebCamTexture(webcamName, requestedWidth, requestedHeight, requestedFPS);
            }

            webCamTexture.Play();
            yield return new WaitUntil(() => webCamTexture.didUpdateThisFrame);

            screen.texture = webCamTexture;
            screen.rectTransform.sizeDelta = new Vector2(webCamTexture.width, webCamTexture.height);

            // Initialize face landmarker
            var options = new FaceLandmarkerOptions(
                baseOptions: new Tasks.Core.BaseOptions(
                    Tasks.Core.BaseOptions.Delegate.CPU,
                    modelAssetBuffer: modelAsset.bytes
                ),
                runningMode: Tasks.Vision.Core.RunningMode.VIDEO,
                numFaces: 1
            );

            faceLandmarker = FaceLandmarker.CreateFromOptions(options);

            // Create spheres for face oval
            faceOvalSpheres = new GameObject[faceOvalIndices.Length];
            for (int i = 0; i < faceOvalIndices.Length; i++)
            {
                faceOvalSpheres[i] = CreateSphere();
                var renderer = faceOvalSpheres[i].GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = UnityEngine.Color.yellow;
                }
            }

            SetSphereSize(sphereSize);

            isRunning = true;
            StartCoroutine(DetectFaceLandmarks());
        }

        private GameObject CreateSphere()
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.transform.SetParent(screen.transform);
            s.SetActive(false);
            return s;
        }

        private IEnumerator DetectFaceLandmarks()
        {
            var stopwatch = new Stopwatch();

            while (isRunning)
            {
                stopwatch.Restart();

                if (webCamTexture.didUpdateThisFrame)
                {
                    var screenRect = screen.rectTransform.rect;

                    // Process the webcam frame
                    using var textureFrame = new Experimental.TextureFrame(
                        webCamTexture.width,
                        webCamTexture.height,
                        TextureFormat.RGBA32
                    );

                    textureFrame.ReadTextureOnCPU(webCamTexture, flipHorizontally: false, flipVertically: true);
                    using var image = textureFrame.BuildCPUImage();

                    // Get timestamp in milliseconds
                    long timestampMs = (long)(Time.time * 1000);
                    var result = faceLandmarker.DetectForVideo(image, timestampMs);

                    int faceCount = result.faceLandmarks?.Count ?? 0;

                    if (faceCount > 0)
                    {
                        // Collect all face landmarks for multi-face brightening
                        List<List<Vector3>> allFaceLandmarks = new List<List<Vector3>>();
                        
                        for (int f = 0; f < faceCount; f++)
                        {
                            var landmarks = result.faceLandmarks[f].landmarks;

                            // Convert landmarks to Vector3 positions
                            List<Vector3> landmarkPositions = new List<Vector3>();
                            foreach (var landmark in landmarks)
                            {
                                var pos = screenRect.GetPoint(landmark);
                                landmarkPositions.Add(pos);
                            }
                            
                            allFaceLandmarks.Add(landmarkPositions);
                        }

                        // Update face effects shader with all faces
                        if (faceEffectsController != null)
                        {
                            faceEffectsController.UpdateMultipleFacePositions(allFaceLandmarks, screen.rectTransform);
                        }

                        // Update face oval spheres for first face only (visualization)
                        var firstFaceLandmarks = result.faceLandmarks[0].landmarks;
                        for (int i = 0; i < faceOvalIndices.Length; i++)
                        {
                            var pos = screenRect.GetPoint(firstFaceLandmarks[faceOvalIndices[i]]);
                            pos.z = 0;
                            faceOvalSpheres[i].transform.localPosition = pos;
                            faceOvalSpheres[i].SetActive(showSpheres);
                        }
                    }
                    else
                    {
                        // Hide spheres if no face detected
                        foreach (var s in faceOvalSpheres)
                        {
                            s.SetActive(false);
                        }
                    }
                }

                stopwatch.Stop();

                // Calculate FPS
                deltaTime += (stopwatch.Elapsed.Milliseconds / 1000f - deltaTime) * 0.1f;

                yield return null;
            }
        }

        private void Update()
        {
            if (showFPS && fpsText != null)
            {
                float fps = 1.0f / deltaTime;
                fpsText.text = $"FPS: {fps:F1}";
            }
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                SetSphereSize(sphereSize);
                SetSpheresVisibility(showSpheres);
            }
        }

        private void SetSphereSize(float size)
        {
            if (faceOvalSpheres != null)
            {
                foreach (var s in faceOvalSpheres)
                {
                    if (s != null)
                        s.transform.localScale = Vector3.one * size;
                }
            }
        }

        private void SetSpheresVisibility(bool visible)
        {
            if (faceOvalSpheres != null)
            {
                foreach (var s in faceOvalSpheres)
                {
                    if (s != null && isRunning)
                        s.SetActive(visible);
                }
            }
        }

        private void OnDestroy()
        {
            isRunning = false;

            if (webCamTexture != null)
            {
                webCamTexture.Stop();
            }

            if (faceOvalSpheres != null)
            {
                foreach (var s in faceOvalSpheres)
                {
                    if (s != null)
                        Destroy(s);
                }
            }
        }
    }
}