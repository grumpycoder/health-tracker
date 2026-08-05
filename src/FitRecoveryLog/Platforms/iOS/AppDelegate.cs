using Foundation;
using Microsoft.Identity.Client;
using UIKit;

namespace FitRecoveryLog;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	// Hands the MSAL sign-in redirect back to MSAL when it arrives via the app's URL scheme.
	public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
	{
		AuthenticationContinuationHelper.SetAuthenticationContinuationEventArgs(url);
		return true;
	}

	public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
	{
		// Setting the phone down hard reads as a shake, and any focused input
		// (reps, hold seconds) makes iOS offer "Undo Typing" mid-workout.
		// Shake-to-undo has no value in this app's short inputs — disable it.
		application.ApplicationSupportsShakeToEdit = false;
		return base.FinishedLaunching(application, launchOptions);
	}
}
