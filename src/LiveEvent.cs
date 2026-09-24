using System.Runtime.Serialization;

namespace StreamTok.GtaV
{
    /// <summary>
    /// Sobre que envía el sidecar por WebSocket: { "channel": "...", "payload": { ... } }.
    /// Espejo del LiveEventSchema (Zod) de packages/shared.
    /// Los campos que no declaramos (ej. timestamp) se ignoran al deserializar.
    /// </summary>
    [DataContract]
    internal sealed class Envelope
    {
        [DataMember(Name = "channel")]
        public string Channel { get; set; }

        [DataMember(Name = "payload")]
        public LiveEvent Payload { get; set; }
    }

    [DataContract]
    internal sealed class LiveEvent
    {
        /// <summary>Tipo de evento. Debe coincidir con el enum de LiveEventSchema (gift, like, follow…).</summary>
        [DataMember(Name = "event")]
        public string Event { get; set; }

        [DataMember(Name = "username")]
        public string Username { get; set; }

        [DataMember(Name = "giftName")]
        public string GiftName { get; set; }

        /// <summary>Numérico y opcional; double para aceptar enteros o decimales.</summary>
        [DataMember(Name = "value")]
        public double? Value { get; set; }

        [DataMember(Name = "text")]
        public string Text { get; set; }
    }
}
