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

            // Find the TextView and make the hyperlink active
            var creditsView = FindViewById<TextView>(Resource.Id.credits_text_Git_TextView);
            var creditsView2 = FindViewById<TextView>(Resource.Id.credits_text_TMAI_TextView);
            var creditsView3 = FindViewById<TextView>(Resource.Id.credits_text_TFLite_TextView);
            var creditsView4 = FindViewById<TextView>(Resource.Id.credits_text_Azure_TextView);
            var creditsView5 = FindViewById<TextView>(Resource.Id.credits_text_Colab_TextView);

            // 2. Fetch the raw HTML string from resources
            string rawHtml = GetText(Resource.String.credits_text_Git);
            string rawHtml2 = GetText(Resource.String.credits_text_TMAI);
            string rawHtml3 = GetText(Resource.String.credits_text_TFLite);
            string rawHtml4 = GetText(Resource.String.credits_text_Azure);
            string rawHtml5 = GetText(Resource.String.credits_text_Colab);

            // 3. Convert the HTML string into a stylized Spannable string
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.N)
            {
                creditsView.TextFormatted = Android.Text.Html.FromHtml(rawHtml, Android.Text.FromHtmlOptions.ModeLegacy);
                creditsView2.TextFormatted = Android.Text.Html.FromHtml(rawHtml2, Android.Text.FromHtmlOptions.ModeLegacy);
                creditsView3.TextFormatted = Android.Text.Html.FromHtml(rawHtml3, Android.Text.FromHtmlOptions.ModeLegacy);
                creditsView4.TextFormatted = Android.Text.Html.FromHtml(rawHtml4, Android.Text.FromHtmlOptions.ModeLegacy);
                creditsView5.TextFormatted = Android.Text.Html.FromHtml(rawHtml5, Android.Text.FromHtmlOptions.ModeLegacy);
            }
            else
            {
                // For older Android versions (API 23 and below)
                #pragma warning disable CS0618
                creditsView.TextFormatted = Android.Text.Html.FromHtml(rawHtml);
                creditsView2.TextFormatted = Android.Text.Html.FromHtml(rawHtml2);
                creditsView3.TextFormatted = Android.Text.Html.FromHtml(rawHtml3);
                creditsView4.TextFormatted = Android.Text.Html.FromHtml(rawHtml4);
                creditsView5.TextFormatted = Android.Text.Html.FromHtml(rawHtml5);
                #pragma warning restore CS0618
            }

            // 4. Activate the link click listener logic
            creditsView.MovementMethod = Android.Text.Method.LinkMovementMethod.Instance;
            creditsView2.MovementMethod = Android.Text.Method.LinkMovementMethod.Instance;
            creditsView3.MovementMethod = Android.Text.Method.LinkMovementMethod.Instance;
            creditsView4.MovementMethod = Android.Text.Method.LinkMovementMethod.Instance;
            creditsView5.MovementMethod = Android.Text.Method.LinkMovementMethod.Instance;
        }
    }
}