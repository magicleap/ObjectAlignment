using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.MagicLeapSupport;
using UnityEngine.XR.OpenXR.NativeTypes;
using static UnityEngine.XR.OpenXR.Features.MagicLeapSupport.MagicLeapLocalizationMapFeature;

namespace MagicLeap.ObjectAlignment
{
    /// <summary>
    /// Wrapper for the OpenXR Localization and Anchors api to support a few additional use cases,
    /// e.g. Fake anchors provided in a unity editor session.
    /// </summary>
    public class AnchorsApi : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Localization state to return when on a non-ML2 device or in the unity editor.")]
        private LocalizationInfo _fakeLocalizationInfo = new(
            LocalizationMapState.Localized, LocalizationMapType.OnDevice,
            "Default", "DEFAULT_SPACE_ID");

        [SerializeField]
        [Tooltip("Anchors to return when on a non-ML2 device or in the unity editor.")]
        private FakeAnchor[] _fakeAnchors = {
            new()
            {
                Id = "DEFAULT_ANCHOR_ID",
                Pose = Pose.identity
            }
        };

        /// <summary>
        /// Whether the AnchorsApi is ready or not.
        /// </summary>
        public static bool IsReady => _instance._isReady;

        /// <summary>
        /// Query for the latest localization info.  The result will be returned asynchronously in the
        /// <see cref="AnchorsApi.OnLocalizationInfoChangedEvent"/> event.
        /// </summary>
        /// <returns>Returns <see langword="true"/> if the query call succeeded, otherwise <see langword="false"/>.</returns>
        public static bool QueryLocalization() => _instance.GetLocalizationInfoImpl();

        /// <summary>
        /// Query for anchor information within the current localized map.  The result will be returned
        /// asynchronously in the <see cref="AnchorsApi.OnAnchorsUpdatedEvent"/> event.
        /// </summary>
        /// <returns>Returns <see langword="true"/> if the query call succeeded, otherwise <see langword="false"/>.</returns>
        public static bool QueryAnchors() => _instance.QueryAnchorsImpl();

        /// <summary>
        /// Create an anchor to be published to storage within the current localized map.
        /// </summary>
        /// <param name="pose">The pose for the anchor.</param>
        /// <param name="expirationTimeStamp">The expiration for the anchor, provided in seconds since epoch.</param>
        /// <param name="callback">An optional callback to receive the unique id of the created anchor once published.</param>
        /// <returns>Returns <see langword="true"/> if the create call succeeded, otherwise <see langword="false"/>.</returns>
        public static bool CreateAnchor(Pose pose, ulong expirationTimeStamp, Action<string> callback) =>
            _instance.CreateAnchorImpl(pose, expirationTimeStamp, callback);

        /// <summary>
        /// The localization info changed event.
        /// </summary>
        public static event Action<LocalizationInfo> OnLocalizationInfoChangedEvent;

        /// <summary>
        /// The anchors updated event.
        /// </summary>
        public static event Action<Anchor[]> OnAnchorsUpdatedEvent;


        private static AnchorsApi _instance;

        private static readonly ProfilerMarker QueryAnchorsCompletePerfMarker =
            new ProfilerMarker("AnchorsApi.QueryAnchorsComplete");
        private static readonly ProfilerMarker CreateAnchorFromStorageCompletePerfMarker =
            new ProfilerMarker("AnchorsApi.CreateAnchorFromStorageComplete");
        private static readonly ProfilerMarker OnPublishAnchorCompletePerfMarker =
            new ProfilerMarker("AnchorsApi.OnPublishAnchorComplete");
        private static readonly ProfilerMarker OnDeletedAnchorsCompletePerfMarker =
            new ProfilerMarker("AnchorsApi.OnDeletedAnchorsComplete");
        private static readonly ProfilerMarker LocalizationChangedEventPerfMarker =
            new ProfilerMarker("AnchorsApi.LocalizationChangedEvent");
        private static readonly ProfilerMarker PublishAnchorsCoroutinePerfMarker =
            new ProfilerMarker("AnchorsApi.PublishAnchorsCoroutine");

