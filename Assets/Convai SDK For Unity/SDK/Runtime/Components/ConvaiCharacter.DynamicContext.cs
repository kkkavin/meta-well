using System;
using System.Collections;
using System.Collections.Generic;
using Convai.Runtime.DynamicContext;
using Convai.Runtime.Room;
using UnityEngine;

namespace Convai.Runtime.Components
{
    public partial class ConvaiCharacter
    {
        private const float DynamicContextBatchDelaySeconds = 0.5f;
        private const float DynamicContextMaxBatchDelaySeconds = 3f;

        private readonly struct PendingDynamicContextStateChange
        {
            public PendingDynamicContextStateChange(string previousValue, bool hadPreviousValue)
            {
                PreviousValue = previousValue;
                HadPreviousValue = hadPreviousValue;
            }

            public string PreviousValue { get; }
            public bool HadPreviousValue { get; }
        }

        private sealed class CharacterDynamicContextFacade : IConvaiDynamicContext
        {
            private readonly ConvaiCharacter _owner;

            public CharacterDynamicContextFacade(ConvaiCharacter owner) => _owner = owner;

            public void SetState(string name, string value,
                ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly) =>
                _owner.SetTrackedDynamicContextState(name, value, reaction);

            public void SetStates(IReadOnlyDictionary<string, string> states,
                ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly) =>
                _owner.SetTrackedDynamicContextStates(states, reaction);

            public void AddEvent(string text, ConvaiContextReactionMode reaction = ConvaiContextReactionMode.Auto) =>
                _owner.AddTrackedDynamicContextEvent(text, reaction);

            public void RemoveState(string name) => _owner.RemoveTrackedDynamicContextState(name);

            public void Reset(bool removeStatic = false) => _owner.ResetTrackedDynamicContext(removeStatic);

            public void SetCurrentAttentionObject(
                object currentAttentionObject,
                ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly) =>
                _owner.SetTrackedDynamicContextAttentionObject(currentAttentionObject, reaction);

            public void ClearCurrentAttentionObject(
                ConvaiContextReactionMode reaction = ConvaiContextReactionMode.SyncOnly) =>
                _owner.SetTrackedDynamicContextAttentionObject(string.Empty, reaction);

            public void Flush() => _owner.FlushPendingContextUpdates();

            public bool TryGetStateValue(string name, out string value) =>
                _owner.TryGetTrackedDynamicContextStateValue(name, out value);

            public void Apply(ConvaiDynamicContextUpdate update) => _owner.ApplyRawDynamicContextUpdate(update);
        }

        private CharacterDynamicContextFacade _dynamicContext;
        private readonly ConvaiDynamicContextTracker _dynamicContextTracker = new();
        private readonly HashSet<string> _pendingDynamicContextEventSet = new(StringComparer.Ordinal);
        private readonly List<string> _pendingDynamicContextStateOrder = new();
        private readonly Dictionary<string, PendingDynamicContextStateChange> _pendingDynamicContextStateChanges = new(StringComparer.Ordinal);
        private bool _hasPendingDynamicContextSync;
        private bool _hasPendingDynamicContextReset;
        private bool _hasPendingDynamicContextTextChange;
        private bool _hasPendingDynamicContextAttention;
        private bool _pendingDynamicContextRemoveStatic;
        private ConvaiContextReactionMode _pendingDynamicContextReaction = ConvaiContextReactionMode.SyncOnly;
        private object _pendingDynamicContextAttentionObject;
        private Coroutine _dynamicContextFlushCoroutine;
        private float _dynamicContextBatchWindowStartTime = -1f;
        private float _dynamicContextNextFlushTime = -1f;
        private long _dynamicContextUpdateSequence;

        /// <summary>
        ///     Character-owned runtime surface for tracked dynamic context state and events.
        /// </summary>
        public IConvaiDynamicContext DynamicContext => _dynamicContext ??= new CharacterDynamicContextFacade(this);

        private IConvaiDynamicContextTransport DynamicContextTransport => ConnectionService as IConvaiDynamicContextTransport;

        private void SetTrackedDynamicContextState(string name, string value, ConvaiContextReactionMode reaction)
        {
            if (!TryValidateDynamicContextStateName(name) || !TryValidateDynamicContextStateValue(name, value)) return;

            ConvaiDynamicContextStateChangeResult result = _dynamicContextTracker.SetState(name, value);
            if (!result.HasChanged) return;

            StagePendingDynamicContextStateChange(name, result);
            MarkPendingDynamicContextSync(reaction, textChanged: true);
        }

