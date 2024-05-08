using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.MagicLeap;

namespace MagicLeap.ObjectAlignment
{
    /// <summary>
    /// Manager for Spatial Anchor game objects. Spatial Anchors are periodically queried and
    /// kept in sync.
    /// </summary>
    public class AnchorsManager : MonoBehaviour
    {
        /// <summary>
        /// Get the current anchor results, sorted by Id
        /// </summary>
        public AnchorsApi.Anchor[] Anchors => _anchors;

        /// <summary>
        /// Get whether the last query for anchors was successful.
        /// </summary>
        public bool LastQuerySuccessful => _lastQuerySuccessful;

        /// <summary>
        /// Get whether the last anchor query was received and contains good anchors.
        /// </summary>
        public bool QueryReceivedOk => _queryReceivedOk;

        [SerializeField, Tooltip("The parent node where anchor game objects should be added.")]
        private GameObject _anchorContainer;

        [SerializeField, Tooltip("The anchor prefab.")]
        private GameObject _anchorPrefab;

        [SerializeField, Tooltip("Animation curve used to lerp/slerp an anchor to the latest pose")]
        private AnimationCurve _anchorLerpAnimation;

        [SerializeField, Tooltip("The maximum positional change for an anchor to have while still" +
                                 " animating to the new position")]
        private float _maxPositionChangeToAnimate = 0.5f;

        [SerializeField, Tooltip("The maximum rotational angle change for an anchor to have while" +
                                 " still animating to the new rotation (in degrees)")]
        private float _maxRotationAngleChangeToAnimate = 15.0f;

        private const float AnchorsUpdateDelaySeconds = .5f;

        private AnchorsApi.Anchor[] _anchors = new AnchorsApi.Anchor[0];
        // TODO: _lastQuerySuccessful meaning changed to just a successful query, not that valid anchors received from query.
        // Change this back when we can rely on the event OnQueryComplete once 0 anchors trigger it. See SDKUNITY-6760
        private bool _lastQuerySuccessful;
        private bool _queryReceivedOk;
        private Dictionary<string, GameObject> _anchorGameObjectsMap = new();
        private Dictionary<string, GameObject> _missingAnchorGameObjectsMap = new();

        private IEnumerator _updateAnchorsCoroutine;

        /// <summary>
        /// Try to get the game object representation for an anchor.
        /// </summary>
        /// <param name="anchorId">The anchor id to find</param>
        /// <param name="gameObject">The game object that is representing this anchor.</param>
        /// <returns>True if the game object could be found, or false otherwise.</returns>
        public bool TryGetAnchorGameObject(string anchorId, out GameObject gameObject)
        {
            if (anchorId == null)
            {
                gameObject = null;
                return false;
            }
            return _anchorGameObjectsMap.TryGetValue(anchorId, out gameObject);
        }

        private void Awake()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            // Side effect: Ensure MLDevice is initialized early
            Debug.Log("AnchorsManager Awake: MLDevice Platform Level: "
                      + MLDevice.PlatformLevel);
#endif
        }

        private void Start()
        {
            AnchorsApi.OnAnchorsUpdatedEvent += UpdateAnchors;

            _updateAnchorsCoroutine = UpdateAnchorsPeriodically();
            StartCoroutine(_updateAnchorsCoroutine);
        }

        private void OnDestroy()
        {
            AnchorsApi.OnAnchorsUpdatedEvent -= UpdateAnchors;
            StopCoroutine(_updateAnchorsCoroutine);
        }

        private IEnumerator UpdateAnchorsPeriodically()
        {
            while (true)
            {
                if (AnchorsApi.IsReady)
                {
                    _lastQuerySuccessful = AnchorsApi.QueryAnchors();
                    if (!LastQuerySuccessful)
                    {
                        Debug.LogError("Error querying anchors.");
                    }
                }

                // Wait before querying again for localization status
                yield return new WaitForSeconds(AnchorsUpdateDelaySeconds);
            }
        }

        private void UpdateAnchors(AnchorsApi.Anchor[] anchors)
        {
            bool hasValidPoses = true;
            foreach (AnchorsApi.Anchor anchor in anchors)
            {
                if (anchor.Pose.rotation.x == 0 && anchor.Pose.rotation.y == 0
                    && anchor.Pose.rotation.z == 0 && anchor.Pose.rotation.w == 0)
                {
                    hasValidPoses = false;
                }
            }

            if (hasValidPoses)
            {
                Array.Sort(anchors, (a, b) =>
                    string.CompareOrdinal(a.Id, b.Id));

                UpdateAnchorObjects(anchors);
                return;
            }

            // TODO(ghazen): Find and report the root cause for invalid anchor poses.
            Debug.LogError("UpdateAnchorsOnWorkerThread: some anchors have invalid poses");
            _queryReceivedOk = false;
        }

        private void UpdateAnchorObjects(AnchorsApi.Anchor[] anchors)
        {
            _anchors = anchors;
            _queryReceivedOk = true;

            HashSet<string> removedAnchorIds = new HashSet<string>();
            foreach (KeyValuePair<string, GameObject> anchorEntry in _anchorGameObjectsMap)
            {
                removedAnchorIds.Add(anchorEntry.Key);
            }

            foreach (AnchorsApi.Anchor anchor in _anchors)
            {
                removedAnchorIds.Remove(anchor.Id);

                if (_anchorPrefab != null && _anchorContainer != null)
                {
                    GameObject anchorGameObject;
                    AnchorView anchorView;
                    bool animateAnchor = true;
                    if (!_anchorGameObjectsMap.TryGetValue(anchor.Id, out anchorGameObject))
                    {
                        Debug.Log("Anchor Found: " + anchor.Id);

                        if (_missingAnchorGameObjectsMap.TryGetValue(anchor.Id, out anchorGameObject))
                        {
                            anchorView = anchorGameObject.GetComponent<AnchorView>();
                            anchorGameObject.SetActive(true);
                            _anchorGameObjectsMap.Add(anchor.Id, anchorGameObject);
                            _missingAnchorGameObjectsMap.Remove(anchor.Id);
                        }
                        else
                        {
                            anchorGameObject = Instantiate(_anchorPrefab, _anchorContainer.transform);
                            anchorView = anchorGameObject.GetComponent<AnchorView>();
                            anchorView.Initialize(anchor, true);

                            _anchorGameObjectsMap.Add(anchor.Id, anchorGameObject);
                        }

                        animateAnchor = false;
                    }
                    else
                    {
                        anchorView = anchorGameObject.GetComponent<AnchorView>();
                    }

                    anchorView.UpdateData(anchor, animateAnchor, _anchorLerpAnimation,
                        _maxPositionChangeToAnimate, _maxRotationAngleChangeToAnimate);
                }
            }

            foreach (string anchorId in removedAnchorIds)
            {
                Debug.Log("Anchor Lost: " + anchorId);

                GameObject anchorGameObject;
                if (_anchorGameObjectsMap.Remove(anchorId, out anchorGameObject))
                {
                    anchorGameObject.SetActive(false);
                    _missingAnchorGameObjectsMap.Add(anchorId, anchorGameObject);
                }
            }
        }
    }
}