        private bool _isReady;
        private MLXrAnchorSubsystem _mlXrAnchorSubsystem;
        private MagicLeapLocalizationMapFeature _localizationMapFeature = null;
        private MagicLeapSpatialAnchorsFeature _spatialAnchorsFeature = null;
        private MagicLeapSpatialAnchorsStorageFeature _spatialAnchorsStorageFeature = null;
        private HashSet<string> _anchorIdsToBeCreated = new();
        private Dictionary<string, Anchor> _activeAnchors = new();
        private Dictionary<ARAnchor, (bool, ulong, ulong, Action<string>)> _pendingPublishedAnchors = new();
#pragma warning disable CS0414 // assigned but never used
        private IEnumerator _publishPendingAnchorsCoroutine;
#pragma warning restore CS0414
        private YieldInstruction _waitForEndOfFrame = new WaitForEndOfFrame();

        [Serializable]
        public struct LocalizationInfo
        {
            /// <summary>
            /// The localization status at the time this structure was returned.
            /// </summary>
            public LocalizationMapState LocalizationStatus;

            /// <summary>
            /// The current mapping mode.
            /// </summary>
            public LocalizationMapType MappingMode;

            /// <summary>
            /// If localized, this will contain the name of the current space.
            /// </summary>
            public string SpaceName;

            /// <summary>
            /// If localized, the identifier of the space.
            /// </summary>
            public string SpaceId;

#if !UNITY_OPENXR_1_9_0_OR_NEWER
            // Space Origin is not currently supported in OpenXR. See SDKUNITY-6778.
            /// <summary>
            /// The space origin for the purposes of 3D mesh alignment, etc.
            /// </summary>
            public Pose TargetSpaceOriginPose;
#endif

            public LocalizationInfo(LocalizationMapState localizationStatus,
                LocalizationMapType mappingMode, string spaceName, string spaceId
#if !UNITY_OPENXR_1_9_0_OR_NEWER
                // Space Origin is not currently supported in OpenXR. See SDKUNITY-6778.
                , Pose targetSpaceOriginPose
#endif
                )
            {
                LocalizationStatus = localizationStatus;
                MappingMode = mappingMode;
                SpaceName = spaceName;
                SpaceId = spaceId;
#if !UNITY_OPENXR_1_9_0_OR_NEWER
                // Space Origin is not currently supported in OpenXR. See SDKUNITY-6778.
                TargetSpaceOriginPose = targetSpaceOriginPose;
#endif
            }

            public LocalizationInfo Clone()
            {
                return new LocalizationInfo
                {
                    LocalizationStatus = LocalizationStatus,
                    MappingMode = MappingMode,
                    SpaceName = SpaceName,
                    SpaceId = SpaceId,
#if !UNITY_OPENXR_1_9_0_OR_NEWER
                    // Space Origin is not currently supported in OpenXR. See SDKUNITY-6778.
                    TargetSpaceOriginPose = TargetSpaceOriginPose
#endif
                };
            }

            public override string ToString() => $"LocalizationStatus: {LocalizationStatus}, MappingMode: {MappingMode},\nSpaceName: {SpaceName}, SpaceId: {SpaceId}";
        }

        public class Anchor
        {
            /// <summary>
            /// The anchor's unique ID.  This is a unique identifier for a single Spatial Anchor that is generated and managed by the
            /// Spatial Anchor system.
            /// </summary>
            public string Id;

            /// <summary>
            /// The anchor's ID provided by the active XR Anchor Subsystem.  Used to associate and track the active anchor by the
            /// ARAnchorManager.
            /// </summary>
            public ulong ArAnchorId;

            /// <summary>
            /// The associated ARAnchor component representing this anchor in the XR Anchor Subsystem.  Used when creating and publishing
            /// an anchor.  Can be null if this anchor already existed and was created from storage.
            /// </summary>
            public ARAnchor ArAnchor;

            /// <summary>
            /// Pose.
            /// </summary>
            public Pose Pose;

            /// <summary>
            /// Indicates whether or not the anchor has been persisted via a call to #MLSpatialAnchorPublish.
            /// </summary>
            public bool IsPersisted;
        }

