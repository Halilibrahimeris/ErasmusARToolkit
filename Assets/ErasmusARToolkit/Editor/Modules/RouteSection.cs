using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Adds, orders and removes the stops of a school route (image targets with a stop number).</summary>
    internal static class RouteBuilder
    {
        public const string TrackerName = "School Route";

        /// <summary>The route stops of the config, by number.</summary>
        public static List<ImageTargetItem> Stops(ARToolkitConfig config)
        {
            return config == null
                ? new List<ImageTargetItem>()
                : config.ImageTargets.Where(t => t != null && t.RouteStop.IsStop).OrderBy(t => t.RouteStop.Order).ToList();
        }

        /// <param name="image">The printed image of the stop; null makes a new marker to print.</param>
        public static ImageTargetItem AddStop(ARToolkitConfig config, Texture2D image, float widthMeters, string title,
            string body, string nextHint, RouteArrow arrow, GameObject model)
        {
            var number = Stops(config).Select(s => s.RouteStop.Order).DefaultIfEmpty(0).Max() + 1;
            if (image == null)
            {
                image = MarkerFiles.Create(ToolkitPaths.RouteFolder, "Stop " + number);
            }

            var name = ConfigEdits.UniqueTargetName(config, string.IsNullOrWhiteSpace(title) ? "Stop " + number : title);
            var item = new ImageTargetItem(name, image, Mathf.Max(0.05f, widthMeters), model,
                new InfoLabelData(title, body), null, new RouteStop(number, nextHint, arrow));
            ConfigEdits.AddImageTarget(config, item, "Add Route Stop");
            return item;
        }

        /// <summary>Moves a stop one place earlier (-1) or later (+1) on the route.</summary>
        public static void Move(ARToolkitConfig config, ImageTargetItem stop, int delta)
        {
            var stops = Stops(config);
            var from = stops.IndexOf(stop);
            var to = from + delta;
            if (from < 0 || to < 0 || to >= stops.Count)
            {
                return;
            }

            Undo.RecordObject(config, "Move Route Stop");
            var a = stops[from].RouteStop;
            var b = stops[to].RouteStop;
            stops[from].SetRouteStop(new RouteStop(b.Order, a.NextHint, a.Arrow));
            stops[to].SetRouteStop(new RouteStop(a.Order, b.NextHint, b.Arrow));
            ConfigEdits.Save(config, false);
        }

        /// <summary>Removes a stop; the stops after it move up one number.</summary>
        public static void Remove(ARToolkitConfig config, ImageTargetItem stop)
        {
            ConfigEdits.RemoveImageTarget(config, stop, "Remove Route Stop");
            Renumber(config);
        }

        /// <summary>Numbers the stops 1, 2, 3 ... in their order.</summary>
        public static void Renumber(ARToolkitConfig config)
        {
            var stops = Stops(config);
            for (var i = 0; i < stops.Count; i++)
            {
                var stop = stops[i].RouteStop;
                if (stop.Order != i + 1)
                {
                    stops[i].SetRouteStop(new RouteStop(i + 1, stop.NextHint, stop.Arrow));
                }
            }

            ConfigEdits.Save(config, false);
        }

        /// <summary>The route tracker of the open scene; made when there is none.</summary>
        public static RouteTracker EnsureTracker(string routeName, out bool created)
        {
            var existing = SceneQueries.FindAll<RouteTracker>().FirstOrDefault(t => t.gameObject.scene.IsValid());
            created = existing == null;
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(string.IsNullOrWhiteSpace(routeName) ? TrackerName : routeName.Trim());
            var tracker = go.AddComponent<RouteTracker>();
            var serialized = new SerializedObject(tracker);
            serialized.FindProperty("routeName").stringValue = go.name;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return tracker;
        }

        public static string Describe(ImageTargetItem stop)
        {
            var next = RouteText.Next(stop.RouteStop);
            return $"{stop.RouteStop.Order}. {stop.ResolvedName}" + (next.Length > 0 ? "  -  " + next : string.Empty);
        }
    }

    /// <summary>Projects tab: school route (printed signs all over the school, found one after the other).</summary>
    internal sealed class RouteSection : ProjectSection
    {
        readonly TextField routeNameField;
        readonly ObjectField imageField;
        readonly FloatField widthField;
        readonly TextField titleField;
        readonly TextField bodyField;
        readonly TextField hintField;
        readonly EnumField arrowField;
        readonly ObjectField modelField;
        readonly Button addButton;

        public RouteSection() : base("School Route",
            "Printed signs all over the school. At each stop the phone shows its content, \"Route 2 / 6\" and an arrow\n" +
            "with the way to the next stop. Leave the image empty to get a new marker to print.")
        {
            routeNameField = AddField(new TextField("Route name") { value = RouteBuilder.TrackerName });
            imageField = AddField(new ObjectField("Stop image") { objectType = typeof(Texture2D), allowSceneObjects = false },
                "The printed sign. Empty: the toolkit makes a marker to print.");
            widthField = AddField(new FloatField("Printed width (m)") { value = 0.21f });
            titleField = AddField(new TextField("Title") { value = "Entrance" });
            bodyField = AddField(new TextField("Text") { multiline = true, value = "What happens at this place." });
            hintField = AddField(new TextField("Next stop is") { value = "Library, upstairs" },
                "How to get to the next stop.");
            arrowField = AddField(new EnumField("Arrow to next", RouteArrow.Right),
                "Which way the next stop is, for someone looking at this sign.");
            modelField = AddField(new ObjectField("3D model (optional)") { objectType = typeof(GameObject), allowSceneObjects = false });
            addButton = AddButton("Add Stop", AddStop, true);
            AddButton("Select Route Tracker", () =>
            {
                var tracker = SceneQueries.FindAll<RouteTracker>().FirstOrDefault();
                if (tracker != null)
                {
                    SelectAndPing(tracker.gameObject);
                }
            });
        }

        void AddStop()
        {
            var config = Config;
            if (config == null)
            {
                return;
            }

            var tracker = RouteBuilder.EnsureTracker(routeNameField.value, out var created);
            if (created)
            {
                RegisterCreated(tracker.gameObject, "Add Route Tracker");
            }

            var stop = RouteBuilder.AddStop(config, imageField.value as Texture2D, widthField.value, titleField.value,
                bodyField.value, hintField.value, (RouteArrow)arrowField.value, modelField.value as GameObject);
            ShowMessage($"Added stop {stop.RouteStop.Order} '{stop.ResolvedName}'. Print {AssetDatabase.GetAssetPath(stop.Texture)} " +
                        $"{stop.WidthMeters:0.##} m wide and put it up at its place.");
        }

        protected override void OnRefresh()
        {
            var config = Config;
            addButton.SetEnabled(config != null);
            ListTitle.text = "Stops (in order)";
            Instances.Clear();
            var stops = RouteBuilder.Stops(config);
            if (stops.Count == 0)
            {
                var empty = new Label(config != null ? "None yet." : "Create the AR scene first.");
                empty.AddToClassList("hint");
                Instances.Add(empty);
                return;
            }

            for (var i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                var row = new VisualElement();
                row.AddToClassList("instance-row");
                row.Add(new Label(RouteBuilder.Describe(stop)));
                var up = new Button(() => Change(() => RouteBuilder.Move(config, stop, -1))) { text = "Up" };
                up.SetEnabled(i > 0);
                var down = new Button(() => Change(() => RouteBuilder.Move(config, stop, 1))) { text = "Down" };
                down.SetEnabled(i < stops.Count - 1);
                row.Add(up);
                row.Add(down);
                row.Add(new Button(() => Change(() => RouteBuilder.Remove(config, stop))) { text = "Remove" });
                Instances.Add(row);
            }
        }

        void Change(System.Action change)
        {
            change();
            Refresh(Manager);
        }
    }
}
