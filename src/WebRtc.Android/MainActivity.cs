using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using Google.Android.WebRtc;
using Newtonsoft.Json;
using Square.OkHttp3;
using System;
using System.Threading.Tasks;
using WebRtc.Android.Code;
using WebRtc.Android.Observers;
using Xamarin.Essentials;
using AlertDialog = Android.Support.V7.App.AlertDialog;
using Toolbar = Android.Support.V7.Widget.Toolbar;

namespace WebRtc.Android
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true)]
    public class MainActivity : AppCompatActivity, IWebRtcObserver
    {
        private IWebSocket _socket;
        
        private WebRtcClient _webRtcClient;
        private SurfaceViewRenderer _remoteView;
        private SurfaceViewRenderer _localView;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            SetContentView(Resource.Layout.activity_main);

            var toolbar = FindViewById<Toolbar>(Resource.Id.toolbar);
            SetSupportActionBar(toolbar);           

            var connectButton = FindViewById<Button>(Resource.Id.connect_button);
            connectButton.Click += ConnectButton;            
            var disconnectButton = FindViewById<Button>(Resource.Id.disconnect_button);
            disconnectButton.Click += DisconnectButton;

            var waveButton = FindViewById<Button>(Resource.Id.wave_button);
            waveButton.Text = "👋";
            waveButton.Click += (sender, args) => _webRtcClient.SendMessage(waveButton.Text);

            _remoteView = FindViewById<SurfaceViewRenderer>(Resource.Id.remote_video_view);
            _localView = FindViewById<SurfaceViewRenderer>(Resource.Id.local_video_view);

            _webRtcClient = new WebRtcClient(this, _remoteView, _localView, this);

            RunOnUiThread(async () => await Init());
        }

        private async Task Init()
        {
            var cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
            var micStatus = await Permissions.RequestAsync<Permissions.Microphone>();

            var dialogTcs = new TaskCompletionSource<string>();

            var linearLayout = new LinearLayout(this);
            linearLayout.Orientation = Orientation.Vertical;
            linearLayout.SetPadding(48, 24, 48, 24);
            var ipAddr = new EditText(this) { Hint = "IP Address", Text = "192.168.1.119" };
            var port = new EditText(this) { Hint = "Port", Text = "8080" };
            linearLayout.AddView(ipAddr);
            linearLayout.AddView(port);

            var alert = new AlertDialog.Builder(this)
                .SetTitle("Socket Address")
                .SetView(linearLayout)
                .SetPositiveButton("OK", (sender, args) =>
                {
                    dialogTcs.TrySetResult($"ws://{ipAddr.Text}:{port.Text}");
                })
                .Create();

            alert.Show();

            var wsUrl = await dialogTcs.Task;

            var okHttpClient = new OkHttpClient.Builder()
                .ReadTimeout(0, Java.Util.Concurrent.TimeUnit.Milliseconds)
                .Build();
            var request = new Request.Builder()
                .Url(wsUrl)
                .Build();
            _socket = okHttpClient.NewWebSocket(
                request,
                new WebSocketObserver(ReadMessage));
        }

        private void ConnectButton(object sender, EventArgs e)
        {
            _webRtcClient.Connect((sdp, err) =>
            {
                if (string.IsNullOrEmpty(err))
                {
                    var signal = new SignalingMessage()
                    {
                        Type = sdp.Type.ToString(),
                        Sdp = sdp.Description,
                    };
                    SendViaSocket(signal);
                }
            });
        }

        private void DisconnectButton(object sender, EventArgs e)
        {
            _webRtcClient.Disconnect();
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.menu_main, menu);
            return true;
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            int id = item.ItemId;
            if (id == Resource.Id.action_settings)
            {
                return true;
            }

            return base.OnOptionsItemSelected(item);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
        
        private void SendViaSocket(SignalingMessage msg)
        {
            var json = JsonConvert.SerializeObject(msg);
            _socket.Send(json);
        }

        private void ReadMessage(SignalingMessage msg)
        {
            if (msg.Type?.Equals(SessionDescription.SdpType.Offer.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                _webRtcClient.ReceiveOffer(
                    new SessionDescription(
                        SessionDescription.SdpType.Offer,
                        msg.Sdp),
                    (sdp, err) =>
                    {
                        if (string.IsNullOrEmpty(err))
                        {
                            var signal = new SignalingMessage()
                            {
                                Type = sdp.Type.ToString(),
                                Sdp = sdp.Description,
                            };
                            SendViaSocket(signal);
                        }
                    });
            }
            else if (msg.Type?.Equals(SessionDescription.SdpType.Answer.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                _webRtcClient.ReceiveAnswer(
                    new SessionDescription(
                        SessionDescription.SdpType.Answer,
                        msg.Sdp),
                    (sdp, err) =>
                    {
                    });
            }
            else if (msg.Candidate != null)
            {
                _webRtcClient.ReceiveCandidate(new IceCandidate(
                    msg.Candidate.SdpMid,
                    msg.Candidate.SdpMLineIndex,
                    msg.Candidate.Sdp));                
            }
        }

        #region IWebRtcObserver
        public void OnGenerateCandiate(IceCandidate iceCandidate)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnGenerateCandiate)}");

            var can = new Candidate()
            {
                Sdp = iceCandidate.Sdp,
                SdpMLineIndex = iceCandidate.SdpMLineIndex,
                SdpMid = iceCandidate.SdpMid
            };
            var signal = new SignalingMessage() { Candidate = can };

            SendViaSocket(signal);
        }

        public void OnIceConnectionStateChanged(PeerConnection.IceConnectionState iceConnectionState)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnIceConnectionStateChanged)}");
        }

        public void OnOpenDataChannel()
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnOpenDataChannel)}");
        }

        public void OnReceiveData(byte[] data)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnReceiveData)}");
        }

        public void OnReceiveMessage(string message)
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnReceiveMessage)}");

            var alert = new AlertDialog.Builder(this)
                .SetTitle("Message")
                .SetMessage(message)
                .SetPositiveButton("OK", (sender, args) => { })
                .Create();
            alert.Show();
        }

        public void OnConnectWebRtc()
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnConnectWebRtc)}");
        }

        public void OnDisconnectWebRtc()
        {
            System.Diagnostics.Debug.WriteLine($"{nameof(OnDisconnectWebRtc)}");
        }
        #endregion
    }
}

