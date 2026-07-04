using System;

namespace Convai.Shared.Types
{
    /// <summary>
    ///     Structured action command returned by the backend for the current turn.
    /// </summary>
    [Serializable]
    public sealed class ConvaiActionCommand
    {
        /// <summary>Required action name selected by the backend.</summary>
        public string Name { get; set; }

        /// <summary>Optional object or character target name resolved by the backend.</summary>
        public string Target { get; set; }

        /// <summary>Returns true when the command includes a target reference.</summary>
        public bool HasTarget => !string.IsNullOrWhiteSpace(Target);

        public ConvaiActionCommand()
        {
        }

        public ConvaiActionCommand(string name, string target = null)
        {
            Name = Normalize(name);
            Target = Normalize(target);
        }

        /// <summary>Creates a normalized copy of this action command.</summary>
        public ConvaiActionCommand Clone() => new(Normalize(Name), Normalize(Target));

        /// <inheritdoc />
        public override string ToString() => HasTarget ? $"{Normalize(Name)} {Normalize(Target)}" : Normalize(Name);

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
