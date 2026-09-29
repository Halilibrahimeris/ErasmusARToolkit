using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Checks whether a screen position is over UI so taps on buttons never place objects.
    /// UI Toolkit documents are checked by picking; uGUI canvases through the EventSystem.
    /// </summary>
    public static class UIPointerGuard
    {
        static readonly List<UIDocument> Documents = new List<UIDocument>();
        static readonly List<RaycastResult> Results = new List<RaycastResult>();

        public static void Register(UIDocument document)
        {
            if (document != null && !Documents.Contains(document))
            {
                Documents.Add(document);
            }
        }

        public static void Unregister(UIDocument document)
        {
            Documents.Remove(document);
        }

        /// <param name="screenPosition">Input System screen position (origin bottom-left).</param>
        public static bool IsOverUI(Vector2 screenPosition)
        {
            return IsOverUIToolkit(screenPosition) || IsOverUGui(screenPosition);
        }

        /// <summary>UI Toolkit panels use a top-left origin, Input System uses bottom-left.</summary>
        public static Vector2 ToPanelScreenPosition(Vector2 screenPosition, float screenHeight)
        {
            return new Vector2(screenPosition.x, screenHeight - screenPosition.y);
        }

        static bool IsOverUIToolkit(Vector2 screenPosition)
        {
            Documents.RemoveAll(document => document == null);
            foreach (var document in Documents)
            {
                var panel = document.isActiveAndEnabled ? document.rootVisualElement?.panel : null;
                if (panel == null)
                {
                    continue;
                }

                var panelPosition = RuntimePanelUtils.ScreenToPanel(panel,
                    ToPanelScreenPosition(screenPosition, Screen.height));
                if (panel.Pick(panelPosition) != null)
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsOverUGui(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var pointerData = new PointerEventData(eventSystem) { position = screenPosition };
            Results.Clear();
            eventSystem.RaycastAll(pointerData, Results);
            foreach (var result in Results)
            {
                // UI Toolkit panels are handled above; only count real uGUI graphics here.
                if (result.module is GraphicRaycaster)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
