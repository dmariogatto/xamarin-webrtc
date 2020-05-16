using System;
using UIKit;

namespace WebRtc.iOS.Code
{
    public static class ColorHelper
    {
        public static UIColor SystemBackgroundColor =>
            UIDevice.CurrentDevice.CheckSystemVersion(13, 0)
            ? UIColor.SystemBackgroundColor
            : UIColor.White;
    }
}
