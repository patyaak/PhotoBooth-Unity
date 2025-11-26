//using System.Collections;
//using System.Reflection;
//using Unity.Burst.Intrinsics;
//using Unity.VisualScripting;
//using UnityEditor.PackageManager;
//using UnityEngine;
//using UnityEngine.UI;
//using Stopwatch = System.Diagnostics.Stopwatch;

//namespace Mediapipe.Unity.Tutorial
//{
//    public class FaceMeshLegacy : MonoBehaviour
//    {
//        [SerializeField] private TextAsset configAsset;
//        [SerializeField] private RawImage screen;
//        [SerializeField] private Texture2D inputImage;  // Changed: Load image instead of webcam
//        [SerializeField] private Material smoothMaterial;

//        private CalculatorGraph graph;
//        private OutputStream<ImageFrame> outputVideoStream;
//        private OutputStream<NormalizedLandmarkList> faceLandmarksStream;  // Added: To get landmark data

//        private IEnumerator Start()
//        {
//            // Check if image is assigned
//            if (inputImage == null)
//            {
//                throw new System.Exception("Input image is not assigned");
//            }

//            // Get image dimensions
//            int width = inputImage.width;
//            int height = inputImage.height;

//            var outputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
//            var outputPixelData = new Color32[width * height];

//            screen.rectTransform.sizeDelta = new Vector2(width, height);
//            screen.texture = outputTexture;
//            screen.material = smoothMaterial;

//            IResourceManager resourceManager = new LocalResourceManager();
//            yield return resourceManager.PrepareAssetAsync("face_detection_short_range.bytes");
//            yield return resourceManager.PrepareAssetAsync("face_landmark_with_attention.bytes");

//            graph = new CalculatorGraph(configAsset.text);
//            outputVideoStream = new OutputStream<ImageFrame>(graph, "output_video");
//            outputVideoStream.StartPolling();
//            graph.StartRun();

//            var stopwatch = new Stopwatch();
//            stopwatch.Start();

//            // Changed: Use inputImage instead of webcam texture
//            using var textureFrame = new Experimental.TextureFrame(width, height, TextureFormat.RGBA32);

//            // Read the input image once
//            textureFrame.ReadTextureOnCPU(inputImage, flipHorizontally: false, flipVertically: true);
//            using var imageFrame = textureFrame.BuildImageFrame();

//            var currentTimestamp = stopwatch.ElapsedTicks / ((double)System.TimeSpan.TicksPerMillisecond / 1000);
//            graph.AddPacketToInputStream("input_video", Packet.CreateImageFrameAt(imageFrame, (long)currentTimestamp));

//            var task = outputVideoStream.WaitNextAsync();
//            yield return new WaitUntil(() => task.IsCompleted);

//            if (!task.Result.ok)
//            {
//                throw new System.Exception("Something went wrong");
//            }

//            var outputPacket = task.Result.packet;
//            if (outputPacket != null)
//            {
//                var outputVideo = outputPacket.Get();

//                if (outputVideo.TryReadPixelData(outputPixelData))
//                {
//                    outputTexture.SetPixels32(outputPixelData);
//                    outputTexture.Apply();
//                }
//            }

//            Debug.Log("Face detection completed!");
//        }

//        private void Update()
//        {
//            if (smoothMaterial != null)
//            {
//                smoothMaterial.SetFloat("_BlurSize", Mathf.PingPong(Time.time * 0.2f, 1.0f));
//            }
//        }

//        private void OnDestroy()
//        {
//            outputVideoStream?.Dispose();
//            outputVideoStream = null;

//            if (graph != null)
//            {
//                try
//                {
//                    graph.CloseInputStream("input_video");
//                    graph.WaitUntilDone();
//                }
//                finally
//                {
//                    graph.Dispose();
//                    graph = null;
//                }
//            }
//        }
//    }
//}


