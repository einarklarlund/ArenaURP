using System.Collections.Generic;

namespace Arena.LevelGen
{
    /// <summary>
    /// What <see cref="LevelGenerator.Generate"/> made: a level that passes every hard constraint, or the reason
    /// there is none. A level that breaks a hard constraint is never handed out.
    /// </summary>
    public sealed class GenerationResult
    {
        private readonly LevelDescription _level;

        public bool Success => _level != null;

        /// <summary>Why generation failed; null on success.</summary>
        public readonly string Reason;
        /// <summary>Everything the settings check found, warnings included.</summary>
        public readonly List<SettingsIssue> SettingsIssues;
        /// <summary>On failure, the hard constraints the closest attempt broke. Empty when settings were refused
        /// or no attempt got as far as validating.</summary>
        public readonly List<ConstraintResult> Failures;
        /// <summary>Whole-level attempts made (0 when the settings were refused).</summary>
        public readonly int Attempts;

        public GenerationResult(LevelDescription level, string reason, List<SettingsIssue> settingsIssues,
            List<ConstraintResult> failures, int attempts)
        {
            _level = level;
            Reason = reason;
            SettingsIssues = settingsIssues;
            Failures = failures ?? new List<ConstraintResult>();
            Attempts = attempts;
        }

        /// <summary>The level. Throws with the reason when generation failed, so check <see cref="Success"/> first.</summary>
        public LevelDescription Level =>
            _level ?? throw new System.InvalidOperationException($"No level was generated: {Reason}");
    }
}
