using Android.App;
using Android.Content.PM;
using Android.OS;

namespace Broadside.Viewer.Maui.Sample;

[Activity(Name = "dev.broadside.viewer.sample.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Smoke run: adb shell am start -n dev.broadside.viewer.sample/.MainActivity --ez smoke true
        if (Intent?.GetBooleanExtra("smoke", false) == true)
        {
            SmokeTest.Requested = true;
        }

        // Frame bench (#357): adb shell am start -n dev.broadside.viewer.sample/.MainActivity --ez frames true
        if (Intent?.GetBooleanExtra("frames", false) == true)
        {
            FrameBench.Requested = true;
        }

        // Large-photo bench (#428): --ez photo true, and --ez photoFull true for a full-resolution decode
        if (Intent?.GetBooleanExtra("photo", false) == true)
        {
            PhotoBench.Requested = true;
            PhotoBench.FullDecode = Intent.GetBooleanExtra("photoFull", false);
        }

        base.OnCreate(savedInstanceState);
    }
}
