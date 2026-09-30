using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>One project type in the Projects tab.</summary>
    internal interface IProjectSection
    {
        VisualElement Root { get; }

        /// <param name="manager">The toolkit of the open scene, or null.</param>
        void Refresh(ARToolkitManager manager);
    }

    /// <summary>
    /// A foldout of the Projects tab: a short description, a few fields, the Add button and a list of
    /// what already exists (with Select and Remove).
    /// </summary>
    internal abstract class ProjectSection : IProjectSection
    {
        readonly Foldout foldout;

        protected ProjectSection(string title, string description)
        {
            foldout = new Foldout { text = title, value = false, viewDataKey = "erasmus-project-" + title.Replace(' ', '-') };
            foldout.AddToClassList("project-section");
            var hint = new Label(description);
            hint.AddToClassList("hint");
            foldout.Add(hint);

            Fields = new VisualElement();
            Fields.AddToClassList("project-fields");
            foldout.Add(Fields);
            Buttons = new VisualElement();
            Buttons.AddToClassList("button-row");
            foldout.Add(Buttons);
            Messages = new VisualElement();
            Messages.AddToClassList("issues");
            foldout.Add(Messages);
            ListTitle = new Label();
            ListTitle.AddToClassList("list-title");
            foldout.Add(ListTitle);
            Instances = new VisualElement();
            Instances.AddToClassList("instances");
            foldout.Add(Instances);
        }

        public VisualElement Root => foldout;

        protected VisualElement Fields { get; }

        protected VisualElement Buttons { get; }

        protected VisualElement Messages { get; }

        protected Label ListTitle { get; }

        protected VisualElement Instances { get; }

        /// <summary>The toolkit of the open scene, or null.</summary>
        protected ARToolkitManager Manager { get; private set; }

        /// <summary>The config the scene uses, or null.</summary>
        protected ARToolkitConfig Config => Manager != null ? Manager.Config : null;

        public void Refresh(ARToolkitManager manager)
        {
            Manager = manager;
            OnRefresh();
        }

        protected abstract void OnRefresh();

        protected Button AddButton(string text, Action action, bool primary = false)
        {
            var button = new Button(() => Run(action)) { text = text };
            if (primary)
            {
                button.AddToClassList("primary");
            }

            Buttons.Add(button);
            return button;
        }

        protected T AddField<T>(T field, string tooltip = null) where T : VisualElement
        {
            if (!string.IsNullOrEmpty(tooltip))
            {
                field.tooltip = tooltip;
            }

            Fields.Add(field);
            return field;
        }

        protected void ShowMessage(string text, HelpBoxMessageType type = HelpBoxMessageType.Info)
        {
            Messages.Clear();
            if (!string.IsNullOrEmpty(text))
            {
                Messages.Add(EditorUi.Message(text, type));
            }
        }

        protected void ShowRows<TItem>(string title, IReadOnlyList<TItem> items, Func<TItem, string> describe,
            Action<TItem> select, Action<TItem> remove, string emptyText)
        {
            ListTitle.text = title;
            Instances.Clear();
            if (items.Count == 0)
            {
                var empty = new Label(emptyText);
                empty.AddToClassList("hint");
                Instances.Add(empty);
                return;
            }

            foreach (var item in items)
            {
                var row = new VisualElement();
                row.AddToClassList("instance-row");
                row.Add(new Label(describe(item)));
                if (select != null)
                {
                    row.Add(new Button(() => Run(() => select(item))) { text = "Select" });
                }

                if (remove != null)
                {
                    row.Add(new Button(() => Run(() => remove(item))) { text = "Remove" });
                }

                Instances.Add(row);
            }
        }

        protected static void SelectAndPing(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        /// <summary>Undo, selection and a dirty scene for an object the section made.</summary>
        protected static void RegisterCreated(GameObject created, string undoName)
        {
            Undo.RegisterCreatedObjectUndo(created, undoName);
            Selection.activeGameObject = created;
            EditorSceneManager.MarkSceneDirty(created.scene);
        }

        void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowMessage(exception.Message, HelpBoxMessageType.Error);
            }

            Refresh(Manager);
        }
    }

    /// <summary>A project type made of a component in the scene (a design, a portal, a story ...).</summary>
    internal abstract class SceneProjectSection<T> : ProjectSection where T : Component
    {
        readonly Button addButton;

        protected SceneProjectSection(string title, string description, string addText) : base(title, description)
        {
            addButton = AddButton(addText, () =>
            {
                if (Manager != null)
                {
                    OnAdd();
                }
            }, true);
        }

        protected Button MainButton => addButton;

        protected virtual string ListTitleText => "In the open scene";

        protected abstract void OnAdd();

        protected virtual bool CanAdd => Manager != null;

        protected virtual string Describe(T item) => item.name;

        protected virtual void Remove(T item)
        {
            Undo.DestroyObjectImmediate(item.gameObject);
        }

        protected virtual void Select(T item)
        {
            SelectAndPing(item.gameObject);
        }

        /// <summary>The components of this type in the open scenes, by name.</summary>
        protected static List<T> Existing()
        {
            return SceneQueries.FindAll<T>()
                .Where(item => item != null && item.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(item))
                .OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        protected override void OnRefresh()
        {
            addButton.SetEnabled(CanAdd);
            ShowRows(ListTitleText, Existing(), Describe, Select, Remove, "None in the scene yet.");
            OnRefreshMore();
        }

        protected virtual void OnRefreshMore()
        {
        }
    }
}
