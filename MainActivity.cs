using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Views;
using AndroidX.AppCompat;
using AndroidX.AppCompat.App;
using Google.Android.Material.FloatingActionButton;
using Google.Android.Material.Snackbar;
using Android.Content;
using Android.Provider;
using AndroidX.Core.Content;
using Android;
using Android.Content.PM;
using AndroidX.Core.App;

using Xamarin.TensorFlow.Lite;
using Java.Nio;
using Java.Nio.Channels;
using Android.Graphics;
using Android.Widget;
using Java.IO;

using Android.Media; // Required for ExifInterface
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.Views.Animations;

using Microsoft.ML.OnnxRuntime;
using Android.Content.Res;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Linq;

namespace MediScanAI
{
    [Activity(Label = "@string/app_name", Theme = "@style/MediScanAI.Splash", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        // UI Elements
        private TextView resultText;
        private TextView schoolText;
        private Spinner productSpinner;
        private View laserLine;
        //private Button scanButton;
        private FloatingActionButton scanFAB;

        // AI & Logic
        private MappedByteBuffer modelBuffer;
        private Interpreter tfLite;
        private List<string> labels;
        private List<string> products;
        private Java.IO.File photoFile;
        private const int CameraRequestCode = 0;
        private readonly Color skyBlue = Color.ParseColor("#87CEEB");
        private Android.Graphics.Color schoolTextColor = Android.Graphics.Color.Black;

        private InferenceSession session;
        private int productIndex;

        //To prevent Out of Memory(OOM) crashes, you should decode the image at a size close to what your AI model
        //actually needs(e.g., 224x224) rather than loading the massive 12MP file into memory first.
        private int targetWidthTFLite = 224; // Match your model input width
        private int targetHeightTFLite = 224; // Match your model input height
        private int targetWidthONNX = 320; // Match your model input width
        private int targetHeightONNX = 320; // Match your model input height

        // Adjust this variable to change your application's scanning sensitivity
        private readonly float confidenceThreshold = 0.15f;

        //private bool isInitialCheck = true;

        //List<string> productCategories = new List<string>();
        //ArrayAdapter<string> adapter;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            // Switch to your main app theme before everything else
            // This replaces the splash image with your actual app UI
            SetTheme(Resource.Style.AppTheme);

            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            // 1. Initialize UI
            AndroidX.AppCompat.Widget.Toolbar toolbar = FindViewById<AndroidX.AppCompat.Widget.Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);

            scanFAB = FindViewById<FloatingActionButton>(Resource.Id.scanFAB);
            scanFAB.Click += scanFABOnClick;

            //scanButton = FindViewById<Button>(Resource.Id.scanButton);
            //scanButton.Click += (s, e) => onButtonClick();

            resultText = FindViewById<TextView>(Resource.Id.resultText);
            schoolText = FindViewById<TextView>(Resource.Id.schoolText);
            productSpinner = FindViewById<Spinner>(Resource.Id.productSpinner);
            laserLine = FindViewById<View>(Resource.Id.laserLine);

            // 2. Load AI and Labels in background
            Task.Run(async () => {
                try
                {
                    // Load the Labels immediately after the model
                    LoadLabels();

                    // Update Spinner on UI Thread
                    RunOnUiThread(() => setupProductSpinner());
                }
                catch (System.Exception ex)
                {
                    // Handle initialization errors (e.g., file missing)
                    Android.Util.Log.Error("MediScanAI", "Failed to load AI: " + ex.Message);
                    RunOnUiThread(() => resultText.Text = "Failed to load AI: " + ex.Message);
                }
            });
        }

