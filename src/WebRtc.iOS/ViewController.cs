using System;
using Cirrious.FluentLayouts.Touch;
using CoreFoundation;
using Foundation;
using Google.iOS.WebRtc;
using Newtonsoft.Json;
using Square.SocketRocket;
using UIKit;
using WebRtc.iOS.Code;
using Xamarin.Essentials;

namespace WebRtc.iOS
{
    public class ViewController : UIViewController, IWebRtcClientDelegate
    {
        private readonly WebRtcClient _webRtcClient;

        private WebSocket _socket;

        public ViewController()
        {
            View.BackgroundColor = ColorHelper.SystemBackgroundColor;

            _webRtcClient = new WebRtcClient(this);
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            // Perform any additional setup after loading the view, typically from a nib.            
        }

        public override void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);

            var videoContainer = new UIView();
            videoContainer.BackgroundColor = ColorHelper.SystemBackgroundColor;
            View.AddSubview(videoContainer);

            var remoteVideoView = _webRtcClient.RemoteVideoView;
            remoteVideoView.BackgroundColor = UIColor.SystemGrayColor;
            videoContainer.AddSubview(remoteVideoView);

            var localVideoView = _webRtcClient.LocalVideoView;
            localVideoView.BackgroundColor = UIColor.SystemBlueColor;
            videoContainer.AddSubview(localVideoView);

            var controlsContainer = new UIView();
            controlsContainer.BackgroundColor = ColorHelper.SystemBackgroundColor;
            View.AddSubview(controlsContainer);

            var connectButton = new UIButton(UIButtonType.System);
            connectButton.SetTitle("Connect", UIControlState.Normal);
            connectButton.TouchUpInside += ConnectButton_TouchUpInside;
            controlsContainer.AddSubview(connectButton);

            var disconnectButton = new UIButton(UIButtonType.System);
            disconnectButton.SetTitle("Disconnect", UIControlState.Normal);
            disconnectButton.TouchUpInside += DisconnectButton_TouchUpInside;
            controlsContainer.AddSubview(disconnectButton);

            var waveButton = new UIButton(UIButtonType.System);
            waveButton.SetTitle("👋", UIControlState.Normal);
            waveButton.TouchUpInside += SendWaveButton_TouchUpInside;
            controlsContainer.AddSubview(waveButton);

            videoContainer.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints();
            controlsContainer.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints();
            View.SubviewsDoNotTranslateAutoresizingMaskIntoConstraints();           

            videoContainer.AddConstraints(new[]
            {
                remoteVideoView.WithSameCenterX(videoContainer),
                remoteVideoView.WithSameCenterY(videoContainer),
                remoteVideoView.WithSameHeight(videoContainer),
                remoteVideoView.WithSameWidth(videoContainer),

                localVideoView.WithSameLeft(videoContainer),
                localVideoView.WithSameBottom(videoContainer),
                localVideoView.WithRelativeHeight(videoContainer, 0.25f),
                localVideoView.Width()
                              .EqualTo()
                              .HeightOf(videoContainer)
                              .WithMultiplier(0.25f * 16 / 9f),
            });

            controlsContainer.AddConstraints(new[]
            {
                connectButton.AtLeftOf(controlsContainer, 5f),
                connectButton.WithSameCenterY(controlsContainer),

                waveButton.WithSameCenterX(controlsContainer),
                waveButton.WithSameCenterY(controlsContainer),

                disconnectButton.AtRightOf(controlsContainer, 5f),
                disconnectButton.WithSameCenterY(controlsContainer),
            });

            View.AddConstraints(new[]
            {
                remoteVideoView.WithSameTop(View),
                remoteVideoView.WithSameLeft(View),
                remoteVideoView.WithSameRight(View),
                remoteVideoView.WithSameWidth(View),
                remoteVideoView.Above(controlsContainer),

                controlsContainer.WithSameBottom(View),
                controlsContainer.WithSameWidth(View),
                controlsContainer.WithRelativeHeight(View, 0.20f),
            });

