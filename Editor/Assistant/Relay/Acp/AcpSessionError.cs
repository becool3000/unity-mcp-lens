using Newtonsoft.Json.Linq;

namespace Unity.Relay.Editor.Acp
{
    class AcpSessionError
    {
        public string Message { get; }
        public string Code { get; }
        public string ProviderId { get; }
        public JToken Details { get; }

        public AcpSessionError(string message, string code = null, string providerId = null, JToken details = null)
        {
            Message = string.IsNullOrEmpty(message) ? "Unknown error" : message;
            Code = code;
            ProviderId = providerId;
            Details = details;
        }

        public static AcpSessionError FromToken(JToken token)
        {
            if (token == null)
            {
                return new AcpSessionError("Unknown error");
            }

            if (token.Type == JTokenType.Object)
            {
                var message = token["message"]?.ToString();
                var code = token["code"]?.ToString();
                var providerId = token["providerId"]?.ToString();
                var details = token["details"];
                return new AcpSessionError(message, code, providerId, details);
            }

            if (token.Type == JTokenType.String)
            {
                return new AcpSessionError(token.ToString());
            }

            return new AcpSessionError(token.ToString());
        }

        public override string ToString() => Message;
    }
}
