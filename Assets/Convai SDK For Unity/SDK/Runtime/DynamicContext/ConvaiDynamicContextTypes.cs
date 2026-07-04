using System;
using System.Collections.Generic;
using UnityEngine;

namespace Convai.Runtime.DynamicContext
{
    /// <summary>
    ///     Controls whether a dynamic context update should immediately influence bot reasoning.
    /// </summary>
    public enum ConvaiContextReactionMode
    {
        Auto = 0,
        ReactImmediately,
        SyncOnly
    }

    /// <summary>
    ///     Controls how dynamic context text is applied on the backend.
    /// </summary>
    public enum ConvaiContextUpdateMode
    {
        Append = 0,
        Replace,
        Reset
    }

    /// <summary>
    ///     Serializable name/value pair used by inspector-facing dynamic context tooling.
    /// </summary>
    [Serializable]
    public sealed class ConvaiContextStateEntry
    {
        [SerializeField] private string _name = string.Empty;
        [SerializeField] private string _value = string.Empty;

        public ConvaiContextStateEntry() { }

        public ConvaiContextStateEntry(string name, string value)
        {
            _name = name ?? string.Empty;
            _value = value;
        }

        public string Name => _name;
        public string Value => _value;
    }

    /// <summary>
    ///     Advanced typed request used to send raw dynamic context updates without exposing transport strings.
    /// </summary>
    public sealed class ConvaiDynamicContextUpdate
    {
        public ConvaiDynamicContextUpdate(
            string text,
            ConvaiContextUpdateMode mode = ConvaiContextUpdateMode.Append,
            ConvaiContextReactionMode reaction = ConvaiContextReactionMode.Auto,
            bool removeStatic = false,
            object currentAttentionObject = null,
            string updateId = null)
        {
            Text = text;
            Mode = mode;
            Reaction = reaction;
            RemoveStatic = removeStatic;
            CurrentAttentionObject = currentAttentionObject;
            UpdateId = updateId;
        }

        public string Text { get; }
        public ConvaiContextUpdateMode Mode { get; }
        public ConvaiContextReactionMode Reaction { get; }
        public bool RemoveStatic { get; }
        public object CurrentAttentionObject { get; }
        public string UpdateId { get; }
    }

    /// <summary>
    ///     Character-owned runtime context surface for authoring stateful dynamic context.
    /// </summary>
    public interface IConvaiDynamicContext
    {
        /// <summary>
        ///     Sets or updates one tracked state entry.
        /// </summary>
        public void SetState(string name, string value,
            ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly);

        /// <summary>
        ///     Sets or updates multiple tracked state entries in one call.
        /// </summary>
        public void SetStates(IReadOnlyDictionary<string, string> states,
            ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly);

        /// <summary>
        ///     Appends a chronological event entry.
        /// </summary>
        public void AddEvent(string text, ConvaiContextReactionMode reaction = ConvaiContextReactionMode.Auto);

        /// <summary>
        ///     Removes one tracked state entry.
        /// </summary>
        public void RemoveState(string name);

        /// <summary>
        ///     Clears all tracked dynamic context for this character.
        /// </summary>
        public void Reset(bool removeStatic = false);

        /// <summary>
        ///     Updates the current object used by backend action-reference grounding.
        /// </summary>
        public void SetCurrentAttentionObject(object currentAttentionObject,
            ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly);

        /// <summary>
        ///     Clears the current object used by backend action-reference grounding.
        /// </summary>
        public void ClearCurrentAttentionObject(ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly);

        /// <summary>
        ///     Sends staged dynamic context changes immediately.
        /// </summary>
        public void Flush();

        /// <summary>
        ///     Attempts to fetch the latest tracked value for a state.
        /// </summary>
        public bool TryGetStateValue(string name, out string value);

        /// <summary>
        ///     Sends a raw typed update without mutating tracked local state.
        /// </summary>
        public void Apply(ConvaiDynamicContextUpdate update);
    }
}
