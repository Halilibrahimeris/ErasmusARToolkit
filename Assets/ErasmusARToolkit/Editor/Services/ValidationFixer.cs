using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.XR.CoreUtils.Editor;
using UnityEditor;
using UnityEngine;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Runs Unity's XR Project Validation rules (the same list as Project Settings >
    /// XR Plug-in Management > Project Validation) and applies the automatic fixes.
    /// </summary>
    public static class ValidationFixer
    {
        // The issue query is internal in XR Core Utils, so it is reached by reflection.
        // If it ever changes, the toolkit falls back to opening the validation window.
        static readonly MethodInfo GetIssuesMethod = typeof(BuildValidator).GetMethod(
            "GetCurrentValidationIssues", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        public static bool IsAvailable => GetIssuesMethod != null;

        /// <summary>Returns the failing rules, or null when the query is unavailable.</summary>
        public static IReadOnlyList<BuildValidationRule> GetIssues(BuildTargetGroup group)
        {
            if (!IsAvailable)
            {
                return null;
            }

            try
            {
                var failures = new HashSet<BuildValidationRule>();
                GetIssuesMethod.Invoke(null, new object[] { failures, group });
                return failures.ToList();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Erasmus AR Toolkit] Could not read XR validation issues: " + exception.Message);
                return null;
            }
        }

        /// <summary>Applies every automatic fix. Returns how many rules were fixed, or -1 if unavailable.</summary>
        public static int FixAll(BuildTargetGroup group)
        {
            var issues = GetIssues(group);
            if (issues == null)
            {
                OpenValidationWindow();
                return -1;
            }

            var fixable = issues.Where(issue => issue.FixItAutomatic && issue.FixIt != null).ToList();
            if (fixable.Count > 0)
            {
                BuildValidator.FixIssues(fixable, "Erasmus AR Toolkit");
                AssetDatabase.SaveAssets();
            }

            return fixable.Count;
        }

        public static void OpenValidationWindow()
        {
            SettingsService.OpenProjectSettings("Project/XR Plug-in Management/Project Validation");
        }
    }
}
