using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.MagicLeap;

namespace MagicLeap.ObjectAlignment
{
    /// <summary>
    /// Manager for checking the latest state of map localization. Periodically checks the
    /// state and fires events when it changes.
    /// </summary>
    public class SpaceLocalizationManager : MonoBehaviour
    {
        public AnchorsApi.LocalizationInfo LocalizationInfo => _localizationInfo;

        public event Action<AnchorsApi.LocalizationInfo> OnLocalizationInfoChanged;

        private const float LocalizationStatusUpdateDelaySeconds = .5f;

        private AnchorsApi.LocalizationInfo _localizationInfo;
        private IEnumerator _updateLocalizationStatusCoroutine;

        private void Awake()
        {
#if !UNITY_EDITOR && UNITY_ANDROID
            // Side effect: Ensure MLDevice is initialized early
            Debug.Log("MapLocalizationManager Awake: MLDevice Platform Level: "
                      + MLDevice.PlatformLevel);
#endif
        }

        void Start()
        {
            AnchorsApi.OnLocalizationInfoChangedEvent += UpdateLocalizationStatus;

            _updateLocalizationStatusCoroutine = UpdateLocalizationStatusPeriodically();
            StartCoroutine(_updateLocalizationStatusCoroutine);
        }

        void OnDestroy()
        {
            AnchorsApi.OnLocalizationInfoChangedEvent -= UpdateLocalizationStatus;
            StopCoroutine(_updateLocalizationStatusCoroutine);
        }

        private IEnumerator UpdateLocalizationStatusPeriodically()
        {
            while (true)
            {
                if (AnchorsApi.IsReady && !AnchorsApi.QueryLocalization())
                {
                    Debug.LogError("Error querying localization.");
                }

                // Wait before querying again for localization status
                yield return new WaitForSeconds(LocalizationStatusUpdateDelaySeconds);
            }
        }

        private void UpdateLocalizationStatus(AnchorsApi.LocalizationInfo info)
        {
            if (!_localizationInfo.Equals(info))
            {
                _localizationInfo = info;
                OnLocalizationInfoChanged?.Invoke(info);
            }
        }
    }
}