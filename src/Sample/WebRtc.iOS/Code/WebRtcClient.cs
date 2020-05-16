using AVFoundation;
using Cirrious.FluentLayouts.Touch;
using CoreFoundation;
using CoreGraphics;
using CoreMedia;
using Foundation;
using System;
using System.Linq;
using UIKit;
using Xam.WebRtc.iOS;
using Xamarin.Essentials;

namespace WebRtc.iOS.Code
{
    public interface IWebRtcClientDelegate
    {
        void DidGenerateCandiate(RTCIceCandidate iceCandidate);
        void DidIceConnectionStateChanged(RTCIceConnectionState iceConnectionState);
        void DidOpenDataChannel();
        void DidReceiveData(NSData data);
        void DidReceiveMessage(string message);
        void DidConnectWebRtc();
        void DidDisconnectWebRtc();
    }

    public class WebRtcClient : NSObject, IRTCPeerConnectionDelegate, IRTCVideoViewDelegate, IRTCDataChannelDelegate
    {
        private RTCPeerConnectionFactory _peerConnectionFactory;
        
        private RTCVideoCapturer _videoCapturer;
        private RTCVideoTrack _localVideoTrack;
        private RTCAudioTrack _localAudioTrack;
        
        private RTCMediaStream _remoteStream;

        private RTCEAGLVideoView _localRenderView;
        private UIView _localView;
        private RTCEAGLVideoView _remoteRenderView;
        private UIView _remoteView;

        private readonly object _connectionLock = new object();
        private RTCPeerConnection _peerConnection;
        private RTCDataChannel _dataChannel;

        private bool _isConnected;

        private (RTCPeerConnection peer, RTCDataChannel data) _connection
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

        public UIView LocalVideoView => _localView;
        public UIView RemoteVideoView => _remoteView;

        public bool IsConnected => _isConnected;

        public IWebRtcClientDelegate Delegate { get; private set; }

        public WebRtcClient(IWebRtcClientDelegate @delegate)
        {
            Delegate = @delegate;

            // will crash on simulator... device is fine
            //var metalView = new RTCMTLVideoView();

            var videoEncoderFactory = new RTCDefaultVideoEncoderFactory();
            var videoDecoderFactory = new RTCDefaultVideoDecoderFactory();
            _peerConnectionFactory = new RTCPeerConnectionFactory(videoEncoderFactory, videoDecoderFactory);

            _localRenderView = new RTCEAGLVideoView();
            _localRenderView.Delegate = this;
            _localView = new UIView();
            _localView.AddSubview(_localRenderView);

            _remoteRenderView = new RTCEAGLVideoView();
            _remoteRenderView.Delegate = this;
            _remoteView = new UIView();
            _remoteView.AddSubview(_remoteRenderView);

            _localView.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints();
            _localView.AddConstraints(new[]
            {
                _localRenderView.WithSameCenterX(_localView),
                _localRenderView.WithSameCenterY(_localView),
                _localRenderView.WithSameHeight(_localView),
                _localRenderView.WithSameWidth(_localView)
            });

            _remoteView.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints();
            _remoteView.AddConstraints(new[]
            {
                _remoteRenderView.WithSameCenterX(_remoteView),
                _remoteRenderView.WithSameCenterY(_remoteView),
                _remoteRenderView.WithSameHeight(_remoteView),
                _remoteRenderView.WithSameWidth(_remoteView)
            });
        }

        public void SetupMediaTracks()
        {
            _localVideoTrack = CreateVideoTrack();

            StartCaptureLocalVideo(AVCaptureDevicePosition.Front, 640, Convert.ToInt32(640 * 16 / 9f), 30);
            _localVideoTrack.AddRenderer(_localRenderView);

            _localAudioTrack = CreateAudioTrack();
        }

        public void Connect(Action<RTCSessionDescription, NSError> completionHandler)
        {
            _dataChannel = SetupDataChannel();
            _dataChannel.Delegate = this;

            MakeOffer(completionHandler);
        }

        public void Disconnect()
        {
            if (_peerConnection != null)
            {
                lock (_connectionLock)
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
            }
        }

        public void ReceiveOffer(RTCSessionDescription offerSdp, Action<RTCSessionDescription, NSError> completionHandler)
        {
            _connection.peer.SetRemoteDescription(offerSdp, (err) =>
            {
                if (err == null)
                {
                    MakeAnswer(completionHandler);
                }
                else
                {
                    completionHandler(offerSdp, err);
                }
            });
        }

        public void ReceiveAnswer(RTCSessionDescription answerSdp, Action<RTCSessionDescription, NSError> completionHandler)
        {
            _connection.peer.SetRemoteDescription(answerSdp, (err) =>
            {
                completionHandler(answerSdp, err);
            });
        }