        /// <summary>
        /// A fake anchor for use outside of a device session.
        /// </summary>
        [Serializable]
        public class FakeAnchor : Anchor
        {
            public FakeAnchor()
            {
            }

            public FakeAnchor Clone()
            {
                return new FakeAnchor
                {
                    Id = Id,
                    ArAnchorId = ArAnchorId,
                    ArAnchor = ArAnchor,
                    Pose = Pose,
                    IsPersisted = IsPersisted
                };
            }
        }


        private void Awake()
        {
            _instance = this;
        }

        private IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return new WaitUntil(OpenXRSubsystemsAreLoaded);
            yield return new WaitUntil(OpenXRFeaturesEnabled);
            yield return new WaitUntil(OpenXRLocalizationEventsEnabled);

            _spatialAnchorsStorageFeature.OnQueryComplete += QueryAnchorsComplete;
            _spatialAnchorsStorageFeature.OnCreationCompleteFromStorage += CreateAnchorFromStorageComplete;
            _spatialAnchorsStorageFeature.OnPublishComplete += OnPublishAnchorComplete;
            _spatialAnchorsStorageFeature.OnDeletedComplete += OnDeletedAnchorsComplete;
            MagicLeapLocalizationMapFeature.OnLocalizationChangedEvent += LocalizationChangedEvent;
#endif

            _isReady = true;
            yield break;
        }

        private bool OpenXRSubsystemsAreLoaded()
        {
            if (XRGeneralSettings.Instance == null ||
                XRGeneralSettings.Instance.Manager == null ||
                XRGeneralSettings.Instance.Manager.activeLoader == null)
            {
                return false;
            }
            _mlXrAnchorSubsystem = XRGeneralSettings.Instance.Manager.activeLoader.GetLoadedSubsystem<XRAnchorSubsystem>() as MLXrAnchorSubsystem;
            return _mlXrAnchorSubsystem != null;
        }

        private bool OpenXRFeaturesEnabled()
        {
            _localizationMapFeature = OpenXRSettings.Instance.GetFeature<MagicLeapLocalizationMapFeature>();
            _spatialAnchorsFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsFeature>();
            _spatialAnchorsStorageFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsStorageFeature>();

            if (_localizationMapFeature == null || !_localizationMapFeature.enabled ||
                _spatialAnchorsFeature == null || !_spatialAnchorsFeature.enabled ||
                _spatialAnchorsStorageFeature == null || !_spatialAnchorsStorageFeature.enabled)
            {
                Debug.LogError("The OpenXR localization and/or spatial anchor features are not enabled.");
                return false;
            }
            return true;
        }

        private bool OpenXRLocalizationEventsEnabled()
        {
            XrResult result = _localizationMapFeature.EnableLocalizationEvents(true);
            if (result != XrResult.Success)
            {
                Debug.LogError($"MagicLeapLocalizationMapFeature.EnableLocalizationEvents failed, result = {result}");
                return false;
            }
            return true;
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_isReady)
            {
                _spatialAnchorsStorageFeature.OnQueryComplete -= QueryAnchorsComplete;
                _spatialAnchorsStorageFeature.OnCreationCompleteFromStorage -= CreateAnchorFromStorageComplete;
                _spatialAnchorsStorageFeature.OnPublishComplete -= OnPublishAnchorComplete;
                _spatialAnchorsStorageFeature.OnDeletedComplete -= OnDeletedAnchorsComplete;
                MagicLeapLocalizationMapFeature.OnLocalizationChangedEvent -= LocalizationChangedEvent;
            }
