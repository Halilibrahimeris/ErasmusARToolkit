using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    public enum IssueSeverity
    {
        Warning,
        Error,
    }

    public readonly struct ConfigIssue
    {
        public ConfigIssue(IssueSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public IssueSeverity Severity { get; }
        public string Message { get; }

        public override string ToString() => $"{Severity}: {Message}";
    }

    /// <summary>Checks a config for mistakes and returns user-friendly messages.</summary>
    public static class ConfigValidator
    {
        /// <summary>Image targets this wide are unusual and usually a unit mistake.</summary>
        const float LargeImageWidthMeters = 1f;

        public static IReadOnlyList<ConfigIssue> Validate(ARToolkitConfig config)
        {
            if (config == null)
            {
                return new[] { new ConfigIssue(IssueSeverity.Error, "No toolkit config is assigned.") };
            }

            return Validate(config.Placeables, config.ImageTargets, config.MaxPlacedObjects, config.ScaleLimits);
        }

        public static IReadOnlyList<ConfigIssue> Validate(
            IReadOnlyList<PlaceableItem> placeables,
            IReadOnlyList<ImageTargetItem> imageTargets,
            int maxPlacedObjects,
            Vector2 scaleLimits)
        {
            var issues = new List<ConfigIssue>();
            issues.AddRange(ValidatePlaceables(placeables ?? new PlaceableItem[0]));
            issues.AddRange(ValidateImageTargets(imageTargets ?? new ImageTargetItem[0]));
            issues.AddRange(ValidateLimits(maxPlacedObjects, scaleLimits));
            return issues;
        }

        public static bool HasErrors(IEnumerable<ConfigIssue> issues)
        {
            return issues.Any(issue => issue.Severity == IssueSeverity.Error);
        }

        static IEnumerable<ConfigIssue> ValidatePlaceables(IReadOnlyList<PlaceableItem> placeables)
        {
            if (placeables.Count == 0)
            {
                yield return Warning("No placeable objects. Add at least one in the Content tab to use tap-to-place.");
                yield break;
            }

            for (var i = 0; i < placeables.Count; i++)
            {
                var item = placeables[i];
                var label = $"Placeable #{i + 1}";
                if (item == null || item.Prefab == null)
                {
                    yield return Error($"{label} has no prefab.");
                    continue;
                }

                if (item.DefaultScale <= 0f)
                {
                    yield return Error($"{label} '{item.ResolvedName}' must have a scale above 0.");
                }
            }

            foreach (var name in FindDuplicates(placeables.Where(p => p != null).Select(p => p.ResolvedName)))
            {
                yield return Warning($"More than one placeable is called '{name}'. HUD buttons will look the same.");
            }
        }

        static IEnumerable<ConfigIssue> ValidateImageTargets(IReadOnlyList<ImageTargetItem> targets)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var item = targets[i];
                var label = $"Image target #{i + 1}";
                if (item == null || item.Texture == null)
                {
                    yield return Error($"{label} has no image.");
                    continue;
                }

                if (item.WidthMeters <= 0f)
                {
                    yield return Error($"{label} '{item.ResolvedName}' must have a printed width above 0 meters.");
                }
                else if (item.WidthMeters >= LargeImageWidthMeters)
                {
                    yield return Warning($"{label} '{item.ResolvedName}' is set to {item.WidthMeters:0.##} m wide. " +
                                         "Enter the real printed width (A4 is about 0.21 m).");
                }

                if (item.Prefab == null && !item.Info.HasContent)
                {
                    yield return Error($"{label} '{item.ResolvedName}' needs a prefab or an info label.");
                }
            }

            foreach (var name in FindDuplicates(targets.Where(t => t != null && t.Texture != null).Select(t => t.ResolvedName)))
            {
                yield return Error($"More than one image target is called '{name}'. Names must be unique.");
            }
        }

        static IEnumerable<ConfigIssue> ValidateLimits(int maxPlacedObjects, Vector2 scaleLimits)
        {
            if (maxPlacedObjects < 1)
            {
                yield return Error("Max placed objects must be at least 1.");
            }

            if (scaleLimits.x <= 0f || scaleLimits.y <= 0f || scaleLimits.x > scaleLimits.y)
            {
                yield return Error("Scale limits must be positive and min must not be larger than max.");
            }
        }

        static IEnumerable<string> FindDuplicates(IEnumerable<string> names)
        {
            return names
                .Where(name => !string.IsNullOrEmpty(name))
                .GroupBy(name => name)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);
        }

        static ConfigIssue Error(string message) => new ConfigIssue(IssueSeverity.Error, message);
        static ConfigIssue Warning(string message) => new ConfigIssue(IssueSeverity.Warning, message);
    }
}
