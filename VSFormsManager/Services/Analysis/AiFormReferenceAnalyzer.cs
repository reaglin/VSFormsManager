using VSFormsManager.Models;

namespace VSFormsManager.Services.Analysis
{
    /// <summary>
    /// AI fallback for form-reference detection.
    ///
    /// Called when <see cref="FormReferenceScanner"/> finds <c>new SomeClass()</c>
    /// calls that it cannot confirm as forms via static analysis alone (because
    /// the class name is unconventional, instantiated via factory, etc.).
    ///
    /// Sends the form's code + the list of unresolved candidates to the AI and
    /// asks it to identify which are form classes. The AI is given the full list
    /// of known form class names as context.
    /// </summary>
    public class AiFormReferenceAnalyzer
    {
        private readonly AppSettings _settings;

        public AiFormReferenceAnalyzer(AppSettings settings)
        {
            _settings = settings;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Sends <paramref name="formCode"/> and the list of
        /// <paramref name="unresolvedCandidates"/> to the AI.
        ///
        /// Returns the subset of candidates that the AI identifies as forms,
        /// or an empty list on failure (the caller treats this gracefully).
        /// </summary>
        public async Task<List<string>> ResolveAsync(
            string              formCode,
            IEnumerable<string> unresolvedCandidates,
            IEnumerable<string> knownFormClasses,
            CancellationToken   cancellationToken)
        {
            var candidates = unresolvedCandidates.ToList();
            if (candidates.Count == 0) return new List<string>();

            try
            {
                var provider = AiProviderRouter.GetProvider(
                    AiTask.FormAnalysis, _settings);

                var systemPrompt =
                    "You are a C# code analyst. Identify which class names from a " +
                    "candidate list are Visual Studio Form classes instantiated in " +
                    "the provided source code. Reply ONLY with a comma-separated list " +
                    "of class names that are forms. If none are forms, reply with the " +
                    "single word: NONE";

                var knownList = string.Join(", ", knownFormClasses.Take(40));
                var candList  = string.Join(", ", candidates);

                var userPrompt =
                    $"Known form classes in this project (for context): {knownList}\r\n\r\n" +
                    $"Candidate class names to evaluate: {candList}\r\n\r\n" +
                    $"Source code of the form being analysed:\r\n\r\n{formCode}";

                var response = await provider.GenerateAsync(
                    systemPrompt, userPrompt, cancellationToken);

                return ParseResponse(response, candidates);
            }
            catch
            {
                // AI unavailable or no key — return empty, caller uses static results only
                return new List<string>();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static List<string> ParseResponse(
            string response, IReadOnlyList<string> candidates)
        {
            var trimmed = response.Trim();

            if (trimmed.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return new List<string>();

            // Accept only names that were in our candidate list to avoid hallucinations
            var candidateSet = new HashSet<string>(
                candidates, StringComparer.OrdinalIgnoreCase);

            return trimmed
                .Split(new[] { ',', ';', '\n', '\r' },
                       StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => candidateSet.Contains(s))
                .ToList();
        }
    }
}
