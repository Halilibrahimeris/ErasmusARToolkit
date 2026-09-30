using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Project types for the open scene (ProjectsTab.uxml): each section has a few fields and an Add button
    /// that builds a working example; teams then change it in the Inspector.
    /// </summary>
    public sealed class ProjectsTab : IToolkitTab
    {
        readonly VisualElement noToolkit;
        readonly VisualElement warnings;
        readonly List<IProjectSection> sections;

        public ProjectsTab()
        {
            Root = EditorUi.Clone("ProjectsTab");
            noToolkit = EditorUi.Q<VisualElement>(Root, "no-toolkit");
            warnings = EditorUi.Q<VisualElement>(Root, "warnings");
            var container = EditorUi.Q<VisualElement>(Root, "sections");
            sections = ProjectSections.Create();
            foreach (var section in sections)
            {
                container.Add(section.Root);
            }
        }

        public string Title => "Projects";

        public VisualElement Root { get; }

        internal int SectionCount => sections.Count;

        public void Refresh()
        {
            var manager = SceneQueries.FindFirst<ARToolkitManager>();
            EditorUi.SetShown(noToolkit, manager == null);
            foreach (var section in sections)
            {
                section.Refresh(manager);
            }

            warnings.Clear();
            foreach (var warning in ProjectChecks.Warnings(SceneQueries.FindAll<ModulePlacement>()))
            {
                warnings.Add(EditorUi.Message(warning, HelpBoxMessageType.Warning));
            }
        }
    }

    /// <summary>The sections of the Projects tab, in the order they are shown.</summary>
    internal static class ProjectSections
    {
        public static List<IProjectSection> Create()
        {
            return new List<IProjectSection>
            {
                new DesignSection(),
                new PosterSection(),
                new GallerySection(),
                new RouteSection(),
                new VisionSection(),
                new FillSection(),
                new PortalSection(),
                new StorySection(),
                new InfoCardSection(),
            };
        }
    }

    /// <summary>Things in the open scene that will not work as the team expects.</summary>
    internal static class ProjectChecks
    {
        public static IEnumerable<string> Warnings(IEnumerable<ModulePlacement> placements)
        {
            var atStart = placements
                .Where(p => p != null && p.gameObject.scene.IsValid() && p.PlacementMode == PlacementMode.AtAppStart)
                .Select(p => p.name)
                .ToList();
            if (atStart.Count > 1)
            {
                yield return $"{string.Join(", ", atStart)} are all placed At App Start, so they all come to where " +
                             "the phone is. Set all but one to Tap To Place (Module Placement in the Inspector).";
            }
        }
    }
}
