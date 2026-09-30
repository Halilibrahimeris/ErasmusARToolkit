using System;
using UnityEngine;

namespace Erasmus.ARToolkit
{
    /// <summary>Which way the next stop of a route is, seen by someone looking at the stop's image.</summary>
    public enum RouteArrow
    {
        None,
        Left,
        Right,
        Straight,
        Up,
        Down,
        Back,
    }

    /// <summary>
    /// Makes an image target a stop of a route through the school: its number, and how to get to the
    /// next stop ("Library, upstairs" and an arrow).
    /// </summary>
    [Serializable]
    public sealed class RouteStop
    {
        [SerializeField, Min(0), Tooltip("Number of the stop on the route (1, 2, 3 ...). 0: not a route stop.")]
        int order;
        [SerializeField, Tooltip("How to get to the next stop, for example 'Library, upstairs'.")]
        string nextHint = string.Empty;
        [SerializeField, Tooltip("Which way the next stop is, for someone looking at this image.")]
        RouteArrow arrow = RouteArrow.None;

        public RouteStop()
        {
        }

        public RouteStop(int order, string nextHint, RouteArrow arrow)
        {
            this.order = Mathf.Max(0, order);
            this.nextHint = nextHint ?? string.Empty;
            this.arrow = arrow;
        }

        public int Order => order;
        public string NextHint => nextHint ?? string.Empty;
        public RouteArrow Arrow => arrow;

        /// <summary>True when the image is a stop of the route.</summary>
        public bool IsStop => order > 0;

        /// <summary>The stop says how to go on (a hint or an arrow).</summary>
        public bool HasDirections => !string.IsNullOrWhiteSpace(nextHint) || arrow != RouteArrow.None;
    }
}
