using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Erasmus.ARToolkit
{
    /// <summary>
    /// The space the room scan keeps its model in ("map space"), and where that space lies in the world.
    /// ARCore corrects its picture of the room while the phone moves: world coordinates of real things
    /// shift by centimeters, and after tracking was lost by more. A model kept in world coordinates would
    /// stay behind; the scanned floor would sink below the real one. So the model hangs from AR anchors,
    /// which ARCore moves along with the real room:
    /// <list type="bullet">
    /// <item>a free anchor gives the position and the direction (only the turn around the vertical axis is
    /// used: a floor must stay level),</item>
    /// <item>once the floor is known, an anchor attached to ARCore's floor plane gives the height.</item>
    /// </list>
    /// Everything that belongs to the scan (the drawn surfaces, the objects placed on them) is a child of
    /// <see cref="Root"/>. Without an AR rig (tests, the Editor) map space is world space.
    /// </summary>
    internal sealed class MapFrame : IDisposable
    {
        public const string RootName = "Room Surfaces (scanned)";
        const string AnchorName = "Room Scan Anchor";

        Transform parent;
        ARAnchorManager anchors;
        GameObject rootObject;
        GameObject anchorObject;
        ARAnchor poseAnchor;
        ARAnchor floorAnchor;
        TrackableId floorPlaneId = TrackableId.invalidId;
        Transform testPose;
        Vector3 anchorPosition;
        Quaternion anchorRotation = Quaternion.identity;
        float floorReference;
        bool hasFloorReference;
        float floorCorrection;
        bool hasPose;

        /// <summary>The frame was begun: <see cref="Root"/> exists and the model belongs to it.</summary>
        public bool IsBegun { get; private set; }

        /// <summary>The frame follows an AR anchor (false: map space is where it was begun).</summary>
        public bool IsAnchored => testPose != null || poseAnchor != null;

        /// <summary>The height comes from an anchor on ARCore's floor plane.</summary>
        public bool HasFloorAnchor => floorAnchor != null;

        /// <summary>
        /// False while ARCore does not know where the anchor is (tracking was lost): what the camera sees
        /// cannot be put into the model then.
        /// </summary>
        public bool IsTracking
        {
            get
            {
                if (testPose != null || poseAnchor == null || !HasSubsystem)
                {
                    return true;
                }

                return poseAnchor.pending || poseAnchor.trackingState == TrackingState.Tracking;
            }
        }

        /// <summary>Parent of everything that belongs to the scan. Made when first asked for.</summary>
        public Transform Root
        {
            get
            {
                if (rootObject == null)
                {
                    rootObject = new GameObject(RootName);
                    if (parent != null)
                    {
                        rootObject.transform.SetParent(parent, false);
                    }

                    hasPose = false;
                }

                return rootObject.transform;
            }
        }

        public Matrix4x4 WorldToMap => rootObject != null ? rootObject.transform.worldToLocalMatrix : Matrix4x4.identity;

        public Matrix4x4 MapToWorld => rootObject != null ? rootObject.transform.localToWorldMatrix : Matrix4x4.identity;

        /// <summary>Correction of the height (meters) found by checking the model against the room.</summary>
        public float HeightOffset { get; private set; }

        bool HasSubsystem => anchors != null && anchors.isActiveAndEnabled && anchors.subsystem != null;

        /// <summary>
        /// Puts the origin of map space at a world position (on the floor under the camera) and anchors it
        /// there. False when there is nothing to anchor to: map space then stays where it is.
        /// </summary>
        public bool Begin(XROrigin origin, ARAnchorManager anchorManager, Vector3 seedWorld)
        {
            Release();
            anchors = anchorManager;
            parent = origin != null ? (origin.TrackablesParent != null ? origin.TrackablesParent : origin.transform) : null;
            if (rootObject != null && rootObject.transform.parent != parent)
            {
                rootObject.transform.SetParent(parent, false);
            }

            IsBegun = true;
            HeightOffset = 0f;
            floorCorrection = 0f;
            hasFloorReference = false;
            if (parent == null)
            {
                anchorPosition = Vector3.zero;
                anchorRotation = Quaternion.identity;
                Apply();
                return false;
            }

            anchorPosition = parent.InverseTransformPoint(seedWorld);
            anchorRotation = Quaternion.identity;
            CreateAnchor();
            Apply();
            return poseAnchor != null;
        }

        /// <summary>
        /// Takes the height from ARCore's floor plane from now on: the plane follows the real floor when
        /// ARCore corrects its map. Only the height is used, because a plane turns while it grows.
        /// </summary>
        public bool TryAttachFloor(ARPlane floorPlane)
        {
            if (!IsBegun || floorPlane == null || !HasSubsystem || floorPlane.trackingState != TrackingState.Tracking
                || floorPlane.trackableId == floorPlaneId)
            {
                return false;
            }

            ARAnchor attached = null;
            try
            {
                if (anchors.descriptor != null && anchors.descriptor.supportsTrackableAttachments)
                {
                    var onPlane = Root.position;
                    onPlane.y = floorPlane.transform.position.y;
                    attached = anchors.AttachAnchor(floorPlane, new Pose(onPlane, Quaternion.identity));
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Erasmus AR Toolkit] The room scan could not anchor to the floor plane: "
                                 + exception.Message);
            }

            if (attached == null)
            {
                return false;
            }

            DropFloorAnchor();
            floorAnchor = attached;
            floorPlaneId = floorPlane.trackableId;
            hasFloorReference = false;
            return true;
        }

        /// <summary>The floor plane is gone (ARCore merged it into another one): the height moves on to that one.</summary>
        public void OnPlaneRemoved(TrackableId planeId, ARPlane subsumedBy)
        {
            if (floorAnchor == null || planeId != floorPlaneId)
            {
                return;
            }

            DropFloorAnchor();
            if (subsumedBy != null)
            {
                TryAttachFloor(subsumedBy);
            }
        }

        /// <summary>
        /// Moves <see cref="Root"/> to where the anchors are now. True when it moved; by how much is the jump
        /// ARCore made since the last call.
        /// </summary>
        public bool Refresh(out float movedMeters, out float turnedDegrees)
        {
            movedMeters = 0f;
            turnedDegrees = 0f;
            if (!IsBegun)
            {
                return false;
            }

            if (testPose != null)
            {
                anchorPosition = testPose.localPosition;
                anchorRotation = testPose.localRotation;
            }
            else if (parent != null)
            {
                if (anchorObject == null)
                {
                    // Removed with the session, or by someone tidying up: map space stays where it was.
                    CreateAnchor();
                }
                else if (poseAnchor != null && !poseAnchor.pending
                         && poseAnchor.trackingState == TrackingState.Tracking)
                {
                    var anchorTransform = anchorObject.transform;
                    anchorPosition = anchorTransform.localPosition;
                    anchorRotation = anchorTransform.localRotation;
                }

                ReadFloorAnchor();
            }

            var root = Root;
            var before = root.localPosition;
            var beforeRotation = root.localRotation;
            var hadPose = hasPose;
            Apply();
            if (!hadPose)
            {
                return true;
            }

            movedMeters = Vector3.Distance(before, root.localPosition);
            turnedDegrees = Quaternion.Angle(beforeRotation, root.localRotation);
            return movedMeters > 1e-5f || turnedDegrees > 1e-3f;
        }

        /// <summary>Raises (or lowers) the model: checking it against the room showed that the floor is off.</summary>
        public void AddHeightOffset(float delta)
        {
            HeightOffset += delta;
            if (IsBegun)
            {
                Apply();
            }
        }

        /// <summary>Tests move map space by hand: the frame follows this transform like an anchor.</summary>
        internal void UseTransformForTests(Transform pose)
        {
            testPose = pose;
            IsBegun = true;
        }

        public Vector3 ToMap(Vector3 world) => WorldToMap.MultiplyPoint3x4(world);

        public Vector3 ToWorld(Vector3 map) => MapToWorld.MultiplyPoint3x4(map);

        public Ray ToMap(Ray world)
        {
            var matrix = WorldToMap;
            return new Ray(matrix.MultiplyPoint3x4(world.origin), matrix.MultiplyVector(world.direction));
        }

        public Pose ToWorld(Pose map)
        {
            if (rootObject == null)
            {
                return map;
            }

            var root = rootObject.transform;
            return new Pose(root.TransformPoint(map.position), root.rotation * map.rotation);
        }

        /// <summary>World height of a height in map space, at the origin of the map.</summary>
        public float ToWorldHeight(float mapHeight) => ToWorld(new Vector3(0f, mapHeight, 0f)).y;

        public void Dispose()
        {
            Release();
            ObjectUtility.SafeDestroy(rootObject);
            rootObject = null;
            IsBegun = false;
            hasPose = false;
        }

        /// <summary>
        /// Direction of the anchor on the floor. An anchor that ARCore tilted a little still gives a level
        /// map: only its turn around the vertical axis counts.
        /// </summary>
        internal static float YawOf(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                // Looking straight up or down: the side axis still tells the direction.
                forward = Vector3.Cross(rotation * Vector3.right, Vector3.up);
                forward.y = 0f;
            }

            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        void Apply()
        {
            var root = Root;
            root.localPosition = new Vector3(anchorPosition.x, anchorPosition.y + floorCorrection + HeightOffset,
                anchorPosition.z);
            root.localRotation = Quaternion.Euler(0f, YawOf(anchorRotation), 0f);
            hasPose = true;
        }

        void ReadFloorAnchor()
        {
            if (floorAnchor == null)
            {
                return;
            }

            if (floorAnchor.pending || floorAnchor.trackingState != TrackingState.Tracking)
            {
                return;
            }

            var height = parent.InverseTransformPoint(floorAnchor.transform.position).y;
            if (!hasFloorReference)
            {
                // From here on only changes count: the model keeps the height it has.
                floorReference = height - (anchorPosition.y + floorCorrection);
                hasFloorReference = true;
            }

            floorCorrection = height - floorReference - anchorPosition.y;
        }

        void CreateAnchor()
        {
            anchorObject = new GameObject(AnchorName);
            anchorObject.transform.SetParent(parent, false);
            anchorObject.transform.localPosition = anchorPosition;
            anchorObject.transform.localRotation = anchorRotation;
            poseAnchor = anchors != null ? anchorObject.AddComponent<ARAnchor>() : null;
        }

        void DropFloorAnchor()
        {
            if (floorAnchor != null)
            {
                ObjectUtility.SafeDestroy(floorAnchor.gameObject);
            }

            floorAnchor = null;
            floorPlaneId = TrackableId.invalidId;
            hasFloorReference = false;
        }

        void Release()
        {
            DropFloorAnchor();
            ObjectUtility.SafeDestroy(anchorObject);
            anchorObject = null;
            poseAnchor = null;
        }
    }
}