        public bool SendMessage(string message)
        {
            if (_connection.data != null && _connection.data.ReadyState == RTCDataChannelState.Open)
            {
                var buffer = new RTCDataBuffer(NSData.FromString(message, NSStringEncoding.UTF8), false);
                var result = _connection.data.SendData(buffer);
                return result;
            }

            return false;
        }

        public void ReceiveCandidate(RTCIceCandidate candidate)
        {
            _connection.peer.AddIceCandidate(candidate);
        }

        private RTCPeerConnection SetupPeerConnection()
        {
            var rtcConfig = new RTCConfiguration();
            rtcConfig.IceServers = new RTCIceServer[]
            {
                new RTCIceServer(new [] { "stun:stun.l.google.com:19302" })
            };
            var mediaConstraints = new RTCMediaConstraints(null, null);
            var pc = _peerConnectionFactory.PeerConnectionWithConfiguration(rtcConfig, mediaConstraints, this);

            pc.AddTrack(_localVideoTrack, new[] { "stream0" });
            pc.AddTrack(_localAudioTrack, new[] { "stream0" });

            return pc;
        }

        private RTCDataChannel SetupDataChannel()
        {
            var dataChannelConfig = new RTCDataChannelConfiguration();
            dataChannelConfig.ChannelId = 1;

            var dc = _connection.peer.DataChannelForLabel("dataChannel", dataChannelConfig);
            dc.Delegate = this;
            return dc;
        }

        private RTCAudioTrack CreateAudioTrack()
        {
            var audioConstraints = new RTCMediaConstraints(null, null);
            var audioSource = _peerConnectionFactory.AudioSourceWithConstraints(audioConstraints);
            var audioTrack = _peerConnectionFactory.AudioTrackWithSource(audioSource, "audio0");

            return audioTrack;
        }

        private void MakeOffer(Action<RTCSessionDescription, NSError> completionHandler)
        {
            var mediaConstraints = new RTCMediaConstraints(null, null);
            _connection.peer.OfferForConstraints(mediaConstraints, (sdp, err0) =>
            {
                if (err0 == null)
                {
                    _connection.peer.SetLocalDescription(sdp, (err1) =>
                    {
                        completionHandler(sdp, err1);
                    });
                }
                else
                {
                    completionHandler(null, err0);
                }
            });
        }

        private void MakeAnswer(Action<RTCSessionDescription, NSError> completionHandler)
        {
            var mediaConstraints = new RTCMediaConstraints(null, null);
            _connection.peer.AnswerForConstraints(mediaConstraints, (sdp, err0) =>
            {
                if (err0 == null)
                {
                    _connection.peer.SetLocalDescription(sdp, (err1) =>
                    {
                        completionHandler(sdp, err1);
                    });
                }
                else
                {
                    completionHandler(null, err0);
                }
            });
        }

        private RTCVideoTrack CreateVideoTrack()
        {
            var videoSource = _peerConnectionFactory.VideoSource;

            if (DeviceInfo.DeviceType == DeviceType.Virtual)
            {
                _videoCapturer = new RTCFileVideoCapturer();
                _videoCapturer.Delegate = videoSource;
            }
            else
            {
                _videoCapturer = new RTCCameraVideoCapturer();
                _videoCapturer.Delegate = videoSource;
            }

            var videoTrack = _peerConnectionFactory.VideoTrackWithSource(videoSource, "video0");
            return videoTrack;
        }

