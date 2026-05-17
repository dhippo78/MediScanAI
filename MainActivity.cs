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
        
        private bool isInitialCheck = true;

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
            products = new List<string> { "Select a product...", "Moko Isopropyl Alchohol 200 ml", "NCP Liquid Antiseptic 100 ml" };
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
                        //Maps the .tflite file from your Assets into memory so the AI engine can read it.
                        modelBuffer = LoadModelFile("model_unquant.tflite");
                        break;
                    case 2: // NCP Liquid Antiseptic 100 ml
                        modelBuffer = LoadModelFile("model_unquant_TCP.tflite");
                        break;
                    default:
                        modelBuffer = LoadModelFile("model_unquant.tflite");
                        break;
                }

                // Initialize the Interpreter
                tfLite = new Interpreter(modelBuffer);

                // For Native Android Spinner, notify the adapter:
                adapter.NotifyDataSetChanged();
            };
        }

        //Maps the.tflite file from your Assets into memory so the AI engine can read it i.e. Loads the model from Assets
        private MappedByteBuffer LoadModelFile(string fileName)
        {
            using (var assetFileDescriptor = Assets.OpenFd(fileName))
            using (var inputStream = new Java.IO.FileInputStream(assetFileDescriptor.FileDescriptor))
            {
                var fileChannel = inputStream.Channel;
                long startOffset = assetFileDescriptor.StartOffset;
                long declaredLength = assetFileDescriptor.DeclaredLength;
                return fileChannel.Map(FileChannel.MapMode.ReadOnly, startOffset, declaredLength);
            }
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
            Java.IO.File storageDir = GetExternalFilesDir(Android.OS.Environment.DirectoryPictures);
            photoFile = Java.IO.File.CreateTempFile("scan_", ".jpg", storageDir);

            // Get the secure URI for the file
            var photoUri = FileProvider.GetUriForFile(this, PackageName + ".fileprovider", photoFile);

            intent.PutExtra(MediaStore.ExtraOutput, photoUri);

            StartActivityForResult(intent, CameraRequestCode); // 0 is the request code
        }

        private ByteBuffer ConvertBitmapToBuffer(Bitmap bitmap)
        {
            // 1. Calculate buffer size: 1 image * Width * Height * 3 channels (RGB) * 4 bytes per float
            int inputSize = 224; // Change this if your model expects a different size
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
            //To prevent Out of Memory(OOM) crashes, you should decode the image at a size close to what your AI model
            //actually needs(e.g., 224x224) rather than loading the massive 12MP file into memory first.
            int targetWidth = 224; // Match your model input width
            int targetHeight = 224; // Match your model input height

            // 1. Get the dimensions of the original image without loading it into memory
            BitmapFactory.Options options = new BitmapFactory.Options
            {
                InJustDecodeBounds = true
            };
            BitmapFactory.DecodeFile(photoFile.AbsolutePath, options);

            int photoW = options.OutWidth;
            int photoH = options.OutHeight;

            // 2. Calculate how much to downsample (e.g., if image is 2000px, 
            // and we need 200px, scaleFactor is 10)
            int scaleFactor = Math.Min(photoW / targetWidth, photoH / targetHeight);

            // 3. Decode the image for real, using the scaleFactor
            options.InJustDecodeBounds = false;
            options.InSampleSize = scaleFactor;
            options.InPurgeable = true; // Helps with memory on older devices


            // 4. Extract the thumbnail bitmap from the intent
            // Note: For full-resolution images, you'd usually use a File Path, 
            // but for initial AI testing, the 'data' extra works.
            // For a low-resolution thumbnail, extract the bitmap from the intent extras
            //Bitmap photo = (Android.Graphics.Bitmap)data.Extras.Get("data");

            // Load the full-resolution bitmap from the file path
            //Bitmap fullResBitmap = BitmapFactory.DecodeFile(_photoFile.AbsolutePath);
            Bitmap highResOptimized = BitmapFactory.DecodeFile(photoFile.AbsolutePath, options);

            // Get the rotation from EXIF metadata
            ExifInterface exif = new ExifInterface(photoFile.AbsolutePath);
            int orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Android.Media.Orientation.Normal);

            // Rotate the bitmap if necessary
            Bitmap finalImage = RotateBitmapIfRequired(highResOptimized, orientation);

            // Display the image in an ImageView
            //_imageView.SetImageBitmap(photo);

            // 5. Call the AI scanning function
            //ScanImage(photo);

            // Run your AI scan on the high-quality image
            //ScanImage(fullResBitmap);
            ScanImage(finalImage);

            // Optional: Delete the file after scanning to save space
            // _photoFile.Delete();
        }

        //Since TFLite expects data in a specific format (usually a 4D array of pixels),
        //you must resize and convert your camera bitmap.
        private void ScanImage(Bitmap photo)
        {
            // 1. Resize to what your model expects (e.g., 224x224)
            Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(photo, 224, 224, true);

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

            // Determine the result based on a confidence threshold (e.g., 70%)
            string status;
            Android.Graphics.Color color;

            if (genuineProb > 0.70f)
            {
                status = $"Genuine Probability ({genuineProb:P0})";
                color = Android.Graphics.Color.Green;
            }
            else if (counterfeitProb > 0.70f)
            {
                status = $"Counterfeit Probability ({counterfeitProb:P0})";
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
                Android.Widget.Toast.MakeText(this, $"{labels[bestIndex]} ({maxProb:P0})", ToastLength.Long).Show();
            });

            // Clean up memory
            photo.Recycle();

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