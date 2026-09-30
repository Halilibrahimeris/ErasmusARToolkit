using System.Collections.Generic;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// Something that wants the next tap on the scene before the toolkit uses it to select or place
    /// objects: a project module waiting to be placed, a story waiting for "continue". Register it with
    /// <see cref="InteractionController.PushTapHandler"/>.
    /// </summary>
    public interface ITapHandler
    {
        /// <summary>True while the handler wants taps. Asked when the finger goes down.</summary>
        bool WantsTap { get; }

        /// <summary>A tap (not a drag) on the scene, not on a button. Screen position in pixels.</summary>
        void OnTap(Vector2 screenPosition);
    }

    /// <summary>Tap handlers in the order they were added: the last one that wants a tap gets it.</summary>
    internal sealed class TapHandlerStack
    {
        readonly List<ITapHandler> handlers = new List<ITapHandler>();

        public int Count => handlers.Count;

        public void Push(ITapHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            handlers.Remove(handler);
            handlers.Add(handler);
        }

        public void Remove(ITapHandler handler)
        {
            handlers.Remove(handler);
        }

        /// <summary>The handler that gets the next tap, or null (the toolkit's own behaviour).</summary>
        public ITapHandler TopWanting()
        {
            for (var i = handlers.Count - 1; i >= 0; i--)
            {
                var handler = handlers[i];
                if (IsGone(handler))
                {
                    handlers.RemoveAt(i);
                    continue;
                }

                if (handler.WantsTap)
                {
                    return handler;
                }
            }

            return null;
        }

        static bool IsGone(ITapHandler handler)
        {
            return handler == null || (handler is Object unityObject && unityObject == null);
        }
    }
}