        private void SetTrackedDynamicContextStates(
            IReadOnlyDictionary<string, string> states,
            ConvaiContextReactionMode reaction)
        {
            if (states == null || states.Count == 0)
            {
                Logger?.Warning($"[ConvaiCharacter] [{_characterName}] Cannot set empty dynamic context states");
                return;
            }

            bool changed = false;

            foreach (KeyValuePair<string, string> state in states)
            {
                if (!TryValidateDynamicContextStateName(state.Key) ||
                    !TryValidateDynamicContextStateValue(state.Key, state.Value))
                    continue;

                ConvaiDynamicContextStateChangeResult result = _dynamicContextTracker.SetState(state.Key, state.Value);
                if (!result.HasChanged) continue;

                StagePendingDynamicContextStateChange(state.Key, result);
                changed = true;
            }

            if (changed) MarkPendingDynamicContextSync(reaction, textChanged: true);
        }

        private void AddTrackedDynamicContextEvent(string text, ConvaiContextReactionMode reaction)
        {
            if (!TryValidateDynamicContextEventText(text)) return;

            if (!_pendingDynamicContextEventSet.Add(text)) return;

            _dynamicContextTracker.AddEvent(text);
            MarkPendingDynamicContextSync(reaction, textChanged: true);
        }

        private void RemoveTrackedDynamicContextState(string name)
        {
            if (!TryValidateDynamicContextStateName(name)) return;
            if (!_dynamicContextTracker.RemoveState(name)) return;

            MarkPendingDynamicContextSync(ConvaiContextReactionMode.SyncOnly, textChanged: true);
        }

        private void ResetTrackedDynamicContext(bool removeStatic)
        {
            _dynamicContextTracker.Reset();
            _pendingDynamicContextEventSet.Clear();
            _pendingDynamicContextStateOrder.Clear();
            _pendingDynamicContextStateChanges.Clear();
            _hasPendingDynamicContextReset = true;
            _pendingDynamicContextRemoveStatic = removeStatic;
            _hasPendingDynamicContextSync = false;
            _hasPendingDynamicContextTextChange = false;
            _hasPendingDynamicContextAttention = false;
            _pendingDynamicContextReaction = ConvaiContextReactionMode.SyncOnly;

            ResetDynamicContextBatchTiming();
            ScheduleDynamicContextFlush();
        }

        private bool TryGetTrackedDynamicContextStateValue(string name, out string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                value = null;
                return false;
            }

            return _dynamicContextTracker.TryGetStateValue(name, out value);
        }

        private void ApplyRawDynamicContextUpdate(ConvaiDynamicContextUpdate update)
        {
            if (!TryValidateRawDynamicContextUpdate(update)) return;

            if (!IsInConversation)
            {
                Logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Cannot apply raw dynamic context update: not in conversation");
                return;
            }

            TrySendDynamicContextUpdate(update, "raw dynamic context update");
        }

        private void FlushPendingDynamicContext()
        {
            if (!IsInConversation) return;

            StopScheduledDynamicContextFlush();

            if (_hasPendingDynamicContextReset)
            {
                if (TrySendDynamicContextUpdate(
                        new ConvaiDynamicContextUpdate(
                            null,
                            ConvaiContextUpdateMode.Reset,
                            ConvaiContextReactionMode.SyncOnly,
                            removeStatic: _pendingDynamicContextRemoveStatic,
                            updateId: CreateDynamicContextUpdateId()),
                        "pending dynamic context reset"))
                {
                    _hasPendingDynamicContextReset = false;
                    _pendingDynamicContextRemoveStatic = false;
                    ResetDynamicContextBatchTiming();
                }

                return;
            }

            if (!_hasPendingDynamicContextSync) return;

            ConvaiDynamicContextUpdate update = BuildPendingDynamicContextUpdate();
            if (TrySendDynamicContextUpdate(update, "dynamic context batch"))
                ClearPendingDynamicContextSync();
        }

        private void FlushPendingContextUpdates()
        {
            FlushPendingDynamicContext();
            FlushPendingSceneMetadata();
        }

        private void MarkPendingDynamicContextSync(ConvaiContextReactionMode reaction, bool textChanged)
        {
            _hasPendingDynamicContextSync = true;
            _hasPendingDynamicContextReset = false;
            _hasPendingDynamicContextTextChange |= textChanged;
            _pendingDynamicContextReaction = AggregateDynamicContextReaction(_pendingDynamicContextReaction, reaction);
            ScheduleDynamicContextFlush();
        }

        private void StagePendingDynamicContextStateChange(
            string name,
            ConvaiDynamicContextStateChangeResult result)
        {
            if (_pendingDynamicContextStateChanges.ContainsKey(name)) return;

            _pendingDynamicContextStateOrder.Add(name);
            _pendingDynamicContextStateChanges[name] = new PendingDynamicContextStateChange(
                result.PreviousValue,
                !result.IsNew);
        }

