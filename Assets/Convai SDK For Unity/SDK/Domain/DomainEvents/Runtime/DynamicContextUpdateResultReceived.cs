using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Convai.Domain.DomainEvents.Runtime
{
    /// <summary>
    ///     Domain event raised when the backend acknowledges a dynamic context update.
    /// </summary>
    public readonly struct DynamicContextUpdateResultReceived
    {
        private DynamicContextUpdateResultReceived(
            string status,
            string message,
            string updateId,
            int contextRevision,
            int tokenCount,
            int staticTokenCount,
            int runtimeTokenCount,
            int remainingTokens,
            string requestedRunLlm,
            string actualRunLlm,
            string downgradeReason,
            bool interrupted,
            bool llmTriggered,
            bool promptRebuild,
            JObject rawExtras,
            DateTime timestamp)
        {
            Status = status ?? string.Empty;
            Message = message ?? string.Empty;
            UpdateId = updateId ?? string.Empty;
            ContextRevision = contextRevision;
            TokenCount = tokenCount;
            StaticTokenCount = staticTokenCount;
            RuntimeTokenCount = runtimeTokenCount;
            RemainingTokens = remainingTokens;
            RequestedRunLlm = requestedRunLlm ?? string.Empty;
            ActualRunLlm = actualRunLlm ?? string.Empty;
            DowngradeReason = downgradeReason ?? string.Empty;
            Interrupted = interrupted;
            LlmTriggered = llmTriggered;
            PromptRebuild = promptRebuild;
            RawExtras = rawExtras;
            Timestamp = timestamp;
        }

        public string Status { get; }
        public string Message { get; }
        public string UpdateId { get; }
        public int ContextRevision { get; }
        public int TokenCount { get; }
        public int StaticTokenCount { get; }
        public int RuntimeTokenCount { get; }
        public int RemainingTokens { get; }
        public string RequestedRunLlm { get; }
        public string ActualRunLlm { get; }
        public string DowngradeReason { get; }
        public bool Interrupted { get; }
        public bool LlmTriggered { get; }
        public bool PromptRebuild { get; }
        public JObject RawExtras { get; }
        public DateTime Timestamp { get; }

        public static DynamicContextUpdateResultReceived Create(
            string status,
            string message,
            JObject extras)
        {
            extras ??= new JObject();
            return new DynamicContextUpdateResultReceived(
                status,
                message,
                ReadString(extras, "update_id"),
                ReadInt(extras, "context_revision", "revision"),
                ReadInt(extras, "token_count", "word_count"),
                ReadInt(extras, "static_token_count"),
                ReadInt(extras, "runtime_token_count"),
                ReadInt(extras, "remaining_tokens", "remaining_words"),
                ReadString(extras, "requested_run_llm"),
                ReadString(extras, "actual_run_llm"),
                ReadString(extras, "downgrade_reason"),
                ReadBool(extras, "interrupted"),
                ReadBool(extras, "llm_triggered"),
                ReadBool(extras, "prompt_rebuild"),
                extras,
                DateTime.UtcNow);
        }

        private static string ReadString(JObject obj, string key)
        {
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return string.Empty;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>() ? "true" : "false";
            if (token.Type == JTokenType.String) return token.Value<string>() ?? string.Empty;

            return token.ToString(Formatting.None);
        }

        private static bool ReadBool(JObject obj, string key)
        {
            JToken token = obj[key];
            if (token == null || token.Type == JTokenType.Null) return false;
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (token.Type == JTokenType.Integer) return token.Value<int>() != 0;
            if (bool.TryParse(token.ToString(), out bool value)) return value;

            return false;
        }

        private static int ReadInt(JObject obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                JToken token = obj[key];
                if (token == null || token.Type == JTokenType.Null) continue;
                if (token.Type == JTokenType.Integer) return token.Value<int>();
                if (int.TryParse(token.ToString(), out int value)) return value;
            }

            return 0;
        }
    }
}
