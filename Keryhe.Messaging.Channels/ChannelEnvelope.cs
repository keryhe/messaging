namespace Keryhe.Messaging.Channels
{
    // Internal wrapper so trace context can travel alongside the payload across the channel's
    // reader/writer boundary. Not part of the public API — callers only ever see T.
    internal class ChannelEnvelope<T>
    {
        public T Payload { get; set; }
        public string MessageId { get; set; }
        public string TraceParent { get; set; }
        public string TraceState { get; set; }
    }
}
