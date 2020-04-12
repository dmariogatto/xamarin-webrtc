using Foundation;
using UIKit;

namespace WebRtc.iOS
{
    // The UIApplicationDelegate for the application. This class is responsible for launching the
    // User Interface of the application, as well as listening (and optionally responding) to application events from iOS.
    [Register("AppDelegate")]
    public class AppDelegate : UIResponder, IUIApplicationDelegate
    {
        private UIWindow _window;

        [Export("application:didFinishLaunchingWithOptions:")]
        public bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            // Override point for customization after application launch.
            // If not required for your application you can safely delete this method

            _window = new UIWindow(UIScreen.MainScreen.Bounds);
            var vc = new ViewController();

            _window.RootViewController = vc;
            _window.MakeKeyAndVisible();

            return true;
        }
    }
}