#endif
        }

        private void QueryAnchorsComplete(List<string> anchorMapPositionId)
        {
            using (QueryAnchorsCompletePerfMarker.Auto())
            {
                // Update existing matching anchors
                bool activeAnchorsUpdated = false;

                foreach (var anchorId in anchorMapPositionId)
                {
                    if (_activeAnchors.TryGetValue(anchorId, out var anchor))
                    {
                        Pose pose = _mlXrAnchorSubsystem.GetAnchorPoseFromID(anchor.ArAnchorId);
                        if (pose != anchor.Pose)
                        {
                            anchor.Pose = pose;
                            activeAnchorsUpdated = true;
                        }
                    }
                }

                _anchorIdsToBeCreated = anchorMapPositionId.ToHashSet();

                // Cleanup both the active anchors and ids to be created if not already active.
                foreach (var (anchorId, anchor) in _activeAnchors.ToArray())
                {
                    // Remove the active anchor id from the queried list if it already exists,
                    // otherwise remove this active anchor since it isn't represented in the query.
                    if (!_anchorIdsToBeCreated.Remove(anchorId))
                    {
                        if (anchor.ArAnchor != null)
                        {
                            GameObject.Destroy(anchor.ArAnchor.gameObject);
                        }
                        _activeAnchors.Remove(anchorId);
                        activeAnchorsUpdated = true;
                    }
                }

                if (activeAnchorsUpdated)
                {
                    OnAnchorsUpdatedEvent?.Invoke(_activeAnchors.Values.ToArray());
                }

                if (_anchorIdsToBeCreated.Count > 0)
                {
                    _spatialAnchorsStorageFeature.CreateSpatialAnchorsFromStorage(_anchorIdsToBeCreated.ToList());
                }
            }
        }

        private void CreateAnchorFromStorageComplete(Pose pose, ulong anchorId, string anchorStorageId, XrResult result)
        {
            using (CreateAnchorFromStorageCompletePerfMarker.Auto())
            {
                if (result != XrResult.Success)
                {
                    Debug.LogError($"MagicLeapSpatialAnchorsStorageFeature.CreateSpatialAnchorsFromStorage returned error code, {result}, for anchor {anchorStorageId}.");
                    return;
                }

                // Get or create the anchor for this id
                if (!_activeAnchors.TryGetValue(anchorStorageId, out Anchor anchor))
                {
                    anchor = new Anchor();
                    _activeAnchors.Add(anchorStorageId, anchor);
                }
                anchor.Id = anchorStorageId;
                anchor.ArAnchorId = anchorId;
                anchor.ArAnchor = null;
                anchor.Pose = pose;
                anchor.IsPersisted = true;

                // Remove this anchor from the current anchor IDs to be created.
                // When all IDs have been created, invoke anchors updated event.
                _anchorIdsToBeCreated.Remove(anchorStorageId);
                if (_anchorIdsToBeCreated.Count == 0)
                {
                    OnAnchorsUpdatedEvent?.Invoke(_activeAnchors.Values.ToArray());
                }
            }
        }

        private void OnPublishAnchorComplete(ulong anchorId, string anchorMapPositionId)
        {
            using (OnPublishAnchorCompletePerfMarker.Auto())
            {
                // Get or create the anchor for this id
                if (!_activeAnchors.TryGetValue(anchorMapPositionId, out Anchor anchor))
                {
                    anchor = new Anchor();
                    _activeAnchors.Add(anchorMapPositionId, anchor);
                }
                anchor.Id = anchorMapPositionId;
                anchor.ArAnchorId = anchorId;
                anchor.ArAnchor = null; // Will find associated ARAnchor below
                anchor.Pose = _mlXrAnchorSubsystem.GetAnchorPoseFromID(anchorId);
                anchor.IsPersisted = true;

                OnAnchorsUpdatedEvent?.Invoke(_activeAnchors.Values.ToArray());

                //Finally, call any associated callback and cleanup pending published anchors.
                foreach (var (arAnchor, (_, _, pendingId, callback)) in _pendingPublishedAnchors)
                {
                    if (pendingId == anchorId)
                    {
                        anchor.ArAnchor = arAnchor;
                        callback?.Invoke(anchorMapPositionId);
                        _pendingPublishedAnchors.Remove(arAnchor);
                        break;
                    }
                }
            }
        }

        private void OnDeletedAnchorsComplete(List<string> anchorMapPositionId)
        {
            using (OnDeletedAnchorsCompletePerfMarker.Auto())
            {
                foreach (var id in anchorMapPositionId)
                {
                    _activeAnchors.Remove(id);
                }

                OnAnchorsUpdatedEvent?.Invoke(_activeAnchors.Values.ToArray());
            }
        }

        private void LocalizationChangedEvent(LocalizationEventData data)
        {
            using (LocalizationChangedEventPerfMarker.Auto())
            {
                var info = new LocalizationInfo(data.State, data.Map.MapType, data.Map.Name, data.Map.MapUUID);
                OnLocalizationInfoChangedEvent?.Invoke(info);
            }
        }

        private bool GetLocalizationInfoImpl()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_localizationMapFeature != null &&
                _localizationMapFeature.GetLatestLocalizationMapData(out LocalizationEventData data))
            {
                var info = new LocalizationInfo(data.State, data.Map.MapType, data.Map.Name, data.Map.MapUUID);
                StartCoroutine(CallActionAtEndOfFrame(() =>
                    OnLocalizationInfoChangedEvent?.Invoke(info)));
                return true;
            }
            
            return false;