            DispatchQueue.MainQueue.DispatchAsync(async () =>
            {
                var cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
                var micStatus = await Permissions.RequestAsync<Permissions.Microphone>();

                _webRtcClient.SetupMediaTracks();

                var ipAddrField = default(UITextField);
                var portField = default(UITextField);

                var alertVc = UIAlertController.Create("Socket Address", null, UIAlertControllerStyle.Alert);
                alertVc.AddTextField((tf) =>
                {
                    ipAddrField = tf;
                    tf.Placeholder = "IP Address";
                    tf.Text = "192.168.1.119";
                });
                alertVc.AddTextField((tf) =>
                {
                    portField = tf;
                    tf.Placeholder = "Port";
                    tf.Text = "8080";
                });

                alertVc.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, (a) =>
                {
                    var ip = ipAddrField.Text;
                    var port = portField.Text;
                    var url = new NSUrl($"ws://{ip}:{port}");

                    _socket = new WebSocket(url);
                    _socket.ReceivedMessage += SocketReceiveMessage;
                    _socket.Open();
                }));

                PresentViewController(alertVc, true, null);                
            });
        }

        private void ConnectButton_TouchUpInside(object sender, EventArgs e)
        {
            if (_socket.ReadyState == ReadyState.Closed)
            {
                _socket.Open();
            }

            _webRtcClient.Connect((sdp, err) =>
            {
                if (err == null)
                {
                    SendSdp(sdp);
                }
            });
        }

        private void DisconnectButton_TouchUpInside(object sender, EventArgs e)
        {
            _webRtcClient.Disconnect();
        }

        private void SendWaveButton_TouchUpInside(object sender, EventArgs e)
        {
            _webRtcClient.SendMessage("👋");
        }

        #region IWebRtcClientDelegate
        public void DidGenerateCandiate(RTCIceCandidate iceCandidate)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DidGenerateCandiate)}");
            SendCandidate(iceCandidate);
        }

        public void DidIceConnectionStateChanged(RTCIceConnectionState iceConnectionState)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DidIceConnectionStateChanged)}");
        }

        public void DidOpenDataChannel()
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DidOpenDataChannel)}");
        }

        public void DidReceiveData(NSData data)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DidReceiveData)}");
        }

        public void DidReceiveMessage(string message)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DidReceiveMessage)}");

            if (!string.IsNullOrEmpty(message))
            {
                var alertVc = UIAlertController.Create("Message", message, UIAlertControllerStyle.Alert);
                alertVc.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                PresentViewController(alertVc, true, null);
            }
        }

        public void DidConnectWebRtc()
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DidConnectWebRtc)}");
        }

        public void DidDisconnectWebRtc()
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(DidDisconnectWebRtc)}");
        }
        #endregion

        private void SendSdp(RTCSessionDescription sdp)
        {
            var signal = new SignalingMessage()
            {
                Type = sdp.Type.ToString(),
                Sdp = sdp.Sdp,                
            };
            SendViaSocket(signal);
        }

        private void SendCandidate(RTCIceCandidate iceCandidate)
        {
            var can = new Candidate()
            {                
                Sdp = iceCandidate.Sdp,
                SdpMLineIndex = iceCandidate.SdpMLineIndex,
                SdpMid = iceCandidate.SdpMid
            };
            var signal = new SignalingMessage() { Candidate = can };
            SendViaSocket(signal);
        }

        private void SendViaSocket(SignalingMessage msg)
        {
            var json = JsonConvert.SerializeObject(msg);
            var nsMsg = new NSString(json, NSStringEncoding.UTF8);
            _socket.Send(nsMsg);
        }

        private void SocketReceiveMessage(object sender, WebSocketReceivedMessageEventArgs args)
        {
            var msg = JsonConvert.DeserializeObject<SignalingMessage>(args.Message.ToString());
            ReadMessage(msg);
        }

        private void ReadMessage(SignalingMessage msg)
        {
            if (msg.Type?.Equals(RTCSdpType.Offer.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                _webRtcClient.ReceiveOffer(new RTCSessionDescription(RTCSdpType.Offer, msg.Sdp), (sdp, err) =>
                {
                    if (err == null)
                    {
                        SendSdp(sdp);
                    }
                });
            }
            else if (msg.Type?.Equals(RTCSdpType.Answer.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                _webRtcClient.ReceiveAnswer(new RTCSessionDescription(RTCSdpType.Answer, msg.Sdp), (sdp, err) =>
                {

                });
            }
            else if (msg.Candidate != null)
            {
                _webRtcClient.ReceiveCandidate(new RTCIceCandidate(msg.Candidate.Sdp, msg.Candidate.SdpMLineIndex, msg.Candidate.SdpMid));
            }
        }
    }
}