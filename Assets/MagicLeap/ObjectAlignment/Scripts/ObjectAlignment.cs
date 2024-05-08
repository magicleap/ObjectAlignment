using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using static UnityEngine.XR.OpenXR.Features.MagicLeapSupport.MagicLeapLocalizationMapFeature;
using TransformExtensions = Unity.XR.CoreUtils.TransformExtensions;

namespace MagicLeap.ObjectAlignment
{
    /// <summary>
    /// The main Object Alignment application.
    /// </summary>
    /// <remarks>
    /// Contains the startup and shutdown logic, brush-stroke and network events handling,
    /// error monitoring, and main panels and application flow control.
    /// </remarks>
    [RequireComponent(typeof(AnchorsManager))]
    public class ObjectAlignment : MonoBehaviour
    {
        [Header("UI Panels and Popups")]

        [SerializeField]
        private GameObject _inspectPanelPrefab;

        [SerializeField]
        private MainPanel _mainPanel;

        [SerializeField]
        private GameObject _load3DModelPanel;

        [SerializeField]
        private GameObject _notLocalizedPanel;

        [Header("Tools")]

        [SerializeField]
        private PinTool pinTool;

        [Header("Miscellaneous")]

        [SerializeField, Tooltip("The text used to display status information.")]
        private TMP_Text _statusText;

        [SerializeField]
        private GameObject _spaceOriginAxis;

        private System.Random _random = new();
        private IEnumerator _updateStatusTextCoroutine;
        private SpaceLocalizationManager _localizationManager;
        private AnchorsManager _anchorsManager;
        private External3DModelManager _external3dModelManager;
        private Dictionary<string, External3DModel> _externalModelMap = new();
        private IEnumerator _maybeCreateAnchorAfterLocalizationWithDelayCoroutine;
        private string _headClosestAnchorId;

        private enum Tool
        {
            Laser,
            Pin
        }

        private Tool _currentTool = Tool.Laser;
        private External3DModel _pinningExternalModel;
        private List<External3DModel> _targettedExternalModels = new();
        private int _errorsLogged = 0;
        private int _exceptionsOrAssertsLogged = 0;
        private Camera _camera;
        private MagicLeapInputsOpenXR _magicLeapInputs;

        private const float StatusTextUpdateDelaySeconds = .1f;
        private static readonly Vector3 External3DModelRelativeStartPosition = new(0, 0, 1.5f);
        private MagicLeapInputsOpenXR.ControllerActions _controllerActions;

        private void Awake()
        {
            Application.logMessageReceived += OnLogMessageReceived;

            _localizationManager = GetComponent<SpaceLocalizationManager>();
            _anchorsManager = GetComponent<AnchorsManager>();
            _external3dModelManager = GetComponent<External3DModelManager>();

            _camera = Camera.main;
        }

        private void Start()
        {
            _updateStatusTextCoroutine = UpdateStatusTextPeriodically();
            StartCoroutine(_updateStatusTextCoroutine);

            _mainPanel.OnLoad3DModel += OnPlaceNewExternal3DModel;

            _localizationManager.OnLocalizationInfoChanged += OnLocalizationInfoChanged;

            pinTool.OnPinCancelled += PinCancelled;
            pinTool.OnPinFinished += PinFinished;

            _magicLeapInputs = new MagicLeapInputsOpenXR();
            _magicLeapInputs.Enable();
            _controllerActions = new MagicLeapInputsOpenXR.ControllerActions(_magicLeapInputs);
            _controllerActions.Bumper.performed += OnBumperClicked;
            _controllerActions.MenuButton.performed += OnMenuClicked;

#if UNITY_OPENXR_1_9_0_OR_NEWER
            // Space Origin is not currently supported in OpenXR. See SDKUNITY-6778.
            _spaceOriginAxis.SetActive(false);
#endif

            UpdateActiveTool();
            RepositionMainPanelInFront();
            UpdateSubPanelVisibility();
        }

