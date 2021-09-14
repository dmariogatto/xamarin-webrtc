using Android.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebRtc.Android.Observers;
using Xam.WebRtc.Android;
using Xamarin.Essentials;

namespace WebRtc.Android.Code
{
    public interface IWebRtcObserver
    {
        void OnGenerateCandiate(IceCandidate iceCandidate);
        void OnIceConnectionStateChanged(PeerConnection.IceConnectionState iceConnectionState);
        void OnOpenDataChannel();
        void OnReceiveData(byte[] data);
        void OnReceiveMessage(string message);
        void OnConnectWebRtc();
        void OnDisconnectWebRtc();
    }

    public class WebRtcClient : Java.Lang.Object, PeerConnection.IObserver, DataChannel.IObserver
    {
        private readonly Context _context;

        private readonly IWebRtcObserver _observer;

        private readonly SurfaceViewRenderer _remoteView;
        private readonly SurfaceViewRenderer _localView;

        private readonly List<PeerConnection.IceServer> _iceServers;

        private IEglBase _eglBase;
        private PeerConnectionFactory _peerConnectionFactory;

        private VideoTrack _localVideoTrack;
        private AudioTrack _localAudioTrack;

        private readonly object _connectionLock = new object();
        private PeerConnection _peerConnection;
        private DataChannel _dataChannel;

        private (PeerConnection peer, DataChannel data) _connection
        {
            get
            {
                if (_peerConnection == null)
                {
                    lock (_connectionLock)
                    {
                        if (_peerConnection == null)
                        {
                            _peerConnection = SetupPeerConnection();
                        }
                    }
                }

                return (_peerConnection, _dataChannel);
            }
        }

        private bool _isConnected;

        public WebRtcClient(
            Context context,
            SurfaceViewRenderer remoteView,
            SurfaceViewRenderer localView,
            IWebRtcObserver observer)
        {
            _context = context;
            _remoteView = remoteView;
            _localView = localView;

            _observer = observer;

            _iceServers = new List<PeerConnection.IceServer>(1)
            {
                PeerConnection.IceServer
                .InvokeBuilder("stun:stun.l.google.com:19302")
                .CreateIceServer()
            };

            var options = PeerConnectionFactory.InitializationOptions
                .InvokeBuilder(_context)
                .CreateInitializationOptions();

            PeerConnectionFactory.Initialize(options);

            _eglBase = EglBase.Create();
            _peerConnectionFactory = PeerConnectionFactory.InvokeBuilder()
                .SetVideoDecoderFactory(new DefaultVideoDecoderFactory(_eglBase.EglBaseContext))
                .SetVideoEncoderFactory(new DefaultVideoEncoderFactory(_eglBase.EglBaseContext, true, true))
                .SetOptions(new PeerConnectionFactory.Options())
                .CreatePeerConnectionFactory();

            InitView(_localView);
            InitView(_remoteView);

            var cameraEnum = new Camera2Enumerator(_context);
            var deviceNames = cameraEnum.GetDeviceNames();
            var cameraName = deviceNames.First(dn => DeviceInfo.DeviceType == DeviceType.Virtual
                ? cameraEnum.IsBackFacing(dn)
                : cameraEnum.IsFrontFacing(dn));
            var videoCapturer = cameraEnum.CreateCapturer(cameraName, null);

            var localVideoSource = _peerConnectionFactory.CreateVideoSource(false);
            var surfaceTextureHelper = SurfaceTextureHelper.Create(
                Java.Lang.Thread.CurrentThread().Name,
                _eglBase.EglBaseContext);

            videoCapturer.Initialize(surfaceTextureHelper, _context, localVideoSource.CapturerObserver);
            videoCapturer.StartCapture(640, 480, 30);

            _localVideoTrack = _peerConnectionFactory.CreateVideoTrack("video0", localVideoSource);
            _localVideoTrack.AddSink(_localView);

            var localAudioSource = _peerConnectionFactory.CreateAudioSource(new MediaConstraints());
            _localAudioTrack = _peerConnectionFactory.CreateAudioTrack("audio0", localAudioSource);
        }

        public void Connect(Action<SessionDescription, string> completionHandler)
        {
            _dataChannel = SetupDataChannel();

            var mediaConstraints = new MediaConstraints();
            _connection.peer.CreateOffer(SdpObserver.OnCreateSuccess((sdp) =>
            {
                _connection.peer.SetLocalDescription(
                    SdpObserver.OnSet(() =>
                    {
                        completionHandler(sdp, string.Empty);
                    },
                    (string err) =>
                    {
                        completionHandler(null, err);
                    }),
                    sdp);

            }), mediaConstraints);
        }

        public void Disconnect()
        {
            if (_peerConnection != null)
            {
                // https://bugs.chromium.org/p/webrtc/issues/detail?id=6924
                Task.Run(() =>
                {
                    lock (_peerConnection)
                    {
                        if (_peerConnection != null)
                        {
                            _dataChannel?.Close();
                            _peerConnection?.Close();

                            _dataChannel?.Dispose();
                            _peerConnection?.Dispose();

                            _dataChannel = null;
                            _peerConnection = null;
                        }
                    }
                });
            }
        }

        public void ReceiveOffer(SessionDescription offerSdp, Action<SessionDescription, string> completionHandler)
        {
            _connection.peer.SetRemoteDescription(
                SdpObserver.OnSet(() =>
                {
                    var mediaConstraints = new MediaConstraints();
                    _connection.peer.CreateAnswer(
                        SdpObserver.OnCreate((answerSdp) =>
                        {
                            _connection.peer.SetLocalDescription(SdpObserver.OnSet(() =>
                            {
                                completionHandler(answerSdp, string.Empty);
                            },
                            (err) =>
                            {
                                completionHandler(null, err);
                            }),
                            answerSdp);
                        },
                        (err) =>
                        {
                            completionHandler(null, err);
                        }),
                        mediaConstraints);
                },
                (err) =>
                {
                    completionHandler(null, err);
                }),
                offerSdp);
        }

