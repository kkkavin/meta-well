using Newtonsoft.Json;
using Convai.Runtime.DynamicContext;

namespace Convai.Infrastructure.Protocol.Messages
{
    /// <summary>
    ///     Payload for updating the bot's ephemeral (temporary runtime) context.
    /// </summary>
    public class DynamicContext
    {
        public DynamicContext() { }

        public DynamicContext(ConvaiDynamicContextUpdate update)
        {
            Text = update?.Text;
            Mode = update?.Mode switch
            {
                ConvaiContextUpdateMode.Replace => "replace",
                ConvaiContextUpdateMode.Reset => "reset",
                _ => "append"
            };
            RunLlm = update?.Reaction switch
            {
                ConvaiContextReactionMode.ReactImmediately => "true",
                ConvaiContextReactionMode.SyncOnly => "false",
                _ => "auto"
            };
            RemoveStatic = update?.RemoveStatic == true;
            CurrentAttentionObject = update?.CurrentAttentionObject;
            UpdateId = update?.UpdateId;
        }

        /// <summary>New context text to apply. Required when <see cref="Mode" /> is not "reset".</summary>
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        /// <summary>How to apply the context: "append", "replace", or "reset". Default "append".</summary>
        [JsonProperty("mode")]
        public string Mode { get; set; }

        /// <summary>Whether to trigger an LLM response: "true", "false", or "auto". Default "auto".</summary>
        [JsonProperty("run_llm")]
        public string RunLlm { get; set; }

        /// <summary>Whether reset should also remove static connect-time context.</summary>
        [JsonProperty("remove_static", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool RemoveStatic { get; set; }

        /// <summary>
        ///     Optional action-config object name or object payload used for action-reference grounding.
        ///     Send empty string to clear the current attention object.
        /// </summary>
        [JsonProperty("current_attention_object", NullValueHandling = NullValueHandling.Ignore)]
        public object CurrentAttentionObject { get; set; }

        /// <summary>Optional client-provided update identifier for backend dedupe/retry handling.</summary>
        [JsonProperty("update_id", NullValueHandling = NullValueHandling.Ignore)]
        public string UpdateId { get; set; }
    }
}
