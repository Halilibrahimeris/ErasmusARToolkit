using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Base of the project modules (design walkthrough, portal, room fill, story ...). It finds the toolkit
    /// in the scene, calls <see cref="OnModeReady"/> once the toolkit knows whether it runs in AR or
    /// Desktop mode, and gives the module HUD buttons and lines that are removed again when the module is
    /// switched off. Write your own modules the same way.
    /// </summary>
    public abstract class ToolkitModule : MonoBehaviour
    {
        readonly List<VisualElement> hudItems = new List<VisualElement>();
        ARToolkitManager manager;
        bool started;
        bool warnedNoManager;
        bool hasInfoLine;
        bool hasSubtitle;

        /// <summary>The toolkit of the scene (null when the scene has none).</summary>
        protected ARToolkitManager Manager => manager;

        /// <summary>The on-screen controls (null in scenes without a HUD).</summary>
        protected ARToolkitHUD Hud => manager != null ? manager.Hud : null;

        /// <summary>Touch and click handling (null in scenes without one).</summary>
        protected InteractionController Interaction => manager != null ? manager.Interaction : null;

        /// <summary>True after <see cref="OnModeReady"/> ran (until the module is switched off).</summary>
        public bool IsModeReady { get; private set; }

        /// <summary>True in real AR on a phone.</summary>
        protected bool IsAR => manager != null && manager.Mode == ToolkitMode.AR;

        /// <summary>The camera that renders the scene now (AR camera or Desktop camera), or null.</summary>
        protected Camera ViewCamera => manager != null ? manager.ActiveCamera : null;

        /// <summary>
        /// Called once the mode is known (AR or Desktop), and again when the module is switched on again.
        /// Add HUD buttons here.
        /// </summary>
        protected abstract void OnModeReady(ToolkitMode mode);

        protected virtual void OnEnable()
        {
            manager = FindManager();
            if (manager == null)
            {
                if (!warnedNoManager)
                {
                    warnedNoManager = true;
                    Debug.LogWarning($"[Erasmus AR Toolkit] '{name}' needs an AR Toolkit in the scene " +
                                     "(Toolkit window > Scene > Create New AR Scene).", this);
                }

                return;
            }

            manager.ModeChanged += OnModeChanged;
            if (started)
            {
                TryReady();
            }
        }

        protected virtual void Start()
        {
            started = true;
            TryReady();
        }

        protected virtual void OnDisable()
        {
            if (manager != null)
            {
                manager.ModeChanged -= OnModeChanged;
            }

            var hud = Hud;
            foreach (var item in hudItems)
            {
                if (hud != null)
                {
                    hud.RemoveModuleButton(item);
                }
                else
                {
                    item.RemoveFromHierarchy();
                }
            }

            hudItems.Clear();
            if (hud != null && hasInfoLine)
            {
                hud.SetInfoLine(this, null);
            }

            if (hud != null && hasSubtitle)
            {
                hud.SetSubtitle(this, null);
            }

            hasInfoLine = false;
            hasSubtitle = false;
            IsModeReady = false;
        }

        /// <summary>A button in the module strip of the HUD. It is removed when the module is switched off.</summary>
        protected Button AddHudButton(string text, Action onClick)
        {
            var hud = Hud;
            Button button;
            if (hud != null)
            {
                button = hud.AddModuleButton(text, onClick);
            }
            else
            {
                button = new Button(onClick) { text = text };
                button.AddToClassList(ModuleHud.ButtonClass);
            }

            hudItems.Add(button);
            return button;
        }

        /// <summary>Any element (a slider) in the module strip of the HUD.</summary>
        protected void AddHudElement(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            var hud = Hud;
            if (hud != null)
            {
                hud.AddModuleElement(element);
            }

            hudItems.Add(element);
        }

        protected void RemoveHudButton(VisualElement button)
        {
            if (button == null)
            {
                return;
            }

            hudItems.Remove(button);
            var hud = Hud;
            if (hud != null)
            {
                hud.RemoveModuleButton(button);
            }
            else
            {
                button.RemoveFromHierarchy();
            }
        }

        /// <summary>This module's line under the status line; empty removes it.</summary>
        protected void SetInfoLine(string text)
        {
            var hud = Hud;
            if (hud != null)
            {
                hud.SetInfoLine(this, text);
                hasInfoLine = !string.IsNullOrWhiteSpace(text);
            }
        }

        /// <summary>A subtitle above the object bar; empty removes it.</summary>
        protected void SetSubtitle(string text)
        {
            var hud = Hud;
            if (hud != null)
            {
                hud.SetSubtitle(this, text);
                hasSubtitle = !string.IsNullOrWhiteSpace(text);
            }
        }

        /// <summary>A short message on the HUD (also written to the log).</summary>
        protected void Toast(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var hud = Hud;
            if (hud != null && hud.isActiveAndEnabled)
            {
                hud.ShowToast(message);
            }

            Debug.Log("[Erasmus AR Toolkit] " + message, this);
        }

        void OnModeChanged(ToolkitMode mode)
        {
            // Before Start the other components of the object may not be awake yet: Start takes over.
            if (started)
            {
                TryReady();
            }
        }

        void TryReady()
        {
            if (IsModeReady || manager == null || manager.Mode == ToolkitMode.Undetermined || !isActiveAndEnabled)
            {
                return;
            }

            IsModeReady = true;
            OnModeReady(manager.Mode);
        }

        static ARToolkitManager FindManager()
        {
            return FindAnyObjectByType<ARToolkitManager>();
        }
    }
}