        private void MarkPendingDynamicContextAttention(object currentAttentionObject, ConvaiContextReactionMode reaction)
        {
            _hasPendingDynamicContextAttention = true;
            _pendingDynamicContextAttentionObject = currentAttentionObject;
            MarkPendingDynamicContextSync(reaction, textChanged: false);
        }

        private void SetTrackedDynamicContextAttentionObject(
            object currentAttentionObject,
            ConvaiContextReactionMode reaction)
        {
            if (currentAttentionObject == null)
            {
                Logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Dynamic context attention object cannot be null");
                return;
            }

            if (currentAttentionObject is string objectName)
            {
                if (objectName.Length > 0)
                {
                    string normalizedObjectName = objectName.Trim();
                    if (string.IsNullOrWhiteSpace(normalizedObjectName))
                    {
                        Logger?.Warning(
                            $"[ConvaiCharacter] [{_characterName}] Dynamic context attention object cannot be empty");
                        return;
                    }

                    if (!CanUseAttentionObject(normalizedObjectName))
                    {
                        Logger?.Warning(
                            $"[ConvaiCharacter] [{_characterName}] Cannot set attention object '{normalizedObjectName}': it is not present in the active action_config objects.");
                        return;
                    }

                    currentAttentionObject = normalizedObjectName;
                }
            }

            MarkPendingDynamicContextAttention(currentAttentionObject, reaction);
        }

        private ConvaiDynamicContextUpdate BuildPendingDynamicContextUpdate()
        {
            string text = _hasPendingDynamicContextTextChange
                ? BuildPendingDynamicContextText()
                : null;
            ConvaiContextUpdateMode mode = _hasPendingDynamicContextTextChange
                ? ConvaiContextUpdateMode.Replace
                : ConvaiContextUpdateMode.Append;

            return new ConvaiDynamicContextUpdate(
                text,
                mode,
                _pendingDynamicContextReaction,
                currentAttentionObject: _hasPendingDynamicContextAttention ? _pendingDynamicContextAttentionObject : null,
                updateId: CreateDynamicContextUpdateId());
        }

        private string BuildPendingDynamicContextText()
        {
            bool shouldIncludeDeltaTail = _pendingDynamicContextReaction != ConvaiContextReactionMode.SyncOnly;
            List<string> deltaLines = shouldIncludeDeltaTail ? BuildPendingDynamicContextDeltaLines() : null;
            HashSet<string> firstAppearanceKeys = shouldIncludeDeltaTail ? BuildFirstAppearanceKeySet() : null;
            string canonical = _dynamicContextTracker.BuildCanonicalContext(firstAppearanceKeys);

            if (deltaLines == null || deltaLines.Count == 0) return canonical;
            string deltaText = string.Join("\n", deltaLines);
            return string.IsNullOrEmpty(canonical) ? deltaText : $"{canonical}\n{deltaText}";
        }

        private List<string> BuildPendingDynamicContextDeltaLines()
        {
            var deltaLines = new List<string>(_pendingDynamicContextStateOrder.Count);
            foreach (string stateName in _pendingDynamicContextStateOrder)
            {
                if (!_dynamicContextTracker.TryGetStateValue(stateName, out string currentValue)) continue;
                if (!_pendingDynamicContextStateChanges.TryGetValue(stateName, out PendingDynamicContextStateChange change))
                    continue;

                deltaLines.Add(change.HadPreviousValue
                    ? BuildDynamicContextChangedDeltaLine(stateName, change.PreviousValue, currentValue)
                    : $"{stateName} is {currentValue}");
            }

            return deltaLines;
        }

        private HashSet<string> BuildFirstAppearanceKeySet()
        {
            var firstAppearanceKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stateName in _pendingDynamicContextStateOrder)
                if (_pendingDynamicContextStateChanges.TryGetValue(stateName, out PendingDynamicContextStateChange change) &&
                    !change.HadPreviousValue)
                    firstAppearanceKeys.Add(stateName);

