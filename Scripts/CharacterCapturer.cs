using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IGC
{
    public class CharacterCapturer : MonoBehaviour
    {
        public enum DepthFormat
        {
            None = 0,
            _16 = 16,
            _24 = 24,
            _32 = 32
        }

        public enum IGCStage
        {
            None,
            Capturing,
            Uploading,
            CheckOut
        }

        [Tooltip("API Key for the IGC Platform")]
        public string API_Key = "TEST_KEY";

        [Space(10)]

        [Tooltip("GameObject that the camera will capture")]
        public GameObject target;
        public Material MaskerMat;

        private Camera Camera;
        private Camera MaskCamera;

        [SerializeField] private DepthFormat depthFormat;

        [Header("Capture Parameters")]
        public float CaptureRadius = 5;
        public float FOV = 45;
        public int Frames = 100;
        [Range(0, 10)]
        private float xSpeed = 5.17f;
        public int Dimension = 2048;

        [Space(10)]

        //[Tooltip("Should only be used if target character and lighting does not change at all between frames")]
        //public bool shouldCaptureAsynchronously = false;

        [Space(10)]

        [Tooltip("Add Y-axis offset to where the camera is looking on the target")]
        public float HeightOffset = 0.85f; // ~half the height of the unity pawn
        private float ScaleMult = 1.5f;

        [Tooltip("Layers that will show up for the camera")]
        public string[] ShownLayers;

        private int aabb = 64;
        private string OutputPath;
        [Tooltip("If empty, will render to OUTPUT folder above Asset folder")]
        public string CustomOutputPath;

        private SphericalManager Spherical;
        private CaptureUploader Uploader;

        public delegate void CaptureFinish(string message);
        public event CaptureFinish onCaptureFinish;

        public delegate void UploadSuccess(string url, Texture2D qrcode);
        public delegate void UploadError(string message);
        public event UploadSuccess onUploadSuccess;
        public event UploadError onUploadError;

        private IEnumerator captureCoroutine;
        //private bool isCapturing => captureCoroutine != null;

        private IEnumerator uploadCouroutine;
        //private bool isUploading => uploadCouroutine != null;

        [HideInInspector] public IGCStage CurrentStage = IGCStage.None;
        
        void Awake()
        {
            Spherical = GetComponent<SphericalManager>();
            Uploader = GetComponent<CaptureUploader>();
        }

        void Start()
        {
            if (!string.IsNullOrWhiteSpace(CustomOutputPath))
            {
                OutputPath = CustomOutputPath;
            }
            else
            {
                #if UNITY_EDITOR

                OutputPath = Path.Combine(Application.dataPath, "../OUTPUT/Captures");

                #else

                // consider using the Application.temporaryCachePath (some OS clean this up now and again, or you can manage files in there)
                OutputPath = Path.Combine(Application.persistentDataPath, "Captures");

                #endif
            }
            SetUpCameras();
            SetUpLayerMasks();
        }

        void LateUpdate()
        {
            if (Keyboard.current.kKey.wasPressedThisFrame)
            {
                StartCapture();
            }
            if (Keyboard.current.lKey.wasPressedThisFrame)
            {
                UploadCaptures();
            }
            if (Keyboard.current.jKey.wasPressedThisFrame)
            {
                StartCaptureThenUpload(true);
            }
        }

        //--------------------------------------------------------

        public void StartCapture(bool asyncCapture = false)
        // async should NOT be used if either target character and/or lighting changes or moves at all between frames
        {
            if (target == null)
            {
                Debug.LogWarning("No target found! Could not start Captures");
            }
            else
            {
                DeleteCaptures(OutputPath);
                if (asyncCapture)
                {
                    if (captureCoroutine != null)
                    {
                        Debug.Log("Capture is already in progress");
                    }
                    else
                    {
                        captureCoroutine = CaptureCoroutine();
                        StartCoroutine(captureCoroutine);
                    }
                }
                else
                {
                    Capture();
                }
            }
        }

        public void StartCaptureThenUpload(bool asyncCapture = false)
        // async should NOT be used if either target character and/or lighting changes or moves at all between frames
        {
            if (asyncCapture)
            {
                if (captureCoroutine == null && uploadCouroutine == null)
                {
                    StartCoroutine(CaptureThenUploadCoroutine());
                }
                else
                {
                    Debug.Log("Capturing/Uploading already in progress");
                }
            }
            else
            {
                StartCapture();
                UploadCaptures();
            }

            IEnumerator CaptureThenUploadCoroutine()
            {
                captureCoroutine = CaptureCoroutine();
                yield return CaptureCoroutine();
                captureCoroutine = null;

                uploadCouroutine = UploadCapturesCoroutine();
                yield return UploadCapturesCoroutine();
                uploadCouroutine = null;
            }
        }


        //--------------------------------------------------------

        private void Capture()
        {
            Debug.Log("Capture Starting");
            CurrentStage = IGCStage.Capturing;
            var CI = CreateCaptureInformation();

            for (int i = 0; i < Frames; i++)
            {
                Vector3 center = target.transform.position + new Vector3(0, HeightOffset, 0);
                transform.position = Spherical.GetSpiralLocation(CaptureRadius, Frames, i, xSpeed, center, target.transform.forward, phiMin: 0.01f, phiMax: 0.99f);
                // if phi = 0, camera doesn't point to target properly

                transform.LookAt(center);
                string fileName = new string('0', 3 - i.ToString().Length) + i + ".png";
                string filePath = "./images/" + fileName;
                SaveRender(fileName);

                CI = AddToCaptureInformation(i, CI, center, transform: transform, filePath: filePath);
            }
            SaveInformationToJson(CI);
            CurrentStage = IGCStage.None;
            CaptureFinished("Capture finished");
            Debug.Log("Capture Finished");
        }


        private IEnumerator CaptureCoroutine()
        {
            Debug.Log("Capture starting");
            CurrentStage = IGCStage.Capturing;
            var CI = CreateCaptureInformation();

            for (int i = 0; i < Frames; i++)
            {
                Vector3 center = target.transform.position + new Vector3(0, HeightOffset, 0);
                transform.position = Spherical.GetSpiralLocation(CaptureRadius, Frames, i, xSpeed, center, target.transform.forward, phiMin: 0.01f, phiMax: 0.99f);
                // if phi = 0, camera doesn't point to target properly

                transform.LookAt(center);
                string fileName = new string('0', 3 - i.ToString().Length) + i + ".png";
                string filePath = "./images/" + fileName;
                SaveRender(fileName);

                CI = AddToCaptureInformation(i, CI, center, transform: transform, filePath: filePath);

                // wait until next frame...
                yield return null;
            }
            SaveInformationToJson(CI);
            CaptureFinished("Capture finished");
            Debug.Log("Capture finished");
        }

        private void CaptureFinished(string message)
        {
            CurrentStage = IGCStage.None;
            captureCoroutine = null;
            if (onCaptureFinish != null)
            {
                onCaptureFinish(message);
            }
        }

        //-------------------------------------------------------


        public void UploadCaptures()
        {
            if (uploadCouroutine != null)
            {
                Debug.Log("Upload is already in progress");
            }
            else
            {
                CurrentStage = IGCStage.Uploading;
                uploadCouroutine = UploadCapturesCoroutine();
                StartCoroutine(uploadCouroutine);
            }
        }


        private IEnumerator UploadCapturesCoroutine()
        {
            bool isUploadFinished = false;
            Uploader.UploadCaptures(OutputPath, API_Key, UploadSucceeded, CheckoutStarted, UploadFailed);

            while (true)
            {
                // wait for uploader...
                yield return new WaitUntil(() => isUploadFinished);
            }

            void UploadSucceeded(string orderURL, Texture2D qrCode)
            {
                uploadCouroutine = null;
                isUploadFinished = true;
                CurrentStage = IGCStage.None;
                Debug.Log("Upload succeeded");
                if (onUploadSuccess != null)
                {
                    onUploadSuccess(orderURL, qrCode);
                }
            }

            void CheckoutStarted()
            {
                CurrentStage = IGCStage.CheckOut;
            }

            void UploadFailed(string errorMessage)
            {
                uploadCouroutine = null;
                isUploadFinished = true;
                Debug.Log($"Upload failed: {errorMessage}");
                CurrentStage = IGCStage.None;
                if (onUploadError != null)
                {
                    onUploadError(errorMessage);
                }
            }
        }

        //--------------------------------------------------------


        private void SetUpLayerMasks()
        {
            if (ShownLayers.Length == 0)
            {
                Camera.cullingMask = -1; // turns on every layer
                MaskCamera.cullingMask = -1; // turns on every layer
                return;
            }
            foreach (string layerName in ShownLayers)
            {
                int layer = LayerMask.NameToLayer(layerName);
                Camera.cullingMask |= 1 << layer;
                MaskCamera.cullingMask |= 1 << layer;
            }
        }

        private void SetUpCameras()
        {
            Camera = transform.Find("Camera_RGB").GetComponent<Camera>();
            MaskCamera = transform.Find("Camera_Mask").GetComponent<Camera>();
            // cameras are manually rendered, disable them to stop rendering every frame
            Camera.enabled = false;
            MaskCamera.enabled = false;
            // depth of 0 can throw errors depending on other parts of the URP pipeline
            Camera.targetTexture = new RenderTexture(Dimension, Dimension, (int)depthFormat, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);
            MaskCamera.targetTexture = new RenderTexture(Dimension, Dimension, (int)depthFormat, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);

            Camera.fieldOfView = FOV;
            MaskCamera.fieldOfView = FOV;
        }


        //--------------------------------------------------------

        private void SaveRender(string filename)
        // gets camera render textures and exports to png
        {
            Texture2D RGBImage = GetCameraImage(Camera);
            MaskerMat.SetTexture("_RGB", RGBImage);
            Texture2D Output;
            if (ShownLayers.Length != 0)
            {
                Texture2D MaskImage = GetCameraImage(MaskCamera);
                MaskerMat.SetTexture("_Mask", MaskImage);

                // avoid leak, you must manually release/destroy a rendertexture if you create them
                // or use a temporary one that Unity will pool and cleanup
                var RT = RenderTexture.GetTemporary(Dimension, Dimension, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

                Graphics.Blit(MaskerMat.mainTexture, RT, MaskerMat, 0); // convert material to render texture

                RenderTexture.active = RT;
                Output = new Texture2D(RT.width, RT.height, TextureFormat.ARGB32, false);
                Output.ReadPixels(new Rect(0, 0, Camera.targetTexture.width, Camera.targetTexture.height), 0, 0);
                Destroy(MaskImage);

                RenderTexture.ReleaseTemporary(RT);
            }
            else
            {
                Output = RGBImage;
            }

            byte[] bytes = Output.EncodeToPNG();

            Destroy(RGBImage);
            Destroy(Output);

            string filePath = Path.Combine(OutputPath, "images");
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }
            var imagePath = Path.Combine(filePath, filename);
            File.WriteAllBytes(imagePath, bytes);
        }

        private Texture2D GetCameraImage(Camera camera)
        {
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            camera.Render();

            Texture2D image = new Texture2D(camera.targetTexture.width, camera.targetTexture.height);
            image.ReadPixels(new Rect(0, 0, camera.targetTexture.width, camera.targetTexture.height), 0, 0);
            image.Apply();
            RenderTexture.active = currentRT;

            return image;
        }

        //--------------------------------------------------------

        private CaptureInformation CreateCaptureInformation()
        {
            float camera_angle = Mathf.Deg2Rad * Camera.fieldOfView;
            float focal_length = Dimension / (2 * Mathf.Tan(camera_angle / 2));
            return new CaptureInformation(camera_angle, camera_angle, focal_length, focal_length, Dimension, Dimension, aabb, Frames);
        }

        private CaptureInformation AddToCaptureInformation(int index, CaptureInformation CI, Vector3 center, string filePath = "", float sharpness = 50, Transform transform = null)
        {
            Transform transf = transform;
            // transform to make compatible with api
            transf.position = ((transform.position - center) * -1) * ScaleMult;
            transf.RotateAround(new Vector3(), new Vector3(0, 1, 0), 180);
            transf.RotateAround(new Vector3(), new Vector3(1, 0, 0), 270);
            transf.Rotate(new Vector3(0, 0, 180));

            Matrix4x4 matrix = transf.localToWorldMatrix;
            List<Vector4> tm = new List<Vector4>();

            for (int i = 0; i < 4; i++)
            {
                tm.Add(matrix.GetRow(i));
            }
            FrameInformation FI = new FrameInformation(filePath, sharpness, tm);
            CI.AddFrameInformation(index, FI);

            return CI;
        }
        private void SaveInformationToJson(CaptureInformation CI)
        {
            string output = JsonUtility.ToJson(CI, true);
            var outputPath = Path.Combine(OutputPath, "transforms.json");
            System.IO.File.WriteAllText(outputPath, output);
        }

        //--------------------------------------------------------

        private void DeleteCaptures(string path)
        {
            var imageFolder = Path.Combine(path, "images");
            if (Directory.Exists(imageFolder))
                Directory.Delete(imageFolder, true);

            var transformsPath = Path.Combine(path, "transforms.json");
            if (File.Exists(transformsPath))
                File.Delete(transformsPath);
        }
    }
}

