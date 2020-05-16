using System;
using Xam.WebRtc.Android;

namespace WebRtc.Android.Observers
{
    public class SdpObserver : Java.Lang.Object, ISdpObserver
    {
        private readonly Action<string> _onCreateFailure;
        private readonly Action<SessionDescription> _onCreateSuccess;
        private readonly Action<string> _onSetFailure;
        private readonly Action _onSetSuccess;

        public static SdpObserver Empty() => new SdpObserver();

        public static SdpObserver OnCreate(Action<SessionDescription> onCreateSuccess, Action<string> onCreateFailure) =>
            new SdpObserver(onCreateSuccess: onCreateSuccess, onCreateFailure: onCreateFailure);

        public static SdpObserver OnCreateSuccess(Action<SessionDescription> onCreateSuccess) =>
            new SdpObserver(onCreateSuccess: onCreateSuccess);
        public static SdpObserver OnCreateFailure(Action<string> onCreateFailure) =>
            new SdpObserver(onCreateFailure: onCreateFailure);

        public static SdpObserver OnSet(Action onSetSuccess, Action<string> onSetFailure) =>
            new SdpObserver(onSetSuccess: onSetSuccess, onSetFailure: onSetFailure);

        public static SdpObserver OnSetSuccess(Action onSetSuccess) =>
            new SdpObserver(onSetSuccess: onSetSuccess);
        public static SdpObserver OnSetFailure(Action<string> onSetFailure) =>
            new SdpObserver(onSetFailure: onSetFailure);
        

        public SdpObserver(
            Action<string> onCreateFailure = null,
            Action<SessionDescription> onCreateSuccess = null,
            Action<string> onSetFailure = null,
            Action onSetSuccess = null)
        {
            _onCreateFailure = onCreateFailure;
            _onCreateSuccess = onCreateSuccess;
            _onSetFailure = onSetFailure;
            _onSetSuccess = onSetSuccess;
        }

        public void OnCreateFailure(string p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(SdpObserver)}:{nameof(OnCreateFailure)} '{p0}'");

            _onCreateFailure?.Invoke(p0);
        }

        public void OnCreateSuccess(SessionDescription p0)
        {
            _onCreateSuccess?.Invoke(p0);
        }

        public void OnSetFailure(string p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(SdpObserver)}:{nameof(OnSetFailure)} '{p0}'");

            _onSetFailure?.Invoke(p0);
        }

        public void OnSetSuccess()
        {
            _onSetSuccess?.Invoke();
        }
    }
}