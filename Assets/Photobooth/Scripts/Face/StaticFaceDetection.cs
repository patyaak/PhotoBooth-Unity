using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Unity.CoordinateSystem;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Mediapipe.Unity.Tutorial
{
    public class StaticFaceDetection : MonoBehaviour
    {
        public static StaticFaceDetection Instance;

        [Header("Input Settings")]
        [SerializeField] public RawImage screen;

        [SerializeField] public Texture2D inputImage;
        [SerializeField] private TextAsset modelAsset;

        [Header("Visualization")]
        [SerializeField, Range(0f, 10f)] private float sphereSize = 5f;

        [SerializeField] private bool showSpheres = true;
        [SerializeField] private bool showFaceOval = true;
        [SerializeField] private bool showEyes = true;
        [SerializeField] private bool showIris = true;
        [SerializeField] private bool showEyebrows = true;
        [SerializeField] private bool showMouth = true;

        [Header("Face Effects")]
        [SerializeField] private FaceEffectsController faceEffectsController;

        [SerializeField] private FaceSegmentationProvider faceSegmentationProvider;

        // Face oval landmarks (jawline and face contour)
        private int[] faceOvalIndices = { 10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288,
                                          397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136,
                                          172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109 };

        // Eye and facial feature landmarks
        private int[] leftEyeIndices = { 33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246 };

        private int[] rightEyeIndices = { 362, 382, 381, 380, 374, 373, 390, 249, 263, 466, 388, 387, 386, 385, 384, 398 };
        private int[] leftIrisIndices = { 468, 469, 470, 471, 472 };
        private int[] rightIrisIndices = { 473, 474, 475, 476, 477 };
        private int[] leftEyebrowIndices = { 70, 63, 105, 66, 107, 55, 65, 52, 53, 46 };
        private int[] rightEyebrowIndices = { 300, 293, 334, 296, 336, 285, 295, 282, 283, 276 };

        private int[] mouthIndices = {
            61, 185, 40, 39, 37, 0, 267, 269, 270, 409, 291,  // Upper outer lip
            375, 321, 405, 314, 17, 84, 181, 91, 146,         // Lower outer lip
            78, 191, 80, 81, 82, 13, 312, 311, 310, 415, 308, // Upper inner lip
            324, 318, 402, 317, 14, 87, 178, 88, 95           // Lower inner lip
        };

        private List<GameObject[]> faceOvalSpheresList = new List<GameObject[]>();
        private List<GameObject[]> leftEyeSpheresList = new List<GameObject[]>();
        private List<GameObject[]> rightEyeSpheresList = new List<GameObject[]>();
        private List<GameObject[]> leftIrisSpheresList = new List<GameObject[]>();
        private List<GameObject[]> rightIrisSpheresList = new List<GameObject[]>();
        private List<GameObject[]> leftEyebrowSpheresList = new List<GameObject[]>();
        private List<GameObject[]> rightEyebrowSpheresList = new List<GameObject[]>();
        private List<GameObject[]> mouthSpheresList = new List<GameObject[]>();

        private void Awake()
        {
            Instance = this;
        }

        public IEnumerator OnDetectImage()
        {
            if (inputImage == null)
            {
                Debug.LogError("Input image is not assigned");
                yield break;
            }

            if (modelAsset == null)
            {
                Debug.LogError("Model asset is not assigned");
                yield break;
            }

            // Display input image
            screen.texture = inputImage;
            screen.rectTransform.sizeDelta = new Vector2(inputImage.width, inputImage.height);

            // Create face landmarker options
            var options = new FaceLandmarkerOptions(
                baseOptions: new Tasks.Core.BaseOptions(
                    Tasks.Core.BaseOptions.Delegate.CPU,
                    modelAssetBuffer: modelAsset.bytes
                ),
                runningMode: Tasks.Vision.Core.RunningMode.IMAGE
            );

            using var faceLandmarker = FaceLandmarker.CreateFromOptions(options);
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var screenRect = screen.rectTransform.rect;

            // Helper function to create sphere
            GameObject CreateSphere()
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.transform.SetParent(screen.transform);
                s.SetActive(false);
                return s;
            }

            // Process the input image
            using var textureFrame = new Experimental.TextureFrame(inputImage.width, inputImage.height, TextureFormat.RGBA32);
            textureFrame.ReadTextureOnCPU(inputImage, flipHorizontally: false, flipVertically: true);
            using var image = textureFrame.BuildCPUImage();

            // Detect faces
            var result = faceLandmarker.Detect(image);

            int faceCount = result.faceLandmarks?.Count ?? 0;
            Debug.Log($"✅ Detected {faceCount} face(s)");

            if (faceCount > 0)
            {
                // Process segmentation for first face (if available)
                if (faceSegmentationProvider != null)
                {
                    faceSegmentationProvider.ProcessImage(inputImage);
                    Debug.Log("✅ Segmentation processed");
                }

                // Collect all face landmarks for multi-face brightening
                List<List<Vector3>> allFaceLandmarks = new List<List<Vector3>>();

                for (int f = 0; f < faceCount; f++)
                {
                    var landmarks = result.faceLandmarks[f].landmarks;

                    // Convert landmarks to Vector3 positions
                    List<Vector3> landmarkPositions = new List<Vector3>();
                    foreach (var landmark in landmarks)
                    {
                        landmarkPositions.Add(screenRect.GetPoint(landmark));
                    }

                    allFaceLandmarks.Add(landmarkPositions);
                }

                // Update face effects controller (handles ALL effects: eye enlargement + brightening + smoothing)
                if (faceEffectsController != null)
                {
                    // This single call now handles eye enlargement automatically!
                    faceEffectsController.UpdateMultipleFacePositions(allFaceLandmarks, screen.rectTransform);

                    // Set eye, eyebrow, and mouth exclusion data for smoothing
                    faceEffectsController.SetEyeAndEyebrowExclusionData(allFaceLandmarks, screen.rectTransform);

                    // Pass segmentation mask if available (applies to all faces)
                    if (faceSegmentationProvider != null && faceSegmentationProvider.IsSegmentationEnabled)
                    {
                        faceEffectsController.SetSegmentationMask(faceSegmentationProvider.SegmentationMask);
                    }

                    Debug.Log($"✅ All face effects (eye enlargement + brightening + smoothing) updated for {faceCount} face(s)");
                }
                else
                {
                    Debug.LogWarning("⚠️ FaceEffectsController not assigned");
                }

                // Create visualization spheres for all faces
                for (int f = 0; f < faceCount; f++)
                {
                    var faceLandmarks = result.faceLandmarks[f].landmarks;

                    // Face Oval Spheres
                    if (showFaceOval)
                    {
                        GameObject[] faceOvalSpheres = new GameObject[faceOvalIndices.Length];
                        for (int i = 0; i < faceOvalIndices.Length; i++)
                        {
                            faceOvalSpheres[i] = CreateSphere();
                            var pos = screenRect.GetPoint(faceLandmarks[faceOvalIndices[i]]);
                            pos.z = 0;
                            faceOvalSpheres[i].transform.localPosition = pos;
                            faceOvalSpheres[i].transform.localScale = Vector3.one * sphereSize;
                            faceOvalSpheres[i].SetActive(showSpheres);

                            var renderer = faceOvalSpheres[i].GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.material.color = UnityEngine.Color.yellow;
                        }
                        faceOvalSpheresList.Add(faceOvalSpheres);
                    }

                    // Left Eye Spheres
                    if (showEyes)
                    {
                        GameObject[] leftEyeSpheres = new GameObject[leftEyeIndices.Length];
                        for (int i = 0; i < leftEyeIndices.Length; i++)
                        {
                            leftEyeSpheres[i] = CreateSphere();
                            var pos = screenRect.GetPoint(faceLandmarks[leftEyeIndices[i]]);
                            pos.z = 0;
                            leftEyeSpheres[i].transform.localPosition = pos;
                            leftEyeSpheres[i].transform.localScale = Vector3.one * sphereSize;
                            leftEyeSpheres[i].SetActive(showSpheres);

                            var renderer = leftEyeSpheres[i].GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.material.color = UnityEngine.Color.cyan;
                        }
                        leftEyeSpheresList.Add(leftEyeSpheres);

                        // Right Eye Spheres
                        GameObject[] rightEyeSpheres = new GameObject[rightEyeIndices.Length];
                        for (int i = 0; i < rightEyeIndices.Length; i++)
                        {
                            rightEyeSpheres[i] = CreateSphere();
                            var pos = screenRect.GetPoint(faceLandmarks[rightEyeIndices[i]]);
                            pos.z = 0;
                            rightEyeSpheres[i].transform.localPosition = pos;
                            rightEyeSpheres[i].transform.localScale = Vector3.one * sphereSize;
                            rightEyeSpheres[i].SetActive(showSpheres);

                            var renderer = rightEyeSpheres[i].GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.material.color = UnityEngine.Color.cyan;
                        }
                        rightEyeSpheresList.Add(rightEyeSpheres);
                    }

                    // Iris Spheres
                    if (showIris)
                    {
                        GameObject[] leftIrisSpheres = new GameObject[leftIrisIndices.Length];
                        for (int i = 0; i < leftIrisIndices.Length; i++)
                        {
                            leftIrisSpheres[i] = CreateSphere();
                            var pos = screenRect.GetPoint(faceLandmarks[leftIrisIndices[i]]);
                            pos.z = 0;
                            leftIrisSpheres[i].transform.localPosition = pos;
                            leftIrisSpheres[i].transform.localScale = Vector3.one * sphereSize;
                            leftIrisSpheres[i].SetActive(showSpheres);

                            var renderer = leftIrisSpheres[i].GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.material.color = UnityEngine.Color.blue;
                        }
                        leftIrisSpheresList.Add(leftIrisSpheres);

                        GameObject[] rightIrisSpheres = new GameObject[rightIrisIndices.Length];
                        for (int i = 0; i < rightIrisIndices.Length; i++)
                        {
                            rightIrisSpheres[i] = CreateSphere();
                            var pos = screenRect.GetPoint(faceLandmarks[rightIrisIndices[i]]);
                            pos.z = 0;
                            rightIrisSpheres[i].transform.localPosition = pos;
                            rightIrisSpheres[i].transform.localScale = Vector3.one * sphereSize;
                            rightIrisSpheres[i].SetActive(showSpheres);

                            var renderer = rightIrisSpheres[i].GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.material.color = UnityEngine.Color.blue;
                        }
                        rightIrisSpheresList.Add(rightIrisSpheres);
                    }

                    // Eyebrow Spheres
                    if (showEyebrows)
                    {
                        GameObject[] leftEyebrowSpheres = new GameObject[leftEyebrowIndices.Length];
                        for (int i = 0; i < leftEyebrowIndices.Length; i++)
                        {
                            leftEyebrowSpheres[i] = CreateSphere();
                            var pos = screenRect.GetPoint(faceLandmarks[leftEyebrowIndices[i]]);
                            pos.z = 0;
                            leftEyebrowSpheres[i].transform.localPosition = pos;
                            leftEyebrowSpheres[i].transform.localScale = Vector3.one * sphereSize;
                            leftEyebrowSpheres[i].SetActive(showSpheres);

                            var renderer = leftEyebrowSpheres[i].GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.material.color = UnityEngine.Color.green;
                        }
                        leftEyebrowSpheresList.Add(leftEyebrowSpheres);

                        GameObject[] rightEyebrowSpheres = new GameObject[rightEyebrowIndices.Length];
                        for (int i = 0; i < rightEyebrowIndices.Length; i++)
                        {
                            rightEyebrowSpheres[i] = CreateSphere();
                            var pos = screenRect.GetPoint(faceLandmarks[rightEyebrowIndices[i]]);
                            pos.z = 0;
                            rightEyebrowSpheres[i].transform.localPosition = pos;
                            rightEyebrowSpheres[i].transform.localScale = Vector3.one * sphereSize;
                            rightEyebrowSpheres[i].SetActive(showSpheres);

                            var renderer = rightEyebrowSpheres[i].GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.material.color = UnityEngine.Color.green;
                        }
                        rightEyebrowSpheresList.Add(rightEyebrowSpheres);
                    }

                    // Mouth Spheres
                    if (showMouth)
                    {
                        GameObject[] mouthSpheres = new GameObject[mouthIndices.Length];
                        for (int i = 0; i < mouthIndices.Length; i++)
                        {
                            mouthSpheres[i] = CreateSphere();
                            var pos = screenRect.GetPoint(faceLandmarks[mouthIndices[i]]);
                            pos.z = 0;
                            mouthSpheres[i].transform.localPosition = pos;
                            mouthSpheres[i].transform.localScale = Vector3.one * sphereSize;
                            mouthSpheres[i].SetActive(showSpheres);

                            var renderer = mouthSpheres[i].GetComponent<Renderer>();
                            if (renderer != null)
                                renderer.material.color = UnityEngine.Color.red;
                        }
                        mouthSpheresList.Add(mouthSpheres);
                    }
                }

                Debug.Log($"✅ Created face visualizations - Faces: {faceCount}, FaceOval: {faceOvalSpheresList.Count}, Eyes: {leftEyeSpheresList.Count}, Eyebrows: {leftEyebrowSpheresList.Count}, Mouth: {mouthSpheresList.Count}");
            }
            else
            {
                Debug.LogWarning("❌ No faces detected in the image");
            }

            stopwatch.Stop();
            Debug.Log($"⏱️ Detection completed in {stopwatch.ElapsedMilliseconds}ms");

            yield return null;
        }

        private void OnValidate()
        {
            SetSphereSize(sphereSize);
            SetSpheresVisibility(showSpheres);
        }

        private void SetSphereSize(float size)
        {
            foreach (var sArray in faceOvalSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.transform.localScale = Vector3.one * size;

            foreach (var sArray in leftEyeSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.transform.localScale = Vector3.one * size;

            foreach (var sArray in rightEyeSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.transform.localScale = Vector3.one * size;

            foreach (var sArray in leftIrisSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.transform.localScale = Vector3.one * size;

            foreach (var sArray in rightIrisSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.transform.localScale = Vector3.one * size;

            foreach (var sArray in leftEyebrowSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.transform.localScale = Vector3.one * size;

            foreach (var sArray in rightEyebrowSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.transform.localScale = Vector3.one * size;

            foreach (var sArray in mouthSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.transform.localScale = Vector3.one * size;
        }

        private void SetSpheresVisibility(bool visible)
        {
            foreach (var sArray in faceOvalSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.SetActive(visible && showFaceOval);

            foreach (var sArray in leftEyeSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.SetActive(visible && showEyes);

            foreach (var sArray in rightEyeSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.SetActive(visible && showEyes);

            foreach (var sArray in leftIrisSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.SetActive(visible && showIris);

            foreach (var sArray in rightIrisSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.SetActive(visible && showIris);

            foreach (var sArray in leftEyebrowSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.SetActive(visible && showEyebrows);

            foreach (var sArray in rightEyebrowSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.SetActive(visible && showEyebrows);

            foreach (var sArray in mouthSpheresList)
                foreach (var s in sArray)
                    if (s != null) s.SetActive(visible && showMouth);
        }

        private void OnDestroy()
        {
            // Clean up all spheres
            foreach (var sArray in faceOvalSpheresList)
                foreach (var s in sArray)
                    if (s != null) Destroy(s);

            foreach (var sArray in leftEyeSpheresList)
                foreach (var s in sArray)
                    if (s != null) Destroy(s);

            foreach (var sArray in rightEyeSpheresList)
                foreach (var s in sArray)
                    if (s != null) Destroy(s);

            foreach (var sArray in leftIrisSpheresList)
                foreach (var s in sArray)
                    if (s != null) Destroy(s);

            foreach (var sArray in rightIrisSpheresList)
                foreach (var s in sArray)
                    if (s != null) Destroy(s);

            foreach (var sArray in leftEyebrowSpheresList)
                foreach (var s in sArray)
                    if (s != null) Destroy(s);

            foreach (var sArray in rightEyebrowSpheresList)
                foreach (var s in sArray)
                    if (s != null) Destroy(s);

            foreach (var sArray in mouthSpheresList)
                foreach (var s in sArray)
                    if (s != null) Destroy(s);

            faceOvalSpheresList.Clear();
            leftEyeSpheresList.Clear();
            rightEyeSpheresList.Clear();
            leftIrisSpheresList.Clear();
            rightIrisSpheresList.Clear();
            leftEyebrowSpheresList.Clear();
            rightEyebrowSpheresList.Clear();
            mouthSpheresList.Clear();
        }

    }
}