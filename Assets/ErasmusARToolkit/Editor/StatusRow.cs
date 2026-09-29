using System;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit.Editor
{
    /// <summary>
    /// Checklist row used in the toolkit UXML: a status dot, a label and an optional fix button.
    /// Usage: &lt;e:StatusRow name="row-arcore" text="ARCore" action-text="Install" /&gt;
    /// </summary>
    [UxmlElement]
    public partial class StatusRow : VisualElement
    {
        public const string OkClass = "status-row--ok";

        readonly Label label;
        readonly Button actionButton;
        Action action;

        public StatusRow()
        {
            AddToClassList("status-row");

            var dot = new VisualElement();
            dot.AddToClassList("status-dot");
            Add(dot);

            label = new Label();
            label.AddToClassList("status-label");
            Add(label);

            actionButton = new Button(() => action?.Invoke());
            actionButton.AddToClassList("status-action");
            actionButton.style.display = DisplayStyle.None;
            Add(actionButton);
        }

        [UxmlAttribute]
        public string Text
        {
            get => label.text;
            set => label.text = value;
        }

        [UxmlAttribute]
        public string ActionText
        {
            get => actionButton.text;
            set => actionButton.text = value;
        }

        public bool IsOk => ClassListContains(OkClass);

        /// <summary>Shows the state. The fix button appears only when not ok and a fix is given.</summary>
        public void SetState(bool ok, Action fix = null, string text = null)
        {
            EnableInClassList(OkClass, ok);
            action = fix;
            actionButton.style.display = !ok && fix != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (text != null)
            {
                Text = text;
            }
        }
    }
}
