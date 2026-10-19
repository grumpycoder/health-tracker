using Foundation;
using Microsoft.Identity.Client;
using UIKit;

namespace FitRecoveryLog;

// iOS 27 requires the UIScene lifecycle for apps built against the iOS 26+ SDK —
// without this (and the UIApplicationSceneManifest in Info.plist) UIKit traps at
// launch before FinishedLaunching runs. Registered as "SceneDelegate" to match
// the UISceneDelegateClassName in Info.plist.
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
	// Under the scene lifecycle iOS delivers URL opens to the scene, not to
	// AppDelegate.OpenUrl, so the MSAL sign-in redirect is handed back here.
	[Export("scene:openURLContexts:")]
	public void OpenUrlContexts(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
	{
		foreach (var context in urlContexts)
			AuthenticationContinuationHelper.SetAuthenticationContinuationEventArgs(context.Url);
	}
}
