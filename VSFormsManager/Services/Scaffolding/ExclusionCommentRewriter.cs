using System.Text.RegularExpressions;

namespace VSFormsManager.Services.Scaffolding
{
    /// <summary>
    /// When the user excludes a form or code file from the scaffold, this class
    /// rewrites the included files that reference it — wrapping the relevant
    /// lines in clearly marked comment blocks so the project compiles (or at
    /// least fails with obvious, fixable errors).
    ///
    /// Comment-out strategy:
    ///   • using directives for excluded namespaces → line comment
    ///   • Lines that instantiate an excluded class  → block comment with notice
    ///   • Lines that call .Show() / .ShowDialog() on an excluded-typed var → block comment
    ///
    /// All comment blocks are tagged with <c>// EXCLUDED: ClassName</c> so they
    /// are easy to find and restore.
    /// </summary>
    public static class ExclusionCommentRewriter
    {
        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Rewrites <paramref name="sourceContent"/> to comment out all references
        /// to any class in <paramref name="excludedClassNames"/>.
        /// </summary>
        public static string Rewrite(
            string              sourceContent,
            IEnumerable<string> excludedClassNames)
        {
            var excluded = excludedClassNames.ToList();
            if (excluded.Count == 0) return sourceContent;

            var lines  = sourceContent.Split('\n');
            var result = new System.Text.StringBuilder();

            foreach (var rawLine in lines)
            {
                var line         = rawLine.TrimEnd('\r');
                var commentedOut = false;

                foreach (var className in excluded)
                {
                    if (!LineReferencesClass(line, className)) continue;

                    // Preserve indentation
                    var indent = line.Length - line.TrimStart().Length;
                    var pad    = new string(' ', indent);

                    result.AppendLine($"{pad}// EXCLUDED: {className} — " +
                                      "reference commented out by VSFormsManager");
                    result.Append("// ");
                    result.AppendLine(line.TrimStart());
                    commentedOut = true;
                    break;
                }

                if (!commentedOut)
                    result.AppendLine(line);
            }

            return result.ToString();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when <paramref name="line"/> meaningfully references
        /// <paramref name="className"/> — i.e. the name appears as a whole
        /// word (not as a substring of another identifier).
        /// </summary>
        private static bool LineReferencesClass(string line, string className)
        {
            // Skip blank lines and pure comment lines
            var trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("//") ||
                trimmed.StartsWith("/*"))
                return false;

            // Whole-word match using word boundaries
            return Regex.IsMatch(line, $@"\b{Regex.Escape(className)}\b");
        }
    }
}