            return firstAppearanceKeys;
        }

        private static string BuildDynamicContextChangedDeltaLine(
            string stateName,
            string previousValue,
            string currentValue) =>
            CountWhitespaceSeparatedWords(currentValue) > 3
                ? $"{stateName} changed from {previousValue}"
                : $"{stateName} changed from {previousValue} to {currentValue}";

        private static int CountWhitespaceSeparatedWords(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            int wordCount = 0;
            bool inWord = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    inWord = false;
                    continue;
                }

                if (!inWord)
                {
                    inWord = true;
                    if (++wordCount > 3) break;
                }
            }

            return wordCount;
        }

        private void ClearPendingDynamicContextSync()
        {
            _hasPendingDynamicContextSync = false;
            _hasPendingDynamicContextTextChange = false;
            _hasPendingDynamicContextAttention = false;
            _pendingDynamicContextAttentionObject = null;
            _pendingDynamicContextReaction = ConvaiContextReactionMode.SyncOnly;
            _pendingDynamicContextEventSet.Clear();
            _pendingDynamicContextStateOrder.Clear();
            _pendingDynamicContextStateChanges.Clear();
            ResetDynamicContextBatchTiming();
        }

        private static ConvaiContextReactionMode AggregateDynamicContextReaction(
            ConvaiContextReactionMode current,
            ConvaiContextReactionMode incoming) =>
            GetDynamicContextReactionRank(incoming) > GetDynamicContextReactionRank(current) ? incoming : current;

        private static int GetDynamicContextReactionRank(ConvaiContextReactionMode reaction) =>
            reaction switch
            {
                ConvaiContextReactionMode.ReactImmediately => 2,
                ConvaiContextReactionMode.Auto => 1,
                _ => 0
            };

        private void ScheduleDynamicContextFlush()
        {
            if (!IsInConversation || !isActiveAndEnabled) return;

            float now = Time.unscaledTime;
            if (_dynamicContextBatchWindowStartTime < 0f)
                _dynamicContextBatchWindowStartTime = now;

            float maxDeadline = _dynamicContextBatchWindowStartTime + DynamicContextMaxBatchDelaySeconds;
            _dynamicContextNextFlushTime = Mathf.Min(now + DynamicContextBatchDelaySeconds, maxDeadline);
            if (_dynamicContextFlushCoroutine != null) return;

            _dynamicContextFlushCoroutine = StartCoroutine(FlushDynamicContextAfterDelay());
        }

        private IEnumerator FlushDynamicContextAfterDelay()
        {
            while (true)
            {
                float remaining = _dynamicContextNextFlushTime - Time.unscaledTime;
                if (remaining <= 0f) break;

                yield return new WaitForSecondsRealtime(remaining);
            }

            _dynamicContextFlushCoroutine = null;
            FlushPendingContextUpdates();
        }

        private void StopScheduledDynamicContextFlush()
        {
            if (_dynamicContextFlushCoroutine == null) return;

            StopCoroutine(_dynamicContextFlushCoroutine);
            _dynamicContextFlushCoroutine = null;
        }

        private void ResetDynamicContextBatchTiming()
        {
            _dynamicContextBatchWindowStartTime = -1f;
            _dynamicContextNextFlushTime = -1f;
        }

        private string CreateDynamicContextUpdateId()
        {
            _dynamicContextUpdateSequence++;
            string characterId = string.IsNullOrWhiteSpace(CharacterId) ? "character" : CharacterId;
            return $"unity-{characterId}-{_dynamicContextUpdateSequence}";
        }

        private bool TrySendDynamicContextUpdate(ConvaiDynamicContextUpdate update, string purpose)
        {
            IConvaiDynamicContextTransport transport = DynamicContextTransport;
            if (transport == null)
            {
                Logger?.Warning(
                    $"[ConvaiCharacter] [{_characterName}] Dynamic context transport unavailable for {purpose}");
                return false;
            }

            if (transport.SendDynamicContext(update)) return true;

            Logger?.Warning(
                $"[ConvaiCharacter] [{_characterName}] Connection not ready for {purpose}");
            return false;
        }

        private bool TryValidateDynamicContextStateName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) return true;

            Logger?.Warning($"[ConvaiCharacter] [{_characterName}] Dynamic context state name cannot be empty");
            return false;
        }

        private bool TryValidateDynamicContextStateValue(string name, string value)
        {
            if (value != null) return true;

            Logger?.Warning(
                $"[ConvaiCharacter] [{_characterName}] Dynamic context state '{name}' cannot use a null value");
            return false;
        }

        private bool TryValidateDynamicContextEventText(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) return true;

            Logger?.Warning($"[ConvaiCharacter] [{_characterName}] Dynamic context event text cannot be empty");
            return false;
        }

        private bool TryValidateRawDynamicContextUpdate(ConvaiDynamicContextUpdate update)
        {
            if (update != null && (update.Mode == ConvaiContextUpdateMode.Reset || update.Text != null)) return true;

            Logger?.Warning(
                $"[ConvaiCharacter] [{_characterName}] Raw dynamic context updates require text unless mode is Reset");
            return false;
        }
    }
}