        public void ReceiveAnswer(SessionDescription answerSdp, Action<SessionDescription, string> completionHandler)
        {
            _connection.peer.SetRemoteDescription(
                SdpObserver.OnSet(() =>
                {
                    completionHandler(answerSdp, string.Empty);
                },
                (err) =>
                {
                    completionHandler(null, err);
                }),
                answerSdp);
        }

        public bool SendMessage(string message)
        {
            if (_connection.data != null && _connection.data.InvokeState() == DataChannel.State.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                var buffer = new DataChannel.Buffer(Java.Nio.ByteBuffer.Wrap(bytes), false);
                var result = _connection.data.Send(buffer);
                return result;
            }

            return false;
        }

        public void ReceiveCandidate(IceCandidate candidate)
        {
            _connection.peer.AddIceCandidate(candidate);
        }

        private void InitView(SurfaceViewRenderer view)
        {
            view.SetMirror(true);
            view.SetEnableHardwareScaler(true);
            view.Init(_eglBase.EglBaseContext, null);
        }

        private PeerConnection SetupPeerConnection()
        {
            var rtcConfig = new PeerConnection.RTCConfiguration(_iceServers);

            var pc = _peerConnectionFactory.CreatePeerConnection(
                rtcConfig,
                this);

            pc.AddTrack(_localVideoTrack, new[] { "stream0" });
            pc.AddTrack(_localAudioTrack, new[] { "stream0" });

            return pc;
        }

        private DataChannel SetupDataChannel()
        {
            var init = new DataChannel.Init()
            {
                Id = 1
            };

            var dc = _connection.peer.CreateDataChannel("dataChannel", init);
            dc.RegisterObserver(this);

            return dc;
        }

        #region PeerConnectionObserver
        public void OnAddStream(MediaStream p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnAddStream)}");

            var videoTracks = p0?.VideoTracks?.OfType<VideoTrack>();
            videoTracks?.FirstOrDefault()?.AddSink(_remoteView);

            var audioTracks = p0?.AudioTracks?.OfType<AudioTrack>();
            audioTracks?.FirstOrDefault()?.SetEnabled(true);
            audioTracks?.FirstOrDefault()?.SetVolume(10);
        }

        public void OnAddTrack(RtpReceiver p0, MediaStream[] p1)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnAddTrack)}");
        }

        public void OnDataChannel(DataChannel p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnDataChannel)}");

            MainThread.BeginInvokeOnMainThread(() => _observer?.OnOpenDataChannel());

            _dataChannel = p0;
            _dataChannel.RegisterObserver(this);
        }

        public void OnIceCandidate(IceCandidate p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnIceCandidate)}");

            MainThread.BeginInvokeOnMainThread(() => _observer?.OnGenerateCandiate(p0));
        }

        public void OnIceCandidatesRemoved(IceCandidate[] p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnIceCandidatesRemoved)}");
        }

        public void OnIceConnectionChange(PeerConnection.IceConnectionState p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnIceConnectionChange)}");

            if (p0 == PeerConnection.IceConnectionState.Connected ||
                p0 == PeerConnection.IceConnectionState.Completed)
            {
                if (!_isConnected)
                {
                    _isConnected = true;
                    MainThread.BeginInvokeOnMainThread(() => _observer?.OnConnectWebRtc());
                }
            }
            else if (_isConnected)
            {
                if (_isConnected)
                {
                    _isConnected = false;
                    Disconnect();
                    MainThread.BeginInvokeOnMainThread(() => _observer?.OnDisconnectWebRtc());
                }
            }

            MainThread.BeginInvokeOnMainThread(() => _observer?.OnIceConnectionStateChanged(p0));
        }

        public void OnIceConnectionReceivingChange(bool p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnIceConnectionReceivingChange)}");
        }

        public void OnIceGatheringChange(PeerConnection.IceGatheringState p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnIceGatheringChange)}");
        }

        public void OnRemoveStream(MediaStream p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnRemoveStream)}");
        }

        public void OnRenegotiationNeeded()
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnRenegotiationNeeded)}");
        }

        public void OnSignalingChange(PeerConnection.SignalingState p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnSignalingChange)}");
        }
        #endregion

        #region DataChannelObserver
        public void OnBufferedAmountChange(long p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnBufferedAmountChange)}");
        }

        public void OnMessage(DataChannel.Buffer p0)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnMessage)}");

            var bytes = new byte[p0.Data.Remaining()];
            p0.Data.Get(bytes);

            if (p0.Binary)
            {
                MainThread.BeginInvokeOnMainThread(() => _observer?.OnReceiveData(bytes));
            }
            else
            {
                var msg = Encoding.UTF8.GetString(bytes);
                MainThread.BeginInvokeOnMainThread(() => _observer?.OnReceiveMessage(msg));
            }
        }

        public void OnStateChange()
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnStateChange)}");
        }

        public void OnConnectionChange(PeerConnection.PeerConnectionState newState)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnConnectionChange)}:{newState}");
        }

        public void OnSelectedCandidatePairChanged(CandidatePairChangeEvent e)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnSelectedCandidatePairChanged)}:{e.Reason}");
        }

        public void OnTrack(RtpTransceiver transceiver)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnTrack)}:{transceiver.MediaType}");
        }
        #endregion
    }
}

