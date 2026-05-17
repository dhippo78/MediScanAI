using Android.App;
using Android.OS;
using Android.Widget;

namespace MediScanAI
{
    [Activity(Label = "CreditsActivity")]
    public class CreditsActivity : Activity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Create your application here

            // Set our view from the "credits_layout" layout resource
            SetContentView(Resource.Layout.credits);
        }
    }
}