        private void Update()
        {
            Vector3 headPosition = _camera.transform.position;

            // Determine which Anchor is closest to the user's head currently.
            AnchorsApi.Anchor anchorClosestToHead = null;
            float minHeadToAnchorDistanceSqr = Mathf.Infinity;
            for (int i = 0; i < _anchorsManager.Anchors.Length; ++i)
            {
                AnchorsApi.Anchor anchor = _anchorsManager.Anchors[i];

                float headDistanceToAnchorSqr =
                    (anchor.Pose.position - headPosition).sqrMagnitude;
                if (headDistanceToAnchorSqr < minHeadToAnchorDistanceSqr)
                {
                    anchorClosestToHead = anchor;
                    minHeadToAnchorDistanceSqr = headDistanceToAnchorSqr;
                }
            }

            GameObject anchorClosestToHeadGameObject = null;
            if (anchorClosestToHead != null)
            {
                _anchorsManager.TryGetAnchorGameObject(anchorClosestToHead.Id,
                    out anchorClosestToHeadGameObject);
            }

            if (anchorClosestToHeadGameObject != null)
            {
                _headClosestAnchorId = anchorClosestToHead.Id;
            }
            else
            {
                _headClosestAnchorId = null;
            }

            Vector3 headDirectionNoTilt = _camera.transform.forward;
            headDirectionNoTilt.y = 0;
            headDirectionNoTilt.Normalize();

            _statusText.transform.position = _camera.transform.position
                                             + headDirectionNoTilt * 1.3f
                                             + Vector3.up * 0.35f;
            _statusText.transform.rotation = Quaternion.LookRotation(
                (_statusText.transform.position - _camera.transform.position).normalized,
                Vector3.up);

            ThreadDispatcher.DispatchAll();
        }

        /// <summary>
        /// Unity lifecycle event for this component being destroyed.
        /// </summary>
        private void OnDestroy()
        {
            ThreadDispatcher.DispatchAllAndShutdown();

            StopCoroutine(_updateStatusTextCoroutine);
            if (_maybeCreateAnchorAfterLocalizationWithDelayCoroutine != null)
            {
                StopCoroutine(_maybeCreateAnchorAfterLocalizationWithDelayCoroutine);
            }

            _localizationManager.OnLocalizationInfoChanged -= OnLocalizationInfoChanged;

            Application.logMessageReceived -= OnLogMessageReceived;
        }

        private void OnBumperClicked(InputAction.CallbackContext obj)
        {
            if ((_currentTool == Tool.Laser || _currentTool == Tool.Pin)
                && _targettedExternalModels.Count > 0)
            {
                var externalModel = _targettedExternalModels.First();

                if (externalModel.InspectPanel == null)
                {
                    InspectPanel inspectPanel = Instantiate(_inspectPanelPrefab, transform)
                        .GetComponent<InspectPanel>();
                    inspectPanel.Show(externalModel);
                    externalModel.InspectPanel = inspectPanel;
                }

                externalModel.InspectPanel.transform.position =
                    _camera.transform.position + _camera.transform.forward * 1.2f;
                externalModel.InspectPanel.transform.LookAt(
                    _camera.transform.position + _camera.transform.forward * 2f, Vector3.up);
            }
        }

        private void OnMenuClicked(InputAction.CallbackContext obj)
        {
            _mainPanel.gameObject.SetActive(!_mainPanel.gameObject.activeSelf);

            RepositionMainPanelInFront();
        }

        private void RepositionMainPanelInFront()
        {
            if (_mainPanel.gameObject.activeSelf)
            {
                Vector3 headDirectionNoTilt = _camera.transform.forward;
                headDirectionNoTilt.y = 0;
                headDirectionNoTilt.Normalize();

                _mainPanel.transform.position = _camera.transform.position
                                                + headDirectionNoTilt * 1.5f
                                                + Vector3.up * -.15f;
                _mainPanel.transform.rotation = Quaternion.LookRotation(
                    headDirectionNoTilt,
                    Vector3.up);
            }
        }

        private void UpdateSubPanelVisibility()
        {
            _notLocalizedPanel.gameObject.SetActive(
                _localizationManager.LocalizationInfo.LocalizationStatus
                != LocalizationMapState.Localized);
            _load3DModelPanel.gameObject.SetActive(!_notLocalizedPanel.activeSelf);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
            {
                RepositionMainPanelInFront();
            }
        }

        private void PinCancelled()
        {
            if (_pinningExternalModel != null)
            {
                _pinningExternalModel.CancelPinning();
                pinTool.CancelPinning();
                _pinningExternalModel = null;
                UpdateActiveTool();
            }
        }