        private void StartCaptureLocalVideo(AVCaptureDevicePosition position, int width, int? height, int fps)
        {
            if (_videoCapturer is RTCCameraVideoCapturer cameraCapturer)
            {
                var devices = RTCCameraVideoCapturer.CaptureDevices;
                var targetDevice = devices.FirstOrDefault(d => d.Position == position);

                if (targetDevice != null)
                {
                    var formats = RTCCameraVideoCapturer.SupportedFormatsForDevice(targetDevice);

                    var targetFormat = formats.FirstOrDefault(f =>
                    {
                        var description = f.FormatDescription;
                        if (description is CMVideoFormatDescription videoDescription)
                        {
                            var dimensions = videoDescription.Dimensions;
                            if ((dimensions.Width == width && dimensions.Height == height) ||
                                (dimensions.Width == width))
                            {
                                return true;
                            }
                        }

                        return false;
                    });

                    if (targetFormat != null)
                    {
                        cameraCapturer.StartCaptureWithDevice(targetDevice, targetFormat, fps);
                    }
                }
            }
            else if (_videoCapturer is RTCFileVideoCapturer fileCapturer)
            {
                var file = NSBundle.MainBundle.PathForResource("sample.mp4", null);
                if (file != null)
                {
                    fileCapturer.StartCapturingFromFileNamed("sample.mp4", (err) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"err: {err}");
                    });
                }
            }
        }        
        
        #region IRTCDataChannelDelegate
        public void DataChannel(RTCDataChannel dataChannel, RTCDataBuffer buffer)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DataChannel)}");

            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                if (buffer.IsBinary)
                {
                    Delegate?.DidReceiveData(buffer.Data);
                }
                else
                {
                    Delegate?.DidReceiveMessage(new NSString(buffer.Data, NSStringEncoding.UTF8));
                }
            });
        }

        public void DataChannelDidChangeState(RTCDataChannel dataChannel)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DataChannelDidChangeState)}");
        }
        #endregion

        #region IRTCVideoViewDelegate
        public void DidChangeVideoSize(IRTCVideoRenderer videoView, CGSize size)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DidChangeVideoSize)}");

            if (videoView is RTCEAGLVideoView rendererView &&
                rendererView.Superview is UIView parentView)
            {
                var constraints = parentView.Constraints
                    .Where(lc => lc.SecondAttribute == NSLayoutAttribute.Width ||
                                 lc.SecondAttribute == NSLayoutAttribute.Height)
                    .ToArray();
                parentView.RemoveConstraints(constraints);

                var isLandscape = size.Width > size.Height;

                if (isLandscape)
                {                    
                    parentView.AddConstraints(new[]
                    {
                        rendererView.WithSameWidth(parentView),
                        rendererView.Height()
                                    .EqualTo()
                                    .WidthOf(parentView)
                                    .WithMultiplier(size.Height / size.Width)
                    });
                }
                else
                {
                    parentView.AddConstraints(new[]
                    {
                        rendererView.Width()
                                    .EqualTo()
                                    .HeightOf(parentView)
                                    .WithMultiplier(size.Width / size.Height),
                        rendererView.WithSameHeight(parentView)
                    });
                }
            }
                        
        }
        #endregion

        #region IRTCPeerConnectionDelegate
        public void PeerConnection(RTCPeerConnection peerConnection, RTCSignalingState stateChanged)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(RTCSignalingState)} changed {stateChanged}");
        }

        public void PeerConnection(RTCPeerConnection peerConnection, RTCIceConnectionState newState)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(RTCIceConnectionState)} changed {newState}");

            switch (newState)
            {
                case RTCIceConnectionState.Connected:
                case RTCIceConnectionState.Completed:
                    if (!_isConnected)
                    {
                        _isConnected = true;
                        DispatchQueue.MainQueue.DispatchAsync(() =>
                        {
                            _remoteRenderView.Hidden = false;
                            Delegate?.DidConnectWebRtc();
                        });
                    }
                    break;
                default:
                    if (_isConnected)
                    {
                        _isConnected = false;
                        DispatchQueue.MainQueue.DispatchAsync(() =>
                        {
                            _remoteRenderView.Hidden = true;
                            Disconnect();
                            Delegate?.DidDisconnectWebRtc();
                        });
                    }
                    break;
            }

            DispatchQueue.MainQueue.DispatchAsync(() =>
            {                
                Delegate?.DidIceConnectionStateChanged(newState);
            });
        }

        public void PeerConnection(RTCPeerConnection peerConnection, RTCIceGatheringState newState)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(RTCIceGatheringState)} changed {newState}");
        }

        public void PeerConnection(RTCPeerConnection peerConnection, RTCIceCandidate candidate)
        {
            System.Diagnostics.Debug.WriteLine($"PeerConnectionIceCandiate");
            Delegate?.DidGenerateCandiate(candidate);
        }

        public void PeerConnection(RTCPeerConnection peerConnection, RTCIceCandidate[] candidates)
        {
            System.Diagnostics.Debug.WriteLine($"PeerConnectionIceCandiates");
        }

        public void PeerConnection(RTCPeerConnection peerConnection, RTCDataChannel dataChannel)
        {
            System.Diagnostics.Debug.WriteLine($"PeerConnectionDidOpenDataChannel");
            Delegate?.DidOpenDataChannel();

            _dataChannel?.Close();
            _dataChannel?.Dispose();
            _dataChannel = null;

            _dataChannel = dataChannel;
            dataChannel.Delegate = this;
        }

        public void PeerConnectionDidAddStream(RTCPeerConnection peerConnection, RTCMediaStream stream)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(PeerConnectionDidAddStream)}");

            _remoteStream = stream;

            if (_remoteStream.VideoTracks.FirstOrDefault() is RTCVideoTrack vTrack)
            {
                vTrack.AddRenderer(_remoteRenderView);
            }

            if (_remoteStream.AudioTracks.FirstOrDefault() is RTCAudioTrack aTrack)
            {
                aTrack.Source.Volume = 10;
            }
        }

        public void PeerConnectionDidRemoveStream(RTCPeerConnection peerConnection, RTCMediaStream stream)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(PeerConnectionDidRemoveStream)}");
        }

        public void PeerConnectionShouldNegotiate(RTCPeerConnection peerConnection)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(PeerConnectionShouldNegotiate)}");
        }
        #endregion
    }
}
