using System;
using Google.Android.WebRtc;

namespace WebRtc.Android.Code
{
    public class SignalingMessage
    {
        public string Type { get; set; }
        public string Sdp { get; set; }
        public Candidate Candidate { get; set; }
    }

    public class Candidate
    {
        public string Sdp { get; set; }
        public int SdpMLineIndex { get; set; }
        public string SdpMid { get; set; }
    }
}
