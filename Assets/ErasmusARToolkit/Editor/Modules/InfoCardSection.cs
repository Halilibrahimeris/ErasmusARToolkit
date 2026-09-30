using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>Info cards for any object in the scene (parts of a design, a portal world, characters).</summary>
    internal sealed class InfoCardSection : SceneProjectSection<InfoCard>
    {
        readonly TextField titleField;
        readonly TextField bodyField;

        public InfoCardSection() : base("Info Cards",
            "Any object in the scene can have an info card: tap it on the phone to read its title and text.\n" +
            "Select objects in the Hierarchy (a chair, a painting), fill in the text and add the card.",
            "Add Info Card To Selection")
        {
            titleField = AddField(new TextField("Title") { value = "Title" });
            bodyField = AddField(new TextField("Text") { multiline = true, value = "What this object is." });
        }

        protected override string ListTitleText => "Objects with a card";

        protected override void OnAdd()
        {
            var added = InfoCardTools.AddToSelection(titleField.value, bodyField.value);
            ShowMessage(added == 0 ? "Select one or more objects in the scene first." : $"Added {added} card(s).",
                added == 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
        }

        protected override string Describe(InfoCard item)
        {
            return string.IsNullOrWhiteSpace(item.Info.Title) ? item.name : $"{item.name}: {item.Info.Title}";
        }

        protected override void Remove(InfoCard item)
        {
            Undo.DestroyObjectImmediate(item);
        }
    }

    /// <summary>Adds and fills info cards (used by several sections).</summary>
    internal static class InfoCardTools
    {
        /// <summary>Adds a card to every selected scene object. Returns how many got one.</summary>
        public static int AddToSelection(string title, string body)
        {
            var count = 0;
            foreach (var selected in Selection.gameObjects)
            {
                if (selected == null || !selected.scene.IsValid() || EditorUtility.IsPersistent(selected))
                {
                    continue;
                }

                Add(selected, title, body);
                count++;
            }

            return count;
        }

        /// <summary>Adds (or refills) the card of one object, with undo.</summary>
        public static InfoCard Add(GameObject target, string title, string body, bool showOnStart = false)
        {
            var card = target.GetComponent<InfoCard>();
            if (card == null)
            {
                card = Undo.AddComponent<InfoCard>(target);
            }

            Fill(card, title, body, showOnStart);
            return card;
        }

        public static void Fill(InfoCard card, string title, string body, bool showOnStart = false)
        {
            var serialized = new SerializedObject(card);
            serialized.FindProperty("info.title").stringValue = title ?? string.Empty;
            serialized.FindProperty("info.body").stringValue = body ?? string.Empty;
            serialized.FindProperty("info.showOnStart").boolValue = showOnStart;
            serialized.ApplyModifiedProperties();
        }
    }
}
