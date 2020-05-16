using Java.Lang;
using Newtonsoft.Json;
using Square.OkHttp3;
using System;
using WebRtc.Android.Code;

namespace WebRtc.Android.Observers
{
    public class WebSocketObserver : WebSocketListener
    {
        private Action<SignalingMessage> _readMessage;

        public WebSocketObserver(Action<SignalingMessage> readMessage)
        {
            _readMessage = readMessage;
        }

        public override void OnOpen(IWebSocket webSocket, Response response)
        {
            base.OnOpen(webSocket, response);
        }

        public override void OnFailure(IWebSocket webSocket, Throwable t, Response response)
        {
            base.OnFailure(webSocket, t, response);
        }

        public override void OnMessage(IWebSocket webSocket, string text)
        {
            base.OnMessage(webSocket, text);

            var msg = JsonConvert.DeserializeObject<SignalingMessage>(text);
            _readMessage(msg);
        }
    }
}