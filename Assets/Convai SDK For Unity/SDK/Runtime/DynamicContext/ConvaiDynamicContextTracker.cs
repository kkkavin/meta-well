using System;
using System.Collections.Generic;

namespace Convai.Runtime.DynamicContext
{
    internal readonly struct ConvaiDynamicContextStateChangeResult
    {
        internal ConvaiDynamicContextStateChangeResult(bool hasChanged, bool isNew, string previousValue)
        {
            HasChanged = hasChanged;
            IsNew = isNew;
            PreviousValue = previousValue;
        }

        public bool HasChanged { get; }
        public bool IsNew { get; }
        public string PreviousValue { get; }
    }

    internal sealed class ConvaiDynamicContextTracker
    {
        private readonly List<string> _eventLines = new();
        private readonly List<string> _stateOrder = new();
        private readonly Dictionary<string, string> _stateValues = new(StringComparer.Ordinal);

        public bool HasTrackedContent => _stateOrder.Count > 0 || _eventLines.Count > 0;

        public ConvaiDynamicContextStateChangeResult SetState(string name, string value)
        {
            if (_stateValues.TryGetValue(name, out string existingValue))
            {
                if (existingValue == value)
                    return new ConvaiDynamicContextStateChangeResult(false, false, existingValue);

                _stateValues[name] = value;
                return new ConvaiDynamicContextStateChangeResult(true, false, existingValue);
            }

            _stateValues[name] = value;
            _stateOrder.Add(name);
            return new ConvaiDynamicContextStateChangeResult(true, true, null);
        }

        public bool TryGetStateValue(string name, out string value) => _stateValues.TryGetValue(name, out value);

        public void AddEvent(string text) => _eventLines.Add(text);

        public bool RemoveState(string name)
        {
            if (!_stateValues.Remove(name)) return false;

            _stateOrder.Remove(name);
            return true;
        }

        public void Reset()
        {
            _stateValues.Clear();
            _stateOrder.Clear();
            _eventLines.Clear();
        }

        public string BuildCanonicalContext(ICollection<string> excludedStateNames = null)
        {
            if (!HasTrackedContent) return string.Empty;

            var lines = new List<string>(_stateOrder.Count + _eventLines.Count);
            foreach (string stateName in _stateOrder)
            {
                if (excludedStateNames != null && excludedStateNames.Contains(stateName)) continue;
                if (_stateValues.TryGetValue(stateName, out string stateValue))
                    lines.Add($"{stateName} is {stateValue}");
            }

            lines.AddRange(_eventLines);
            return string.Join("\n", lines);
        }
    }
}