        private void PinFinished()
        {
            if (_pinningExternalModel != null)
            {
                _pinningExternalModel.FinishPinning();
                pinTool.FinishPinning();
                _pinningExternalModel = null;
                UpdateActiveTool();
            }
        }

        /// <summary>
        /// Handler for map localization changes. The user may have lost or regained localization.
        /// </summary>
        /// <param name="info">The updated localization info.</param>
        private void OnLocalizationInfoChanged(AnchorsApi.LocalizationInfo info)
        {
            if (info.LocalizationStatus == LocalizationMapState.Localized)
            {
                if (_maybeCreateAnchorAfterLocalizationWithDelayCoroutine == null)
                {
                    _maybeCreateAnchorAfterLocalizationWithDelayCoroutine
                        = MaybeCreateAnchorAfterLocalizationWithDelay();
                    StartCoroutine(_maybeCreateAnchorAfterLocalizationWithDelayCoroutine);
                }

#if !UNITY_OPENXR_1_9_0_OR_NEWER
                // Space Origin is not currently supported in OpenXR. See SDKUNITY-6778.
                TransformExtensions.SetWorldPose(
                    _spaceOriginAxis.transform, info.TargetSpaceOriginPose);
#endif
            }
            else if (_maybeCreateAnchorAfterLocalizationWithDelayCoroutine != null)
            {
                StopCoroutine(_maybeCreateAnchorAfterLocalizationWithDelayCoroutine);
                _maybeCreateAnchorAfterLocalizationWithDelayCoroutine = null;
            }

            UpdateSubPanelVisibility();
        }

        private void OnExternalModelSelect(External3DModel externalModel,
            XRRayInteractor xrRayInteractor)
        {
            if (_currentTool == Tool.Pin)
            {
                if (_pinningExternalModel == null)
                {
                    if (externalModel.EditingPins)
                    {
                        _pinningExternalModel = externalModel;

                        if (xrRayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit raycastHit))
                        {
                            pinTool.gameObject.transform.position = raycastHit.point;
                        }

                        externalModel.StartPinning(pinTool.gameObject.transform);
                        pinTool.StartPinning();
                    }
                }
                else
                {
                    _pinningExternalModel.FinishPinning();
                    _pinningExternalModel = null;
                    pinTool.FinishPinning();
                }
                UpdateActiveTool();
            }
        }

        private void ExternalModelFirstHoverEntered(External3DModel external3DModel)
        {
            _targettedExternalModels.Add(external3DModel);
        }

        private void ExternalModelLastHoverExited(External3DModel external3DModel)
        {
            _targettedExternalModels.Remove(external3DModel);
        }

        private void OnExternalModelEditingPinsChanged(
            External3DModel externalModel, bool editingPins)
        {
            _currentTool = editingPins ? Tool.Pin : Tool.Laser;
            UpdateActiveTool();
        }

        /// <summary>
        /// Handler for the user placing a new 3D model.
        /// </summary>
        /// <param name="modelInfo">The information about the 3D model to place.</param>
        private void OnPlaceNewExternal3DModel(External3DModelManager.ModelInfo modelInfo)
        {
            _currentTool = Tool.Laser;
            UpdateActiveTool();

            Vector3 modelPosition = _camera.transform.TransformPoint(
                External3DModelRelativeStartPosition);
            GameObject anchorGameObject;
            if (_anchorsManager.TryGetAnchorGameObject(_headClosestAnchorId, out anchorGameObject))
            {
                External3DModel externalModel = _external3dModelManager.LoadModelAsync(
                    modelInfo.FileName, anchorGameObject.transform);
                externalModel.Id = "M" + _random.Next(0, Int32.MaxValue);
                externalModel.AnchorId = _headClosestAnchorId;
                externalModel.OnSelect += OnExternalModelSelect;
                externalModel.OnFirstHoverEntered += ExternalModelFirstHoverEntered;
                externalModel.OnLastHoverExited += ExternalModelLastHoverExited;
                externalModel.OnEditingPinsChanged += OnExternalModelEditingPinsChanged;
                externalModel.SetUsingLaserTool(_currentTool == Tool.Laser);

                _externalModelMap[externalModel.Id] = externalModel;
                externalModel.OnDestroyed += () => _externalModelMap.Remove(externalModel.Id);

                Vector3 modelLookDir = _camera.transform.position - modelPosition;
                modelLookDir.y = 0;

                Pose modelPose = new Pose(modelPosition,
                    Quaternion.LookRotation(modelLookDir.normalized, Vector3.up));
                TransformExtensions.SetWorldPose(externalModel.transform, modelPose);
            }
        }