        private void scanFABOnClick(object sender, EventArgs eventArgs)
        {
            /*View view = (View)sender;
            Snackbar.Make(view, "Replace with your own action", Snackbar.LengthLong)
                .SetAction("Action", (View.IOnClickListener)null).Show();*/

            // Use this check before launching the intent
            if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) != (int)Permission.Granted)
            {
                // If not granted, request it
                ActivityCompat.RequestPermissions(this, new string[] { Manifest.Permission.Camera }, 101);
            }
            else
            {
                // If already granted, you can safely open the camera
                OpenCameraIntent();
            }
        }

        private void onButtonClick()
        {
            //Launch credits
            var intent = new Intent(this, typeof(CreditsActivity));
            StartActivity(intent);
        }

        private void LoadLabels()
        {
            // Clear old data to prevent duplicates
            //labels.Clear();
            labels = new List<string>();

            using (var reader = new StreamReader(Assets.Open("labels.txt")))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    // Split on space and take the second part (class name)
                    var parts = line.Split(' ');
                    //labels.Add(reader.ReadLine().Split(' ')[1]);
                    if (parts.Length > 1)
                    {
                        labels.Add(parts[1]); // e.g., "Genuine", "Counterfeit"
                    }
                    else
                    {
                        labels.Add(line); // fallback if format is unexpected
                    }
                }
            }
        }

        private void setupProductSpinner()
        {   
            products = new List<string> { "Select a product...", "Moko Isopropyl Alchohol 200 ml", "NCP Liquid Antiseptic 100 ml", "Loratadine", "Jucopan", "Divamine" };
            var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, products);
            adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            productSpinner.Adapter = adapter;

            productSpinner.ItemSelected += (s, e) => {
                //string selected = products[e.Position];
                //Toast.MakeText(this, "Selected: " + selected, ToastLength.Short).Show();

                if (e.Position == 0)
                {
                    scanFAB.Visibility = ViewStates.Invisible;
                    return; // Skip index 0 execution on load
                }

                resultText.Visibility = ViewStates.Invisible;
                schoolText.Visibility = ViewStates.Invisible;

                //scanButton.Visibility = ViewStates.Visible;
                scanFAB.Visibility = ViewStates.Visible;

                switch (e.Position)
                {
                    case 1: // Moko Isopropyl Alchohol 200 ml
                        productIndex = e.Position;
                        //Maps the .tflite file from your Assets into memory so the AI engine can read it.
                        //modelBuffer = LoadModelFileTFLite("model_unquant.tflite");
                        //modelBuffer = LoadModelFileTFLite("model_unquant_MOKOS_20260517.tflite");
                        //LoadModelFileONNX(this.Assets, "model_unquant_MOKOS_20260519_Opset20.onnx");
                        //LoadModelFileONNX(this.Assets, "model_unquant_MOKOS_20260519_Opset19.onnx");
                        //LoadModelFileONNX(this.Assets, "model_unquant_MOKOS_20260523_Opset19_Scaled_1.onnx");
                        LoadModelFileONNX(this.Assets, "model_unquant_MOKOS_20260523_Opset19_Scaled_2.onnx");
                        break;
                    case 2: // NCP Liquid Antiseptic 100 ml
                        productIndex = e.Position;
                        //modelBuffer = LoadModelFileTFLite("model_unquant_TCP.tflite");
                        //modelBuffer = LoadModelFileTFLite("model_unquant_NCP_20260517.tflite");
                        //LoadModelFileONNX(this.Assets, "model_unquant_NCP_20260522_Opset19_Scaled.onnx");
                        //LoadModelFileONNX(this.Assets, "model_unquant_NCP_20260524_Opset19_Scaled_1.onnx");
                        LoadModelFileONNX(this.Assets, "model_unquant_NCP_20260524_Opset19_Scaled_2.onnx");
                        break;
                    case 3: // Loratadine
                        productIndex = e.Position;
                        //modelBuffer = LoadModelFileTFLite("model_unquant_TCP.tflite");
                        modelBuffer = LoadModelFileTFLite("model_unquant_Loratadine_20260518.tflite");
                        //LoadModelFileONNX(this.Assets, "model_unquant_NCP_20260517.onnx");
                        break;
                    case 4: // Jucopan
                        productIndex = e.Position;
                        //modelBuffer = LoadModelFileTFLite("model_unquant_TCP.tflite");
                        modelBuffer = LoadModelFileTFLite("model_unquant_Jucopan_20260518.tflite");
                        //LoadModelFileONNX(this.Assets, "model_unquant_NCP_20260517.onnx");
                        break;
                    case 5: // Divamine
                        productIndex = e.Position;
                        //modelBuffer = LoadModelFileTFLite("model_unquant_TCP.tflite");
                        modelBuffer = LoadModelFileTFLite("model_unquant_Divamine_20260518.tflite");
                        //LoadModelFileONNX(this.Assets, "model_unquant_NCP_20260517.onnx");
                        break;
                    default:
                        productIndex = e.Position;
                        modelBuffer = LoadModelFileTFLite("model_unquant.tflite");
                        //LoadModelFileONNX(this.Assets, "model_unquant.onnx");
                        break;
                }

                // Initialize the Interpreter
                if (productIndex > 2)
                    tfLite = new Interpreter(modelBuffer);

                // For Native Android Spinner, notify the adapter:
                adapter.NotifyDataSetChanged();
            };
        }

        //Maps the.tflite file from your Assets into memory so the AI engine can read it i.e. Loads the model from Assets
        private MappedByteBuffer LoadModelFileTFLite(string modelFileName)
        {
            using (var assetFileDescriptor = Assets.OpenFd(modelFileName))
            using (var inputStream = new Java.IO.FileInputStream(assetFileDescriptor.FileDescriptor))
            {
                var fileChannel = inputStream.Channel;
                long startOffset = assetFileDescriptor.StartOffset;
                long declaredLength = assetFileDescriptor.DeclaredLength;
                return fileChannel.Map(FileChannel.MapMode.ReadOnly, startOffset, declaredLength);
            }
        }

        public void LoadModelFileONNX(AssetManager assets, string modelFileName)
        {
            // Open the model from the Android project's Assets folder
            using (System.IO.Stream inputStream = assets.Open(modelFileName))
            using (MemoryStream ms = new MemoryStream())
            {
                inputStream.CopyTo(ms);
                byte[] modelBytes = ms.ToArray();

                // Initialize the Microsoft ONNX runtime session
                session = new InferenceSession(modelBytes);
            }
        }

        private DenseTensor<float> ConvertBitmapToNormalizedTensor_1(Bitmap bitmap, int width, int height)
        {
            // 1. Force the bitmap to a standard 32-bit ARGB format to unblock pixel tables
            Bitmap rgbaBitmap = bitmap.Copy(Bitmap.Config.Argb8888, true);

            // Resize the camera frame to match Microsoft's model requirements
            //Bitmap resized = Bitmap.CreateScaledBitmap(bitmap, width, height, false);
            Bitmap resized = Bitmap.CreateScaledBitmap(rgbaBitmap, width, height, false);

            // Setup tensor shape: 1 Image, 3 Channels (RGB), Width, Height
            var tensor = new DenseTensor<float>(new[] { 1, 3, width, height });

            // Extract the raw pixel data table out of the Android hardware layer instantly
            int[] pixels = new int[width * height];
            resized.GetPixels(pixels, 0, width, 0, 0, width, height);

            // Extract pixels and normalize values between 0.0 and 1.0 (as required by Azure models)
            for (int y = 0; y < targetWidthONNX; y++)
            {
                for (int x = 0; x < targetWidthONNX; x++)
                {
                    int index = (y * targetWidthONNX) + x;
                    int pixel = pixels[index];

                    // 3. Native Android color extraction (safely converts directly to 0-255 ints)
                    float r = (float)Android.Graphics.Color.GetRedComponent(pixel);
                    float g = (float)Android.Graphics.Color.GetGreenComponent(pixel);
                    float b = (float)Android.Graphics.Color.GetBlueComponent(pixel);

                    // 4. Force strict floating-point division
                    tensor[0, 0, y, x] = r / 255.0f;
                    tensor[0, 1, y, x] = g / 255.0f;
                    tensor[0, 2, y, x] = b / 255.0f;
                }
            }

            // Clean up memory leaks instantly
            resized.Recycle();
            rgbaBitmap.Recycle();


            var diags = $"[TENSOR CHECK] Sample Pixel (100,100) -> R: {tensor[0, 0, 100, 100]:F4}, G: {tensor[0, 1, 100, 100]:F4}, B: {tensor[0, 2, 100, 100]:F4}";
            System.Diagnostics.Debug.WriteLine(diags);

            RunOnUiThread(() => {
                // Also show a quick popup
                Android.Widget.Toast.MakeText(this, diags, Android.Widget.ToastLength.Long).Show();
            });

            return tensor;
        }
        private Tensor<float> ConvertBitmapToNormalizedTensor_2(Bitmap bitmap, int width, int height)
        {
            Bitmap scaled = Bitmap.CreateScaledBitmap(bitmap, width, height, false);
            var tensor = new DenseTensor<float>(new[] { 1, 3, width, height });

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixel = scaled.GetPixel(x, y);
                    // Extract channels and normalize between 0.0 and 1.0
                    tensor[0, 0, y, x] = Color.GetRedComponent(pixel) / 255f;
                    tensor[0, 1, y, x] = Color.GetGreenComponent(pixel) / 255f;
                    tensor[0, 2, y, x] = Color.GetBlueComponent(pixel) / 255f;
                }
            }
            return tensor;
        }
        private DenseTensor<float> ConvertBitmapToNormalizedTensor_3(Bitmap bitmap)
        {
            var modelInputSize = targetWidthONNX;

            Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, modelInputSize, modelInputSize, false);
            var tensor = new DenseTensor<float>(new[] { 1, 3, modelInputSize, modelInputSize });

            for (int y = 0; y < modelInputSize; y++)
            {
                for (int x = 0; x < modelInputSize; x++)
                {
                    int pixel = scaledBitmap.GetPixel(x, y);
                    // Extract channels and normalize between 0.0f and 1.0f
                    tensor[0, 0, y, x] = Color.GetRedComponent(pixel) / 255f;
                    tensor[0, 1, y, x] = Color.GetGreenComponent(pixel) / 255f;
                    tensor[0, 2, y, x] = Color.GetBlueComponent(pixel) / 255f;
                }
            }
            return tensor;
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                onButtonClick();

                return true;
            }

            return base.OnOptionsItemSelected(item);
        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);


            if (CheckSelfPermission(Android.Manifest.Permission.Camera) != Android.Content.PM.Permission.Granted)
            {
                RequestPermissions(new string[] { Android.Manifest.Permission.Camera }, 0);
            }
            else
            {
                // If already granted, you can safely open the camera
                OpenCameraIntent();
            }

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        void OpenCameraIntent()
        {
            // If already granted, you can safely open the camera
            Intent intent = new Intent(MediaStore.ActionImageCapture);

            // Create a temporary file to store the high-res image
            //Java.IO.File storageDir = GetExternalFilesDir(Android.OS.Environment.DirectoryPictures);
            // FIX: Use internal App Cache directory instead of external storage
            // This completely bypasses SD card permission restrictions
            Java.IO.File storageDir = this.CacheDir;

            // Create a temporary file safely inside internal storage memory
            photoFile = Java.IO.File.CreateTempFile("scan_", ".jpg", storageDir);

            // Get the secure URI for the file
            var photoUri = FileProvider.GetUriForFile(this, PackageName + ".fileprovider", photoFile);

            intent.PutExtra(MediaStore.ExtraOutput, photoUri);

            StartActivityForResult(intent, CameraRequestCode); // 0 is the request code
        }

        private ByteBuffer ConvertBitmapToBuffer(Bitmap bitmap)
        {
            // 1. Calculate buffer size: 1 image * Width * Height * 3 channels (RGB) * 4 bytes per float
            int inputSize = targetWidthTFLite; // Change this if your model expects a different size
            int bufferSize = 1 * inputSize * inputSize * 3 * 4;

            ByteBuffer byteBuffer = ByteBuffer.AllocateDirect(bufferSize);
            byteBuffer.Order(ByteOrder.NativeOrder());
            byteBuffer.Rewind();

            // 2. Resize the captured image to match model requirements
            Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, inputSize, inputSize, true);

            // 3. Extract pixel data into an integer array
            int[] intValues = new int[inputSize * inputSize];
            scaledBitmap.GetPixels(intValues, 0, scaledBitmap.Width, 0, 0, scaledBitmap.Width, scaledBitmap.Height);

            // 4. Loop through pixels and normalize them
            // Most Custom Vision/AutoML models use a 0-255 range normalized to 0.0-1.0
            foreach (int pixelValue in intValues)
            {
                // Extract RGB channels using bit-shifting
                float r = (float)((pixelValue >> 16) & 0xFF) / 255.0f;
                float g = (float)((pixelValue >> 8) & 0xFF) / 255.0f;
                float b = (float)(pixelValue & 0xFF) / 255.0f;

                byteBuffer.PutFloat(r);
                byteBuffer.PutFloat(g);
                byteBuffer.PutFloat(b);
            }

            return byteBuffer;
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            // Check if this is our camera request (0) and if the user actually took a photo (Ok)
            if (requestCode == CameraRequestCode && resultCode == Result.Ok)
            {   
                ToggleScanningAnimation(true);
                ProcessCapturedImage();
            }
        }

        private void ToggleScanningAnimation(bool start)
        {
            if (start)
            {
                laserLine.Visibility = ViewStates.Visible;
                TranslateAnimation anim = new TranslateAnimation(Dimension.RelativeToParent, 0f, Dimension.RelativeToParent, 0f, Dimension.RelativeToParent, 0f, Dimension.RelativeToParent, 0.9f);
                anim.Duration = 1200;
                anim.RepeatCount = Animation.Infinite;
                anim.RepeatMode = RepeatMode.Reverse;
                laserLine.StartAnimation(anim);
            }
            else
            {
                laserLine.ClearAnimation();
                laserLine.Visibility = ViewStates.Gone;
            }
        }

        private void ProcessCapturedImage()
        {
            try
            {
                // 1. Get the dimensions of the original image without loading it into memory
                BitmapFactory.Options options = new BitmapFactory.Options
                {
                    InMutable = true,
                    InSampleSize = 2, // Safely downsamples huge 12MP photos by half for fast mobile processing
                    InJustDecodeBounds = false,
                    InPurgeable = true // Helps with memory on older devices
                };

                // 3. Decode the local file into a usable Android Bitmap object
                Bitmap capturedBitmap = BitmapFactory.DecodeFile(photoFile.AbsolutePath, options);

                if (capturedBitmap != null)
                {
                    int photoW = options.OutWidth;
                    int photoH = options.OutHeight;

                    // 2. Calculate how much to downsample (e.g., if image is 2000px, 
                    // and we need 200px, scaleFactor is 10)
                    int scaleFactor;

                    if (productIndex > 2)
                        scaleFactor = Math.Min(photoW / targetWidthTFLite, photoH / targetHeightTFLite);
                    else if (productIndex <= 2)
                        scaleFactor = Math.Min(photoW / targetWidthONNX, photoH / targetHeightONNX);
                    else
                        scaleFactor = 1;

                    options.InSampleSize = scaleFactor;

                    // 3. Decode the image for real, using the scaleFactor
                    //options.InJustDecodeBounds = false;
                    //options.InSampleSize = scaleFactor;
                    //options.InPurgeable = true; // Helps with memory on older devices


                    // 4. Extract the thumbnail bitmap from the intent
                    // Note: For full-resolution images, you'd usually use a File Path, 
                    // but for initial AI testing, the 'data' extra works.
                    // For a low-resolution thumbnail, extract the bitmap from the intent extras
                    //Bitmap photo = (Android.Graphics.Bitmap)data.Extras.Get("data");

                    // Load the full-resolution bitmap from the file path
                    //Bitmap fullResBitmap = BitmapFactory.DecodeFile(_photoFile.AbsolutePath);
                    Bitmap highResOptimized = BitmapFactory.DecodeFile(photoFile.AbsolutePath, options);

                    if (highResOptimized != null)
                    {
                        // Get the rotation from EXIF metadata
                        ExifInterface exif = new ExifInterface(photoFile.AbsolutePath);
                        int orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Android.Media.Orientation.Normal);

                        // Rotate the bitmap if necessary
                        Bitmap finalImage = RotateBitmapIfRequired(highResOptimized, orientation);

                        // Run your AI scan on the high-quality image
                        if (productIndex <= 2)
                            CheckProduct(finalImage);
                        else if (productIndex > 2)
                            ScanImage(finalImage);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Android.Util.Log.Error("INTENT_SCANNER", $"Failed to process picture file: {ex.Message}");
            }

            // Optional: Delete the file after scanning to save space
            if (photoFile != null && photoFile.Exists())
            {
                photoFile.Delete();
            }
        }

        //Since TFLite expects data in a specific format (usually a 4D array of pixels),
        //you must resize and convert your camera bitmap.
        private void ScanImage(Bitmap photo)
        {
            // 1. Resize to what your model expects (e.g., 224x224)
            Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(photo, targetWidthTFLite, targetHeightTFLite, true);

            // 2. Create an output container by converting Bitmap to ByteBuffer (Float32 format)
            // If your model has 2 labels (0: Genuine, 1: Counterfeit), create a float array of size 2
            var output = new float[1][] { new float[labels.Count] };
            //var output = new float[1, labels.Count];
            //var outputObj = Java.Interop.JavaObjectExtensions.ToJavaObject(output);
            var outputObj = Java.Lang.Object.FromArray(output);

            // 3. Convert Bitmap to ByteBuffer (Required for TFLite)
            // Note: You typically need a helper to loop pixels and convert to float32
            ByteBuffer inputBuffer = ConvertBitmapToBuffer(scaledBitmap);

            // 4. Run Inference
            tfLite.Run(inputBuffer, outputObj);

            // 5. Retrieve results back into a C# array
            var results = outputObj.ToArray<float[]>();

            if (results == null)
            {
                photo.Recycle();

                Android.Widget.Toast.MakeText(this, $"Unknown Error", ToastLength.Long).Show();
            }

            float genuineProb = results[0][0];      // index 0 = Genuine
            float counterfeitProb = results[0][1];  // index 1 = Counterfeit

            // Find which label has the highest probability
            float maxProb = -1;
            int bestIndex = -1;

            for (int i = 0; i < labels.Count; i++)
            {
                if (results[0][i] > maxProb)
                {
                    maxProb = results[0][i];
                    bestIndex = i;
                }
            }

            // Display the result
            //Toast.MakeText(this, $"Genuine Probability: {genuineProb:P}", ToastLength.Long).Show();
            //Toast.MakeText(this, $"Counterfeit Probability: {counterfeitProb:P}", ToastLength.Long).Show();
            //Toast.MakeText(this, $"{labels[bestIndex]} ({maxProb:P0})", ToastLength.Long).Show();

            processResults(genuineProb, counterfeitProb);            

            // Clean up memory
            photo.Recycle();
        }

        private void CheckProduct(Bitmap photo)
        {
            // 1. Convert image to Microsoft ML-compatible format
            //var inputTensor = ConvertBitmapToNormalizedTensor_3(photo);
            var inputTensor = ConvertBitmapToNormalizedTensor_1(photo, targetWidthONNX, targetHeightONNX);            
            // Shape layout remains: [1 Image, 3 Channels, 320 Width, 320 Height]

            // 2. Wrap tensor in the model's required input node name (usually "data" or "input_1")
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            // 3. Run the Microsoft ONNX engine
            using (var outputs = session.Run(inputs))
            {
                // 4. Extract output probabilities (usually named "loss" or "output_1")
                //var results = outputs.FirstOrDefault(o => o.Name == "loss")?.AsTensor<float>();
                // Access the YOLOv8 output matrix
                var outputTensor = outputs.FirstOrDefault(o => o.Name == "output0")?.AsTensor<float>();


                if (outputTensor == null)
                {
                    photo.Recycle();

                    Android.Widget.Toast.MakeText(this, $"Unknown Error", ToastLength.Long).Show();
                }

                // Read scores (Index 0 = Authentic, Index 1 = Counterfeit based on your training labels)
                if (productIndex > 2)
                {
                    float genuineProb = outputTensor[0, 1];   // index 0 = Genuine
                    float counterfeitProb = outputTensor[0, 0]; // index 1 = Counterfeit

                    processResults(genuineProb, counterfeitProb);
                }
                else if (productIndex <= 2)
                {
                    var results = ParseYoloV8Output_4(outputTensor, photo.Width, photo.Height);

                    RunOnUiThread(() =>
                    {
                        // 4.Find your overlay image and status tracking text boxes in your UI layout
                        ImageView overlayCanvasView = FindViewById<ImageView>(Resource.Id.overlayCanvas);
                        //TextView statusTextView = FindViewById<TextView>(Resource.Id.statusText);

                        var statusTextView = $"Scanning merchandise... Center item in frame.";                        

                        if (results != null)
                        {
                            processResults(results);
                            //DrawDetectionOnScreen(results, overlayCanvasView);
                        }
                        else
                        {
                            //statusTextView.Text = "Scanning merchandise... Center item in frame.";
                            Android.Widget.Toast.MakeText(this, statusTextView, Android.Widget.ToastLength.Long).Show();
                            overlayCanvasView.SetImageBitmap(null);

                            ToggleScanningAnimation(false);
                        }
                    });
                }
            }

            // Clean up memory
            photo.Recycle();
        }

        private ScanResult ParseYoloV8Output_1(Tensor<float> output, int origWidth, int origHeight)
        {
            // Initialize holders to find the highest-confidence bounding box array item
            float bestScore = 0f;
            int bestClassIndex = 0;
            Rect bestBox = null;

            // YOLOv8 produces 2100 candidate bounding boxes for a 320x320 image map grid resolution
            int totalCandidates = 2100;

            for (int i = 0; i < totalCandidates; i++)
            {
                // Extract confidence scores for our target definitions
                float genuineScore = output[0, 4, i];
                float counterfeitScore = output[0, 5, i];

                float currentMaxScore = Math.Max(genuineScore, counterfeitScore);
                int currentClassIndex = genuineScore > counterfeitScore ? 0 : 1;

                // Only track items exceeding our minimum target reliability threshold
                if (currentMaxScore > bestScore && currentMaxScore > 0.65f)
                {
                    bestScore = currentMaxScore;
                    bestClassIndex = currentClassIndex;

                    // Extract normalized matrix points
                    float xCenter = output[0, 0, i] / targetWidthONNX;
                    float yCenter = output[0, 1, i] / targetHeightONNX;
                    float boxWidth = output[0, 2, i] / targetWidthONNX;
                    float boxHeight = output[0, 3, i] / targetHeightONNX;

                    // Transform relative fractions back into actual UI screen pixel points
                    int left = (int)((xCenter - (boxWidth / 2)) * origWidth);
                    int top = (int)((yCenter - (boxHeight / 2)) * origHeight);
                    int right = (int)((xCenter + (boxWidth / 2)) * origWidth);
                    int bottom = (int)((yCenter + (boxHeight / 2)) * origHeight);

                    bestBox = new Rect(left, top, right, bottom);
                }
            }

            if (bestBox == null) return null; // No match found in current frame

            return new ScanResult
            {
                Label = labels[bestClassIndex],
                Confidence = bestScore,
                DisplayBox = bestBox
            };
        }

        private ScanResult ParseYoloV8Output_2(Tensor<float> output, int origWidth, int origHeight)
        {
            float bestScore = 0f;
            int bestClassIndex = 0;
            Rect bestBox = null;

            // YOLOv8 outputs 2100 candidate bounding boxes for a 320x320 image map grid resolution
            int totalCandidates = 2100;

            for (int i = 0; i < totalCandidates; i++)
            {
                // CRITICAL FIX: Extract confidence scores directly from Row 4 and Row 5
                float genuineScore = output[0, 4, i];
                float counterfeitScore = output[0, 5, i];

                float currentMaxScore = Math.Max(genuineScore, counterfeitScore);
                int currentClassIndex = genuineScore > counterfeitScore ? 0 : 1;

                // This check will now hit successfully when an object comes into view!
                if (currentMaxScore > bestScore && currentMaxScore > 0.65f)
                {
                    bestScore = currentMaxScore;
                    bestClassIndex = currentClassIndex;

                    // Extract normalized coordinates from Rows 0, 1, 2, and 3
                    float xCenter = output[0, 0, i];
                    float yCenter = output[0, 1, i];
                    float boxWidth = output[0, 2, i];
                    float boxHeight = output[0, 3, i];

                    // Transform relative values back into actual UI screen pixel points
                    int left = (int)((xCenter - (boxWidth / 2f)) * origWidth);
                    int top = (int)((yCenter - (boxHeight / 2f)) * origHeight);
                    int right = (int)((xCenter + (boxWidth / 2f)) * origWidth);
                    int bottom = (int)((yCenter + (boxHeight / 2f)) * origHeight);

                    bestBox = new Rect(left, top, right, bottom);
                }
            }

            if (bestBox == null) return null; // Returns null if no authentic or counterfeit item is detected

            return new ScanResult
            {
                Label = labels[bestClassIndex],
                Confidence = bestScore,
                DisplayBox = bestBox
            };
        }

        private ScanResult ParseYoloV8Output_3(Tensor<float> output, int origWidth, int origHeight)
        {
            float bestScore = 0f;
            int bestClassIndex = 0;
            Rect bestBox = null;

            // Get the exact dimensions of the output tensor to handle transposition dynamically
            int dim1 = output.Dimensions[1]; // Could be 6 or 2100
            int dim2 = output.Dimensions[2]; // Could be 2100 or 6

            int totalCandidates = (dim1 == 2100) ? dim1 : dim2;
            bool isTransposed = (dim1 == 2100);

            System.Diagnostics.Debug.WriteLine($"[ML DIAGNOSTICS] ONNX Output Dimensions detected: [1, {dim1}, {dim2}]");

            for (int i = 0; i < totalCandidates; i++)
            {
                float genuineScore = 0f;
                float counterfeitScore = 0f;
                float xCenter = 0f, yCenter = 0f, boxWidth = 0f, boxHeight = 0f;

                if (isTransposed)
                {
                    // Format: [1, 2100, 6] -> Indexing is [0, candidate, row]
                    xCenter = output[0, i, 0];
                    yCenter = output[0, i, 1];
                    boxWidth = output[0, i, 2];
                    boxHeight = output[0, i, 3];
                    genuineScore = output[0, i, 4];
                    counterfeitScore = output[0, i, 5];
                }
                else
                {
                    // Format: [1, 6, 2100] -> Indexing is [0, row, candidate]
                    xCenter = output[0, 0, i];
                    yCenter = output[0, 1, i];
                    boxWidth = output[0, 2, i];
                    boxHeight = output[0, 3, i];
                    genuineScore = output[0, 4, i];
                    counterfeitScore = output[0, 5, i];
                }

                float currentMaxScore = Math.Max(genuineScore, counterfeitScore);
                int currentClassIndex = genuineScore > counterfeitScore ? 0 : 1;

                // Print a sample line to the Output window to verify the actual values
                if (i == 0 || currentMaxScore > 0.10f)
                {
                    System.Diagnostics.Debug.WriteLine($"[ML TRACE] Candidate {i} -> Gen Conf: {genuineScore:F4}, Fake Conf: {counterfeitScore:F4}");
                }

                if (currentMaxScore > bestScore && currentMaxScore > 0.40f) // Dropped threshold to 40% for testing
                {
                    bestScore = currentMaxScore;
                    bestClassIndex = currentClassIndex;

                    // Transform relative values back into actual UI screen pixel positions
                    int left = (int)((xCenter - (boxWidth / 2f)) * origWidth);
                    int top = (int)((yCenter - (boxHeight / 2f)) * origHeight);
                    int right = (int)((xCenter + (boxWidth / 2f)) * origWidth);
                    int bottom = (int)((yCenter + (boxHeight / 2f)) * origHeight);

                    bestBox = new Rect(left, top, right, bottom);
                }
            }

            if (bestBox == null)
            {
                System.Diagnostics.Debug.WriteLine("[ML WARNING] Loop complete. No boxes crossed the confidence threshold.");
                return null;
            }

            return new ScanResult
            {
                Label = labels[bestClassIndex],
                Confidence = bestScore,
                DisplayBox = bestBox
            };
        }

        private ScanResult ParseYoloV8Output_4(Tensor<float> output, int origWidth, int origHeight)
        {
            float bestScore = 0f;
            int bestClassIndex = 0;
            Rect bestBox = null;

            // Flatten the multi-dimensional tensor into a standard one-dimensional C# array
            float[] flatOutput = output.ToArray();

            //int totalRows = 6;         // cx, cy, w, h, Genuine, Counterfeit
            int totalCandidates = 2100; // Grid bounding anchor count

            for (int i = 0; i < totalCandidates; i++)
            {
                // Calculate the exact flat indices for each row in column 'i'
                // Formula: (row_index * total_columns) + column_index
                float cx = flatOutput[(0 * totalCandidates) + i];
                float cy = flatOutput[(1 * totalCandidates) + i];
                float boxWidth = flatOutput[(2 * totalCandidates) + i];
                float boxHeight = flatOutput[(3 * totalCandidates) + i];

                // Extract raw logit values
                //float rawGenuine = flatOutput[(4 * totalCandidates) + i];
                //float rawFake = flatOutput[(5 * totalCandidates) + i];
                float genuineScore = flatOutput[(4 * totalCandidates) + i];
                float counterfeitScore = flatOutput[(5 * totalCandidates) + i];

                //float genuineScore = flatOutput[(4 * totalCandidates) + i];
                //float counterfeitScore = flatOutput[(5 * totalCandidates) + i];
                // CRITICAL FIX: Apply Sigmoid Activation to convert raw logits to 0.0 - 1.0 percentages
                //float genuineScore = 1.0f / (1.0f + (float)Math.Exp(-rawGenuine));
                //float counterfeitScore = 1.0f / (1.0f + (float)Math.Exp(-rawFake));

                float currentMaxScore = Math.Max(genuineScore, counterfeitScore);
                int currentClassIndex = genuineScore > counterfeitScore ? 0 : 1;

                // Print debug traces for the first few candidates to find your real class scores
                if (i < 5)
                {                    
                    var diags1 = $"[DEBUG RE-MAPPED] Candidate {i} -> Rows: [{cx:F1}, {cy:F1}, {boxWidth:F1}, {boxHeight:F1}] | Gen: {genuineScore:F4}, Fake: {counterfeitScore:F4}";
                    System.Diagnostics.Debug.WriteLine(diags1);
                    //var diags2 = $"[SIGMOID ACTIVATED] Candidate {i} -> Gen: {genuineScore:P1}, Fake: {counterfeitScore:P1}";
                    //System.Diagnostics.Debug.WriteLine(diags2);
                    var diags2 = $"[YOLOv8 DIRECT] Candidate {i} -> Gen: {genuineScore:P1}, Fake: {counterfeitScore:P1}";
                    System.Diagnostics.Debug.WriteLine(diags2);                    

                    RunOnUiThread(() => {
                        // Also show a quick popup
                        Android.Widget.Toast.MakeText(this, diags1, Android.Widget.ToastLength.Long).Show();
                        Android.Widget.Toast.MakeText(this, diags2, Android.Widget.ToastLength.Long).Show();
                        //Android.Widget.Toast.MakeText(this, $"{labels[bestIndex]} ({maxProb:P0})", ToastLength.Long).Show();
                    });
                }

                // Your threshold check will now catch real floating values
                if (currentMaxScore > bestScore && currentMaxScore > confidenceThreshold)
                {
                    bestScore = currentMaxScore;
                    bestClassIndex = currentClassIndex;

                    // Convert normalized coordinates back to actual UI screen pixels
                    int left = (int)((cx - (boxWidth / 2f)) * origWidth);
                    int top = (int)((cy - (boxHeight / 2f)) * origHeight);
                    int right = (int)((cx + (boxWidth / 2f)) * origWidth);
                    int bottom = (int)((cy + (boxHeight / 2f)) * origHeight);

                    bestBox = new Rect(left, top, right, bottom);
                }
            }

            if (bestBox == null)
            {
                System.Diagnostics.Debug.WriteLine("[ML WARNING] Loop complete. No boxes crossed the threshold.");
                return null;
            }

            return new ScanResult
            {
                Label = labels[bestClassIndex],
                Confidence = bestScore,
                DisplayBox = bestBox
            };
        }
        public class ScanResult
        {
            public string Label { get; set; }
            public float Confidence { get; set; }
            public Rect DisplayBox { get; set; }
        }
        private void processResults(ScanResult results)
        {
            // Determine the result based on a confidence threshold (e.g., 70%)
            string status;
            Android.Graphics.Color color;

            if (results.Confidence > 0.70f && results.Label == "Genuine") // 70% threshold
            {
                status = $"Genuine Probability ({results.Confidence:P0} match)";
                color = Android.Graphics.Color.Green;
            }
            else if (results.Confidence > 0.70f && results.Label == "Counterfeit") // 70% threshold
            {
                status = $"Counterfeit Probability: ({results.Confidence:P0} match)";
                color = Android.Graphics.Color.Red;
            }
            else
            {
                status = "UNSURE - Please scan again";
                color = Android.Graphics.Color.Orange;
            }

            // 3. Update the UI on the Main Thread
            RunOnUiThread(() => {
                resultText.Text = status;
                resultText.SetTextColor(color);
                resultText.Visibility = ViewStates.Visible;

                schoolText.SetTextColor(schoolTextColor);
                schoolText.Visibility = ViewStates.Visible;

                //scanButton.Visibility = ViewStates.Invisible;
                //scanFAB.Visibility = ViewStates.Invisible;

                // Also show a quick popup
                Android.Widget.Toast.MakeText(this, status, Android.Widget.ToastLength.Long).Show();
                //Android.Widget.Toast.MakeText(this, $"{labels[bestIndex]} ({maxProb:P0})", ToastLength.Long).Show();
            });

            ToggleScanningAnimation(false);
        }
        private void processResults(float genuineProb, float counterfeitProb)
        {
            // Determine the result based on a confidence threshold (e.g., 70%)
            string status;
            Android.Graphics.Color color;

            if (genuineProb > 0.80f) // 80% threshold
            {
                status = $"Genuine Probability ({genuineProb:P0} match)";
                color = Android.Graphics.Color.Green;
            }
            else if (counterfeitProb > 0.80f) // 80% threshold
            {
                status = $"Counterfeit Probability: ({counterfeitProb:P0} match)";
                color = Android.Graphics.Color.Red;
            }
            else
            {
                status = "UNSURE - Please scan again";
                color = Android.Graphics.Color.Orange;
            }

            // 3. Update the UI on the Main Thread
            RunOnUiThread(() => {
                resultText.Text = status;
                resultText.SetTextColor(color);
                resultText.Visibility = ViewStates.Visible;

                schoolText.SetTextColor(schoolTextColor);
                schoolText.Visibility = ViewStates.Visible;

                //scanButton.Visibility = ViewStates.Invisible;
                //scanFAB.Visibility = ViewStates.Invisible;

                // Also show a quick popup
                Android.Widget.Toast.MakeText(this, status, Android.Widget.ToastLength.Long).Show();
                //Android.Widget.Toast.MakeText(this, $"{labels[bestIndex]} ({maxProb:P0})", ToastLength.Long).Show();
            });

            ToggleScanningAnimation(false);
        }

        public void DrawDetectionOnScreen(ScanResult result, ImageView overlayImageView)
        {
            if (result == null || result.Confidence < 0.35f) // Using 35% threshold for small dataset testing
            {
                // Clear screen if confidence is too low or nothing is found
                overlayImageView.SetImageBitmap(null);
                return;
            }

            // 1. Create a transparent canvas matching your display area
            Bitmap bitmap = Bitmap.CreateBitmap(overlayImageView.Width, overlayImageView.Height, Bitmap.Config.Argb8888);
            Canvas canvas = new Canvas(bitmap);

            // 2. Setup your brand coloring rules dynamically
            Color boxColor = (result.Label == "Genuine") ? Color.Green : Color.Red;

            // 3. Configure the brush styles for the bounding box
            Paint boxPaint = new Paint
            {
                Color = boxColor,
                StrokeWidth = 8f
            };
            boxPaint.SetStyle(Paint.Style.Stroke);

            // 4. Configure text styles for the label overlay banner
            Paint textPaint = new Paint
            {
                Color = Color.White,
                TextSize = 40f,
                FakeBoldText = true
            };

            Paint textBackgroundPaint = new Paint { Color = boxColor };

            // 5. Draw the bounding rectangle over the detected coordinates
            canvas.DrawRect(result.DisplayBox, boxPaint);

            // 6. Draw a solid banner box right above it to hold the percentage text
            Rect banner = new Rect(result.DisplayBox.Left, result.DisplayBox.Top - 50, result.DisplayBox.Left + 350, result.DisplayBox.Top);
            canvas.DrawRect(banner, textBackgroundPaint);

            // 7. Write the final readable specifier: e.g. "Genuine: 84%"
            canvas.DrawText($"{result.Label}: {result.Confidence:P0}", result.DisplayBox.Left + 10, result.DisplayBox.Top - 12, textPaint);

            // 8. Push the final canvas straight onto your UI screen layout view
            overlayImageView.SetImageBitmap(bitmap);

            ToggleScanningAnimation(false);
        }

        private Bitmap RotateBitmapIfRequired(Bitmap img, int orientation)
        {
            Matrix matrix = new Matrix();

            switch (orientation)
            {
                case (int)Android.Media.Orientation.Rotate90:
                    matrix.PostRotate(90);
                    break;
                case (int)Android.Media.Orientation.Rotate180:
                    matrix.PostRotate(180);
                    break;
                case (int)Android.Media.Orientation.Rotate270:
                    matrix.PostRotate(270);
                    break;
                default:
                    return img; // No rotation needed
            }

            Bitmap rotatedBitmap = Bitmap.CreateBitmap(img, 0, 0, img.Width, img.Height, matrix, true);
            img.Recycle(); // Free up the memory from the original unrotated image
            return rotatedBitmap;
        }
    }
}