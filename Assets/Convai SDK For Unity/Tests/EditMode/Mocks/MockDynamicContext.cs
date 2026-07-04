using System.Collections.Generic;
using Convai.Runtime.DynamicContext;

namespace Convai.Tests.EditMode.Mocks
{
    public sealed class MockDynamicContext : IConvaiDynamicContext
    {
        public List<ConvaiDynamicContextUpdate> AppliedUpdates { get; } = new();
        public List<(string Name, string Value, ConvaiContextReactionMode Reaction)> StateUpdates { get; } = new();
        public List<(string Text, ConvaiContextReactionMode Reaction)> EventUpdates { get; } = new();
        public List<(object AttentionObject, ConvaiContextReactionMode Reaction)> AttentionUpdates { get; } = new();
        public List<string> RemovedStates { get; } = new();
        public int ResetCount { get; private set; }
        public int FlushCount { get; private set; }

        public void SetState(string name, string value,
            ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly) =>
            StateUpdates.Add((name, value, reaction));

        public void SetStates(IReadOnlyDictionary<string, string> states,
            ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly)
        {
            if (states == null) return;

            foreach (KeyValuePair<string, string> state in states)
                StateUpdates.Add((state.Key, state.Value, reaction));
        }

        public void AddEvent(string text, ConvaiContextReactionMode reaction = ConvaiContextReactionMode.Auto) =>
            EventUpdates.Add((text, reaction));

        public void RemoveState(string name) => RemovedStates.Add(name);

        public void Reset(bool removeStatic = false) => ResetCount++;

        public void SetCurrentAttentionObject(
            object currentAttentionObject,
            ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly) =>
            AttentionUpdates.Add((currentAttentionObject, reaction));

        public void ClearCurrentAttentionObject(ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly) =>
            AttentionUpdates.Add((string.Empty, reaction));

        public void Flush() => FlushCount++;

        public bool TryGetStateValue(string name, out string value)
        {
            value = null;
            return false;
        }

        public void Apply(ConvaiDynamicContextUpdate update)
        {
            if (update == null) return;
            AppliedUpdates.Add(update);
        }
    }
}