        /// <summary>
        /// Update the visibility of various tools based on UI state.
        /// </summary>
        private void UpdateActiveTool()
        {
            pinTool.gameObject.SetActive(_currentTool == Tool.Pin);

            foreach (var entry in _externalModelMap)
            {
                entry.Value.SetUsingLaserTool(_currentTool == Tool.Laser);
            }
        }

        /// <summary>
        /// Coroutine to wait a brief duration and then attempt to create an anchor for the
        /// current space. A Space must have at least one anchor in order to persist content.
        /// </summary>
        /// <returns></returns>
        private IEnumerator MaybeCreateAnchorAfterLocalizationWithDelay()
        {
            yield return new WaitForSeconds(1.0f);

            if (_anchorsManager.Anchors.Length == 0 && _anchorsManager.LastQuerySuccessful)
            {
                if (!AnchorsApi.CreateAnchor(
                    Pose.identity,
                    (ulong)(DateTimeOffset.Now.ToUnixTimeSeconds() + TimeSpan.FromDays(365).TotalSeconds),
                    (string anchorId) =>
                    {
                        Debug.Log($"Anchor created, Id = {anchorId}.");
                    }))
                {
                    Debug.LogError("Failed to create new anchor.");
                }
            }

            _maybeCreateAnchorAfterLocalizationWithDelayCoroutine = null;
        }

        /// <summary>
        /// Coroutine to periodically up the the status text.
        /// </summary>
        private IEnumerator UpdateStatusTextPeriodically()
        {
            while (true)
            {
                UpdateStatusText();

                yield return new WaitForSeconds(StatusTextUpdateDelaySeconds);
            }
        }

        /// <summary>
        /// Refresh the status text display.
        /// </summary>
        private void UpdateStatusText()
        {
            if (!_statusText.gameObject.activeInHierarchy)
            {
                return;
            }

            string localizationString = _localizationManager.LocalizationInfo.ToString();
            if (_localizationManager.LocalizationInfo.LocalizationStatus == LocalizationMapState.Localized &&
                _localizationManager.LocalizationInfo.MappingMode == LocalizationMapType.Cloud)
            {
                localizationString = "<color=#00ff00>" + localizationString + "</color>";
            }
            else
            {
                localizationString = "<color=#ffa500>" + localizationString + "</color>";
            }

            StringBuilder statusTextBuilder = new StringBuilder();
            statusTextBuilder.AppendFormat(
                "<color=#dbfb76><b>Object Alignment v{0}</b></color>\n" +
                "Map Localization: <i>{1}</i>\n",
                Application.version,
                localizationString);

            if (_exceptionsOrAssertsLogged > 0)
            {
                statusTextBuilder.AppendFormat(
                    "<color=#ee0000><b>{0} errors and {1} exceptions logged</b></color>\n",
                    _errorsLogged,
                    _exceptionsOrAssertsLogged);
            }
            else if (_errorsLogged > 0)
            {
                statusTextBuilder.AppendFormat(
                    "<color=#dbfb76><b>{0} errors logged</b></color>\n",
                    _errorsLogged);
            }

            _statusText.text = statusTextBuilder.ToString();
        }

        /// <summary>
        /// Handle a Unity log messing being posted. Keep track of errors an exceptions.
        /// </summary>
        private void OnLogMessageReceived(string condition, string stacktrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                    _errorsLogged += 1;
                    break;
                case LogType.Assert:
                case LogType.Exception:
                    if (!string.IsNullOrEmpty(stacktrace)
                        && stacktrace.Contains("ContextSensitiveCursor.UpdateCursor"))
                    {
                        Debug.Log("Ignoring ContextSensitiveCursor.UpdateCursor exception");
                        return;
                    }
                    _exceptionsOrAssertsLogged += 1;
                    break;
            }
        }
    }
}