#else
            var info = _fakeLocalizationInfo.Clone();
            StartCoroutine(CallActionAtEndOfFrame(() =>
                OnLocalizationInfoChangedEvent?.Invoke(info)));
            return true;
#endif
        }

        private bool QueryAnchorsImpl()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_spatialAnchorsStorageFeature != null)
            {
                return _spatialAnchorsStorageFeature.QueryStoredSpatialAnchors(Camera.main.transform.position, 0f);
            }

            return false;
#else
            // Return a default anchor for non-ML2 applications.
            bool anchorsChanged = false;
            for (int i = 0; i < _fakeAnchors.Length; i++)
            {
                var id = _fakeAnchors[i].Id;
                if (!_activeAnchors.ContainsKey(id))
                {
                    _activeAnchors.Add(id, _fakeAnchors[i].Clone());
                    anchorsChanged = true;
                }
            }

            if (anchorsChanged)
            {
                StartCoroutine(CallActionAtEndOfFrame(() =>
                    OnAnchorsUpdatedEvent?.Invoke(_activeAnchors.Values.ToArray())));
            }

            return true;
#endif
        }

        private bool CreateAnchorImpl(Pose pose, ulong expirationTimeStamp, Action<string> callback)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_spatialAnchorsStorageFeature != null)
            {
                var pendingAnchorObject = new GameObject("Pending Anchor");
                pendingAnchorObject.transform.SetParent(transform);
                // The object transform must be set to the desired pose before adding ARAnchor
                pendingAnchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
                ARAnchor arAnchor = pendingAnchorObject.AddComponent<ARAnchor>();
                _pendingPublishedAnchors.Add(arAnchor, (false, expirationTimeStamp, 0, callback));

                if (_publishPendingAnchorsCoroutine == null)
                {
                    _publishPendingAnchorsCoroutine = PublishAnchorsCoroutine();
                    StartCoroutine(_publishPendingAnchorsCoroutine);
                }
                return true;
            }

            return false;
#else
            return false;
#endif
        }

        private IEnumerator PublishAnchorsCoroutine()
        {
            while (true)
            {
                if (_pendingPublishedAnchors.Count == 0)
                {
                    _publishPendingAnchorsCoroutine = null;
                    yield break;
                }
                else
                {
                    yield return null;
                }

                using (PublishAnchorsCoroutinePerfMarker.Auto())
                {
                    foreach (var (arAnchor, (requestMade, expiration, _, callback)) in _pendingPublishedAnchors.ToArray())
                    {
                        if (!requestMade && arAnchor.trackingState == TrackingState.Tracking)
                        {
                            ulong newAnchorId = _mlXrAnchorSubsystem.GetAnchorId(arAnchor);
                            if (!_spatialAnchorsStorageFeature.PublishSpatialAnchorsToStorage(new List<ulong>() { newAnchorId }, expiration))
                            {
                                Debug.LogError($"Failed to publish anchor {newAnchorId} at position {arAnchor.transform.position} to storage");
                            }
                            else
                            {
                                _pendingPublishedAnchors[arAnchor] = (true, expiration, newAnchorId, callback);
                            }
                        }
                    }
                }
            }
        }

        private IEnumerator CallActionAtEndOfFrame(Action action)
        {
            yield return _waitForEndOfFrame;
            action();
        }
    }
}